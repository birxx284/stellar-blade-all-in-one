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
                using (var main = new UnifiedForm())
                {
                    main.ShowInTaskbar = false; main.Show(); main.Controller.ShowLoadErrors = false; Application.DoEvents();
                    check(main.ClientSize.Width <= 740 && main.ClientSize.Height <= 540, "main editor opens in a compact window");
                    var gold = (NumericUpDown)main.Controls.Find("GoldAmount", true).Single();
                    var stage = (Button)main.Controls.Find("StageGold", true).Single(); var save = (Button)main.Controls.Find("SaveCopy", true).Single();
                    check(!gold.Enabled && !stage.Enabled && !save.Enabled, "editing and saving stay disabled before a valid save is loaded");
                    main.Controller.LoadSave(sample); Application.DoEvents();
                    check(gold.Enabled && stage.Enabled && main.Controller.HasLoadedSave, "opening a valid save populates the compact gold control");
                    gold.Value = 777777; stage.PerformClick();
                    check(main.Controller.QuantityRequests["BetaCrystal"] == 777777 && !save.Enabled, "preparing gold updates shared edits and requires review before saving");
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
                    check(main.Controller.CurrentPlan == null && !save.Enabled, "a subsequent edit invalidates the old review in every window");
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
