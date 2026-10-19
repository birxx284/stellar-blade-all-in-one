using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using StellarAdvanced;

namespace StellarUnified
{
    static class GuideTests
    {
        static Value Items(Archive a) { return a.Field("DataMap_SBItem").Data.Entries.Single(delegate(Pair p) { return p.Key.Text == "Item"; }).Item; }
        public static int Run(string sample, string output)
        {
            var report = new StringBuilder(); int result = 0;
            Action<bool, string> check = delegate(bool ok, string label) { if (!ok) throw new Exception("FAIL: " + label); report.AppendLine("PASS: " + label); };
            string folder = Path.Combine(Path.GetDirectoryName(output), "guide-fixture-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(folder);
            string hash = SaveEditor.Hash(File.ReadAllBytes(sample));
            try
            {
                var catalog = CatalogItem.Load();
                check(catalog.Count == 304 && catalog.Select(delegate(CatalogItem i) { return i.Alias; }).Distinct().Count() == 304, "304 unique verified cosmetic identifiers remain the backend allowlist");
                check(catalog.All(delegate(CatalogItem i) { return !String.IsNullOrWhiteSpace(i.HowToGet) && !String.IsNullOrWhiteSpace(i.Area) && !String.IsNullOrWhiteSpace(i.Location) && !String.IsNullOrWhiteSpace(i.Requirements) && i.SourceUrl.StartsWith("https://"); }), "every catalog item has acquisition, location, requirements and source details");
                check(catalog.Count(delegate(CatalogItem i) { return i.Category == "nano_suits"; }) == 126, "coverage includes 126 EVE Nano Suits, with other categories counted separately");
                check(catalog.Single(delegate(CatalogItem i) { return i.Alias == "Earring_018"; }).HowToGet.Contains("Tachy"), "Symbol of Legacy describes its actual transformation effect");
                check(catalog.Single(delegate(CatalogItem i) { return i.Alias == "BS_10"; }).Requirements.Contains("NG+"), "White Full Dress explicitly identifies its New Game Plus requirement");
                check(catalog.Single(delegate(CatalogItem i) { return i.Alias == "BS_19"; }).HowToGet.Contains("49"), "Black Pearl explains its 49-can reward requirement");
                var references = CosmeticGuide.SpecialLooks();
                check(references.Count == 3 && references.All(delegate(CatalogItem i) { return i.ReferenceOnly && !catalog.Any(delegate(CatalogItem c) { return c.Alias == i.Alias; }); }), "three special looks are reference entries outside the save-edit allowlist");
                check(references.Single(delegate(CatalogItem i) { return i.Alias == "REF_AngelPermanent"; }).RequiredAlias == "BS_38", "permanent Angel reference prepares the author's actual Red Passion replacement");

                using (var main = new UnifiedForm())
                {
                    main.ShowInTaskbar = false; main.Show(); main.OpenWardrobe(); Application.DoEvents();
                    var controller = main.Controller; controller.ShowLoadErrors = controller.ShowSaveMessages = false;
                    var list = (ListView)controller.Controls.Find("Wardrobe", true).Single();
                    var search = (TextBox)controller.Controls.Find("CosmeticSearch", true).Single();
                    var category = (ComboBox)controller.Controls.Find("CosmeticCategory", true).Single();
                    var details = (TextBox)controller.Controls.Find("CosmeticDetails", true).Single();
                    var prepare = (Button)controller.Controls.Find("PrepareCosmetic", true).Single();
                    check(list.Items.Count == 307 && list.Enabled, "catalog and special references can be browsed without a save");
                    controller.FocusCosmetic("REF_AngelFusion"); Application.DoEvents();
                    check(list.Items.Count == 3 && details.Text.Contains("Symbol of Legacy") && details.Text.Contains("Tachy Mode"), "Angel shortcut selects the special guide and displays transformation instructions");
                    check(!prepare.Enabled && !((Button)main.Controls.Find("SaveCopy", true).Single()).Enabled, "offline browsing cannot prepare items or save a copy");
                    list.SelectedItems[0].Checked = true;
                    check(controller.SelectedCount == 0 && !list.SelectedItems[0].Checked, "reference checkboxes cannot become fake costume requests");
                    category.SelectedIndex = 0; search.Text = "ángel"; Application.DoEvents();
                    check(list.Items.Cast<ListViewItem>().Any(delegate(ListViewItem row) { return ((CatalogItem)row.Tag).Alias == "REF_AngelFusion"; }), "accent-insensitive search finds Angel acquisition text");

                    var fixture = Archive.Parse(File.ReadAllBytes(sample)); Value items = Items(fixture);
                    items.Field("Inventory").Data.Elements.RemoveAll(delegate(Value v) { return PlayerEditor.Instance(v, "Earring_018") != null || PlayerEditor.Instance(v, "BS_38") != null; });
                    items.Field("ItemOtaineSet").Data.Elements.RemoveAll(delegate(Value v) { return v.Text == "Earring_018" || v.Text == "BS_38"; });
                    fixture.Field("SBAchievement").Require("AchievementList").Elements.RemoveAll(delegate(Value v) { return v.Field("AchievementAlias") != null && v.Field("AchievementAlias").Data.Text == "Acquire_Item_Earring_018"; });
                    Value template = items.Field("Inventory").Data.Elements.First(delegate(Value v) { return PlayerEditor.Instance(v, "BS_Green_Suit") != null; });
                    Value unknown = SaveEditor.AddStack(items, template, "BS_TestUncatalogued"); unknown.Field("ItemCount").Data.Set(1);
                    string path = Path.Combine(folder, "before.sav"); File.WriteAllBytes(path, fixture.Serialize());
                    controller.LoadSave(path); controller.FocusCosmetic("REF_AngelFusion"); Application.DoEvents();
                    check(prepare.Enabled && !controller.CurrentEditor.Owned().Contains("Earring_018"), "Angel requirement can be prepared from a save without the earrings");
                    prepare.PerformClick(); Application.DoEvents();
                    check(controller.SelectedCount == 1 && controller.HasPendingChanges, "Prepare required item queues exactly one real accessory");
                    string copy = Path.Combine(folder, "angel.sav"); controller.CopyDestination = delegate { return copy; };
                    ((Button)main.Controls.Find("SaveCopy", true).Single()).PerformClick(); Application.DoEvents();
                    check(File.Exists(copy), "direct Save copy works after preparing an Angel reference");
                    var after = new SaveEditor(copy);
                    check(after.Inventory.Count(delegate(Value v) { return PlayerEditor.Instance(v, "Earring_018") != null; }) == 1 && after.Owned().Contains("Earring_018"), "output contains one actual Symbol of Legacy item with ownership");
                    check(after.Records.Any(delegate(Value v) { return v.Field("AchievementAlias") != null && v.Field("AchievementAlias").Data.Text == "Acquire_Item_Earring_018" && v.Field("ProgressValue").Data.Integer == 1; }), "accessory acquisition record is synchronized");
                    check(!after.Owned().Any(delegate(string a) { return a.StartsWith("REF_"); }), "no UI reference identifiers are written to inventory or ownership");
                    foreach (Node node in controller.CurrentEditor.Archive.Fields.Where(delegate(Node n) { return n.Name != "DataMap_SBItem" && n.Name != "SBAchievement"; }))
                        if (!Writer.NodeBytes(node).SequenceEqual(Writer.NodeBytes(after.Archive.Field(node.Name)))) throw new Exception("Unrelated section changed: " + node.Name);
                    check(true, "all world, quest, story, actor and other unrelated sections remain byte-identical");
                    check(File.ReadAllBytes(path).SequenceEqual(File.ReadAllBytes(controller.LastBackupPath)), "permanent backup is identical to the original fixture");
                    controller.ClearAllChanges(); controller.FocusCosmetic("REF_AngelPermanent"); prepare.PerformClick(); controller.BuildPlan();
                    check(controller.SelectedCount == 1 && controller.CurrentPlan != null && controller.CurrentPlan.Changes.Any(delegate(string s) { return s.Contains("BS_38"); }), "external Angel action prepares only the real Red Passion suit");
                    check(details.Text.Contains("does not install") && details.Text.Contains("separately"), "external mod instructions distinguish item preparation from separate installation");
                    controller.ClearAllChanges(); controller.FocusCosmetic("REF_Tachy");
                    check(!prepare.Enabled, "story-only transformation has no forced-unlock action");
                    category.SelectedIndex = 10; Application.DoEvents();
                    var unknownRow = list.Items.Cast<ListViewItem>().Single(delegate(ListViewItem row) { return ((CatalogItem)row.Tag).Alias == "BS_TestUncatalogued"; }); unknownRow.Selected = true;
                    unknownRow.Checked = true; Application.DoEvents();
                    check(!prepare.Enabled && !unknownRow.Checked && controller.SelectedCount == 0 && details.Text.Contains("not been verified"), "uncatalogued owned identifiers are informational and cannot be granted");
                    bool rejected = false; try { controller.CurrentEditor.Build(new[] { references[0] }, false, false); } catch (InvalidDataException) { rejected = true; }
                    check(rejected, "backend independently rejects special reference identifiers");
                    controller.Size = controller.MinimumSize; Application.DoEvents();
                    check(prepare.RectangleToScreen(prepare.ClientRectangle).Bottom <= controller.RectangleToScreen(controller.ClientRectangle).Bottom, "guide preparation action stays visible at minimum window size");
                    main.Close(); Application.DoEvents();
                }
                check(hash == SaveEditor.Hash(File.ReadAllBytes(sample)), "guide workflow leaves the original research save unchanged");
                report.AppendLine("COSMETIC GUIDE WORKFLOW TESTS PASSED. In-game verification remains pending.");
            }
            catch (Exception error) { result = 1; report.AppendLine(error.ToString()); }
            File.WriteAllText(output, report.ToString(), new UTF8Encoding(true)); return result;
        }
    }
}
