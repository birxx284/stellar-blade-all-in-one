using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using StellarAdvanced;

namespace StellarUnified
{
    static class EquipmentTests
    {
        static Value Items(Archive a) { return a.Field("DataMap_SBItem").Data.Entries.Single(delegate(Pair p) { return p.Key.Text == "Item"; }).Item; }
        static string Alias(Value entry) { Node n = entry.Field("ItemInstance"); return n == null || n.Field("ItemAlias") == null ? "" : n.Require("ItemAlias").Text; }
        public static int Run(string sample, string output)
        {
            var report = new StringBuilder(); int result = 0;
            Action<bool, string> check = delegate(bool ok, string text) { if (!ok) throw new Exception("FAIL: " + text); report.AppendLine("PASS: " + text); };
            Action<Action, string> rejects = delegate(Action action, string text) { bool rejected = false; try { action(); } catch (InvalidDataException) { rejected = true; } check(rejected, text); };
            string folder = Path.Combine(Path.GetDirectoryName(output), "equipment-fixture-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(folder);
            string hash = SaveEditor.Hash(File.ReadAllBytes(sample));
            try
            {
                var original = new SaveEditor(sample);
                check(EquipmentEditor.Catalog.Count == 178 && EquipmentEditor.Catalog.Select(delegate(EquipmentItem i) { return i.Alias; }).Distinct().Count() == 178, "catalog contains 178 unique equipment identifiers");
                check(EquipmentEditor.Catalog.All(delegate(EquipmentItem i) { return original.Owned().Contains(i.Alias); }), "every supported gear/exospine identifier is independently present in the research inventory");
                check(EquipmentEditor.Catalog.Count(delegate(EquipmentItem i) { return i.Category == "Exospines"; }) == 24 && EquipmentEditor.Catalog.Count(delegate(EquipmentItem i) { return i.Mk2; }) == 89, "catalog separates 24 exospines, 154 gear items and 89 Mk2 variants");
                foreach (bool evas in new[] { false, true })
                {
                    var fixture = Archive.Parse(original.Original);
                    if (evas && !fixture.HasEvas) { byte[] raw = fixture.Serialize(), wrapped = new byte[raw.Length + 12]; Buffer.BlockCopy(Encoding.ASCII.GetBytes("EVAS"), 0, wrapped, 0, 4); wrapped[4] = 1; Buffer.BlockCopy(raw, 0, wrapped, 8, raw.Length); EvasEnvelope.UpdateChecksum(wrapped); fixture = Archive.Parse(wrapped); }
                    Value actor = PlayerEditor.Actor(fixture), items = Items(fixture); var inventory = items.Require("Inventory").Elements;
                    var equipped = new HashSet<string>(actor.Require("Equipment").Entries.Where(delegate(Pair p) { return p.Item.Field("ItemAlias") != null; }).Select(delegate(Pair p) { return p.Item.Require("ItemAlias").Text; }));
                    inventory.RemoveAll(delegate(Value v) { return Alias(v) == "WeaponCore" || (Alias(v).EndsWith("_MK2", StringComparison.Ordinal) && !equipped.Contains(Alias(v))); });
                    items.Require("ItemOtaineSet").Elements.RemoveAll(delegate(Value v) { return v.Text == "WeaponCore" || (v.Text != null && v.Text.EndsWith("_MK2", StringComparison.Ordinal) && !equipped.Contains(v.Text)); });
                    foreach (Value v in inventory) if (Alias(v).StartsWith("PT_", StringComparison.Ordinal)) v.Require("ItemInstance").Require("StatLevel").Set(1);
                    foreach (Pair p in actor.Require("Equipment").Entries) if (p.Item.Field("ItemAlias") != null && p.Item.Require("ItemAlias").Text.StartsWith("PT_", StringComparison.Ordinal)) p.Item.Require("StatLevel").Set(1);
                    actor.Require("LevelArray").Elements[1].Set(4); actor.Require("LevelArray").Elements[2].Set(4); actor.Require("LevelArray").Elements[4].Set(15);
                    string beforePath = Path.Combine(folder, "before-" + evas + ".sav"); File.WriteAllBytes(beforePath, fixture.Serialize()); var editor = new SaveEditor(beforePath);
                    var equipment = new EquipmentRequest(); foreach (var i in EquipmentEditor.Catalog.Where(delegate(EquipmentItem i) { return i.Mk2; })) equipment.Items[i.Alias] = i.Maximum;
                    equipment.Supplies["WeaponCore"] = 100;
                    var player = new PlayerRequest { BodyLevel = 9, BetaLevel = 9, WeaponLevel = 40, GrantMissingCores = true, SkillPoints = 999 };
                    var plan = editor.Build(new CatalogItem[0], false, false, null, player, equipment);
                    string afterPath = Path.Combine(folder, "after-" + evas + ".sav"); editor.SaveCopy(plan, afterPath); var after = new SaveEditor(afterPath);
                    check(equipment.Items.All(delegate(KeyValuePair<string, int> pair) { var found = EquipmentEditor.Find(after.Inventory, pair.Key); return found.Count == 1 && found[0].Require("StatLevel").Integer == pair.Value && after.Owned().Contains(pair.Key); }), "all Mk2 gear and level-3 exospines are granted once with ownership (" + evas + ")");
                    check(PlayerEditor.Read(after).Weapon.Level == 40 && PlayerEditor.Read(after).Weapon.Pieces == 75, "weapon level 15 -> 40 spends 25 cores from the newly granted 100-core stack (" + evas + ")");
                    check(PlayerEditor.Read(after).Body.Level == 9 && PlayerEditor.Read(after).Beta.Level == 9 && PlayerEditor.Read(after).SkillPoints == 999, "HP, Beta and SP changes compose with equipment and weapon grants (" + evas + ")");
                    var equippedSpines = PlayerEditor.Actor(after.Archive).Require("Equipment").Entries.Where(delegate(Pair p) { return p.Item.Field("ItemAlias") != null && p.Item.Require("ItemAlias").Text.StartsWith("PT_", StringComparison.Ordinal); });
                    check(equippedSpines.All(delegate(Pair p) { Value actual = EquipmentEditor.Find(after.Inventory, p.Item.Require("ItemAlias").Text).Single(); return actual.Require("StatLevel").Integer == 3 && p.Item.Require("StatLevel").Integer == 3 && actual.Require("SavedGuid").Raw.SequenceEqual(p.Item.Require("SavedGuid").Raw); }), "already-equipped exospine copies retain real identity and matching upgraded levels (" + evas + ")");
                    PlayerEditor.VerifyUnrelated(editor.Archive, after.Archive, player, equipment);
                    foreach (Node n in editor.Archive.Fields.Where(delegate(Node n) { return n.Name != "DataMap_SBActor" && n.Name != "DataMap_SBItem" && n.Name != "SBAchievement"; }))
                        if (!Writer.NodeBytes(n).SequenceEqual(Writer.NodeBytes(after.Archive.Field(n.Name)))) throw new Exception("Unrelated section changed: " + n.Name);
                    check(true, "world, story, quests, skills, NG+ snapshots and unrelated player fields stay unchanged (" + evas + ")");
                    foreach (Node n in Items(editor.Archive).Fields.Where(delegate(Node n) { return n.Name != "Inventory" && n.Name != "ItemOtaineSet"; }))
                        if (!Writer.NodeBytes(n).SequenceEqual(Writer.NodeBytes(Items(after.Archive).Field(n.Name)))) throw new Exception("Unrelated item field changed: " + n.Name);
                    check(true, "world pockets, event items and inventory slot metadata stay byte-identical (" + evas + ")");
                    check(after.Archive.HasEvas == evas && File.ReadAllBytes(plan.BackupPath).SequenceEqual(editor.Original), "format integrity and full permanent original backup are preserved (" + evas + ")");
                    var identities = after.Inventory.Where(delegate(Value v) { return EquipmentEditor.Catalog.Any(delegate(EquipmentItem i) { return i.Alias == Alias(v); }); }).Select(delegate(Value v) { return Convert.ToBase64String(v.Require("ItemInstance").Require("SavedGuid").Raw); }).ToList();
                    check(identities.Distinct().Count() == identities.Count, "all supported equipment has distinct saved identities (" + evas + ")");
                    var repeat = after.Build(new CatalogItem[0], false, false, null, null, new EquipmentRequestWithItems(equipment).Request);
                    check(repeat.Bytes.SequenceEqual(after.Original) && repeat.AddedItems == 0, "repeating equipment grants is byte-identical and does not create duplicates (" + evas + ")");
                    var exact = editor.Build(new CatalogItem[0], false, false, new Dictionary<string, int> { { "WeaponCore", 30 } }, new PlayerRequest { WeaponLevel = 40 }, equipment);
                    string exactPath = Path.Combine(folder, "exact-" + evas + ".sav"); File.WriteAllBytes(exactPath, exact.Bytes);
                    check(PlayerEditor.Read(new SaveEditor(exactPath)).Weapon.Pieces == 5, "explicit Weapon Core quantity takes precedence over minimum stock before upgrade costs (" + evas + ")");
                    rejects(delegate { editor.Build(new CatalogItem[0], false, false, null, new PlayerRequest { WeaponLevel = 40 }); }, "weapon upgrade without held cores or an explicit grant is rejected (" + evas + ")");
                    rejects(delegate { editor.Build(new CatalogItem[0], false, false, null, new PlayerRequest { WeaponLevel = 41, GrantMissingCores = true }); }, "weapon power cannot exceed the NG+ level-40 cap (" + evas + ")");
                    if (!evas)
                    {
                        var invalid = new EquipmentRequest(); invalid.Items["PT_Guard_MK2"] = 4;
                        rejects(delegate { editor.Build(new CatalogItem[0], false, false, null, null, invalid); }, "unsupported exospine levels are rejected");
                        invalid.Items.Clear(); invalid.Items["Gear_MaxBurstGauge_Common_MK2"] = 1;
                        rejects(delegate { editor.Build(new CatalogItem[0], false, false, null, null, invalid); }, "unverified Common Burst gear cannot be fabricated");
                        invalid.Items.Clear(); invalid.Supplies["BurstCore"] = 100;
                        rejects(delegate { editor.Build(new CatalogItem[0], false, false, null, null, invalid); }, "story Burst Core is outside the material grant allowlist");
                        invalid.Supplies.Clear(); invalid.Supplies["WeaponCore"] = -1;
                        rejects(delegate { editor.Build(new CatalogItem[0], false, false, null, null, invalid); }, "negative material quantities are rejected");
                        invalid.Supplies["WeaponCore"] = 10000;
                        rejects(delegate { editor.Build(new CatalogItem[0], false, false, null, null, invalid); }, "material grants above the supported stock cap are rejected");
                        var baseRun = Archive.Parse(editor.Original); baseRun.Field("DataMap_int32").Data.Entries.Single(delegate(Pair p) { return p.Key.Text == "NewGamePlusPlayCount"; }).Item.Set(0);
                        PlayerEditor.Actor(baseRun).Require("LevelArray").Elements[4].Set(10); string basePath = Path.Combine(folder, "base-run.sav"); File.WriteAllBytes(basePath, baseRun.Serialize()); var baseEditor = new SaveEditor(basePath);
                        check(!EquipmentEditor.IsNgPlus(baseEditor) && PlayerEditor.Read(baseEditor).Weapon.Maximum == 15, "normal saves retain the official weapon-level-15 cap");
                        rejects(delegate { baseEditor.Build(new CatalogItem[0], false, false, null, null, equipment); }, "Mk2 grants do not bypass the NG+ requirement");
                        rejects(delegate { baseEditor.Build(new CatalogItem[0], false, false, null, new PlayerRequest { WeaponLevel = 16, GrantMissingCores = true }); }, "normal weapon upgrades cannot bypass their level cap");
                        var noTemplate = Archive.Parse(editor.Original); Items(noTemplate).Require("Inventory").Elements.RemoveAll(delegate(Value v) { return Alias(v).StartsWith("PT_", StringComparison.Ordinal); });
                        string noTemplatePath = Path.Combine(folder, "no-template.sav"); File.WriteAllBytes(noTemplatePath, noTemplate.Serialize()); var noTemplateEditor = new SaveEditor(noTemplatePath);
                        var one = new EquipmentRequest(); one.Items["PT_Guard_MK2"] = 3;
                        rejects(delegate { noTemplateEditor.Build(new CatalogItem[0], false, false, null, null, one); }, "missing genuine exospine templates cause a clear rejection");
                        var duplicate = Archive.Parse(editor.Original); var duplicateItems = Items(duplicate).Require("Inventory").Elements; duplicateItems.Add(Writer.CloneValue(duplicateItems.First(delegate(Value v) { return Alias(v) == "PT_Down_MK2"; })));
                        string duplicatePath = Path.Combine(folder, "duplicate.sav"); File.WriteAllBytes(duplicatePath, duplicate.Serialize()); var duplicateEditor = new SaveEditor(duplicatePath); one.Items.Clear(); one.Items["PT_Down_MK2"] = 3;
                        rejects(delegate { duplicateEditor.Build(new CatalogItem[0], false, false, null, null, one); }, "ambiguous duplicate exospines are rejected without deleting existing items");
                        var lower = Archive.Parse(editor.Original);
                        foreach (Value v in Items(lower).Require("Inventory").Elements) if (Alias(v) == "PT_Down_MK2") v.Require("ItemInstance").Require("ItemAlias").Set("pt_down_mk2");
                        foreach (Pair p in PlayerEditor.Actor(lower).Require("Equipment").Entries) if (p.Item.Field("ItemAlias") != null && p.Item.Require("ItemAlias").Text == "PT_Down_MK2") p.Item.Require("ItemAlias").Set("pt_down_mk2");
                        string lowerPath = Path.Combine(folder, "lowercase.sav"); File.WriteAllBytes(lowerPath, lower.Serialize()); var lowerEditor = new SaveEditor(lowerPath);
                        var lowerPlan = lowerEditor.Build(new CatalogItem[0], false, false, null, null, one);
                        check(EquipmentEditor.Find(Items(Archive.Parse(lowerPlan.Bytes)).Require("Inventory").Elements, "PT_Down_MK2").Single().Require("StatLevel").Integer == 3, "lowercase Unreal aliases preserve equipped identity while upgrading the matching exospine");
                        using (var main = new UnifiedForm())
                        {
                            main.ShowInTaskbar = false; main.Show(); main.OpenEquipment(); Application.DoEvents(); var form = main.OwnedForms.OfType<EquipmentForm>().Single();
                            var chill = (Button)form.Controls.Find("ChillPreset", true).Single(); var list = (ListView)form.Controls.Find("EquipmentList", true).Single();
                            check(!chill.Enabled && list.Items.Count == 89, "new window browses Mk2 equipment offline while its preset stays disabled");
                            main.Controller.ShowLoadErrors = main.Controller.ShowSaveMessages = false; main.Controller.LoadSave(beforePath); Application.DoEvents();
                            chill.PerformClick(); Application.DoEvents();
                            check(main.Controller.EquipmentChanges.Items.Count == 89 && main.Controller.EquipmentChanges.Supplies.Count == 15 && main.Controller.PlayerChanges.WeaponLevel == 40 && main.Controller.PlayerChanges.GrantMissingCores, "Chill preset stages Mk2 equipment, materials and verified NG+ upgrades in one shared session");
                            check(((NumericUpDown)main.Controls.Find("GoldAmount", true).Single()).Value == 500000, "preset gold appears immediately in the main editor field");
                            form.Close(); main.OpenEquipment(); Application.DoEvents();
                            check(!form.IsDisposed && form.Visible && main.OwnedForms.OfType<EquipmentForm>().Count() == 1, "closing and reopening the equipment window retains pending requests");
                            ((NumericUpDown)form.Controls.Find("WeaponLevel", true).Single()).Value = 30;
                            ((NumericUpDown)form.Controls.Find("MaterialMinimum", true).Single()).Value = 120;
                            string uiCopy = Path.Combine(folder, "ui-copy.sav"); main.Controller.CopyDestination = delegate { return uiCopy; };
                            ((Button)main.Controls.Find("SaveCopy", true).Single()).PerformClick(); Application.DoEvents();
                            check(File.Exists(uiCopy), "the real Save copy button accepts combined equipment and unprepared core/weapon input");
                            var ui = new SaveEditor(uiCopy);
                            check(PlayerEditor.Read(ui).Weapon.Level == 30 && PlayerEditor.Read(ui).Weapon.Pieces == 105, "direct saving collects both dirty minimum stock and weapon level before spending cores");
                            check(ui.Stacks().Single(delegate(StackItem i) { return i.Alias == "BetaCrystal"; }).Amount == 500000 && PlayerEditor.Read(ui).SkillPoints == 999 && EquipmentEditor.Find(ui.Inventory, "PT_Guard_MK2").Single().Require("StatLevel").Integer == 3, "gold, SP and Mk2 equipment are saved together");
                            main.Controller.ClearAllChanges(); Application.DoEvents();
                            check(!main.Controller.HasPendingChanges && ((NumericUpDown)form.Controls.Find("WeaponLevel", true).Single()).Value == 15, "discard clears equipment/material requests and restores weapon input to the loaded save");
                            main.Controller.LoadSave(basePath); Application.DoEvents(); check(!chill.Enabled, "loading a normal-run save disables the NG+ preset");
                            form.Size = form.MinimumSize; Application.DoEvents(); var stockButton = (Button)form.Controls.Find("PrepareMaterial", true).Single();
                            check(stockButton.RectangleToScreen(stockButton.ClientRectangle).Bottom <= form.RectangleToScreen(form.ClientRectangle).Bottom, "material actions remain inside the minimum window size");
                            main.Close(); Application.DoEvents(); check(form.IsDisposed, "closing the main editor disposes the equipment window");
                        }
                    }
                }
                check(hash == SaveEditor.Hash(File.ReadAllBytes(sample)), "all equipment workflows leave the original research save unchanged");
                report.AppendLine("NG+ EQUIPMENT WORKFLOW TESTS PASSED. Running-game validation remains pending.");
            }
            catch (Exception error) { result = 1; report.AppendLine(error.ToString()); }
            File.WriteAllText(output, report.ToString(), new UTF8Encoding(true)); return result;
        }
        sealed class EquipmentRequestWithItems
        {
            public readonly EquipmentRequest Request = new EquipmentRequest();
            public EquipmentRequestWithItems(EquipmentRequest input) { foreach (var p in input.Items) Request.Items[p.Key] = p.Value; }
        }
    }
}
