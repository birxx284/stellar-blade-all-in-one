using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace StellarAdvanced
{
    static class EquipmentFields
    {
        public static Value Require(this Value owner, string name)
        {
            Node node = owner.Field(name);
            if (node == null || node.Data == null) throw new InvalidDataException("Missing compatible field: " + name);
            return node.Data;
        }
    }
    sealed class EquipmentItem
    {
        public string Alias, Name, Category;
        public bool Mk2 { get { return Alias.EndsWith("_MK2", StringComparison.Ordinal); } }
        public int Maximum { get { return Category == "Exospines" ? 3 : 1; } }
        public override string ToString() { return Name; }
    }
    sealed class EquipmentRequest
    {
        public readonly Dictionary<string, int> Items = new Dictionary<string, int>(StringComparer.Ordinal);
        public readonly Dictionary<string, int> Supplies = new Dictionary<string, int>(StringComparer.Ordinal);
        public bool Any { get { return Items.Count != 0 || Supplies.Count != 0; } }
        public void Clear() { Items.Clear(); Supplies.Clear(); }
    }
    static class EquipmentEditor
    {
        public static readonly List<EquipmentItem> Catalog = BuildCatalog();
        // Functional identifiers independently cross-checked against real inventory entries.
        public static readonly string[] Materials = { "WeaponCore", "WeaponCore_Damaged", "DroneCore", "TumblerCore", "GearCore", "ExospineCore", "PolymerOrganicFilm", "ElastomerFiber", "ECelluloseRayon", "LowDensityWafer", "HighDensityWafer", "2DQuantumWafer", "Money_JunkIron_A", "Money_JunkIron_B", "Money_JunkIron_C" };
        static List<EquipmentItem> BuildCatalog()
        {
            var result = new List<EquipmentItem>();
            var spines = new Dictionary<string, string> { { "Skill", "Beta Trance" }, { "Burst", "Burst Trance" }, { "Stab", "Camouflage" }, { "Combo", "Chain" }, { "Shoot", "Eagle Eye" }, { "Grenade", "Grenadier" }, { "Crowd", "Impact" }, { "Tachy", "Judgment" }, { "Survive", "Protection" }, { "Recovery", "Recovery" }, { "Guard", "Reflex" }, { "Down", "Suppression" } };
            foreach (var pair in spines) foreach (bool mk2 in new[] { false, true })
                result.Add(new EquipmentItem { Alias = "PT_" + pair.Key + (mk2 ? "_MK2" : ""), Name = pair.Value + "-Type Exospine" + (mk2 ? " Mk2" : ""), Category = "Exospines" });
            var gears = new Dictionary<string, string> {
                { "AdditiveBetaSkillDamage", "Beta skill damage" }, { "AdditiveBustSkillDamage", "Burst skill damage" }, { "AdditiveFixedDamage", "Fixed attack damage" },
                { "AdditiveGiveAndReciveDamageRate", "Double-edged damage" }, { "AttackSpeed", "Attack speed" }, { "BetaCrystalAdditiveRate", "Gold acquisition" },
                { "BetaGaugeAdditiveRate", "Beta energy gain" }, { "BurstGaugeAdditiveRate", "Burst energy gain" }, { "ComboAttackAdditiveDamage", "Combo damage" },
                { "CriticalPercentage", "Critical chance" }, { "CriticalValueRate", "Critical damage" }, { "GainBetaGaugeValueOnDamaged", "Beta energy on damage" },
                { "GainBurstGaugeValueOnDamaged", "Burst energy on damage" }, { "HighHpDamageAdditiveRate", "High-HP attack" }, { "KillEnemyAdditiveDamageRate", "Attack after a kill" },
                { "KillEnemyHealHP", "Healing after a kill" }, { "LowHpDamageAdditiveRate", "Low-HP attack" }, { "MaxBurstGauge", "Burst capacity" }, { "MaxShield", "Shield capacity" },
                { "MeleeAttackDamageReductionRate", "Melee damage protection" }, { "MeleeRangeDamageReductionRate", "General damage protection" }, { "RangeAttackDamageAdditiveRate", "Ranged attack" },
                { "RangeAttackDamageReductionRate", "Ranged damage protection" }, { "SPExpAdditiveRate", "SP experience gain" }, { "ShieldAttackPowerRate", "Shield damage" }, { "ShieldIgnorePercentage", "Shield penetration" }
            };
            foreach (var pair in gears) foreach (string rarity in new[] { "Common", "Uncommon", "Rare" }) foreach (bool mk2 in new[] { false, true })
            {
                if (pair.Key == "MaxBurstGauge" && rarity == "Common") continue;
                result.Add(new EquipmentItem { Alias = "Gear_" + pair.Key + "_" + rarity + (mk2 ? "_MK2" : ""), Name = pair.Value + " gear" + (mk2 ? " Mk2" : "") + " - " + rarity, Category = "Gear" });
            }
            return result.OrderBy(delegate(EquipmentItem i) { return i.Category + i.Name; }).ToList();
        }
        public static bool IsNgPlus(SaveEditor editor)
        {
            Node map = editor.Archive.Field("DataMap_int32");
            var entries = map == null || map.Data.Entries == null ? new List<Pair>() : map.Data.Entries.Where(delegate(Pair p) { return p.Key.Text == "NewGamePlusPlayCount"; }).ToList();
            if (entries.Count != 1 || entries[0].Item.IsRaw || entries[0].Item.Type != "IntProperty" || entries[0].Item.Integer < 0 || entries[0].Item.Integer > 1000)
                throw new InvalidDataException("Cannot verify whether this save is in New Game Plus.");
            return entries[0].Item.Integer > 0;
        }
        public static List<Value> Find(IEnumerable<Value> inventory, string alias)
        { return inventory.Select(delegate(Value v) { return PlayerEditor.Instance(v, alias); }).Where(delegate(Value v) { return v != null; }).ToList(); }
        static void ValidateInstance(Value v, int maximum, bool single)
        {
            foreach (string name in new[] { "StatLevel", "ItemCount", "ItemChargeCount" })
            {
                Node n = v.Field(name);
                if (n == null || n.Data.IsRaw || (n.Type != "IntProperty" && n.Type != "UInt32Property")) throw new InvalidDataException("Unsupported equipment or material field: " + name);
            }
            if (v.Require("StatLevel").Integer < 1 || v.Require("StatLevel").Integer > maximum || v.Require("ItemCount").Integer < 0 || v.Require("ItemCount").Integer > 999999 ||
                v.Require("ItemChargeCount").Integer < 0 || v.Require("ItemChargeCount").Integer > v.Require("ItemCount").Integer || (single && (v.Require("ItemCount").Integer != 1 || v.Require("ItemChargeCount").Integer != 1)))
                throw new InvalidDataException("Unsupported equipment level or quantity.");
            Node guid = v.Field("SavedGuid");
            if (guid == null || guid.Data.Raw == null || guid.Data.Raw.Length != 16) throw new InvalidDataException("Unsupported equipment identity.");
        }
        static Value Template(List<Value> inventory, string category)
        {
            Value template = inventory.Find(delegate(Value entry) {
                Node n = entry.Field("ItemInstance"); if (n == null || n.Field("ItemAlias") == null) return false;
                string alias = n.Require("ItemAlias").Text;
                return category == "Materials" ? Materials.Contains(alias, StringComparer.OrdinalIgnoreCase) : Catalog.Any(delegate(EquipmentItem i) { return String.Equals(i.Alias, alias, StringComparison.OrdinalIgnoreCase) && i.Category == category; });
            });
            if (template == null) throw new InvalidDataException("This save needs an actual " + category.ToLowerInvariant() + " item to create compatible entries. Obtain one in game first.");
            ValidateInstance(template.Require("ItemInstance"), category == "Exospines" ? 3 : 1, category != "Materials");
            return template;
        }
        static Value Create(Value items, string alias, string category)
        {
            Value v = SaveEditor.AddStack(items, Template(items.Field("Inventory").Data.Elements, category), alias);
            v.Require("StatLevel").Set(1);
            foreach (string name in new[] { "Equiped", "bFavorites", "bMustAcquisition", "bWithResetZone" }) if (v.Field(name) != null) v.Require(name).Set(false);
            if (v.Field("MatVarIndex") != null) v.Require("MatVarIndex").Set(0);
            if (v.Field("ItemVisualAlias") != null) v.Require("ItemVisualAlias").Set("None");
            if (v.Field("OverrideSaveType") != null) v.Require("OverrideSaveType").Set(category != "Materials" || alias == "TumblerCore" || alias == "GearCore" ? "ESBItemOverrideSaveType_Save" : "ESBItemOverrideSaveType_None");
            return v;
        }
        static void Own(Value items, string alias)
        { var list = items.Require("ItemOtaineSet").Elements; if (!list.Any(delegate(Value v) { return String.Equals(v.Text, alias, StringComparison.OrdinalIgnoreCase); })) list.Add(Writer.Text(alias)); }
        public static void Apply(SaveEditor original, Archive edited, EquipmentRequest request, Value items, EditPlan plan)
        {
            if (request == null || !request.Any) return;
            bool ng = IsNgPlus(original); List<Value> inventory = items.Require("Inventory").Elements;
            foreach (var pair in request.Items)
            {
                EquipmentItem definition = Catalog.Find(delegate(EquipmentItem i) { return i.Alias == pair.Key; });
                if (definition == null || pair.Value < 1 || pair.Value > definition.Maximum) throw new InvalidDataException("Unsupported equipment or upgrade level: " + pair.Key);
                if (definition.Mk2 && !ng) throw new InvalidDataException("Mk2 equipment requires an existing New Game Plus save. This action does not start a new run.");
                var found = Find(inventory, pair.Key); if (found.Count > 1) throw new InvalidDataException("Duplicate equipment entries: " + pair.Key + ". Resolve these in game first.");
                Value v = found.Count == 0 ? Create(items, pair.Key, definition.Category) : found[0];
                ValidateInstance(v, definition.Maximum, found.Count != 0);
                if (found.Count == 0) { v.Require("ItemCount").Set(1); v.Require("ItemChargeCount").Set(1); plan.AddedItems++; }
                long oldLevel = v.Require("StatLevel").Integer; int level = Math.Max(pair.Value, (int)oldLevel); v.Require("StatLevel").Set(level); Own(items, pair.Key);
                Node equipment = PlayerEditor.Actor(edited).Field("Equipment");
                if (equipment != null && equipment.Data.Entries != null) foreach (Pair slot in equipment.Data.Entries)
                {
                    Node alias = slot.Item.Field("ItemAlias"); if (alias == null || !String.Equals(alias.Data.Text, pair.Key, StringComparison.OrdinalIgnoreCase)) continue;
                    if (slot.Item.Field("SavedGuid") == null || slot.Item.Require("SavedGuid").Raw == null || !slot.Item.Require("SavedGuid").Raw.SequenceEqual(v.Require("SavedGuid").Raw))
                        throw new InvalidDataException("Equipped item identity does not match inventory: " + pair.Key);
                    slot.Item.Require("StatLevel").Set(level);
                }
                if (found.Count == 0 || oldLevel != level) plan.Changes.Add((found.Count == 0 ? "Granted " : "Upgraded ") + definition.Name + (definition.Maximum == 3 ? " to level " + level : "") + ".");
            }
            foreach (var pair in request.Supplies)
            {
                if (!Materials.Contains(pair.Key) || pair.Value < 0 || pair.Value > 9999) throw new InvalidDataException("Unsupported material grant: " + pair.Key);
                var found = Find(inventory, pair.Key); if (found.Count > 1) throw new InvalidDataException("Duplicate material stacks: " + pair.Key);
                if (found.Count == 0 && pair.Value == 0) continue;
                Value v = found.Count == 0 ? Create(items, pair.Key, "Materials") : found[0]; ValidateInstance(v, 1, false);
                long before = v.Require("ItemCount").Integer; int target = Math.Max((int)before, pair.Value);
                v.Require("ItemCount").Set(target); v.Require("ItemChargeCount").Set(target); Own(items, pair.Key);
                if (found.Count == 0) plan.AddedItems++;
                if (before != target) { plan.UpdatedQuantities++; plan.Changes.Add("Material stock: " + pair.Key + " " + before + " -> " + target + " (minimum requested stock)."); }
            }
            plan.Limits.Add("Source chests and core bodies remain collectible: their reward-to-world mapping is not verified, so pickup blocking is not implemented. New equipment must be equipped in game. Selected exospine levels are granted without module costs; an already-equipped copy keeps the same identity and receives the matching level. Mk2 requires an existing NG+ run.");
            plan.Limits.Add("Material grants set minimum stock before exact item-quantity edits and core spending. Explicit quantity edits take precedence; weapon upgrades then spend from the resulting Weapon Core stock.");
        }
        public static void VerifyEquipment(Archive before, Archive after, EquipmentRequest request, PlayerRequest player)
        {
            Node expected = PlayerEditor.Actor(before).Field("Equipment").Clone();
            List<Value> inventory = after.Field("DataMap_SBItem").Data.Entries.Single(delegate(Pair p) { return p.Key.Text == "Item"; }).Item.Require("Inventory").Elements;
            if (request != null) foreach (Pair slot in expected.Data.Entries)
            {
                Node alias = slot.Item.Field("ItemAlias");
                if (alias == null) continue;
                var match = request.Items.Where(delegate(KeyValuePair<string, int> p) { return String.Equals(p.Key, alias.Data.Text, StringComparison.OrdinalIgnoreCase); }).ToList();
                if (match.Count == 0) continue;
                int target = match[0].Value;
                var found = Find(inventory, alias.Data.Text);
                if (found.Count != 1 || !slot.Item.Require("SavedGuid").Raw.SequenceEqual(found[0].Require("SavedGuid").Raw)) throw new InvalidDataException("Equipped/inventory identity mismatch.");
                slot.Item.Require("StatLevel").Set(Math.Max(target, (int)slot.Item.Require("StatLevel").Integer));
            }
            if (player != null && player.BurstGearAlias != null && player.BurstGearSlot != 0)
            {
                var instances = Find(inventory, player.BurstGearAlias); if (instances.Count != 1) throw new InvalidDataException("A unique Burst gear identity is required.");
                expected.Data.Entries.Single(delegate(Pair p) { return p.Key.Integer == player.BurstGearSlot; }).Item = Writer.CloneValue(instances[0]);
            }
            if (!Writer.NodeBytes(expected).SequenceEqual(Writer.NodeBytes(PlayerEditor.Actor(after).Field("Equipment")))) throw new InvalidDataException("An unrelated equipment slot or field changed.");
        }
    }
}
