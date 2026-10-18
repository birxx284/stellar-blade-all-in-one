using System;
using System.Collections.Generic;

namespace StellarAdvanced
{
    sealed class StackItem
    {
        public string Alias, Name, Category;
        public int Maximum;
        public bool IsCurrency;
        public long Amount, Charges;
        public StackItem DefinitionCopy() { return (StackItem)MemberwiseClone(); }
    }
    static class InventoryCatalog
    {
        public static readonly Dictionary<string, StackItem> Items = Build();
        static Dictionary<string, StackItem> Build()
        {
            var items = new Dictionary<string, StackItem>(StringComparer.Ordinal);
            Add(items, "BetaCrystal", "Money / Gold", "Currency", 1000000, true);
            Add(items, "FishingPoint", "Fishing points", "Currency", 9999, true);
            Add(items, "ETC_Item_VendingMachineCoin", "Vending machine coins", "Currency", 9999, true);
            Add(items, "Money_JunkIron_A", "Microcomponent A", "Materials", 9999, true);
            Add(items, "Money_JunkIron_B", "Microcomponent B", "Materials", 9999, true);
            Add(items, "Money_JunkIron_C", "Microcomponent C", "Materials", 9999, true);
            Add(items, "Money_Nier", "NieR currency (if present)", "DLC currency", 9999, true);
            Add(items, "Money_Nikke", "Nikke currency (if present)", "DLC currency", 9999, true);
            Add(items, "HP_Potion_Small", "Small health potion", "Consumables", 999, false);
            Add(items, "HP_Potion", "Health potion", "Consumables", 999, false);
            Add(items, "HealGrenade", "Healing grenade", "Consumables", 999, false);
            Add(items, "FlashGrenade", "Flash grenade", "Consumables", 999, false);
            Add(items, "ConcussionGrenade", "Concussion grenade", "Consumables", 999, false);
            Add(items, "PulseGrenade", "Pulse grenade", "Consumables", 999, false);
            Add(items, "TrapGrenade", "Trap grenade", "Consumables", 999, false);
            Add(items, "ResurrectionItem", "Resurrection item", "Consumables", 999, false);
            Add(items, "SPInitializer", "Skill reset", "Consumables", 999, false);
            foreach (string alias in new string[] { "Bullet_Normal", "Bullet_Scatter", "Bullet_Missile", "Bullet_Explosive", "Bullet_Railgun" })
                Add(items, alias, alias == "Bullet_Normal" ? "Normal ammo" : alias == "Bullet_Scatter" ? "Scatter ammo" : alias == "Bullet_Missile" ? "Missiles" : alias == "Bullet_Explosive" ? "Explosive ammo" : "Railgun ammo", "Ammo", 9999, false);
            foreach (string alias in new string[] { "PolymerOrganicFilm", "ElastomerFiber", "ECelluloseRayon", "LowDensityWafer", "HighDensityWafer", "2DQuantumWafer", "WeaponCore", "WeaponCore_Damaged", "DroneCore", "TumblerCore", "GearCore", "ExospineCore" })
                Add(items, alias, alias, "Materials", 9999, false);
            foreach (string alias in new string[] { "Item_Worm_Bait", "Item_Shrimp_Bait", "Item_Fish_Slice_Bait", "Item_Special_Bait", "Item_Lure_Megabait", "Item_Lure_Weird" })
                Add(items, alias, alias, "Fishing", 999, false);
            return items;
        }
        static void Add(Dictionary<string, StackItem> items, string alias, string name, string category, int maximum, bool currency)
        { items.Add(alias, new StackItem { Alias = alias, Name = name, Category = category, Maximum = maximum, IsCurrency = currency }); }
    }
}
