using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using StellarAdvanced;

namespace StellarUnified
{
    sealed class PlayerForm : Form
    {
        readonly StellarAdvanced.MainForm controller;
        readonly NumericUpDown body = Number("BodyLevel"), beta = Number("BetaLevel"), points = Number("SkillPoints"), tears = Number("StellarTears");
        readonly Label bodyInfo = Ui.Label("Unavailable"), betaInfo = Ui.Label("Unavailable"), pointInfo = Ui.Label("Unavailable"), tearInfo = Ui.Label("Unavailable");
        readonly Label balance = Ui.Label(""), status = Ui.Label("");
        readonly CheckBox grant = new CheckBox { Name = "GrantMissingCores", Text = "Grant missing cores for the selected HP / Beta upgrades", Dock = DockStyle.Fill };
        readonly ComboBox burst = new ComboBox { Name = "BurstGear", DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill };
        readonly ComboBox slot = new ComboBox { Name = "BurstGearSlot", DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill };
        readonly Button stage;
        List<int> slots = new List<int>();
        PlayerSnapshot snapshot;
        StackItem tearStack;
        bool updating, dirty;
        static NumericUpDown Number(string name) { return new NumericUpDown { Name = name, Dock = DockStyle.Fill, Maximum = 9999, ThousandsSeparator = true }; }
        public PlayerForm(StellarAdvanced.MainForm controller)
        {
            this.controller = controller;
            Ui.Style(this, "HP, Beta, Burst and Emil's shop - Stellar Blade", new Size(790, 610), new Size(740, 640));
            var root = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(16), RowCount = 8, ColumnCount = 1 };
            foreach (int height in new[] { 40, 32, 246, 32, 58 }) root.RowStyles.Add(new RowStyle(SizeType.Absolute, height));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); root.RowStyles.Add(new RowStyle(SizeType.Absolute, 40)); root.RowStyles.Add(new RowStyle(SizeType.Absolute, 32)); Controls.Add(root);
            root.Controls.Add(new Label { Text = "Health and energy capacity", Font = new Font("Segoe UI", 18, FontStyle.Bold), Dock = DockStyle.Fill }, 0, 0);
            root.Controls.Add(Ui.Label("HP / Beta: permanent upgrade levels. Burst: expansion gear, not a core level."), 0, 1);
            var grid = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 7 };
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 180)); grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 120));
            for (int i = 0; i < 7; i++) grid.RowStyles.Add(new RowStyle(SizeType.Percent, 100f / 7));
            grid.Controls.Add(Ui.Label("Capacity / setting"), 0, 0); grid.Controls.Add(Ui.Label("Current progress"), 1, 0); grid.Controls.Add(Ui.Label("Target level"), 2, 0);
            AddRow(grid, 1, "Health (HP) upgrades", bodyInfo, body); AddRow(grid, 2, "Beta energy upgrades", betaInfo, beta);
            grid.Controls.Add(Ui.Label("Burst expansion gear"), 0, 3); grid.Controls.Add(burst, 1, 3); grid.SetColumnSpan(burst, 2);
            grid.Controls.Add(Ui.Label("Burst gear destination"), 0, 4); grid.Controls.Add(slot, 1, 4); grid.SetColumnSpan(slot, 2);
            AddRow(grid, 5, "Unspent skill points", pointInfo, points); AddRow(grid, 6, "Stellar Tears - NieR", tearInfo, tears); root.Controls.Add(grid, 0, 2);
            root.Controls.Add(grant, 0, 3); balance.AutoEllipsis = false; root.Controls.Add(balance, 0, 4);
            var note = new TextBox { Multiline = true, ReadOnly = true, BorderStyle = BorderStyle.None, BackColor = BackColor, Dock = DockStyle.Fill, ScrollBars = ScrollBars.Vertical,
                Text = "Each HP/Beta level uses 3 cores. Grant missing cores is optional: it grants and spends only the missing upgrade credits. It does not mark bodies as collected. MAX means the supported game limit is already reached; a separate game-data mod is needed to go higher.\r\n\r\nBurst Expansion Gear increases maximum Burst capacity when equipped. Inventory only does not change the bar until you equip it in game. Choosing a gear slot replaces that slot's item; the old item stays in inventory. Current gauges, Tachy capacity, skills and story unlocks are unchanged." };
            root.Controls.Add(note, 0, 5);
            var actions = Ui.Row(0, 0, 100);
            stage = Ui.Button("Prepare changes", delegate { Stage(); }, true); stage.Name = "StagePlayer"; actions.Controls.Add(stage, 0, 0);
            actions.Controls.Add(Ui.Button("Reset this window", delegate { controller.PlayerChanges.Clear(); controller.QuantityRequests.Remove("Money_Nier"); controller.InvalidatePlan(); }, false), 1, 0);
            actions.Controls.Add(Ui.Button("Done", delegate { Close(); }, false), 2, 0); root.Controls.Add(actions, 0, 6);
            status.ForeColor = Ui.Muted; root.Controls.Add(status, 0, 7);
            foreach (var number in new[] { body, beta, points, tears }) number.ValueChanged += delegate { if (!updating) { dirty = true; RefreshBalance(); } };
            grant.CheckedChanged += delegate { if (!updating) { dirty = true; RefreshBalance(); } };
            burst.SelectedIndexChanged += delegate { if (!updating) dirty = true; }; slot.SelectedIndexChanged += delegate { if (!updating) dirty = true; };
            controller.CommitRequested += CommitInput; controller.SaveLoaded += Loaded; controller.PlanChanged += RefreshValues;
            FormClosed += delegate { controller.SaveLoaded -= Loaded; controller.PlanChanged -= RefreshValues; controller.CommitRequested -= CommitInput; };
            Loaded(controller.CurrentEditor);
        }
        static void AddRow(TableLayoutPanel grid, int row, string label, Label current, NumericUpDown value)
        { value.AccessibleName = label; current.AutoEllipsis = false; grid.Controls.Add(Ui.Label(label), 0, row); grid.Controls.Add(current, 1, row); grid.Controls.Add(value, 2, row); }
        void Loaded(SaveEditor editor)
        {
            updating = true; snapshot = null; tearStack = null; slots.Clear(); string error = null;
            burst.Items.Clear(); burst.Items.Add(new BurstGearOption { Label = "Keep current Burst equipment" });
            slot.Items.Clear(); slot.Items.Add("Inventory only - equip in game");
            if (editor != null)
            {
                try { snapshot = PlayerEditor.Read(editor); slots = BurstGear.Slots(editor.Archive); } catch (Exception e) { error = e.Message; }
                var owned = editor.Owned();
                foreach (var gear in BurstGear.Names) burst.Items.Add(new BurstGearOption { Alias = gear.Key, Label = gear.Value + (owned.Contains(gear.Key) ? " (owned)" : " (will be added)") });
                foreach (int number in slots) slot.Items.Add("Equip in gear slot " + number + " - replace its current gear");
                tearStack = editor.Stacks().Find(delegate(StackItem i) { return i.Alias == "Money_Nier"; });
            }
            burst.SelectedIndex = slot.SelectedIndex = 0; updating = false;
            status.Text = error ?? (editor == null ? "Open a save in the main window." : "Change values here, then click Save copy in the main window.");
            RefreshValues();
        }
        void RefreshValues()
        {
            updating = true; dirty = false;
            SetCore(body, bodyInfo, snapshot == null ? null : snapshot.Body, controller.PlayerChanges.BodyLevel);
            SetCore(beta, betaInfo, snapshot == null ? null : snapshot.Beta, controller.PlayerChanges.BetaLevel);
            grant.Enabled = snapshot != null; grant.Checked = controller.PlayerChanges.GrantMissingCores;
            burst.Enabled = slot.Enabled = snapshot != null;
            int index = -1;
            for (int i = 0; i < burst.Items.Count; i++) if (((BurstGearOption)burst.Items[i]).Alias == controller.PlayerChanges.BurstGearAlias) index = i;
            burst.SelectedIndex = Math.Max(0, index); slot.SelectedIndex = Math.Max(0, slots.IndexOf(controller.PlayerChanges.BurstGearSlot) + 1);
            points.Enabled = snapshot != null; points.Maximum = snapshot == null ? 9999 : Math.Max(9999, snapshot.SkillPoints);
            points.Value = snapshot == null ? 0 : controller.PlayerChanges.SkillPoints ?? snapshot.SkillPoints;
            pointInfo.Text = snapshot == null ? "Unavailable" : snapshot.SkillPoints.ToString("N0") + " available; learned skills preserved";
            tears.Enabled = tearStack != null; tears.Maximum = tearStack == null ? 9999 : tearStack.Maximum;
            int pending; tears.Value = tearStack == null ? 0 : controller.QuantityRequests.TryGetValue("Money_Nier", out pending) ? pending : (decimal)tearStack.Amount;
            tearInfo.Text = tearStack == null ? "No compatible NieR data" : tearStack.Amount.ToString("N0") + " - Emil's shop balance";
            stage.Enabled = snapshot != null || tearStack != null; updating = false; RefreshBalance();
        }
        static void SetCore(NumericUpDown number, Label info, CoreUpgrade core, int? pending)
        {
            number.Enabled = core != null && core.Level <= core.Maximum;
            number.Minimum = 0; number.Maximum = core == null ? 9 : Math.Max(core.Level, core.Maximum); number.Minimum = core == null ? 0 : core.Level; number.Value = core == null ? 0 : pending ?? core.Level;
            info.Text = core == null ? "Unavailable - see opening details" : core.Level > core.Maximum ? "Custom level unsupported: " + core.Level :
                core.Level == core.Maximum ? "MAX: " + core.Level + " / " + core.Maximum + " upgrades; game limit reached" : core.Level + " / " + core.Maximum + " upgrades; " + core.Pieces + " cores held";
        }
        void RefreshBalance()
        {
            if (updating) return;
            if (snapshot == null) { balance.Text = "Player data is unavailable. The details below explain why."; return; }
            int hpMissing = Math.Max(0, -snapshot.Body.Remaining((int)body.Value)), betaMissing = Math.Max(0, -snapshot.Beta.Remaining((int)beta.Value));
            balance.ForeColor = hpMissing + betaMissing > 0 && !grant.Checked ? Color.Firebrick : Ui.Accent;
            balance.Text = "Missing for these targets: " + hpMissing + " Body Cores / " + betaMissing + " Beta Cores.\r\n" +
                (hpMissing + betaMissing == 0 ? "Existing cores cover these changes. MAX cannot go higher without a game-data mod." : grant.Checked ? "The missing credits will be granted and spent. World pickup states stay unchanged." : "Enable Grant missing cores above, or lower the selected target levels.");
        }
        void CommitInput() { if (dirty) Stage(); }
        void Stage()
        {
            if (snapshot != null)
            {
                controller.PlayerChanges.BodyLevel = body.Enabled && (int)body.Value != snapshot.Body.Level ? (int?)body.Value : null;
                controller.PlayerChanges.BetaLevel = beta.Enabled && (int)beta.Value != snapshot.Beta.Level ? (int?)beta.Value : null;
                controller.PlayerChanges.SkillPoints = (int)points.Value != snapshot.SkillPoints ? (int?)points.Value : null;
                controller.PlayerChanges.GrantMissingCores = grant.Checked;
                controller.PlayerChanges.BurstGearAlias = burst.SelectedItem == null ? null : ((BurstGearOption)burst.SelectedItem).Alias;
                controller.PlayerChanges.BurstGearSlot = slot.SelectedIndex > 0 ? slots[slot.SelectedIndex - 1] : 0;
            }
            if (tearStack != null)
            { if ((long)tears.Value == tearStack.Amount) controller.QuantityRequests.Remove("Money_Nier"); else controller.QuantityRequests["Money_Nier"] = (int)tears.Value; }
            dirty = false; controller.InvalidatePlan(); status.Text = "Changes prepared. Click Save copy in the main window; Review is optional.";
        }
    }
}
