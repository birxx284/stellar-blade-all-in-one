using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
namespace StellarAdvanced
{
    static class SaveTests
    {
        static StringBuilder report;
        static void Check(bool success, string description) { if (!success) throw new Exception("FAIL: " + description); report.AppendLine("PASS: " + description); }
        static Value Items(Archive archive) { return archive.Field("DataMap_SBItem").Data.Entries.Find(delegate(Pair p) { return p.Key.Text == "Item"; }).Item; }
        static bool ItemAlias(Value item, string alias) { Node instance = item.Field("ItemInstance"); return instance != null && instance.Require("ItemAlias").Text == alias; }
        static void RemoveOutfit(Archive archive, string alias)
        {
            Value items = Items(archive);
            items.Field("Inventory").Data.Elements.RemoveAll(delegate(Value value) { return ItemAlias(value, alias); });
            items.Field("ItemOtaineSet").Data.Elements.RemoveAll(delegate(Value value) { return value.Text == alias; });
        }
        static void ExpectReject(Action action, string description)
        { bool rejected = false; try { action(); } catch (InvalidOperationException) { rejected = true; } catch (InvalidDataException) { rejected = true; } Check(rejected, description); }
        public static int Run(string samplePath, string reportPath)
        {
            report = new StringBuilder(); report.AppendLine("Stellar Todo en uno 0.3 · structural integration tests");
            string directory = Path.Combine(Path.GetDirectoryName(reportPath), "fixture-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(directory); int result = 0;
            try
            {
                byte[] publicBytes = File.ReadAllBytes(samplePath); var original = Archive.Parse(publicBytes);
                Check(publicBytes.SequenceEqual(original.Serialize()), "real public sample: read/write is byte-for-byte identical");
                Check(original.Fields.Count == 24, "real sample has the expected 24 top-level sections");
                var catalog = CatalogItem.Load(); Check(catalog.Count > 200 && catalog.Select(delegate(CatalogItem c) { return c.Alias; }).Distinct().Count() == catalog.Count, "embedded catalog has unique wardrobe identifiers");
                string fixture = Path.Combine(directory, "synthetic-before.sav");
                var before = Archive.Parse(publicBytes); RemoveOutfit(before, "BS_19");
                before.Field("SBAchievement").Require("AchievementList").Elements.RemoveAll(delegate(Value r)
                { string alias = r.Field("AchievementAlias").Data.Text; return alias.StartsWith("Acquire_Item_Can_") || alias.StartsWith("CollectCan_") || alias == "Acquire_Item_BS_19"; });
                File.WriteAllBytes(fixture, before.Serialize()); byte[] protectedSource = File.ReadAllBytes(fixture);
                var editor = new SaveEditor(fixture); Check(editor.Cans() == 0 && !editor.Owned().Contains("BS_19"), "synthetic fixture has no cans and no Black Pearl");
                EditPlan plan = editor.Build(new CatalogItem[] { catalog.Find(delegate(CatalogItem c) { return c.Alias == "BS_19"; }) }, false, false);
                Check(plan.AddedItems == 1 && plan.UpdatedRecords == 58, "Black Pearl automatically adds 49 can records, 8 milestones and its acquisition record");
                string output = Path.Combine(directory, "after.sav"); editor.SaveCopy(plan, output);
                Check(protectedSource.SequenceEqual(File.ReadAllBytes(fixture)), "saving a modified copy leaves the source unchanged");
                Check(File.Exists(plan.BackupPath) && protectedSource.SequenceEqual(File.ReadAllBytes(plan.BackupPath)), "wardrobe save creates a permanent byte-identical original backup");
                Check(File.Exists(plan.BackupPath + ".txt") && File.ReadAllText(plan.BackupPath + ".txt").Contains(plan.SourceHash), "backup includes recovery instructions and source checksum");
                var after = new SaveEditor(output); Check(after.Cans() == 49 && after.Owned().Contains("BS_19"), "modified copy parses with 49 can records and Black Pearl ownership");
                Check(after.Inventory.Count(delegate(Value value) { return ItemAlias(value, "BS_19"); }) == 1, "no duplicate Black Pearl inventory entries");
                var again = after.Build(new CatalogItem[] { catalog.Find(delegate(CatalogItem c) { return c.Alias == "BS_19"; }) }, false, false);
                Check(again.AddedItems == 0 && again.UpdatedRecords == 0 && again.Bytes.SequenceEqual(plan.Bytes), "applying the same selection twice is idempotent");
                Check(Writer.NodeBytes(before.Field("DataMap_SBEnv")).SequenceEqual(Writer.NodeBytes(after.Archive.Field("DataMap_SBEnv"))), "all world and chest data are preserved instead of guessed");
                Check(Writer.NodeBytes(before.Field("DataMap_SBQuest")).SequenceEqual(Writer.NodeBytes(after.Archive.Field("DataMap_SBQuest"))), "quest and story records are preserved");
                foreach (Node node in before.Fields)
                { if (node.Name != "DataMap_SBItem" && node.Name != "SBAchievement") Check(Writer.NodeBytes(node).SequenceEqual(Writer.NodeBytes(after.Archive.Field(node.Name))), "unchanged section: " + node.Name); }
                var nierBefore = Archive.Parse(publicBytes);
                nierBefore.Field("SBAchievement").Require("AchievementList").Elements.RemoveAll(delegate(Value r)
                { string alias = r.Field("AchievementAlias").Data.Text; return alias.StartsWith("Ach_Album_Unlock_Nier_") || (alias.StartsWith("ChangeState_") && alias.EndsWith("_Nier")); });
                string nierFile = Path.Combine(directory, "nier-before.sav"); File.WriteAllBytes(nierFile, nierBefore.Serialize());
                var nier = new SaveEditor(nierFile); var nierPlan = nier.Build(new CatalogItem[0], false, true);
                Check(nier.HasNierData && nierPlan.UpdatedRecords == 32, "NieR synchronization restores 15 album, 15 pickup and 2 regional records");
                string nierOutput = Path.Combine(directory, "nier-after.sav"); nier.SaveCopy(nierPlan, nierOutput);
                Check(new SaveEditor(nierOutput).Build(new CatalogItem[0], false, true).Bytes.SequenceEqual(nierPlan.Bytes), "NieR progress synchronization is idempotent");
                var bulkBefore = Archive.Parse(publicBytes); Value bulkItems = Items(bulkBefore);
                var baseAliases = new HashSet<string>(catalog.Where(delegate(CatalogItem item) { return String.IsNullOrEmpty(item.Dlc) && item.Alias != "BS_Green_Suit"; }).Select(delegate(CatalogItem item) { return item.Alias; }));
                bulkItems.Field("Inventory").Data.Elements.RemoveAll(delegate(Value value) { Node instance = value.Field("ItemInstance"); return instance != null && baseAliases.Contains(instance.Require("ItemAlias").Text); });
                bulkItems.Field("ItemOtaineSet").Data.Elements.RemoveAll(delegate(Value value) { return baseAliases.Contains(value.Text); });
                string bulkFile = Path.Combine(directory, "bulk-before.sav"); File.WriteAllBytes(bulkFile, bulkBefore.Serialize());
                var bulkEditor = new SaveEditor(bulkFile); var bulkPlan = bulkEditor.Build(catalog.Where(delegate(CatalogItem item) { return baseAliases.Contains(item.Alias); }), false, false);
                Check(bulkPlan.AddedItems == baseAliases.Count, "bulk addition handles every base cosmetic category");
                string bulkOutput = Path.Combine(directory, "bulk-after.sav"); bulkEditor.SaveCopy(bulkPlan, bulkOutput); var bulkAfter = new SaveEditor(bulkOutput);
                Check(baseAliases.All(delegate(string alias) { return bulkAfter.Owned().Contains(alias); }), "all selected base identifiers exist in the modified inventory/ownership view");
                var addedGuids = bulkAfter.Inventory.Where(delegate(Value value) { Node instance = value.Field("ItemInstance"); return instance != null && baseAliases.Contains(instance.Require("ItemAlias").Text); })
                    .Select(delegate(Value value) { return Convert.ToBase64String(value.Field("ItemInstance").Field("SavedGuid").Data.Raw); }).ToList();
                Check(addedGuids.Count == addedGuids.Distinct().Count(), "each added item has a distinct inventory GUID");
                Check(bulkAfter.Build(catalog.Where(delegate(CatalogItem item) { return baseAliases.Contains(item.Alias); }), false, false).Bytes.SequenceEqual(bulkPlan.Bytes), "bulk additions remain idempotent");
                string backupBeforeMoney = plan.BackupPath;
                var quantityChanges = new Dictionary<string, int>(StringComparer.Ordinal) { { "BetaCrystal", 777777 }, { "HP_Potion", 123 }, { "Bullet_Normal", 300 }, { "WeaponCore", 50 } };
                var combined = editor.Build(new CatalogItem[] { catalog.Find(delegate(CatalogItem c) { return c.Alias == "BS_19"; }) }, false, false, quantityChanges);
                Check(combined.UpdatedQuantities == 4 && combined.AddedItems == 1 && combined.UpdatedRecords == 58, "money, consumables, ammo, materials and wardrobe are combined in one plan");
                string combinedOutput = Path.Combine(directory, "combined.sav"); editor.SaveCopy(combined, combinedOutput); var combinedEditor = new SaveEditor(combinedOutput);
                var combinedStacks = combinedEditor.Stacks().ToDictionary(delegate(StackItem item) { return item.Alias; });
                Check(combinedStacks["BetaCrystal"].Amount == 777777 && combinedStacks["BetaCrystal"].Charges == 777777, "money updates both compatible currency counters");
                Check(combinedStacks["HP_Potion"].Amount == 123 && combinedStacks["HP_Potion"].Charges == editor.Stacks().Find(delegate(StackItem item) { return item.Alias == "HP_Potion"; }).Charges, "consumable stock changes preserve separate equipped charges");
                Check(combinedStacks["Bullet_Normal"].Amount == 300 && combinedStacks["Bullet_Normal"].Charges == 300 && combinedStacks["WeaponCore"].Amount == 50, "ammo and material quantities persist after serialization");
                Check(combinedEditor.Cans() == 49 && combinedEditor.Owned().Contains("BS_19"), "combined output also contains the chosen wardrobe and collection changes");
                Check(File.Exists(backupBeforeMoney) && File.Exists(combined.BackupPath) && backupBeforeMoney != combined.BackupPath, "subsequent saves keep previous residual backups and create unique new backups");
                Check(combinedEditor.Build(new CatalogItem[] { catalog.Find(delegate(CatalogItem c) { return c.Alias == "BS_19"; }) }, false, false, quantityChanges).Bytes.SequenceEqual(combined.Bytes), "combined wardrobe and quantity changes are idempotent");
                var reducePlan = combinedEditor.Build(new CatalogItem[0], false, false, new Dictionary<string, int> { { "HP_Potion", 1 } });
                string reduced = Path.Combine(directory, "reduced.sav"); combinedEditor.SaveCopy(reducePlan, reduced);
                Check(new SaveEditor(reduced).Stacks().Find(delegate(StackItem item) { return item.Alias == "HP_Potion"; }).Charges == 1, "lower stock clamps separate charges to the new total");
                ExpectReject(delegate { editor.Build(new CatalogItem[0], false, false, new Dictionary<string, int> { { "Unknown_Item", 10 } }); }, "reject quantity changes for unknown identifiers");
                ExpectReject(delegate { editor.Build(new CatalogItem[0], false, false, new Dictionary<string, int> { { "BetaCrystal", -1 } }); }, "reject negative money");
                ExpectReject(delegate { editor.Build(new CatalogItem[0], false, false, new Dictionary<string, int> { { "BetaCrystal", 1000001 } }); }, "reject money outside the editor range");
                ExpectReject(delegate { editor.Build(new CatalogItem[0], false, false, new Dictionary<string, int> { { "Money_Nier_Dummy_1", 99 } }); }, "collectible NieR counters cannot be altered as arbitrary currency");
                var noNier = Archive.Parse(publicBytes); Value noNierItems = Items(noNier);
                noNierItems.Field("Inventory").Data.Elements.RemoveAll(delegate(Value v) { Node i = v.Field("ItemInstance"); return i != null && i.Require("ItemAlias").Text.IndexOf("Nier", StringComparison.OrdinalIgnoreCase) >= 0; });
                noNierItems.Field("ItemOtaineSet").Data.Elements.RemoveAll(delegate(Value v) { return v.Text.IndexOf("Nier", StringComparison.OrdinalIgnoreCase) >= 0; });
                string noNierFile = Path.Combine(directory, "without-nier.sav"); File.WriteAllBytes(noNierFile, noNier.Serialize()); var noNierEditor = new SaveEditor(noNierFile);
                ExpectReject(delegate { noNierEditor.Build(new CatalogItem[0], false, true); }, "reject NieR synchronization without pre-existing NieR ownership data");
                ExpectReject(delegate { editor.Build(new CatalogItem[] { new CatalogItem { Alias = "Unknown_Item", Name = "Unknown" } }, false, false); }, "reject arbitrary identifiers outside the catalog");
                editor.SaveCopy(plan, fixture);
                Check(plan.OutputPath != fixture && File.Exists(plan.OutputPath) && protectedSource.SequenceEqual(File.ReadAllBytes(fixture)), "choosing the source path creates a renamed copy and preserves the source");
                byte[] previousOutput = File.ReadAllBytes(output); editor.SaveCopy(plan, output);
                Check(plan.OutputPath != output && File.Exists(plan.OutputPath) && previousOutput.SequenceEqual(File.ReadAllBytes(output)), "choosing an existing destination succeeds with a new name and preserves the previous file");
                string nestedDestination = Path.Combine(directory, "new-folder", "nested", "new-save"); editor.SaveCopy(plan, nestedDestination);
                Check(plan.OutputPath.EndsWith(".sav") && File.Exists(plan.OutputPath) && File.Exists(plan.BackupPath), "save creates missing destination directories and appends the save extension");
                string moveFixture = Path.Combine(directory, "moving-source.sav"); File.WriteAllBytes(moveFixture, publicBytes); var movingEditor = new SaveEditor(moveFixture);
                var movingPlan = movingEditor.Build(new CatalogItem[0], false, false, new Dictionary<string, int> { { "BetaCrystal", 500000 } }); File.Delete(moveFixture);
                movingEditor.SaveCopy(movingPlan, Path.Combine(directory, "recovered-from-session.sav"));
                Check(movingPlan.SourceMissing && File.Exists(movingPlan.OutputPath) && publicBytes.SequenceEqual(File.ReadAllBytes(movingPlan.BackupPath)), "a moved or removed source can be saved from the loaded snapshot with a verified residual backup");
                File.AppendAllText(fixture, "changed");
                ExpectReject(delegate { editor.SaveCopy(plan, Path.Combine(directory, "stale.sav")); }, "reject a plan when the source has changed since preview");
                ExpectReject(delegate { Archive.Parse(new byte[64]); }, "reject an invalid archive signature");
                byte[] evas = new byte[publicBytes.Length + 8]; Buffer.BlockCopy(Encoding.ASCII.GetBytes("EVAS"), 0, evas, 0, 4); Buffer.BlockCopy(publicBytes, 0, evas, 8, publicBytes.Length);
                string evasFile = Path.Combine(directory, "evas.sav"); File.WriteAllBytes(evasFile, evas);
                ExpectReject(delegate { new SaveEditor(evasFile); }, "reject an EVAS envelope with unsupported version zero and no CRC");
                EvasTests.Run(directory, publicBytes, protectedSource, Check);
                report.AppendLine("ALL STRUCTURAL TESTS PASSED. In-game behavior and version compatibility are NOT verified.");
            }
            catch (Exception error) { report.AppendLine(error.ToString()); result = 1; }
            finally
            {
                // Only delete explicitly enumerated files in this test's private random directory.
                string testRoot = Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
                foreach (string path in Directory.GetFiles(directory, "*", SearchOption.AllDirectories))
                {
                    if (!Path.GetFullPath(path).StartsWith(testRoot, StringComparison.OrdinalIgnoreCase)) throw new IOException("Test cleanup escaped its private directory.");
                    File.Delete(path);
                }
                foreach (string childDirectory in Directory.GetDirectories(directory, "*", SearchOption.AllDirectories).OrderByDescending(delegate(string value) { return value.Length; }))
                {
                    if (!Path.GetFullPath(childDirectory).StartsWith(testRoot, StringComparison.OrdinalIgnoreCase)) throw new IOException("Test cleanup escaped its private directory.");
                    Directory.Delete(childDirectory);
                }
                Directory.Delete(directory);
                File.WriteAllText(reportPath, report.ToString(), new UTF8Encoding(true)); Console.Write(report.ToString());
            }
            return result;
        }
    }
}
