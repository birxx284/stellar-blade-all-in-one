using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;

namespace StellarUnified
{
    static class CompactTests
    {
        public static int Run(string sample, string output)
        {
            var report = new StringBuilder(); int result = 0;
            Action<bool, string> check = delegate(bool success, string description) { if (!success) throw new Exception("FAIL: " + description); report.AppendLine("PASS: " + description); };
            try
            {
                string hash = StellarAdvanced.SaveEditor.Hash(File.ReadAllBytes(sample));
                string copyFolder = Path.Combine(Path.GetDirectoryName(output), "save-copy-flow-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(copyFolder);
                using (var main = new UnifiedForm())
                {
                    main.ShowInTaskbar = false; main.Show(); main.Controller.ShowLoadErrors = false; Application.DoEvents();
                    check(main.ClientSize.Width <= 740 && main.ClientSize.Height <= 540, "main editor opens in a compact window");
                    var gold = (NumericUpDown)main.Controls.Find("GoldAmount", true).Single();
                    var stage = (Button)main.Controls.Find("StageGold", true).Single(); var save = (Button)main.Controls.Find("SaveCopy", true).Single();
                    check(!gold.Enabled && !stage.Enabled && !save.Enabled, "editing and saving stay disabled before a valid save is loaded");
                    main.Controller.LoadSave(sample); Application.DoEvents();
                    check(gold.Enabled && stage.Enabled && save.Enabled && main.Controller.HasLoadedSave, "opening a valid save populates the compact gold control");
                    main.Controller.ShowSaveMessages = false;
                    int destinations = 0;
                    main.Controller.CopyDestination = delegate(string name) { destinations++; return Path.Combine(copyFolder, "direct-" + destinations + ".sav"); };
                    main.OpenObjects(); main.OpenPlayer(); Application.DoEvents();
                    var directObjects = main.OwnedForms.OfType<QuantityForm>().Single();
                    var directList = (ListView)directObjects.Controls.Find("Items", true).Single();
                    directList.Items.Cast<ListViewItem>().Single(delegate(ListViewItem row) { return ((StellarAdvanced.StackItem)row.Tag).Alias == "HP_Potion"; }).Selected = true;
                    Application.DoEvents();
                    ((NumericUpDown)directObjects.Controls.Find("ObjectAmount", true).Single()).Value = 144;
                    var directPlayer = main.OwnedForms.OfType<PlayerForm>().Single();
                    ((NumericUpDown)directPlayer.Controls.Find("SkillPoints", true).Single()).Value = 222;
                    ((NumericUpDown)directPlayer.Controls.Find("StellarTears", true).Single()).Value = 50;
                    gold.Value = 888888;
                    save.PerformClick(); Application.DoEvents();
                    var directSave = new StellarAdvanced.SaveEditor(Path.Combine(copyFolder, "direct-1.sav"));
                    check(destinations == 1 && directSave.Stacks().Single(delegate(StellarAdvanced.StackItem item) { return item.Alias == "BetaCrystal"; }).Amount == 888888, "Save copy is clickable and saves edited gold without Prepare or Review clicks");
                    check(directSave.Stacks().Single(delegate(StellarAdvanced.StackItem item) { return item.Alias == "HP_Potion"; }).Amount == 144 && StellarAdvanced.PlayerEditor.Read(directSave).SkillPoints == 222 && directSave.Stacks().Single(delegate(StellarAdvanced.StackItem item) { return item.Alias == "Money_Nier"; }).Amount == 50, "direct saving collects simultaneous unprepared item, SP and Emil edits across windows");
                    check(File.Exists(main.Controller.LastBackupPath) && StellarAdvanced.SaveEditor.Hash(File.ReadAllBytes(main.Controller.LastBackupPath)) == hash, "direct Save copy retains a byte-identical permanent original backup");
                    main.Controller.ClearAllChanges(); main.Controller.LoadSave(sample); Application.DoEvents();
                    save.PerformClick();
                    check(File.ReadAllBytes(Path.Combine(copyFolder, "direct-2.sav")).SequenceEqual(File.ReadAllBytes(sample)), "Save copy can also create an unchanged copy instead of remaining grey");
                    int backupCount = Directory.GetFiles(Path.Combine(copyFolder, "CopiasSeguridad"), "*.sav").Length;
                    main.Controller.CopyDestination = delegate { return null; };
                    save.PerformClick();
                    check(Directory.GetFiles(Path.Combine(copyFolder, "CopiasSeguridad"), "*.sav").Length == backupCount, "cancelling the destination does not write a copy or create a backup");
                    main.Controller.CopyDestination = delegate(string name) { destinations++; return Path.Combine(copyFolder, "direct-" + destinations + ".sav"); };
                    main.Controller.QuantityRequests["Money_Nier_Dummy_1"] = 1; main.Controller.InvalidatePlan(); save.PerformClick();
                    check(destinations == 2 && main.Controller.CurrentPlan == null && main.Controller.CurrentStatus.StartsWith("Operation stopped:") && save.Enabled, "invalid edits are rejected before choosing a destination and Save copy remains available for correction");
                    main.Controller.ClearAllChanges(); main.Controller.LoadSave(sample); directPlayer.Close(); directObjects.Close(); Application.DoEvents();
                    gold.Value = 777777; stage.PerformClick();
                    check(main.Controller.QuantityRequests["BetaCrystal"] == 777777 && save.Enabled, "preparing gold updates shared edits and keeps Save copy available without requiring a separate review");
                    main.OpenObjects(); Application.DoEvents();
                    var objects = main.OwnedForms.OfType<QuantityForm>().Single();
                    var list = (ListView)objects.Controls.Find("Items", true).Single();
                    check(list.Items.Count > 0 && list.Items.Cast<ListViewItem>().All(delegate(ListViewItem item) { return ((StellarAdvanced.StackItem)item.Tag).Category == "Consumables"; }), "object window initially shows only consumables");
                    var potion = list.Items.Cast<ListViewItem>().Single(delegate(ListViewItem item) { return ((StellarAdvanced.StackItem)item.Tag).Alias == "HP_Potion"; });
                    potion.Selected = true; potion.Focused = true; Application.DoEvents();
                    ((NumericUpDown)objects.Controls.Find("ObjectAmount", true).Single()).Value = 123;
                    ((Button)objects.Controls.Find("StageObject", true).Single()).PerformClick();
                    check(main.Controller.QuantityRequests["HP_Potion"] == 123 && main.Controller.QuantityRequests.Count == 2, "preparing a consumable preserves the prepared gold in the shared session");
                    objects.Close(); Application.DoEvents();
                    check(!objects.IsDisposed && !objects.Visible, "closing the consumables window hides it without discarding edits");
                    main.OpenObjects(); Application.DoEvents();
                    check(objects.Visible && main.OwnedForms.OfType<QuantityForm>().Count() == 1 && main.Controller.QuantityRequests["HP_Potion"] == 123, "reopening consumables reuses one window and keeps the pending quantity");
                    main.OpenWardrobe(); Application.DoEvents();
                    var wardrobe = main.Controller.Controls.Find("Wardrobe", true).OfType<ListView>().Single();
                    wardrobe.Items.Cast<ListViewItem>().First(delegate(ListViewItem item) { return ((StellarAdvanced.CatalogItem)item.Tag).Alias == "BS_19"; }).Checked = true;
                    check(main.Controller.SelectedCount == 1 && main.Controller.QuantityRequests.Count == 2, "outfit selection and quantity edits coexist across windows");
                    main.Controller.Close(); Application.DoEvents();
                    check(!main.Controller.IsDisposed && !main.Controller.Visible && main.Controller.SelectedCount == 1, "closing the wardrobe keeps its selection for later reopening");
                    main.Controller.BuildPlan(); Application.DoEvents();
                    check(main.Controller.CurrentPlan != null && save.Enabled && main.Controller.CurrentPlan.UpdatedQuantities == 2, "combined review enables the main save button with both quantity changes");
                    gold.Value = 600000; stage.PerformClick();
                    check(main.Controller.CurrentPlan == null && save.Enabled, "a subsequent edit invalidates the old review while keeping direct saving available");
                    save.PerformClick();
                    var latestSave = new StellarAdvanced.SaveEditor(Path.Combine(copyFolder, "direct-3.sav"));
                    check(latestSave.Stacks().Single(delegate(StellarAdvanced.StackItem item) { return item.Alias == "BetaCrystal"; }).Amount == 600000, "saving after an edit rebuilds validation and writes the latest amount rather than an old review");
                    main.Controller.ClearAllChanges();
                    check(!main.Controller.HasPendingChanges && main.Controller.SelectedCount == 0 && main.Controller.QuantityRequests.Count == 0, "discard clears wardrobe, collections and quantities together");
                    main.Size = main.MinimumSize; Application.DoEvents();
                    check(save.Visible && save.RectangleToScreen(save.ClientRectangle).Bottom <= main.RectangleToScreen(main.ClientRectangle).Bottom, "save action remains inside the compact minimum window size");
                    main.Close(); Application.DoEvents(); check(objects.IsDisposed && main.Controller.IsDisposed, "closing the main editor disposes all auxiliary editor windows");
                }
                check(hash == StellarAdvanced.SaveEditor.Hash(File.ReadAllBytes(sample)), "compact UI interaction tests never modify the original save");
                report.AppendLine("COMPACT UI WORKFLOW TESTS PASSED. No new in-game validation is implied.");
            }
            catch (Exception error) { result = 1; report.AppendLine(error.ToString()); }
            File.WriteAllText(output, report.ToString(), new UTF8Encoding(true)); return result;
        }
    }
}
