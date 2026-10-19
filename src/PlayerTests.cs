using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using StellarAdvanced;

namespace StellarUnified
{
    static class PlayerTests
    {
        public static int Run(string sample, string output)
        {
            var report = new StringBuilder(); int result = 0;
            Action<bool, string> check = delegate(bool ok, string label) { if (!ok) throw new Exception("FAIL: " + label); report.AppendLine("PASS: " + label); };
            Action<Action, string> rejects = delegate(Action action, string label) { bool rejected = false; try { action(); } catch (InvalidDataException) { rejected = true; } check(rejected, label); };
            string folder = Path.Combine(Path.GetDirectoryName(output), "player-fixture-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(folder);
            string hash = SaveEditor.Hash(File.ReadAllBytes(sample));
            try
            {
                var original = new SaveEditor(sample); var initial = PlayerEditor.Read(original);
                check(initial.Body.Level == 9 && initial.Beta.Level == 9 && initial.SkillPoints == 32, "public save resolves HP/Beta core levels and unspent SP separately");
                check(initial.Beta.Pieces == 1, "case-insensitive Unreal core name resolves the lowercase betacore stack");
                var nochange = original.Build(new CatalogItem[0], false, false, null, new PlayerRequest { BodyLevel = 9, BetaLevel = 9, SkillPoints = 32 });
                check(nochange.Bytes.SequenceEqual(original.Original), "unchanged player settings preserve every byte");
                rejects(delegate { original.Build(new CatalogItem[0], false, false, null, new PlayerRequest { BetaLevel = 10 }); }, "unsupported capacity above the NG+ limit is rejected");
                rejects(delegate { original.Build(new CatalogItem[0], false, false, null, new PlayerRequest { SkillPoints = -1 }); }, "negative skill points are rejected");
                rejects(delegate { original.Build(new CatalogItem[0], false, false, null, new PlayerRequest { SkillPoints = 10000 }); }, "out-of-range skill points are rejected");
                foreach (bool evas in new[] { false, true })
                {
                    var fixture = Archive.Parse(original.Original);
                    if (evas && !fixture.HasEvas)
                    {
                        byte[] raw = fixture.Serialize(); byte[] wrapped = new byte[raw.Length + 12];
                        Buffer.BlockCopy(Encoding.ASCII.GetBytes("EVAS"), 0, wrapped, 0, 4); wrapped[4] = 1; Buffer.BlockCopy(raw, 0, wrapped, 8, raw.Length); EvasEnvelope.UpdateChecksum(wrapped); fixture = Archive.Parse(wrapped);
                    }
                    PlayerEditor.Actor(fixture).Field("LevelArray").Data.Elements[1].Set(4);
                    PlayerEditor.Actor(fixture).Field("LevelArray").Data.Elements[2].Set(4);
                    Value items = fixture.Field("DataMap_SBItem").Data.Entries.Single(delegate(Pair p) { return p.Key.Text == "Item"; }).Item;
                    Value body = SaveEditor.AddStack(items, items.Field("Inventory").Data.Elements.First(delegate(Value v) { return PlayerEditor.Instance(v, "WeaponCore") != null; }), "BodyCore"); body.Field("ItemCount").Data.Set(7); body.Field("ItemChargeCount").Data.Set(7);
                    Value beta = items.Field("Inventory").Data.Elements.Select(delegate(Value v) { return PlayerEditor.Instance(v, "BetaCore"); }).Single(delegate(Value v) { return v != null; });
                    beta.Field("ItemCount").Data.Set(8); beta.Field("ItemChargeCount").Data.Set(8);
                    string path = Path.Combine(folder, evas ? "before-evas.sav" : "before-gvas.sav"); File.WriteAllBytes(path, fixture.Serialize());
                    var editor = new SaveEditor(path);
                    rejects(delegate { editor.Build(new CatalogItem[0], false, false, null, new PlayerRequest { BodyLevel = 7 }); }, "insufficient collected cores prevent an unfunded upgrade (" + evas + ")");
                    var quantities = new Dictionary<string, int> { { "BetaCrystal", 765432 }, { "HP_Potion", 87 }, { "Money_Nier", 99 } };
                    var request = new PlayerRequest { BodyLevel = 6, BetaLevel = 5, SkillPoints = 500 };
                    var plan = editor.Build(new CatalogItem[0], false, false, quantities, request);
                    string afterPath = Path.Combine(folder, evas ? "after-evas.sav" : "after-gvas.sav"); editor.SaveCopy(plan, afterPath);
                    var after = new SaveEditor(afterPath); var player = PlayerEditor.Read(after);
                    check(player.Body.Level == 6 && player.Body.Pieces == 1 && player.Beta.Level == 5 && player.Beta.Pieces == 5 && player.SkillPoints == 500, "HP/Beta upgrades consume 3 cores per level and set actual unspent points (" + evas + ")");
                    check(3 * player.Body.Level + player.Body.Pieces == 19 && 3 * player.Beta.Level + player.Beta.Pieces == 20, "invested and remaining core totals are conserved (" + evas + ")");
                    check(after.Stacks().Single(delegate(StackItem s) { return s.Alias == "Money_Nier"; }).Amount == 99 && after.Stacks().Single(delegate(StackItem s) { return s.Alias == "BetaCrystal"; }).Amount == 765432, "Emil currency is created from the same-save currency schema and coexists with gold (" + evas + ")");
                    check(after.Archive.HasEvas == evas, "output retains the original GVAS/EVAS format (" + evas + ")");
                    check(File.ReadAllBytes(plan.BackupPath).SequenceEqual(editor.Original) && File.ReadAllBytes(path).SequenceEqual(editor.Original), "combined edit retains a verified permanent original backup (" + evas + ")");
                    PlayerEditor.VerifyUnrelated(editor.Archive, after.Archive);
                    check(true, "all other actor data, XP, SP level, equipment and current gauges remain byte-identical (" + evas + ")");
                    foreach (string section in new[] { "DataMap_SBEnv", "DataMap_SBQuest", "DataMap_SBAcquisitionSkill", "NewGamePlusBaseData", "OnlineAccountInfo" })
                        check(Writer.NodeBytes(editor.Archive.Field(section)).SequenceEqual(Writer.NodeBytes(after.Archive.Field(section))), "preserved " + section + " (" + evas + ")");
                    var repeat = after.Build(new CatalogItem[0], false, false, quantities, request);
                    check(repeat.Bytes.SequenceEqual(plan.Bytes), "repeating the same reviewed edit is idempotent (" + evas + ")");
                }
                rejects(delegate { original.Build(new CatalogItem[0], false, false, null, new PlayerRequest { BodyLevel = 8 }); }, "lowering an upgrade is rejected because refunded cores may be automatically reapplied");
                var withoutNier = Archive.Parse(original.Original);
                Value noItems = withoutNier.Field("DataMap_SBItem").Data.Entries.Single(delegate(Pair p) { return p.Key.Text == "Item"; }).Item;
                noItems.Field("Inventory").Data.Elements.RemoveAll(delegate(Value v) { var n = v.Field("ItemInstance"); return n != null && n.Require("ItemAlias").Text.IndexOf("Nier", StringComparison.OrdinalIgnoreCase) >= 0; });
                noItems.Field("ItemOtaineSet").Data.Elements.RemoveAll(delegate(Value v) { return v.Text.IndexOf("Nier", StringComparison.OrdinalIgnoreCase) >= 0; });
                string noNierPath = Path.Combine(folder, "no-nier.sav"); File.WriteAllBytes(noNierPath, withoutNier.Serialize()); var noNier = new SaveEditor(noNierPath);
                check(!noNier.Stacks().Any(delegate(StackItem s) { return s.Alias == "Money_Nier"; }), "Stellar Tears stay unavailable without existing NieR data");
                rejects(delegate { noNier.Build(new CatalogItem[0], false, false, new Dictionary<string, int> { { "Money_Nier", 10 } }); }, "backend rejects forced NieR currency creation without DLC save data");
                using (var main = new UnifiedForm())
                {
                    main.ShowInTaskbar = false; main.Show(); main.Controller.ShowLoadErrors = false; main.Controller.LoadSave(sample); main.OpenPlayer(); Application.DoEvents();
                    var form = main.OwnedForms.OfType<PlayerForm>().Single();
                    check(((NumericUpDown)form.Controls.Find("BodyLevel", true).Single()).Minimum == 9, "UI retains existing upgrade minimums and the supported game limit");
                    ((NumericUpDown)form.Controls.Find("SkillPoints", true).Single()).Value = 123;
                    ((NumericUpDown)form.Controls.Find("StellarTears", true).Single()).Value = 40;
                    ((Button)form.Controls.Find("StagePlayer", true).Single()).PerformClick();
                    check(main.Controller.PlayerChanges.SkillPoints == 123 && main.Controller.QuantityRequests["Money_Nier"] == 40, "player window stages points and shop edits in the shared session");
                    main.Controller.BuildPlan(); check(main.Controller.CurrentPlan != null, "player window changes build a combined verified review");
                    form.Close(); main.OpenPlayer(); check(!form.IsDisposed && main.OwnedForms.OfType<PlayerForm>().Count() == 1 && main.Controller.PlayerChanges.Any, "closing and reopening preserves one player window and its changes");
                    main.Controller.ClearAllChanges(); check(!main.Controller.HasPendingChanges && !main.Controller.PlayerChanges.Any && ((NumericUpDown)form.Controls.Find("BodyLevel", true).Single()).Value == 9, "discard resets both shared requests and player controls");
                    main.Controller.LoadSave(noNierPath); check(!form.Controls.Find("StellarTears", true).Single().Enabled, "loading another save refreshes DLC availability in the open player window");
                    main.Close(); check(form.IsDisposed, "main close disposes the player window");
                }
                check(hash == SaveEditor.Hash(File.ReadAllBytes(sample)), "all player tests preserve the reference save");
                report.AppendLine("PLAYER UPGRADE TESTS PASSED. Structural and UI validation; no game is installed on this PC.");
            }
            catch (Exception e) { result = 1; report.AppendLine(e.ToString()); }
            File.WriteAllText(output, report.ToString(), new UTF8Encoding(true)); return result;
        }
    }
}
