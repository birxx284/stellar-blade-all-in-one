using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using StellarAdvanced;

namespace StellarUnified
{
    static class CapacityTests
    {
        static Value Items(Archive a) { return a.Field("DataMap_SBItem").Data.Entries.Single(delegate(Pair p) { return p.Key.Text == "Item"; }).Item; }
        public static int Run(string sample, string output)
        {
            var report = new StringBuilder(); int result = 0;
            Action<bool, string> check = delegate(bool ok, string label) { if (!ok) throw new Exception("FAIL: " + label); report.AppendLine("PASS: " + label); };
            Action<Action, string> rejects = delegate(Action action, string label) { bool rejected = false; try { action(); } catch (InvalidDataException) { rejected = true; } check(rejected, label); };
            string folder = Path.Combine(Path.GetDirectoryName(output), "capacity-fixture-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(folder);
            string sourceHash = SaveEditor.Hash(File.ReadAllBytes(sample));
            try
            {
                foreach (bool evas in new[] { false, true })
                {
                    var fixture = Archive.Parse(File.ReadAllBytes(sample));
                    if (evas && !fixture.HasEvas)
                    {
                        byte[] raw = fixture.Serialize(), wrapped = new byte[fixture.Serialize().Length + 12];
                        Buffer.BlockCopy(Encoding.ASCII.GetBytes("EVAS"), 0, wrapped, 0, 4); wrapped[4] = 1; Buffer.BlockCopy(raw, 0, wrapped, 8, raw.Length); EvasEnvelope.UpdateChecksum(wrapped); fixture = Archive.Parse(wrapped);
                    }
                    PlayerEditor.Actor(fixture).Field("LevelArray").Data.Elements[1].Set(4);
                    PlayerEditor.Actor(fixture).Field("LevelArray").Data.Elements[2].Set(4);
                    Value items = Items(fixture);
                    items.Field("Inventory").Data.Elements.RemoveAll(delegate(Value v) { return PlayerEditor.Instance(v, "BodyCore") != null || PlayerEditor.Instance(v, "BetaCore") != null || PlayerEditor.Instance(v, "Gear_MaxBurstGauge_Rare") != null; });
                    items.Field("ItemOtaineSet").Data.Elements.RemoveAll(delegate(Value v) { return v.Text == "Gear_MaxBurstGauge_Rare"; });
                    string path = Path.Combine(folder, "before-" + evas + ".sav"); File.WriteAllBytes(path, fixture.Serialize()); var editor = new SaveEditor(path);
                    rejects(delegate { editor.Build(new CatalogItem[0], false, false, null, new PlayerRequest { BodyLevel = 6 }); }, "unfunded HP upgrade still requires explicit grant selection (" + evas + ")");
                    var request = new PlayerRequest { BodyLevel = 6, BetaLevel = 7, GrantMissingCores = true, BurstGearAlias = "Gear_MaxBurstGauge_Rare", BurstGearSlot = 1 };
                    var plan = editor.Build(new CatalogItem[0], false, false, null, request);
                    string afterPath = Path.Combine(folder, "after-" + evas + ".sav"); editor.SaveCopy(plan, afterPath); var after = new SaveEditor(afterPath); var player = PlayerEditor.Read(after);
                    check(player.Body.Level == 6 && player.Beta.Level == 7 && player.Body.Pieces == 0 && player.Beta.Pieces == 0, "explicit grant upgrades HP/Beta with zero held or missing core stacks (" + evas + ")");
                    check(plan.Changes.Any(delegate(string s) { return s.Contains("Granted and spent 6"); }) && plan.Changes.Any(delegate(string s) { return s.Contains("Granted and spent 9"); }), "review declares exact missing Body/Beta credits rather than claiming world pickups (" + evas + ")");
                    Value gear = after.Inventory.Select(delegate(Value v) { return PlayerEditor.Instance(v, request.BurstGearAlias); }).Single(delegate(Value v) { return v != null; });
                    Value equipped = PlayerEditor.Actor(after.Archive).Field("Equipment").Data.Entries.Single(delegate(Pair p) { return p.Key.Integer == 1; }).Item;
                    check(Writer.ValueBytes(gear).SequenceEqual(Writer.ValueBytes(equipped)) && plan.AddedItems == 1, "Burst gear is granted once and equipped using its actual inventory identity (" + evas + ")");
                    PlayerEditor.VerifyUnrelated(editor.Archive, after.Archive, request); check(true, "other gear slots and player stats remain byte-identical (" + evas + ")");
                    foreach (string section in new[] { "DataMap_SBEnv", "DataMap_SBQuest", "DataMap_SBAcquisitionSkill", "NewGamePlusBaseData" })
                        check(Writer.NodeBytes(editor.Archive.Field(section)).SequenceEqual(Writer.NodeBytes(after.Archive.Field(section))), "preserved " + section + " (" + evas + ")");
                    check(after.Archive.HasEvas == evas && SaveEditor.Hash(File.ReadAllBytes(plan.BackupPath)) == SaveEditor.Hash(editor.Original), "format integrity and permanent original backup are retained (" + evas + ")");
                    var repeat = after.Build(new CatalogItem[0], false, false, null, request);
                    check(repeat.Bytes.SequenceEqual(plan.Bytes) && repeat.AddedItems == 0, "reapplying granted upgrades and equipped Burst gear is idempotent (" + evas + ")");
                    rejects(delegate { after.Build(new CatalogItem[0], false, false, null, new PlayerRequest { BurstGearAlias = request.BurstGearAlias, BurstGearSlot = 2 }); }, "the same Burst gear cannot be duplicated across equipped slots (" + evas + ")");
                    rejects(delegate { editor.Build(new CatalogItem[0], false, false, null, new PlayerRequest { BodyLevel = 10, GrantMissingCores = true }); }, "grant mode cannot bypass the game's maximum core level (" + evas + ")");
                    rejects(delegate { editor.Build(new CatalogItem[0], false, false, null, new PlayerRequest { BurstGearAlias = "BurstCore" }); }, "story BurstCore cannot be granted as expansion gear (" + evas + ")");
                    rejects(delegate { editor.Build(new CatalogItem[0], false, false, null, new PlayerRequest { BurstGearAlias = request.BurstGearAlias, BurstGearSlot = 99 }); }, "invalid Burst equipment slots are rejected (" + evas + ")");
                    var inventoryOnly = editor.Build(new CatalogItem[0], false, false, null, new PlayerRequest { BurstGearAlias = request.BurstGearAlias });
                    check(Writer.NodeBytes(editor.Archive.Field("DataMap_SBActor")).SequenceEqual(Writer.NodeBytes(Archive.Parse(inventoryOnly.Bytes).Field("DataMap_SBActor"))), "inventory-only Burst grant does not pretend to change maximum capacity without equipping (" + evas + ")");
                    if (!evas)
                    {
                        var locked = Archive.Parse(editor.Original); PlayerEditor.Actor(locked).Field("LevelArray").Data.Elements[5].Set(0);
                        string lockedPath = Path.Combine(folder, "locked-slots.sav"); File.WriteAllBytes(lockedPath, locked.Serialize()); var lockedEditor = new SaveEditor(lockedPath);
                        check(BurstGear.Slots(lockedEditor.Archive).SequenceEqual(new[] { 1 }), "only unlocked gear slots are offered, even when locked slot records exist");
                        rejects(delegate { lockedEditor.Build(new CatalogItem[0], false, false, null, new PlayerRequest { BurstGearAlias = request.BurstGearAlias, BurstGearSlot = 2 }); }, "Burst equipment cannot bypass a locked gear slot");
                        var partial = Archive.Parse(editor.Original); Value partialItems = Items(partial);
                        Value core = SaveEditor.AddStack(partialItems, partialItems.Field("Inventory").Data.Elements.First(delegate(Value v) { return PlayerEditor.Instance(v, "WeaponCore") != null; }), "BetaCore");
                        core.Field("ItemCount").Data.Set(2); core.Field("ItemChargeCount").Data.Set(2);
                        string partialPath = Path.Combine(folder, "partial-cores.sav"); File.WriteAllBytes(partialPath, partial.Serialize()); var partialEditor = new SaveEditor(partialPath);
                        var partialPlan = partialEditor.Build(new CatalogItem[0], false, false, null, new PlayerRequest { BetaLevel = 5, GrantMissingCores = true });
                        check(partialPlan.Changes.Any(delegate(string c) { return c.Contains("Granted and spent 1 missing core credits"); }), "grant mode consumes held fragments and adds only the one missing core credit");
                        using (var main = new UnifiedForm())
                        {
                            main.ShowInTaskbar = false; main.Show(); main.Controller.ShowLoadErrors = false; main.Controller.ShowSaveMessages = false;
                            main.Controller.LoadSave(path); main.OpenPlayer(); Application.DoEvents(); var form = main.OwnedForms.OfType<PlayerForm>().Single();
                            var body = (NumericUpDown)form.Controls.Find("BodyLevel", true).Single(); var beta = (NumericUpDown)form.Controls.Find("BetaLevel", true).Single();
                            check(body.Enabled && beta.Enabled && body.Maximum == 9 && !((CheckBox)form.Controls.Find("GrantMissingCores", true).Single()).Checked, "HP/Beta selectors are enabled without spare cores; grants remain opt-in");
                            body.Value = 6; beta.Value = 7; ((CheckBox)form.Controls.Find("GrantMissingCores", true).Single()).Checked = true;
                            var burst = (ComboBox)form.Controls.Find("BurstGear", true).Single();
                            burst.SelectedItem = burst.Items.Cast<BurstGearOption>().Single(delegate(BurstGearOption i) { return i.Alias == request.BurstGearAlias; });
                            ((ComboBox)form.Controls.Find("BurstGearSlot", true).Single()).SelectedIndex = 1;
                            string uiPath = Path.Combine(folder, "ui-capacity.sav"); main.Controller.CopyDestination = delegate { return uiPath; };
                            ((Button)main.Controls.Find("SaveCopy", true).Single()).PerformClick(); var uiAfter = new SaveEditor(uiPath);
                            check(PlayerEditor.Read(uiAfter).Body.Level == 6 && PlayerEditor.Read(uiAfter).Beta.Level == 7 && PlayerEditor.Actor(uiAfter.Archive).Field("Equipment").Data.Entries.Single(delegate(Pair p) { return p.Key.Integer == 1; }).Item.Field("ItemAlias").Data.Text == request.BurstGearAlias, "Save copy commits HP, Beta, grant selection and Burst equipment directly from the new window");
                            main.Close();
                        }
                    }
                }
                check(sourceHash == SaveEditor.Hash(File.ReadAllBytes(sample)), "capacity checks leave the reference save unchanged");
                report.AppendLine("CAPACITY TESTS PASSED. Structural/UI checks; running-game validation is still required.");
            }
            catch (Exception e) { result = 1; report.AppendLine(e.ToString()); }
            File.WriteAllText(output, report.ToString(), new UTF8Encoding(true)); return result;
        }
    }
}
