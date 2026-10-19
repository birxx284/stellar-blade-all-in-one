using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;

namespace StellarAdvanced
{
    static class CosmeticGuide
    {
        public static void Attach(List<CatalogItem> items)
        {
            var map = items.ToDictionary(delegate(CatalogItem i) { return i.Alias; }, StringComparer.Ordinal);
            var seen = new HashSet<string>(StringComparer.Ordinal);
            using (var reader = new StreamReader(Assembly.GetExecutingAssembly().GetManifestResourceStream("wardrobe-guide.tsv"), Encoding.UTF8))
            {
                string line;
                while ((line = reader.ReadLine()) != null)
                {
                    string[] f = line.Split('\t'); CatalogItem item;
                    if (f.Length != 7 || !map.TryGetValue(f[0], out item) || !seen.Add(f[0])) throw new InvalidDataException("Invalid acquisition guide entry.");
                    item.Area = f[1]; item.Location = f[2]; item.Requirements = f[3]; item.HowToGet = f[4]; item.Method = f[5]; item.SourceUrl = f[6];
                }
            }
            if (seen.Count != items.Count) throw new InvalidDataException("The cosmetic acquisition guide is incomplete.");
        }
        public static List<CatalogItem> SpecialLooks()
        {
            return new List<CatalogItem> {
                new CatalogItem { Alias = "REF_AngelFusion", Name = "Angel / Fusion - Symbol of Legacy", Category = "special_looks", ReferenceOnly = true, RequiredAlias = "Earring_018",
                    Area = "Story ending / New Game Plus", Location = "Earrings slot and Tachy Mode", Requirements = "Symbol of Legacy equipped; Tachy Mode already unlocked", Method = "Transformation",
                    HowToGet = "Earn Symbol of Legacy through the Making New Memories ending. Equip those earrings, then enter Tachy Mode to use the Angel / Fusion look. The editor can prepare the real earrings in your inventory; you must equip them in game. The look lasts during the transformation and does not replace your ordinary Nano Suit.",
                    SourceUrl = "https://blog.playstation.com/2024/11/11/stellar-blades-nier-automata-dlc-photo-mode-launch-nov-20/" },
                new CatalogItem { Alias = "REF_AngelPermanent", Name = "Angel - permanent outfit (external mod)", Category = "special_looks", ReferenceOnly = true, RequiredAlias = "BS_38",
                    Area = "PC mod", Location = "Red Passion replacement", Requirements = "Playable Angel Outfit by Kaineriqa, installed separately on the game PC", Method = "External mod",
                    HowToGet = "Download Playable Angel Outfit from its author's Nexus page. The author instructs placing its three mod files in SB/Content/Paks/~mods. It replaces Red Passion: equip that suit after installing the mod. Prepare required item only adds Red Passion; it does not install or bundle the mod. Current-build compatibility has not been tested here. Follow the author's latest instructions.",
                    SourceUrl = "https://www.nexusmods.com/stellarblade/mods/296" },
                new CatalogItem { Alias = "REF_Tachy", Name = "Tachy - story transformation", Category = "special_looks", ReferenceOnly = true,
                    Area = "Main story", Location = "Tachy Mode", Requirements = "Reach the story event that unlocks Tachy Mode", Method = "Story transformation",
                    HowToGet = "Progress the main story to unlock Tachy Mode, then activate that ability using the game's controls. This form is not a separate inventory Nano Suit. The editor does not force its story unlock or add a fabricated costume identifier.",
                    SourceUrl = "https://blog.playstation.com/2024/11/11/stellar-blades-nier-automata-dlc-photo-mode-launch-nov-20/" }
            };
        }
        public static List<CatalogItem> UnknownOwned(SaveEditor editor, IEnumerable<CatalogItem> known)
        {
            var aliases = new HashSet<string>(known.Select(delegate(CatalogItem i) { return i.Alias; }), StringComparer.OrdinalIgnoreCase);
            var output = new List<CatalogItem>();
            if (editor == null) return output;
            string[] prefixes = { "BS_", "Hair_", "Earring_", "FaceAccessory_", "DroneSeal_", "AdamCostume_", "LilyCostume_", "DesignPattern_" };
            foreach (string alias in editor.Owned().OrderBy(delegate(string a) { return a; }))
            {
                if (alias == null || alias.Length > 160 || aliases.Contains(alias) || !prefixes.Any(delegate(string prefix) { return alias.StartsWith(prefix, StringComparison.OrdinalIgnoreCase); })) continue;
                if (alias.Any(delegate(char c) { return !Char.IsLetterOrDigit(c) && c != '_'; })) continue;
                output.Add(new CatalogItem { Alias = alias, Name = "Uncatalogued owned appearance: " + alias, Category = "save_only", ReferenceOnly = true,
                    Area = "Loaded save", Location = "Ownership / inventory records", Requirements = "Already present in this save", Method = "Read-only discovery",
                    HowToGet = "This cosmetic-like identifier exists in your save but is not in the verified catalog. Its display name and original acquisition method have not been verified. This entry is informational and cannot be granted by the editor.",
                    SourceUrl = "https://github.com/birxx284/stellar-blade-all-in-one" });
            }
            return output;
        }
        public static string Details(CatalogItem item)
        {
            if (item == null) return "Select an appearance to see where and how to get it. The catalog can be browsed without opening a save.";
            return item.Name + "\r\n\r\nHOW TO GET\r\n" + item.HowToGet + "\r\n\r\nArea: " + item.Area + "\r\nLocation: " + item.Location + "\r\nRequirements: " + item.Requirements +
                "\r\nType: " + item.Method + "\r\n\r\n" + (item.ReferenceOnly ? "Special/reference entry. Only its stated requirement can be prepared; this look is not written as a fake costume." : "Adding the item does not complete its source chest, quest or purchase. DLC access still depends on the game and your existing content.") + "\r\n\r\nSource: " + item.SourceUrl;
        }
    }
}
