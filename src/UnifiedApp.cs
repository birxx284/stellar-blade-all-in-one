using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace StellarUnified
{
    sealed class QuantityForm : Form
    {
        readonly StellarAdvanced.MainForm controller;
        readonly ListView items = new ListView { Name = "Items" };
        readonly TextBox filter = new TextBox { AccessibleName = "Search items" };
        readonly ComboBox category = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, AccessibleName = "Item group" };
        readonly NumericUpDown amount = new NumericUpDown { Name = "ObjectAmount", ThousandsSeparator = true, Maximum = 1000000 };
        readonly Label info = Ui.Label("Open a save in the main window"), status = Ui.Label("");
        readonly Button stage;
        bool selecting, dirty;
        List<StellarAdvanced.StackItem> stacks = new List<StellarAdvanced.StackItem>();
        public QuantityForm(StellarAdvanced.MainForm controller)
        {
            this.controller = controller;
            Ui.Style(this, "Consumables and items · Stellar Blade", new Size(690, 490), new Size(610, 420));
            var root = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(12), ColumnCount = 1, RowCount = 6 };
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 38)); root.RowStyles.Add(new RowStyle(SizeType.Absolute, 36)); root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 42)); root.RowStyles.Add(new RowStyle(SizeType.Absolute, 42)); root.RowStyles.Add(new RowStyle(SizeType.Absolute, 30)); Controls.Add(root);
            var header = Ui.Row(0, 190); header.Controls.Add(new Label { Text = "Consumables and items", Font = new Font("Segoe UI", 16, FontStyle.Bold), Dock = DockStyle.Fill }, 0, 0); info.TextAlign = ContentAlignment.MiddleRight; header.Controls.Add(info, 1, 0); root.Controls.Add(header, 0, 0);
            var search = Ui.Row(65, 0, 175); search.Controls.Add(Ui.Label("Search"), 0, 0); filter.Dock = DockStyle.Fill; search.Controls.Add(filter, 1, 0);
            category.Items.AddRange(new object[] { "Consumables", "All items", "Ammo", "Materials", "Currency", "DLC currency", "Fishing" }); category.SelectedIndex = 0; category.Dock = DockStyle.Fill; search.Controls.Add(category, 2, 0); root.Controls.Add(search, 0, 1);
            items.Dock = DockStyle.Fill; items.View = View.Details; items.FullRowSelect = true; items.MultiSelect = false; items.HideSelection = false; items.AccessibleName = "Save inventory";
            items.Columns.Add("Item", 300); items.Columns.Add("Current", 90); items.Columns.Add("Prepared", 90); items.Columns.Add("Charges", 75);
            items.Resize += delegate { items.Columns[0].Width = Math.Max(210, items.ClientSize.Width - 275); }; root.Controls.Add(items, 0, 2);
            var entry = Ui.Row(100, 0, 120, 100); entry.Controls.Add(Ui.Label("New quantity"), 0, 0); amount.Dock = DockStyle.Fill; entry.Controls.Add(amount, 1, 0);
            stage = Ui.Button("Prepare", delegate { StageAmount(); }, true); stage.Name = "StageObject"; entry.Controls.Add(stage, 2, 0); entry.Controls.Add(Ui.Button("Remove edit", delegate { RemoveAmount(); }, false), 3, 0); root.Controls.Add(entry, 0, 3);
            var actions = Ui.Row(0, 0, 100);
            actions.Controls.Add(Ui.Button("Review changes", delegate { controller.ReviewChanges(); }, false), 0, 0);
            actions.Controls.Add(Ui.Button("Clear quantities", delegate { controller.QuantityRequests.Clear(); controller.InvalidatePlan(); }, false), 1, 0);
            actions.Controls.Add(Ui.Button("Done", delegate { Close(); }, false), 2, 0); root.Controls.Add(actions, 0, 4);
            status.ForeColor = Ui.Muted; status.Text = "Prepared edits are kept. Save the combined copy from the main window."; root.Controls.Add(status, 0, 5);
            filter.TextChanged += delegate { Populate(); }; category.SelectedIndexChanged += delegate { Populate(); }; items.SelectedIndexChanged += delegate { SelectItem(); };
            amount.ValueChanged += delegate { if (!selecting && amount.Enabled) dirty = true; };
            controller.CommitRequested += CommitInput;
            controller.SaveLoaded += OnLoaded; controller.PlanChanged += Populate;
            FormClosed += delegate { controller.SaveLoaded -= OnLoaded; controller.PlanChanged -= Populate; controller.CommitRequested -= CommitInput; };
            OnLoaded(controller.CurrentEditor);
        }
        void OnLoaded(StellarAdvanced.SaveEditor editor)
        {
            stacks = editor == null ? new List<StellarAdvanced.StackItem>() : editor.Stacks(); filter.Clear();
            info.Text = editor == null ? "No save loaded" : stacks.Count + " available items"; Populate();
        }
        void Populate()
        {
            string previous = items.SelectedItems.Count == 1 ? ((StellarAdvanced.StackItem)items.SelectedItems[0].Tag).Alias : null;
            items.BeginUpdate(); items.Items.Clear();
            string group = category.SelectedIndex == 1 ? null : category.SelectedItem as string;
            foreach (var item in stacks)
            {
                if (group != null && item.Category != group) continue;
                if (filter.Text.Length > 0 && (item.Name + " " + item.Alias).IndexOf(filter.Text, StringComparison.CurrentCultureIgnoreCase) < 0) continue;
                int pending; bool changed = controller.QuantityRequests.TryGetValue(item.Alias, out pending);
                var row = new ListViewItem(item.Name) { Tag = item }; row.SubItems.Add(item.Amount.ToString("N0")); row.SubItems.Add(changed ? pending.ToString("N0") : "—"); row.SubItems.Add(item.Charges.ToString("N0"));
                if (changed) row.ForeColor = Ui.Accent; items.Items.Add(row); if (item.Alias == previous) row.Selected = true;
            }
            items.EndUpdate(); SelectItem();
        }
        void SelectItem()
        {
            dirty = false; bool selected = items.SelectedItems.Count == 1; amount.Enabled = stage.Enabled = selected;
            if (!selected) return;
            selecting = true; var item = (StellarAdvanced.StackItem)items.SelectedItems[0].Tag; amount.Maximum = item.Maximum;
            int pending; amount.Value = controller.QuantityRequests.TryGetValue(item.Alias, out pending) ? pending : (decimal)item.Amount; selecting = false;
        }
        void CommitInput() { if (dirty) StageAmount(); }
        void StageAmount()
        {
            if (items.SelectedItems.Count != 1) return; var item = (StellarAdvanced.StackItem)items.SelectedItems[0].Tag;
            if ((long)amount.Value == item.Amount) controller.QuantityRequests.Remove(item.Alias); else controller.QuantityRequests[item.Alias] = (int)amount.Value;
            dirty = false; controller.InvalidatePlan(); status.Text = "Quantity prepared. It will be saved with the selected gold and outfit changes.";
        }
        void RemoveAmount()
        { if (items.SelectedItems.Count == 1) { controller.QuantityRequests.Remove(((StellarAdvanced.StackItem)items.SelectedItems[0].Tag).Alias); controller.InvalidatePlan(); } }
    }
    sealed class UnifiedForm : Form
    {
        readonly StellarAdvanced.MainForm controller = new StellarAdvanced.MainForm();
        readonly Label loaded = Ui.Label("No save"), file = Ui.Label("Open your .sav to get started"), current = Ui.Label("Current gold: —"), pending = Ui.Label("No pending changes"), status = Ui.Label("The original is preserved. Each save creates a permanent backup.");
        readonly NumericUpDown gold = new NumericUpDown { Name = "GoldAmount", Maximum = 1000000, ThousandsSeparator = true };
        readonly TextBox changes = new TextBox { Name = "PendingChanges", Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, BackColor = Color.White, Dock = DockStyle.Fill };
        readonly Button stageGold, review, save;
        readonly ContextMenuStrip menu = new ContextMenuStrip();
        QuantityForm objects;
        PlayerForm player;
        EquipmentForm equipment;
        StellarAssist.MainForm live;
        StellarAdvanced.StackItem money;
        bool closing, closePending, goldUpdating, goldDirty;
        public StellarAdvanced.MainForm Controller { get { return controller; } }
        public UnifiedForm()
        {
            Ui.Style(this, "Stellar Blade · All in One 0.7.0", new Size(720, 520), new Size(660, 540)); StartPosition = FormStartPosition.CenterScreen;
            var root = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(16, 12, 16, 10), ColumnCount = 1, RowCount = 8 };
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 60)); root.RowStyles.Add(new RowStyle(SizeType.Absolute, 36)); root.RowStyles.Add(new RowStyle(SizeType.Absolute, 24));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 160)); root.RowStyles.Add(new RowStyle(SizeType.Absolute, 26)); root.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); root.RowStyles.Add(new RowStyle(SizeType.Absolute, 42)); root.RowStyles.Add(new RowStyle(SizeType.Absolute, 26)); Controls.Add(root);
            var heading = Ui.Row(0, 230); var name = new Panel { Dock = DockStyle.Fill };
            name.Controls.Add(new Label { Text = "STELLAR BLADE", AutoSize = true, Font = new Font("Segoe UI", 18, FontStyle.Bold), Location = new Point(0, 0) });
            name.Controls.Add(new Label { Text = "All in One · portable save editor · 0.7.0", AutoSize = true, ForeColor = Ui.Muted, Location = new Point(2, 33) }); heading.Controls.Add(name, 0, 0);
            loaded.TextAlign = ContentAlignment.MiddleRight; heading.Controls.Add(loaded, 1, 0); root.Controls.Add(heading, 0, 0);
            var toolbar = Ui.Row(0, 0, 0, 100); toolbar.Controls.Add(Ui.Button("Open .sav…", delegate { controller.OpenSave(); }, false), 0, 0);
            toolbar.Controls.Add(Ui.Button("Paste path…", delegate { controller.OpenSavePath(); }, false), 1, 0); toolbar.Controls.Add(Ui.Button("Find latest", delegate { controller.DetectSave(); }, false), 2, 0);
            var more = Ui.Button("More ▾", delegate(object sender, EventArgs e) { menu.Show((Control)sender, new Point(0, ((Control)sender).Height)); }, false); toolbar.Controls.Add(more, 3, 0); root.Controls.Add(toolbar, 0, 1);
            file.ForeColor = Ui.Muted; root.Controls.Add(file, 0, 2);
            var controls = Ui.Row(0, 0); controls.Margin = new Padding(0, 4, 0, 4);
            var moneyCard = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, BackColor = Color.White, Padding = new Padding(10), Margin = new Padding(2, 0, 6, 0) };
            moneyCard.RowStyles.Add(new RowStyle(SizeType.Absolute, 25)); moneyCard.RowStyles.Add(new RowStyle(SizeType.Absolute, 34)); moneyCard.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            current.Font = new Font(Font, FontStyle.Bold); moneyCard.Controls.Add(current, 0, 0); gold.Dock = DockStyle.Fill; gold.AccessibleName = "New gold amount"; moneyCard.Controls.Add(gold, 0, 1);
            stageGold = Ui.Button("Prepare gold", delegate { StageGold(); }, true); stageGold.Name = "StageGold"; moneyCard.Controls.Add(stageGold, 0, 2); controls.Controls.Add(moneyCard, 0, 0);
            var sections = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 5, Margin = new Padding(4, 0, 0, 0) };
            for (int i = 0; i < 5; i++) sections.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            var objectButton = Ui.Button("Consumables and items…", delegate { OpenObjects(); }, false); objectButton.Name = "OpenObjects"; sections.Controls.Add(objectButton, 0, 0);
            var outfitButton = Ui.Button("Outfits and collections…", delegate { OpenWardrobe(); }, false); outfitButton.Name = "OpenWardrobe"; sections.Controls.Add(outfitButton, 0, 1);
            var playerButton = Ui.Button("Player upgrades and Emil’s shop…", delegate { OpenPlayer(); }, false); playerButton.Name = "OpenPlayer"; sections.Controls.Add(playerButton, 0, 2);
            var equipmentButton = Ui.Button("NG+ equipment and cores…", delegate { OpenEquipment(); }, false); equipmentButton.Name = "OpenEquipment"; sections.Controls.Add(equipmentButton, 0, 3);
            sections.Controls.Add(Ui.Button("In-game controls · manual…", delegate { OpenLive(); }, false), 0, 4); controls.Controls.Add(sections, 1, 0); root.Controls.Add(controls, 0, 3);
            pending.Font = new Font(Font, FontStyle.Bold); root.Controls.Add(pending, 0, 4); root.Controls.Add(changes, 0, 5);
            var actions = Ui.Row(0, 0, 0); actions.Controls.Add(Ui.Button("Discard changes", delegate { controller.ClearAllChanges(); ReloadMoney(); }, false), 0, 0);
            review = Ui.Button("Review changes", delegate { controller.ReviewChanges(); RefreshState(); }, false); review.Name = "Review"; actions.Controls.Add(review, 1, 0);
            save = Ui.Button("Save copy…", delegate { controller.SavePlan(); RefreshState(); }, true); save.Name = "SaveCopy"; actions.Controls.Add(save, 2, 0); root.Controls.Add(actions, 0, 6);
            status.ForeColor = Ui.Muted; root.Controls.Add(status, 0, 7);
            menu.Items.Add("Backups", null, delegate { OpenBackups(); }); menu.Items.Add("Opening report", null, delegate { Ui.Review(this, controller.LoadReport ?? "No save is loaded.", "Opening report"); });
            menu.Items.Add("Help", null, delegate { Ui.Review(this, "Open your .sav file. Prepare gold, items, player upgrades or outfits, review the changes, and save a copy with the game closed.\r\n\r\nPlayer upgrades select HP/Beta levels (3 cores per level). Grant missing cores is optional and does not mark world bodies as collected. Burst capacity can be increased with expansion gear: choose inventory only or an existing unlocked gear slot. Existing core limits are retained; Tachy maximum capacity is not edited. Unspent SP and Emil’s Stellar Tears are also editable.\r\n\r\nAll windows share the pending changes and keep them when closed.\r\n\r\nEach save creates a permanent original backup in CopiasSeguridad beside the destination. Original files and previous copies are preserved.\r\n\r\nSupports GVAS and EVAS v1 with valid integrity checks. Physical chests and quests are unchanged. NieR requires existing DLC save data.\r\n\r\nIn-game controls require manual calibration each session; see their Help tab.\r\n\r\nThe user reported successful operation of 0.3.3 on their PC. Other game versions are not guaranteed.\r\n\r\nSource and licenses are included. Saves are never uploaded and Internet access is not required.", "Help · Stellar Blade"); });
            gold.ValueChanged += delegate { if (!goldUpdating && gold.Enabled) goldDirty = true; };
            controller.CommitRequested += delegate { if (goldDirty) StageGold(); };
            controller.Owner = this; controller.SaveLoaded += delegate { ReloadMoney(); RefreshState(); }; controller.PlanChanged += RefreshState;
            controller.FormClosing += delegate(object sender, FormClosingEventArgs e) { if (!closing) { e.Cancel = true; controller.Hide(); } };
            FormClosing += OnClosing; RefreshState(); KeyPreview = true; KeyDown += delegate(object sender, KeyEventArgs e) { if (e.Control && e.KeyCode == Keys.O) { e.SuppressKeyPress = true; controller.OpenSave(); } };
        }
        void ReloadMoney()
        {
            goldUpdating = true; goldDirty = false;
            money = controller.CurrentEditor == null ? null : controller.CurrentEditor.Stacks().Find(delegate(StellarAdvanced.StackItem item) { return item.Alias == "BetaCrystal"; });
            gold.Enabled = stageGold.Enabled = money != null;
            current.Text = money == null ? "Current gold: —" : "Current gold: " + money.Amount.ToString("N0");
            if (money != null) { gold.Maximum = money.Maximum; int prepared; gold.Value = controller.QuantityRequests.TryGetValue(money.Alias, out prepared) ? prepared : (decimal)money.Amount; }
            goldUpdating = false;
        }
        void StageGold()
        {
            if (money == null) return;
            if ((long)gold.Value == money.Amount) controller.QuantityRequests.Remove(money.Alias); else controller.QuantityRequests[money.Alias] = (int)gold.Value;
            goldDirty = false; controller.InvalidatePlan();
        }
        void RefreshState()
        {
            bool hasSave = controller.HasLoadedSave;
            if (!hasSave) { gold.Enabled = stageGold.Enabled = false; current.Text = "Current gold: —"; }
            if (hasSave && money != null && !goldDirty) { int prepared; goldUpdating = true; gold.Value = controller.QuantityRequests.TryGetValue(money.Alias, out prepared) ? prepared : (decimal)money.Amount; goldUpdating = false; }
            loaded.Text = hasSave ? "SAVE LOADED · " + (controller.CurrentEditor.Archive.HasEvas ? "EVAS" : "GVAS") : "NO SAVE";
            loaded.ForeColor = hasSave ? Ui.Accent : Ui.Muted;
            file.Text = hasSave ? Path.GetFileName(controller.CurrentEditor.SourcePath) : "Open your .sav file, or drop it onto the executable.";
            pending.Text = controller.HasPendingChanges ? "Prepared edits · " + controller.QuantityRequests.Count + " items · " + (controller.PlayerChanges.Any ? "player · " : "") + controller.EquipmentChanges.Items.Count + " equipment · " + controller.SelectedCount + " cosmetics" : "No pending changes";
            changes.Text = controller.PendingDescription(); review.Enabled = hasSave && controller.HasPendingChanges; save.Enabled = hasSave;
            if (controller.CurrentPlan != null) pending.Text = "Changes reviewed · ready to save";
            status.Text = controller.CurrentPlan != null ? "Close the game before saving. A permanent backup will be created." : "Edit values, then Save copy. Validation and a permanent backup are automatic.";
            if (controller.CurrentStatus.StartsWith("Saved:", StringComparison.Ordinal) || controller.CurrentStatus.StartsWith("Could not save:", StringComparison.Ordinal) || controller.CurrentStatus.StartsWith("Operation stopped:", StringComparison.Ordinal)) status.Text = controller.CurrentStatus;
        }
        public void OpenObjects()
        {
            if (objects == null || objects.IsDisposed)
            {
                objects = new QuantityForm(controller); objects.Owner = this;
                objects.FormClosing += delegate(object sender, FormClosingEventArgs e) { if (!closing) { e.Cancel = true; objects.Hide(); } };
            }
            objects.Show(); objects.BringToFront();
        }
        public void OpenPlayer()
        {
            if (player == null || player.IsDisposed)
            {
                player = new PlayerForm(controller); player.Owner = this;
                player.FormClosing += delegate(object sender, FormClosingEventArgs e) { if (!closing) { e.Cancel = true; player.Hide(); } };
            }
            player.Show(); player.BringToFront();
        }
        public void OpenWardrobe() { controller.Show(); controller.BringToFront(); }
        public void OpenEquipment()
        {
            if (equipment == null || equipment.IsDisposed)
            {
                equipment = new EquipmentForm(controller); equipment.Owner = this;
                equipment.FormClosing += delegate(object sender, FormClosingEventArgs e) { if (!closing) { e.Cancel = true; equipment.Hide(); } };
            }
            equipment.Show(); equipment.BringToFront();
        }
        void OpenLive()
        {
            if (live == null || live.IsDisposed)
            {
                live = new StellarAssist.MainForm(); live.Owner = this;
                live.FormClosed += delegate { if (closePending && !IsDisposed) BeginInvoke((Action)delegate { Close(); }); };
            }
            live.Show(); live.BringToFront();
        }
        void OpenBackups()
        {
            if (String.IsNullOrEmpty(controller.LastBackupPath)) { Ui.Review(this, "Saving creates CopiasSeguridad beside the modified copy. Previous backups are preserved.", "Backups"); return; }
            Process.Start("explorer.exe", "/select,\"" + controller.LastBackupPath + "\"");
        }
        void OnClosing(object sender, FormClosingEventArgs e)
        {
            if (live != null && !live.IsDisposed) { closePending = true; live.Close(); if (!live.IsDisposed) { e.Cancel = true; return; } }
            closing = true;
            if (objects != null && !objects.IsDisposed) objects.Close();
            if (player != null && !player.IsDisposed) player.Close();
            if (equipment != null && !equipment.IsDisposed) equipment.Close();
            if (!controller.IsDisposed) controller.Close();
        }
        public void Preview(string output, string sample, int section)
        {
            ShowInTaskbar = false; Opacity = 0; Show(); controller.ShowLoadErrors = false; if (sample != null) controller.LoadSave(sample);
            Form target = this;
            if (section == 1) { OpenWardrobe(); target = controller; }
            if (section == 2) { OpenObjects(); target = objects; }
            if (section == 3) { OpenLive(); target = live; }
            if (section == 4) { OpenPlayer(); target = player; }
            if (section == 5) { OpenWardrobe(); controller.FocusCosmetic("REF_AngelFusion"); target = controller; }
            if (section == 6) { OpenEquipment(); target = equipment; }
            Application.DoEvents(); using (var bitmap = new Bitmap(target.Width, target.Height)) { target.DrawToBitmap(bitmap, new Rectangle(0, 0, target.Width, target.Height)); bitmap.Save(output); } Close();
        }
    }
    static class Program
    {
        [STAThread] static int Main(string[] args)
        {
            try
            {
                if (args.Length > 0 && args[0] == "--fixture") return StellarAssist.SelfTests.Fixture();
                if (args.Length == 2 && args[0] == "--live-test") return StellarAssist.SelfTests.Run(args[1]);
                if (args.Length == 3 && args[0] == "--test") return StellarAdvanced.SaveTests.Run(args[1], args[2]);
                Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
                if (args.Length == 3 && args[0] == "--open-test") return StellarAdvanced.OpenTests.Run(args[1], args[2]);
                if (args.Length == 3 && args[0] == "--capacity-test") return CapacityTests.Run(args[1], args[2]);
                if (args.Length == 3 && args[0] == "--player-test") return PlayerTests.Run(args[1], args[2]);
                if (args.Length == 3 && args[0] == "--ui-test") return CompactTests.Run(args[1], args[2]);
                if (args.Length == 3 && args[0] == "--guide-test") return GuideTests.Run(args[1], args[2]);
                if (args.Length == 3 && args[0] == "--equipment-test") return EquipmentTests.Run(args[1], args[2]);
                if (args.Length > 1 && args[0] == "--preview")
                { using (var form = new UnifiedForm()) form.Preview(args[1], args.Length > 2 ? args[2] : null, args.Length > 3 ? int.Parse(args[3]) : 0); return 0; }
                using (var form = new UnifiedForm())
                {
                    string initial = args.Length == 1 && String.Equals(Path.GetExtension(args[0]), ".sav", StringComparison.OrdinalIgnoreCase) ? args[0] : args.Length == 2 && args[0] == "--open" ? args[1] : null;
                    if (initial != null) form.Shown += delegate { form.Controller.LoadSave(initial); };
                    Application.Run(form);
                }
                return 0;
            }
            catch (Exception error)
            { if (args.Length > 0) { Console.Error.WriteLine(error.ToString()); return 1; } MessageBox.Show(error.Message, "Stellar Blade · All in One", MessageBoxButtons.OK, MessageBoxIcon.Error); return 1; }
        }
    }
}
