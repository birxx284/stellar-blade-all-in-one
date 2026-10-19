using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace StellarAdvanced
{
    sealed class BurstGearOption
    {
        public string Alias, Label;
        public override string ToString() { return Label; }
    }
    static class BurstGear
    {
        public static readonly Dictionary<string, string> Names = new Dictionary<string, string>(StringComparer.Ordinal) {
            { "Gear_MaxBurstGauge_Uncommon", "Burst Expansion Gear · Uncommon" },
            { "Gear_MaxBurstGauge_Rare", "Burst Expansion Gear · Rare" },
            { "Gear_MaxBurstGauge_Uncommon_MK2", "Burst Expansion Gear Mk2 · Uncommon" },
            { "Gear_MaxBurstGauge_Rare_MK2", "Burst Expansion Gear Mk2 · Rare" }
        };
        public static List<int> Slots(Archive archive)
        {
            Node equipment = PlayerEditor.Actor(archive).Field("Equipment");
            if (equipment == null || equipment.Data.Entries == null) return new List<int>();
            Node levels = PlayerEditor.Actor(archive).Field("LevelArray");
            if (levels == null || levels.Data.Elements == null || levels.Data.Elements.Count != 7 || levels.Data.Elements[5].Integer < 0 || levels.Data.Elements[5].Integer > 3) return new List<int>();
            int unlocked = checked((int)levels.Data.Elements[5].Integer + 1);
            return equipment.Data.Entries.Where(delegate(Pair p) { return !p.Key.IsRaw && p.Key.Scalar != null && p.Key.Integer >= 1 && p.Key.Integer <= unlocked; })
                .Select(delegate(Pair p) { return checked((int)p.Key.Integer); }).Distinct().OrderBy(delegate(int i) { return i; }).ToList();
        }
        static List<Value> Inventory(Archive archive)
        { return archive.Field("DataMap_SBItem").Data.Entries.Single(delegate(Pair p) { return p.Key.Text == "Item"; }).Item.Field("Inventory").Data.Elements; }
        static List<Value> Find(List<Value> inventory, string alias)
        { return inventory.Select(delegate(Value v) { return PlayerEditor.Instance(v, alias); }).Where(delegate(Value v) { return v != null; }).ToList(); }
        public static void Apply(SaveEditor original, Archive edited, PlayerRequest request, Value items, EditPlan plan)
        {
            if (request.BurstGearAlias == null) return;
            string label;
            if (!Names.TryGetValue(request.BurstGearAlias, out label)) throw new InvalidDataException("Unsupported Burst expansion gear.");
            if (request.BurstGearSlot != 0 && !Slots(original.Archive).Contains(request.BurstGearSlot)) throw new InvalidDataException("Select an existing unlocked gear slot, or inventory only.");
            List<Value> inventory = items.Field("Inventory").Data.Elements;
            var matches = Find(inventory, request.BurstGearAlias);
            if (matches.Count > 1) throw new InvalidDataException("Duplicate Burst gear entries must be resolved in game first.");
            Value instance;
            if (matches.Count == 0)
            {
                Value template = inventory.Find(delegate(Value v) {
                    Node n = v.Field("ItemInstance"); return n != null && n.Field("ItemAlias") != null && n.Require("ItemAlias").Text.StartsWith("Gear_", StringComparison.Ordinal) &&
                        n.Field("ItemCount") != null && n.Require("ItemCount").Integer == 1 && n.Field("StatLevel") != null && n.Require("StatLevel").Integer == 1;
                });
                if (template == null) throw new InvalidDataException("The save needs an actual level-1 gear item to create compatible Burst expansion gear.");
                instance = SaveEditor.AddStack(items, template, request.BurstGearAlias);
                instance.Field("ItemCount").Data.Set(1); instance.Field("ItemChargeCount").Data.Set(1);
                plan.AddedItems++; plan.Changes.Add("Granted " + label + ". Source chests and story states are unchanged.");
            }
            else instance = matches[0];
            if (instance.Field("ItemCount") == null || instance.Field("StatLevel") == null || instance.Field("SavedGuid") == null ||
                instance.Field("SavedGuid").Data.Raw == null || instance.Field("SavedGuid").Data.Raw.Length != 16 ||
                instance.Field("ItemCount").Data.Integer != 1 || instance.Field("StatLevel").Data.Integer != 1)
                throw new InvalidDataException("Unsupported Burst gear instance.");
            if (request.BurstGearSlot == 0) { plan.Changes.Add(label + " is available in inventory. Equip it in the game's Gear menu to increase maximum Burst capacity."); return; }
            Node equipment = PlayerEditor.Actor(edited).Field("Equipment");
            var slots = equipment.Data.Entries.Where(delegate(Pair p) { return p.Key.Integer == request.BurstGearSlot; }).ToList();
            if (slots.Count != 1 || slots[0].Item.Fields == null) throw new InvalidDataException("A unique compatible equipment slot is required.");
            if (equipment.Data.Entries.Any(delegate(Pair p) { Node alias = p.Item.Field("ItemAlias"); return p.Key.Integer != request.BurstGearSlot && alias != null && String.Equals(alias.Data.Text, request.BurstGearAlias, StringComparison.OrdinalIgnoreCase); }))
                throw new InvalidDataException("That Burst gear is already equipped in another slot. Choose its current slot or another gear variant.");
            slots[0].Item = Writer.CloneValue(instance);
            plan.Changes.Add("Equipped " + label + " in gear slot " + request.BurstGearSlot + ". The previous gear remains in inventory. The game calculates the capacity bonus.");
        }
        public static void Verify(Archive before, Archive after, PlayerRequest request)
        {
            Node beforeEquipment = PlayerEditor.Actor(before).Field("Equipment"), afterEquipment = PlayerEditor.Actor(after).Field("Equipment");
            Node restored = afterEquipment.Clone();
            Pair originalSlot = beforeEquipment.Data.Entries.Single(delegate(Pair p) { return p.Key.Integer == request.BurstGearSlot; });
            Pair changedSlot = restored.Data.Entries.Single(delegate(Pair p) { return p.Key.Integer == request.BurstGearSlot; });
            var instances = Find(Inventory(after), request.BurstGearAlias);
            if (instances.Count != 1 || !Writer.ValueBytes(changedSlot.Item).SequenceEqual(Writer.ValueBytes(instances[0]))) throw new InvalidDataException("Equipped Burst gear does not match its inventory instance.");
            changedSlot.Item = Writer.CloneValue(originalSlot.Item);
            if (!Writer.NodeBytes(restored).SequenceEqual(Writer.NodeBytes(beforeEquipment))) throw new InvalidDataException("An unrelated equipment slot changed.");
        }
    }
}
