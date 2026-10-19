using System;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using StellarAdvanced;

namespace StellarUnified
{
    sealed class EquipmentForm : Form
    {
        readonly StellarAdvanced.MainForm controller;
        readonly ListView list = new ListView { Name = "EquipmentList", View = View.Details, FullRowSelect = true, MultiSelect = false, HideSelection = false, Dock = DockStyle.Fill };
        readonly TextBox search = new TextBox { Name = "EquipmentSearch", Dock = DockStyle.Fill };
        readonly ComboBox category = new ComboBox { Name = "EquipmentCategory", DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill };
        readonly ComboBox material = new ComboBox { Name = "MaterialChoice", DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill };
        readonly NumericUpDown level = new NumericUpDown { Name = "ExospineLevel", Minimum = 1, Maximum = 3, Value = 3, Dock = DockStyle.Fill };
        readonly NumericUpDown stock = new NumericUpDown { Name = "MaterialMinimum", Maximum = 9999, Value = 100, Dock = DockStyle.Fill };
        readonly NumericUpDown weapon = new NumericUpDown { Name = "WeaponLevel", Maximum = 40, Dock = DockStyle.Fill };
        readonly CheckBox grant = new CheckBox { Name = "GrantWeaponCredits", Text = "Grant missing upgrade credits", Dock = DockStyle.Fill };
        readonly Label info = Ui.Label("Open a save"), status = Ui.Label("Source chests and bodies stay collectible; pickup blocking is not verified."), materialInfo = Ui.Label(""), weaponInfo = Ui.Label("");
        readonly Button preset, allMk2, all, prepare, prepareStock, prepareWeapon;
        PlayerSnapshot snapshot;
        bool ng, updating, weaponDirty, stockDirty;
        public EquipmentForm(StellarAdvanced.MainForm controller)
        {
            this.controller = controller;
            Ui.Style(this, "NG+ equipment and cores - Stellar Blade", new Size(850, 690), new Size(800, 690));
            var root = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(12), ColumnCount = 1, RowCount = 10 };
            foreach (int height in new[] { 40, 44, 36 }) root.RowStyles.Add(new RowStyle(SizeType.Absolute, height));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            foreach (int height in new[] { 40, 54, 52, 62, 40, 30 }) root.RowStyles.Add(new RowStyle(SizeType.Absolute, height)); Controls.Add(root);
            var header = Ui.Row(0, 260); header.Controls.Add(new Label { Text = "NG+ equipment and cores", Font = new Font("Segoe UI", 17, FontStyle.Bold), Dock = DockStyle.Fill }, 0, 0); info.TextAlign = ContentAlignment.MiddleRight; header.Controls.Add(info, 1, 0); root.Controls.Add(header, 0, 0);
            var shortcuts = Ui.Row(0, 0, 0);
            preset = Ui.Button("Chill NG+ preset", delegate { ChillPreset(); }, true); preset.Name = "ChillPreset"; shortcuts.Controls.Add(preset, 0, 0);
            allMk2 = Ui.Button("All Mk2 equipment", delegate { QueueAll(true); }, false); allMk2.Name = "AllMk2"; shortcuts.Controls.Add(allMk2, 1, 0);
            all = Ui.Button("All supported equipment", delegate { QueueAll(false); }, false); all.Name = "AllEquipment"; shortcuts.Controls.Add(all, 2, 0); root.Controls.Add(shortcuts, 0, 1);
            var filters = Ui.Row(60, 0, 170); filters.Controls.Add(Ui.Label("Search"), 0, 0); filters.Controls.Add(search, 1, 0);
            category.Items.AddRange(new object[] { "Mk2 equipment", "All equipment", "Exospines", "Gear", "Prepared" }); category.SelectedIndex = 0; filters.Controls.Add(category, 2, 0); root.Controls.Add(filters, 0, 2);
            list.Columns.Add("Equipment", 380); list.Columns.Add("Current", 100); list.Columns.Add("Prepared", 100); list.Resize += delegate { list.Columns[0].Width = Math.Max(250, list.ClientSize.Width - 225); }; root.Controls.Add(list, 0, 3);
            var selection = Ui.Row(130, 95, 0, 110); selection.Controls.Add(Ui.Label("Exospine level"), 0, 0); selection.Controls.Add(level, 1, 0);
            prepare = Ui.Button("Prepare selected item", delegate { PrepareItem(); }, false); prepare.Name = "PrepareEquipment"; selection.Controls.Add(prepare, 2, 0);
            selection.Controls.Add(Ui.Button("Remove edit", delegate { if (list.SelectedItems.Count == 1) { controller.EquipmentChanges.Items.Remove(((EquipmentItem)list.SelectedItems[0].Tag).Alias); controller.InvalidatePlan(); } }, false), 3, 0); root.Controls.Add(selection, 0, 4);
            var materials = Ui.Row(130, 0, 95, 170); materials.Controls.Add(Ui.Label("Core / material stock"), 0, 0); materials.Controls.Add(material, 1, 0); materials.Controls.Add(stock, 2, 0);
            foreach (string alias in EquipmentEditor.Materials) material.Items.Add(new EquipmentItem { Alias = alias, Name = MaterialName(alias) }); material.SelectedIndex = 0;
            prepareStock = Ui.Button("Set minimum stock", delegate { PrepareStock(); }, false); prepareStock.Name = "PrepareMaterial"; materials.Controls.Add(prepareStock, 3, 0); root.Controls.Add(materials, 0, 5);
            var weapons = Ui.Row(130, 95, 0, 170); weapons.Controls.Add(Ui.Label("Weapon power level"), 0, 0); weapons.Controls.Add(weapon, 1, 0); weapons.Controls.Add(grant, 2, 0);
            prepareWeapon = Ui.Button("Prepare weapon", delegate { PrepareWeapon(); }, false); prepareWeapon.Name = "PrepareWeapon"; weapons.Controls.Add(prepareWeapon, 3, 0); root.Controls.Add(weapons, 0, 6);
            var progress = Ui.Row(0, 0); progress.Controls.Add(materialInfo, 0, 0); progress.Controls.Add(weaponInfo, 1, 0); root.Controls.Add(progress, 0, 7);
            var actions = Ui.Row(0, 0, 110); actions.Controls.Add(Ui.Button("Review changes", delegate { controller.ReviewChanges(); }, false), 0, 0);
            actions.Controls.Add(Ui.Button("Clear equipment / stock", delegate { stockDirty = false; controller.EquipmentChanges.Clear(); controller.InvalidatePlan(); }, false), 1, 0); actions.Controls.Add(Ui.Button("Done", delegate { Close(); }, false), 2, 0); root.Controls.Add(actions, 0, 8); status.AutoEllipsis = true; root.Controls.Add(status, 0, 9);
            search.TextChanged += delegate { Populate(); }; category.SelectedIndexChanged += delegate { Populate(); }; list.SelectedIndexChanged += delegate { SelectItem(); };
            material.SelectedIndexChanged += delegate { stockDirty = false; RefreshStock(); };
            stock.ValueChanged += delegate { if (!updating && stock.Enabled) stockDirty = true; };
            weapon.ValueChanged += delegate { if (!updating && weapon.Enabled) weaponDirty = true; }; grant.CheckedChanged += delegate { if (!updating && grant.Enabled) weaponDirty = true; };
            controller.SaveLoaded += OnLoaded; controller.PlanChanged += OnChanged; controller.CommitRequested += Commit;
            FormClosed += delegate { controller.SaveLoaded -= OnLoaded; controller.PlanChanged -= OnChanged; controller.CommitRequested -= Commit; };
            OnLoaded(controller.CurrentEditor);
        }
        static string MaterialName(string alias)
        {
            switch (alias) { case "WeaponCore": return "Weapon Cores"; case "WeaponCore_Damaged": return "Damaged Weapon Cores"; case "DroneCore": return "Drone upgrade modules"; case "TumblerCore": return "Tumbler expansion material"; case "GearCore": return "Omnibolts / gear slots"; case "ExospineCore": return "Exospine expansion module"; }
            StackItem item; return InventoryCatalog.Items.TryGetValue(alias, out item) ? item.Name : alias;
        }
        void OnLoaded(SaveEditor editor)
        {
            snapshot = null; ng = false; weaponDirty = stockDirty = false;
            if (editor != null) try { snapshot = PlayerEditor.Read(editor); ng = EquipmentEditor.IsNgPlus(editor); } catch (Exception error) { status.Text = error.Message; }
            info.Text = editor == null ? "No save - browse only" : snapshot == null ? "Unsupported upgrade data" : ng ? "NG+ detected - Mk2 available" : "Normal run - base equipment only";
            preset.Enabled = allMk2.Enabled = snapshot != null && ng; all.Enabled = editor != null;
            weapon.Enabled = snapshot != null && snapshot.Weapon.Level <= snapshot.Weapon.Maximum; grant.Enabled = prepareWeapon.Enabled = weapon.Enabled; stock.Enabled = prepareStock.Enabled = editor != null;
            OnChanged();
        }
        void OnChanged()
        {
            if (!controller.HasPendingChanges) weaponDirty = stockDirty = false;
            Populate(); RefreshStock();
            if (!weaponDirty)
            {
                updating = true; weapon.Minimum = 0; weapon.Maximum = snapshot == null ? 40 : Math.Max(snapshot.Weapon.Level, snapshot.Weapon.Maximum); weapon.Minimum = snapshot == null ? 0 : snapshot.Weapon.Level;
                weapon.Value = snapshot == null ? 0 : controller.PlayerChanges.WeaponLevel ?? snapshot.Weapon.Level; grant.Checked = controller.PlayerChanges.GrantMissingCores; updating = false;
            }
            weaponInfo.Text = snapshot == null ? "Open a compatible save to upgrade the weapon." : "Weapon: " + snapshot.Weapon.Level + " / " + snapshot.Weapon.Maximum + "; " + snapshot.Weapon.Pieces + " cores held.\r\n1 core per level. Missing credits can be granted.";
        }
        void Populate()
        {
            if (prepare == null) return;
            string selected = list.SelectedItems.Count == 1 ? ((EquipmentItem)list.SelectedItems[0].Tag).Alias : null;
            list.BeginUpdate(); list.Items.Clear();
            foreach (EquipmentItem item in EquipmentEditor.Catalog)
            {
                int pending; bool queued = controller.EquipmentChanges.Items.TryGetValue(item.Alias, out pending);
                if (category.SelectedIndex == 0 && !item.Mk2 || category.SelectedIndex == 2 && item.Category != "Exospines" || category.SelectedIndex == 3 && item.Category != "Gear" || category.SelectedIndex == 4 && !queued) continue;
                if ((item.Name + " " + item.Alias).IndexOf(search.Text, StringComparison.OrdinalIgnoreCase) < 0) continue;
                var found = controller.CurrentEditor == null ? null : EquipmentEditor.Find(controller.CurrentEditor.Inventory, item.Alias);
                string current = found == null ? "No save" : found.Count == 0 ? "Not owned" : found.Count > 1 ? "Duplicate" : "Lv " + found[0].Require("StatLevel").Integer;
                var row = new ListViewItem(item.Name) { Tag = item }; row.SubItems.Add(current); row.SubItems.Add(queued ? "Lv " + pending : "-");
                if (item.Mk2 && !ng) row.ForeColor = Ui.Muted; else if (queued) row.ForeColor = Ui.Accent;
                list.Items.Add(row); if (item.Alias == selected) row.Selected = true;
            }
            list.EndUpdate(); SelectItem();
        }
        void SelectItem()
        {
            EquipmentItem item = list.SelectedItems.Count == 1 ? (EquipmentItem)list.SelectedItems[0].Tag : null;
            prepare.Enabled = controller.HasLoadedSave && item != null && (!item.Mk2 || ng); level.Enabled = prepare.Enabled && item.Maximum == 3;
        }
        void RefreshStock()
        {
            if (material.SelectedItem == null) return; var item = (EquipmentItem)material.SelectedItem;
            var found = controller.CurrentEditor == null ? null : EquipmentEditor.Find(controller.CurrentEditor.Inventory, item.Alias); int pending;
            materialInfo.Text = item.Name + ": " + (found == null ? "No save" : found.Count == 0 ? "0 - can be created" : found.Count > 1 ? "Duplicate - resolve in game" : found[0].Require("ItemCount").Integer.ToString("N0")) + "\r\nMinimum stock grants before exact quantity edits and upgrade costs.";
            if (!stockDirty) { updating = true; stock.Value = controller.EquipmentChanges.Supplies.TryGetValue(item.Alias, out pending) ? pending : 100; updating = false; }
        }
        void PrepareItem()
        {
            if (list.SelectedItems.Count != 1 || !controller.HasLoadedSave) return; var item = (EquipmentItem)list.SelectedItems[0].Tag; if (item.Mk2 && !ng) return;
            controller.EquipmentChanges.Items[item.Alias] = item.Maximum == 3 ? (int)level.Value : 1; controller.InvalidatePlan(); status.Text = "Equipment prepared. Save a copy, then equip new items in game. Exospine upgrades are granted without module costs.";
        }
        void PrepareStock()
        { if (!controller.HasLoadedSave || material.SelectedItem == null) return; controller.EquipmentChanges.Supplies[((EquipmentItem)material.SelectedItem).Alias] = (int)stock.Value; stockDirty = false; controller.InvalidatePlan(); }
        void PrepareWeapon()
        {
            if (snapshot == null || !weapon.Enabled) return; controller.PlayerChanges.WeaponLevel = (int)weapon.Value == snapshot.Weapon.Level ? (int?)null : (int)weapon.Value;
            controller.PlayerChanges.GrantMissingCores = grant.Checked; weaponDirty = false; controller.InvalidatePlan();
        }
        void Commit() { if (stockDirty) PrepareStock(); if (weaponDirty) PrepareWeapon(); }
        void QueueAll(bool mk2Only)
        {
            if (!controller.HasLoadedSave || (mk2Only && !ng)) return;
            foreach (EquipmentItem item in EquipmentEditor.Catalog.Where(delegate(EquipmentItem i) { return (!mk2Only || i.Mk2) && (!i.Mk2 || ng); })) controller.EquipmentChanges.Items[item.Alias] = item.Maximum;
            controller.InvalidatePlan(); status.Text = "Equipment prepared at supported levels. Save a copy, then choose your loadout in game.";
        }
        void ChillPreset()
        {
            if (snapshot == null || !ng) return;
            if (snapshot.Body.Level > snapshot.Body.Maximum || snapshot.Beta.Level > snapshot.Beta.Maximum || snapshot.Weapon.Level > snapshot.Weapon.Maximum) { status.Text = "Custom levels above normal NG+ limits are unsupported by this preset."; return; }
            QueueAll(true);
            controller.PlayerChanges.BodyLevel = snapshot.Body.Maximum; controller.PlayerChanges.BetaLevel = snapshot.Beta.Maximum; controller.PlayerChanges.WeaponLevel = snapshot.Weapon.Maximum;
            controller.PlayerChanges.GrantMissingCores = true; controller.PlayerChanges.SkillPoints = Math.Max(999, snapshot.SkillPoints);
            foreach (string alias in EquipmentEditor.Materials) controller.EquipmentChanges.Supplies[alias] = alias.EndsWith("Core", StringComparison.Ordinal) || alias == "WeaponCore_Damaged" ? 100 : 1000;
            foreach (StackItem item in controller.CurrentEditor.Stacks())
            {
                if (item.Alias == "BetaCrystal") controller.QuantityRequests[item.Alias] = (int)Math.Max(item.Amount, 500000);
                if (item.Category == "Consumables" || item.Category == "Ammo") controller.QuantityRequests[item.Alias] = (int)Math.Max(item.Amount, item.Category == "Ammo" ? 999 : 99);
            }
            stockDirty = weaponDirty = false; controller.InvalidatePlan();
            status.Text = "Chill prepared: Mk2 equipment, upgrades and supplies. Source chests / bodies remain collectible. Save copy to apply.";
        }
    }
}
