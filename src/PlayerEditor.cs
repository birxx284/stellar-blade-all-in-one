using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace StellarAdvanced
{
    sealed class PlayerRequest
    {
        public int? BodyLevel, BetaLevel, WeaponLevel, SkillPoints;
        public bool GrantMissingCores;
        public string BurstGearAlias;
        public int BurstGearSlot;
        public bool Any { get { return BodyLevel.HasValue || BetaLevel.HasValue || WeaponLevel.HasValue || SkillPoints.HasValue || BurstGearAlias != null; } }
        public void Clear() { BodyLevel = BetaLevel = WeaponLevel = SkillPoints = null; GrantMissingCores = false; BurstGearAlias = null; BurstGearSlot = 0; }
    }
    sealed class CoreUpgrade
    {
        public string Name, Alias;
        public int Index, Level, Maximum, Pieces, Cost = 3;
        public int AvailableMaximum { get { return Math.Min(Maximum, Level + Pieces / Cost); } }
        public int Remaining(int target) { return checked(Pieces + Cost * (Level - target)); }
    }
    sealed class PlayerSnapshot
    {
        public int SkillPoints;
        public CoreUpgrade Body, Beta, Weapon;
    }
    static class PlayerEditor
    {
        public static Value Actor(Archive archive)
        {
            Node map = archive.Field("DataMap_SBActor");
            if (map == null || map.Data.Entries == null) throw new InvalidDataException("Player data is unavailable.");
            var matches = map.Data.Entries.Where(delegate(Pair p) { return p.Key.Text == "PlayerActor"; }).ToList();
            if (matches.Count != 1 || matches[0].Item.Fields == null) throw new InvalidDataException("A unique player record is required.");
            return matches[0].Item;
        }
        static Value Integer(Value owner, string name)
        {
            Node n = owner.Field(name);
            if (n == null || n.Data.IsRaw || n.Type != "IntProperty" || n.Data.Integer < 0 || n.Data.Integer > 1000000)
                throw new InvalidDataException("Unsupported player field: " + name);
            return n.Data;
        }
        static List<Value> Levels(Value actor)
        {
            Node n = actor.Field("LevelArray");
            if (n == null || (n.Inner != "IntProperty" && n.Inner != "UInt32Property") || n.Data.Elements == null || n.Data.Elements.Count != 7 ||
                n.Data.Elements.Any(delegate(Value v) { return v.IsRaw || (v.Type != "IntProperty" && v.Type != "UInt32Property") || v.Integer < 0 || v.Integer > 1000; }))
                throw new InvalidDataException("Unsupported core upgrade layout. Expected seven character levels.");
            return n.Data.Elements;
        }
        public static Value Instance(Value entry, string alias)
        {
            Node n = entry.Field("ItemInstance");
            return n != null && n.Field("ItemAlias") != null && String.Equals(n.Require("ItemAlias").Text, alias, StringComparison.OrdinalIgnoreCase) ? n.Data : null;
        }
        static int Pieces(SaveEditor editor, string alias)
        { return Pieces(editor.Inventory, alias); }
        static int Pieces(IEnumerable<Value> inventory, string alias)
        {
            var matches = inventory.Select(delegate(Value v) { return Instance(v, alias); }).Where(delegate(Value v) { return v != null; }).ToList();
            if (matches.Count == 0) return 0;
            if (matches.Count != 1) throw new InvalidDataException("Duplicate core stacks: " + alias);
            Node countNode = matches[0].Field("ItemCount"), chargeNode = matches[0].Field("ItemChargeCount");
            if (countNode == null || chargeNode == null) throw new InvalidDataException("Incomplete core stack: " + alias);
            Value count = countNode.Data, charge = chargeNode.Data;
            if (count.IsRaw || charge.IsRaw || (count.Type != "UInt32Property" && count.Type != "IntProperty") ||
                (charge.Type != "UInt32Property" && charge.Type != "IntProperty") || count.Integer < 0 || count.Integer > 9999 || count.Integer != charge.Integer)
                throw new InvalidDataException("Unsupported core quantities: " + alias);
            return checked((int)count.Integer);
        }
        public static PlayerSnapshot Read(SaveEditor editor)
        {
            Value actor = Actor(editor.Archive); var levels = Levels(actor);
            Node map = editor.Archive.Field("DataMap_int32");
            var ng = map == null || map.Data.Entries == null ? new List<Pair>() : map.Data.Entries.Where(delegate(Pair p) { return p.Key.Text == "NewGamePlusPlayCount"; }).ToList();
            if (ng.Count != 1 || ng[0].Item.IsRaw || ng[0].Item.Type != "IntProperty" || ng[0].Item.Integer < 0 || ng[0].Item.Integer > 1000)
                throw new InvalidDataException("Cannot determine the normal or New Game Plus upgrade limits.");
            int cap = ng[0].Item.Integer > 0 ? 9 : 6;
            var snapshot = new PlayerSnapshot { SkillPoints = checked((int)Integer(actor, "SP").Integer) };
            snapshot.Body = new CoreUpgrade { Name = "HP capacity", Alias = "BodyCore", Index = 1, Level = checked((int)levels[1].Integer), Maximum = cap, Pieces = Pieces(editor, "BodyCore") };
            snapshot.Beta = new CoreUpgrade { Name = "Beta capacity", Alias = "BetaCore", Index = 2, Level = checked((int)levels[2].Integer), Maximum = cap, Pieces = Pieces(editor, "BetaCore") };
            snapshot.Weapon = new CoreUpgrade { Name = "Weapon power", Alias = "WeaponCore", Index = 4, Level = checked((int)levels[4].Integer), Maximum = cap == 9 ? 40 : 15, Pieces = Pieces(editor, "WeaponCore"), Cost = 1 };
            return snapshot;
        }
        public static void Apply(SaveEditor original, Archive edited, PlayerRequest request, Value items, List<Value> records, EditPlan plan)
        {
            if (request == null || !request.Any) return;
            var snapshot = Read(original); Value actor = Actor(edited); var levels = Levels(actor);
            foreach (var core in new[] { snapshot.Body, snapshot.Beta, snapshot.Weapon })
            {
                int? target = core.Index == 1 ? request.BodyLevel : core.Index == 2 ? request.BetaLevel : request.WeaponLevel;
                if (!target.HasValue || target.Value == core.Level) continue;
                core.Pieces = Pieces(items.Require("Inventory").Elements, core.Alias);
                if (core.Level > core.Maximum || target < core.Level || target > core.Maximum)
                    throw new InvalidDataException(core.Name + ": level must be between " + core.Level + " and " + core.Maximum + ". A level above the game limit requires a separate game-data mod.");
                int missing = Math.Max(0, -core.Remaining(target.Value));
                if (missing > 0 && !request.GrantMissingCores)
                    throw new InvalidDataException(core.Name + ": " + missing + " more cores are needed. Enable 'Grant missing cores' in Player upgrades if you want the editor to grant these upgrade credits.");
                int remaining = core.Remaining(target.Value) + missing;
                var matches = items.Field("Inventory").Data.Elements.Select(delegate(Value v) { return Instance(v, core.Alias); }).Where(delegate(Value v) { return v != null; }).ToList();
                if (matches.Count > 1 || (matches.Count == 0 && remaining != 0)) throw new InvalidDataException("A unique collected core stack is required.");
                if (matches.Count == 1)
                {
                    matches[0].Field("ItemCount").Data.Set(remaining); matches[0].Field("ItemChargeCount").Data.Set(remaining);
                }
                levels[core.Index].Set(target.Value);
                // Trophy progress records represent historical milestones, not a refundable balance.
                if (target > core.Level) SaveEditor.Progress(records, core.Index == 1 ? "Trophy_BodyMaxUpgrade" : core.Index == 2 ? "Trophy_BetaMaxUpgrade" : "Trophy_WeaponMaxUpgrade", Math.Min(core.Index == 4 ? 15 : 6, target.Value), plan);
                plan.Changes.Add(core.Name + ": level " + core.Level + " → " + target + "; held cores " + core.Pieces + " → " + remaining + (missing == 0 ? ". Existing core total preserved." : ". Granted and spent " + missing + " missing core credits; world bodies are unchanged."));
            }
            if (request.SkillPoints.HasValue)
            {
                int target = request.SkillPoints.Value;
                if (target < 0 || target > Math.Max(9999, snapshot.SkillPoints)) throw new InvalidDataException("Skill points must be between 0 and 9999 (or the existing higher balance).");
                Integer(actor, "SP").Set(target);
                if (target != snapshot.SkillPoints) plan.Changes.Add("Unspent skill points: " + snapshot.SkillPoints + " → " + target);
            }
            BurstGear.Apply(original, edited, request, items, plan);
            plan.Limits.Add("HP/Beta cost 3 cores per level; weapon power costs 1 Weapon Core per level. Saved permanent levels are edited, not current gauges. Missing credits are granted only when explicitly selected. Existing upgrades cannot be lowered. Weapon limits: 15 normal / 40 NG+.");
            plan.Limits.Add("World pickup states, learned skills, XP and current gauges are preserved. Granted upgrade credits do not mark bodies as collected. Burst capacity comes from expansion gear: inventory-only additions must be equipped in game. Tachy maximum capacity is not edited.");
        }
        public static void VerifyUnrelated(Archive before, Archive after, PlayerRequest request = null, EquipmentRequest equipment = null)
        {
            // Restore only the three permitted player fields in a clone, then compare the complete actor map.
            Node restored = after.Field("DataMap_SBActor").Clone();
            Value original = Actor(before), changed = restored.Data.Entries.Single(delegate(Pair p) { return p.Key.Text == "PlayerActor"; }).Item;
            changed.Field("SP").Data.Set(original.Field("SP").Data.Scalar);
            var a = Levels(original); var b = Levels(changed); b[1].Set(a[1].Scalar); b[2].Set(a[2].Scalar);
            if (request != null && request.WeaponLevel.HasValue) b[4].Set(a[4].Scalar);
            if ((equipment != null && equipment.Any) || (request != null && request.BurstGearAlias != null && request.BurstGearSlot != 0))
            {
                EquipmentEditor.VerifyEquipment(before, after, equipment, request);
                int index = changed.Fields.FindIndex(delegate(Node n) { return n.Name == "Equipment"; });
                changed.Fields[index] = original.Field("Equipment").Clone();
            }
            if (!Writer.NodeBytes(before.Field("DataMap_SBActor")).SequenceEqual(Writer.NodeBytes(restored)))
                throw new InvalidDataException("An unrelated player field changed.");
        }
    }
}
