using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using System.Reflection;

[assembly: AssemblyTitle("Stellar Blade - All in One")]
[assembly: AssemblyVersion("0.7.0.0")]
[assembly: AssemblyFileVersion("0.7.0.0")]

namespace StellarAdvanced
{
    sealed class MainForm : Form
    {
        readonly List<CatalogItem> catalog = CatalogItem.Load();
        readonly List<CatalogItem> specialLooks = CosmeticGuide.SpecialLooks();
        readonly TextBox cosmeticDetails = new TextBox { Name = "CosmeticDetails", ReadOnly = true, Multiline = true, ScrollBars = ScrollBars.Vertical, Dock = DockStyle.Fill, BackColor = Color.White };
        Button prepareCosmetic, openCosmeticSource;
        readonly Label summary = new Label(), status = new Label();
        readonly TextBox search = new TextBox(), preview = new TextBox();
        readonly ListView wardrobe = new ListView { Name = "Wardrobe" };
        readonly ComboBox category = new ComboBox();
        readonly CheckBox cans = new CheckBox(), nier = new CheckBox(), missing = new CheckBox();
        readonly Button build = new Button(), save = new Button();
        readonly HashSet<string> selected = new HashSet<string>(StringComparer.Ordinal);
        SaveEditor editor; EditPlan plan; bool populating, loading, collectingEdits, editsCollected, building, saving;
        public bool ShowLoadErrors = true;
        public string LoadReport { get; private set; }
        public bool HasLoadedSave { get { return editor != null; } }
        public readonly Dictionary<string, int> QuantityRequests = new Dictionary<string, int>(StringComparer.Ordinal);
        public readonly PlayerRequest PlayerChanges = new PlayerRequest();
        public readonly EquipmentRequest EquipmentChanges = new EquipmentRequest();
        public event Action<SaveEditor> SaveLoaded;
        public event Action PlanChanged;
        public event Action CommitRequested;
        internal Func<string, string> CopyDestination;
        internal bool ShowSaveMessages = true;
        public EditPlan CurrentPlan { get { return plan; } }
        public string CurrentStatus { get { return status.Text; } }
        public string LastBackupPath { get; private set; }
        readonly Color muted = Color.FromArgb(74, 92, 111);
        public SaveEditor CurrentEditor { get { return editor; } }
        public int SelectedCount { get { return selected.Count; } }
        public bool HasPendingChanges { get { return selected.Count > 0 || cans.Checked || nier.Checked || QuantityRequests.Count > 0 || PlayerChanges.Any || EquipmentChanges.Any; } }
        IWin32Window DialogHost { get { return Visible || Owner == null ? (IWin32Window)this : Owner; } }
        public string PendingDescription()
        {
            var text = new StringBuilder();
            foreach (var request in QuantityRequests)
            {
                StackItem definition; string name = InventoryCatalog.Items.TryGetValue(request.Key, out definition) ? definition.Name : request.Key;
                text.AppendLine(name + ": " + request.Value.ToString("N0"));
            }
            if (PlayerChanges.BodyLevel.HasValue) text.AppendLine("HP capacity: core level " + PlayerChanges.BodyLevel);
            if (PlayerChanges.BetaLevel.HasValue) text.AppendLine("Beta capacity: core level " + PlayerChanges.BetaLevel);
            if (PlayerChanges.WeaponLevel.HasValue) text.AppendLine("Weapon power: level " + PlayerChanges.WeaponLevel + " (1 Weapon Core per level)");
            if (EquipmentChanges.Any) text.AppendLine("Equipment prepared: " + EquipmentChanges.Items.Count + " gear / exospines; " + EquipmentChanges.Supplies.Count + " material stock requests");
            if (PlayerChanges.GrantMissingCores && (PlayerChanges.BodyLevel.HasValue || PlayerChanges.BetaLevel.HasValue || PlayerChanges.WeaponLevel.HasValue)) text.AppendLine("Grant only missing HP / Beta / weapon upgrade credits (source pickups unchanged)");
            if (PlayerChanges.BurstGearAlias != null) text.AppendLine("Burst: " + PlayerChanges.BurstGearAlias + (PlayerChanges.BurstGearSlot == 0 ? " - inventory only; equip in game" : " - equip in gear slot " + PlayerChanges.BurstGearSlot));
            if (PlayerChanges.SkillPoints.HasValue) text.AppendLine("Unspent skill points: " + PlayerChanges.SkillPoints);
            if (selected.Count > 0) text.AppendLine("Selected cosmetics: " + selected.Count);
            if (cans.Checked) text.AppendLine("49 cans + Black Pearl");
            if (nier.Checked) text.AppendLine("NieR collection: 15 pickups");
            return text.Length == 0 ? "No pending changes.\r\nOpen your save and choose what to edit." : text.ToString();
        }
        public void ClearAllChanges()
        { selected.Clear(); QuantityRequests.Clear(); PlayerChanges.Clear(); EquipmentChanges.Clear(); cans.Checked = nier.Checked = false; Populate(); InvalidatePlan(); }
        public void ReviewChanges()
        {
            BuildPlan();
            if (plan != null) StellarUnified.Ui.Review(DialogHost, plan.Report(), "Review modified copy");
        }
        public MainForm()
        {
            StellarUnified.Ui.Style(this, "Outfits and collections · Stellar Blade", new Size(840, 650), new Size(780, 620));
            var root = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(12), ColumnCount = 1, RowCount = 6 };
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 38)); root.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); root.RowStyles.Add(new RowStyle(SizeType.Absolute, 180)); root.RowStyles.Add(new RowStyle(SizeType.Absolute, 42)); root.RowStyles.Add(new RowStyle(SizeType.Absolute, 30)); Controls.Add(root);
            var heading = StellarUnified.Ui.Row(0, 310);
            heading.Controls.Add(new Label { Text = "Outfits and collections", Font = new Font("Segoe UI", 16, FontStyle.Bold), Dock = DockStyle.Fill }, 0, 0);
            summary.Dock = DockStyle.Fill; summary.TextAlign = ContentAlignment.MiddleRight; summary.AutoEllipsis = true; summary.Text = "Open a save from the main window"; heading.Controls.Add(summary, 1, 0); root.Controls.Add(heading, 0, 0);
            var filters = StellarUnified.Ui.Row(65, 0, 160, 145);
            filters.Controls.Add(Label("Search"), 0, 0); search.Dock = DockStyle.Fill; search.AccessibleName = "Search outfits and acquisition guide"; search.Name = "CosmeticSearch"; filters.Controls.Add(search, 1, 0);
            category.DropDownStyle = ComboBoxStyle.DropDownList; category.Dock = DockStyle.Fill; category.AccessibleName = "Cosmetic category"; category.Name = "CosmeticCategory";
            category.Items.AddRange(new object[] { "All", "EVE outfits", "Hairstyles", "Earrings", "Glasses", "Drone", "Adam", "Lily", "Patterns", "Special looks", "Uncatalogued owned" }); category.SelectedIndex = 0; filters.Controls.Add(category, 2, 0);
            missing.Text = "Missing only"; missing.Dock = DockStyle.Fill; filters.Controls.Add(missing, 3, 0); root.Controls.Add(filters, 0, 1);
            var content = StellarUnified.Ui.Row(0, 215);
            wardrobe.Dock = DockStyle.Fill; wardrobe.View = View.Details; wardrobe.CheckBoxes = true; wardrobe.FullRowSelect = true; wardrobe.HideSelection = false; wardrobe.AccessibleName = "Outfit catalog";
            wardrobe.Columns.Add("Cosmetic", 240); wardrobe.Columns.Add("Status", 95); wardrobe.Columns.Add("Content", 70);
            wardrobe.ItemCheck += delegate(object sender, ItemCheckEventArgs e)
            {
                if (populating || e.NewValue != CheckState.Checked) return;
                var item = (CatalogItem)wardrobe.Items[e.Index].Tag;
                if (editor == null || item.ReferenceOnly) { e.NewValue = CheckState.Unchecked; status.Text = editor == null ? "Open a save to prepare cosmetics. Acquisition details can be browsed offline." : "Reference look: use Prepare required item below, or follow its instructions."; }
            };
            wardrobe.SelectedIndexChanged += delegate { ShowCosmeticDetails(); };
            wardrobe.ItemChecked += delegate(object sender, ItemCheckedEventArgs e)
            {
                if (populating) return; var item = (CatalogItem)e.Item.Tag; if (item.ReferenceOnly || editor == null) return;
                if (e.Item.Checked) selected.Add(item.Alias); else selected.Remove(item.Alias); InvalidatePlan();
            };
            wardrobe.Resize += delegate { wardrobe.Columns[0].Width = Math.Max(180, wardrobe.ClientSize.Width - 185); };
            content.Controls.Add(wardrobe, 0, 0);
            var right = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 7, Padding = new Padding(8, 0, 0, 0) };
            foreach (int height in new int[] { 25, 56, 56, 36, 36, 36 }) right.RowStyles.Add(new RowStyle(SizeType.Absolute, height)); right.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            right.Controls.Add(Label("COLLECTIONS"), 0, 0);
            cans.Dock = DockStyle.Fill; cans.Text = "49 cans + Black Pearl"; right.Controls.Add(cans, 0, 1);
            nier.Dock = DockStyle.Fill; nier.Text = "NieR · 15 pickups\nExisting DLC data required"; right.Controls.Add(nier, 0, 2);
            right.Controls.Add(Button("Select visible base items", delegate { SelectVisible(); }), 0, 3);
            right.Controls.Add(Button("Angel / Fusion guide", delegate { FocusCosmetic("REF_AngelFusion"); }), 0, 4);
            right.Controls.Add(Button("Help and limitations", delegate { ShowHelp(); }), 0, 5);
            right.Controls.Add(new Label { Text = "Closing this window keeps your selection.\n\nOutfits, gold and items are saved together.", Dock = DockStyle.Fill, ForeColor = muted, Padding = new Padding(3, 6, 0, 0) }, 0, 6);
            content.Controls.Add(right, 1, 0); root.Controls.Add(content, 0, 2);
            var details = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, Padding = new Padding(0, 4, 0, 0) };
            details.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); details.RowStyles.Add(new RowStyle(SizeType.Absolute, 36)); details.Controls.Add(cosmeticDetails, 0, 0);
            var detailActions = StellarUnified.Ui.Row(0, 170);
            prepareCosmetic = Button("Prepare selected item", delegate { PrepareCosmetic(); }); prepareCosmetic.Name = "PrepareCosmetic"; detailActions.Controls.Add(prepareCosmetic, 0, 0);
            openCosmeticSource = Button("Open source", delegate { OpenCosmeticSource(); }); openCosmeticSource.Name = "OpenCosmeticSource"; detailActions.Controls.Add(openCosmeticSource, 1, 0);
            details.Controls.Add(detailActions, 0, 1); root.Controls.Add(details, 0, 3);
            var actions = StellarUnified.Ui.Row(0, 0, 0, 100);
            actions.Controls.Add(Button("Clear outfits", delegate { selected.Clear(); cans.Checked = nier.Checked = false; Populate(); InvalidatePlan(); }), 0, 0);
            Configure(build, "Review changes", delegate { ReviewChanges(); }); build.Enabled = false; actions.Controls.Add(build, 1, 0);
            Configure(save, "Save copy…", delegate { SavePlan(); }); save.Enabled = false; actions.Controls.Add(save, 2, 0);
            actions.Controls.Add(Button("Done", delegate { Close(); }), 3, 0); root.Controls.Add(actions, 0, 4);
            status.Dock = DockStyle.Fill; status.AutoEllipsis = true; status.TextAlign = ContentAlignment.MiddleLeft; status.ForeColor = muted; status.Text = "Open your save in the main window to select outfits."; root.Controls.Add(status, 0, 5);
            search.TextChanged += delegate { Populate(); }; category.SelectedIndexChanged += delegate { Populate(); }; missing.CheckedChanged += delegate { Populate(); };
            cans.CheckedChanged += delegate { InvalidatePlan(); }; nier.CheckedChanged += delegate { InvalidatePlan(); }; Populate();
        }
        Label Label(string text) { return new Label { Text = text, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, ForeColor = muted }; }
        Button Button(string text, EventHandler click) { var button = new Button(); Configure(button, text, click); return button; }
        void Configure(Button button, string text, EventHandler click)
        { button.Text = text; button.Dock = DockStyle.Fill; button.Margin = new Padding(4); button.BackColor = Color.White; button.FlatStyle = FlatStyle.Flat; button.FlatAppearance.BorderColor = Color.FromArgb(195, 207, 218); button.Click += click; }
        public void InvalidatePlan() { plan = null; save.Enabled = editor != null; if (collectingEdits) { editsCollected = true; return; } if (PlanChanged != null) PlanChanged(); }
        void CollectEdits()
        {
            collectingEdits = true; editsCollected = false;
            try { if (CommitRequested != null) CommitRequested(); }
            finally { collectingEdits = false; if (editsCollected && PlanChanged != null) PlanChanged(); }
        }
        public void OpenSave()
        {
            string usual = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SB", "Saved", "SaveGames");
            using (var dialog = new OpenFileDialog { Title = "Open a main save (.sav), not settings", Filter = "Stellar Blade save (*.sav)|*.sav|All files|*.*", CheckFileExists = true, Multiselect = false, RestoreDirectory = true, InitialDirectory = Directory.Exists(usual) ? usual : Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments) })
                if (dialog.ShowDialog(DialogHost) == DialogResult.OK) LoadSave(dialog.FileName);
        }
        public void OpenSavePath()
        {
            using (var dialog = new Form { Text = "Open save by full path", ClientSize = new Size(720, 170), StartPosition = FormStartPosition.CenterParent, Font = Font, MinimizeBox = false, MaximizeBox = false, FormBorderStyle = FormBorderStyle.FixedDialog })
            {
                dialog.Controls.Add(new Label { Text = "In File Explorer, right-click your .sav > Copy as path. Paste it below.\nInclude the file name, even when opening from a USB drive.", Location = new Point(14, 12), Size = new Size(690, 48) });
                var input = new TextBox { Location = new Point(14, 66), Width = 690 }; dialog.Controls.Add(input);
                var open = new Button { Text = "Open this .sav", Location = new Point(550, 112), Size = new Size(154, 36), DialogResult = DialogResult.OK }; dialog.Controls.Add(open); dialog.AcceptButton = open;
                var cancel = new Button { Text = "Cancel", Location = new Point(420, 112), Size = new Size(120, 36), DialogResult = DialogResult.Cancel }; dialog.Controls.Add(cancel); dialog.CancelButton = cancel;
                if (dialog.ShowDialog(DialogHost) == DialogResult.OK) LoadSave(input.Text);
            }
        }
        public void DetectSave()
        {
            try
            {
                string root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SB", "Saved", "SaveGames");
                if (!Directory.Exists(root)) { MessageBox.Show(DialogHost, "No save folder was found on this PC. Use Open .sav or Paste path to select a save copied from your other PC or USB drive.", "Detect save"); return; }
                string file = Directory.GetFiles(root, "StellarBladeSave??.sav", SearchOption.AllDirectories).OrderByDescending(File.GetLastWriteTimeUtc).FirstOrDefault();
                if (file == null) { MessageBox.Show(DialogHost, "No main save was found in the default folder. Use Open .sav to choose your file.", "Detect save"); return; } LoadSave(file);
            }
            catch (Exception error) { MessageBox.Show(DialogHost, error.Message, "Could not detect a save"); }
        }
        public void LoadSave(string path)
        {
            if (loading) return; loading = true;
            editor = null; plan = null; build.Enabled = save.Enabled = false; summary.Text = "Opening save…"; selected.Clear(); QuantityRequests.Clear(); PlayerChanges.Clear(); EquipmentChanges.Clear(); LoadReport = null;
            InvalidatePlan();
            try
            {
                UseWaitCursor = true; Application.DoEvents(); var loaded = new SaveEditor(path); editor = loaded; selected.Clear(); cans.Checked = nier.Checked = false;
                search.Clear(); category.SelectedIndex = 0; missing.Checked = false;
                summary.Text = Path.GetFileName(editor.SourcePath) + " · " + editor.Cans() + "/49 cans";
                LoadReport = SaveInput.Report(editor.SourcePath, null, editor);
                preview.Text = "SAVE LOADED\r\n" + Path.GetFileName(editor.SourcePath) + "\r\n\r\nEdit values or select cosmetics, then click Save copy. Your latest values are collected and validated automatically.";
                build.Enabled = true; InvalidatePlan(); Populate(); status.Text = "SAVE LOADED · Edit your values and click Save copy. Review is optional.";
                if (SaveLoaded != null) SaveLoaded(editor);
            }
            catch (Exception error)
            {
                editor = null; build.Enabled = save.Enabled = false; InvalidatePlan(); summary.Text = "SAVE NOT LOADED"; Populate(); status.Text = "Could not open: " + error.Message;
                LoadReport = SaveInput.Report(path, error, null); preview.Text = LoadReport;
                if (SaveLoaded != null) SaveLoaded(null);
                UseWaitCursor = false;
                if (ShowLoadErrors) using (var diagnostic = new SaveDiagnosticForm(LoadReport)) diagnostic.ShowDialog(DialogHost);
            }
            finally { UseWaitCursor = false; loading = false; }
        }
        void Populate()
        {
            string previous = wardrobe.SelectedItems.Count == 1 ? ((CatalogItem)wardrobe.SelectedItems[0].Tag).Alias : null;
            populating = true; wardrobe.BeginUpdate(); wardrobe.Items.Clear(); var owned = editor == null ? new HashSet<string>() : editor.Owned();
            wardrobe.Enabled = true; cans.Enabled = nier.Enabled = editor != null;
            string[] cats = { "", "nano_suits", "hair", "earrings", "glasses", "drone_seals", "adam_costumes", "lily_costumes", "design_patterns", "special_looks", "save_only" };
            var dlcSupport = new Dictionary<string, bool>(StringComparer.Ordinal);
            foreach (CatalogItem entry in catalog.Concat(specialLooks).Concat(CosmeticGuide.UnknownOwned(editor, catalog)))
            {
                if (category.SelectedIndex > 0 && entry.Category != cats[category.SelectedIndex]) continue;
                string searchable = entry.Name + " " + entry.Alias + " " + entry.Area + " " + entry.Location + " " + entry.Requirements + " " + entry.HowToGet;
                if (search.Text.Length > 0 && System.Globalization.CultureInfo.CurrentCulture.CompareInfo.IndexOf(searchable, search.Text, System.Globalization.CompareOptions.IgnoreCase | System.Globalization.CompareOptions.IgnoreNonSpace) < 0) continue;
                if (missing.Checked && !entry.ReferenceOnly && owned.Contains(entry.Alias)) continue;
                var item = new ListViewItem(entry.Name); item.Tag = entry; item.Checked = selected.Contains(entry.Alias);
                string dlcKey = entry.Dlc ?? ""; bool supported;
                if (!dlcSupport.TryGetValue(dlcKey, out supported)) { supported = editor != null && editor.DlcPresent(entry.Dlc); dlcSupport.Add(dlcKey, supported); }
                item.SubItems.Add(entry.ReferenceOnly ? entry.Category == "save_only" ? "Owned / unverified" : "Special guide" : editor == null ? "Open save to edit" : owned.Contains(entry.Alias) ? "Owned" : supported ? "Not owned" : "No DLC data");
                item.SubItems.Add(entry.ReferenceOnly ? "Reference" : String.IsNullOrEmpty(entry.Dlc) ? "Base" : entry.Dlc);
                if (entry.ReferenceOnly || !supported) item.ForeColor = Color.Gray; wardrobe.Items.Add(item);
                if (entry.Alias == previous) { item.Selected = true; item.Focused = true; }
            }
            wardrobe.EndUpdate(); populating = false; ShowCosmeticDetails();
        }
        CatalogItem FocusedCosmetic { get { return wardrobe.SelectedItems.Count == 1 ? (CatalogItem)wardrobe.SelectedItems[0].Tag : null; } }
        void ShowCosmeticDetails()
        {
            if (prepareCosmetic == null) return;
            CatalogItem item = FocusedCosmetic; cosmeticDetails.Text = CosmeticGuide.Details(item);
            prepareCosmetic.Text = item != null && item.ReferenceOnly ? "Prepare required item" : "Prepare selected item";
            prepareCosmetic.Enabled = editor != null && item != null && (!item.ReferenceOnly || !String.IsNullOrEmpty(item.RequiredAlias));
            openCosmeticSource.Enabled = item != null && !String.IsNullOrEmpty(item.SourceUrl);
        }
        public void FocusCosmetic(string alias)
        {
            search.Clear(); category.SelectedIndex = alias.StartsWith("REF_", StringComparison.Ordinal) ? 9 : 0; missing.Checked = false; Populate();
            foreach (ListViewItem row in wardrobe.Items)
            { row.Selected = ((CatalogItem)row.Tag).Alias == alias; if (row.Selected) { row.Focused = true; row.EnsureVisible(); } }
            ShowCosmeticDetails();
        }
        void PrepareCosmetic()
        {
            CatalogItem item = FocusedCosmetic; if (editor == null || item == null) return;
            string alias = item.ReferenceOnly ? item.RequiredAlias : item.Alias;
            CatalogItem requirement = catalog.Find(delegate(CatalogItem i) { return i.Alias == alias; });
            if (requirement == null) return;
            if (!editor.DlcPresent(requirement.Dlc)) { status.Text = "No existing save data for the required DLC. Read the acquisition details above."; return; }
            selected.Add(requirement.Alias); Populate(); InvalidatePlan();
            status.Text = "Prepared: " + requirement.Name + ". Save a copy, then equip the item in game. External mods are not installed by this action.";
        }
        void OpenCosmeticSource()
        {
            CatalogItem item = FocusedCosmetic; Uri uri;
            if (item == null || !Uri.TryCreate(item.SourceUrl, UriKind.Absolute, out uri) || uri.Scheme != Uri.UriSchemeHttps) return;
            Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true });
        }
        void SelectVisible()
        {
            if (editor == null) { status.Text = "Open a compatible save before selecting cosmetics."; return; }
            foreach (ListViewItem row in wardrobe.Items) { var item = (CatalogItem)row.Tag; if (!item.ReferenceOnly && String.IsNullOrEmpty(item.Dlc)) row.Checked = true; }
        }
        public void BuildPlan() { PreparePlan(false); }
        bool PreparePlan(bool allowUnchanged)
        {
            if (building) return false;
            if (editor == null) { if (ShowSaveMessages) MessageBox.Show(DialogHost, "Open a compatible save first. If the list is empty, check the opening report.", "No save loaded"); return false; }
            building = true;
            try
            {
                CollectEdits();
                if (!allowUnchanged && !HasPendingChanges) { if (ShowSaveMessages) MessageBox.Show(DialogHost, "Change a value or select cosmetics before reviewing changes.", "No pending changes"); return false; }
                UseWaitCursor = true; status.Text = "Preparing and verifying the copy…"; Application.DoEvents();
                plan = editor.Build(catalog.Where(delegate(CatalogItem item) { return selected.Contains(item.Alias); }), cans.Checked, nier.Checked, QuantityRequests, PlayerChanges, EquipmentChanges);
                preview.Text = plan.Report(); save.Enabled = true; status.Text = "Copy validated. The original file is unchanged.";
                if (PlanChanged != null) PlanChanged();
                return true;
            }
            catch (Exception error)
            {
                InvalidatePlan(); status.Text = "Operation stopped: " + error.Message;
                if (ShowSaveMessages) MessageBox.Show(DialogHost, error.Message, "Could not prepare the copy", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return false;
            }
            finally { building = false; UseWaitCursor = false; }
        }
        public void SavePlan()
        {
            if (saving || building) return;
            saving = true;
            try
            {
                // Always collect the latest controls and validate. A separate review click is optional.
                if (!PreparePlan(true)) return;
                string destination;
                if (CopyDestination != null) destination = CopyDestination(Path.GetFileNameWithoutExtension(editor.SourcePath) + "-modified.sav");
                else using (var dialog = new SaveFileDialog { Title = "Save a new modified copy", Filter = "Modified save|*.sav", FileName = Path.GetFileNameWithoutExtension(editor.SourcePath) + "-modified.sav", DefaultExt = "sav", AddExtension = true, CheckFileExists = false, CheckPathExists = false, RestoreDirectory = true, OverwritePrompt = false })
                {
                    if (dialog.ShowDialog(DialogHost) != DialogResult.OK) return;
                    destination = dialog.FileName;
                }
                if (String.IsNullOrWhiteSpace(destination)) return;
                editor.SaveCopy(plan, destination); LastBackupPath = plan.BackupPath;
                status.Text = "Saved: " + plan.OutputPath;
                if (PlanChanged != null) PlanChanged();
                if (ShowSaveMessages) MessageBox.Show(DialogHost, "MODIFIED COPY SAVED\n" + plan.OutputPath + "\n\nPERMANENT ORIGINAL BACKUP\n" + plan.BackupPath +
                    (plan.SourceMissing ? "\n\nThe source was no longer at its path. The save loaded in this session was used and backed up." : "") +
                    "\n\nIf that name already existed, a new copy name was used. Your previous file is preserved.", "Save completed", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception error)
            {
                status.Text = "Could not save: " + error.Message;
                if (PlanChanged != null) PlanChanged();
                if (ShowSaveMessages) MessageBox.Show(DialogHost, "Could not save the copy.\n\n" + error.Message + "\n\nThe original save was not replaced.", "Save error · full details", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally { saving = false; }
        }
        void ShowHelp()
        {
            MessageBox.Show(DialogHost,
                "This editor reads your save file and prepares a modified copy.\n\n" +
                "Updates inventory, ownership and known acquisition flags. Black Pearl includes 49 can records; NieR cosmetics include 15 collection and pickup markers.\n\n" +
                "Gold and item changes are included in the same copy. Physical chests and quests are unchanged: their exact outfit links have not been identified.\n\n" +
                "Automated tests check structure, duplicates and preservation of unrelated save data. Test a copy and verify saving and loading in your game.\n\n" +
                "Save with the game closed. A permanent dated original backup is created in CopiasSeguridad beside the destination. Backups are never deleted automatically.\n\n" +
                "The reader is adapted from sblade-explorer under Apache-2.0. This app does not activate DLC licenses or require Internet access.", "Help · limitations", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        public void RenderPreview(string output, string sample)
        {
            ShowInTaskbar = false; Opacity = 0; Show();
            if (sample != null) LoadSave(sample); Application.DoEvents();
            using (var bitmap = new Bitmap(Width, Height)) { DrawToBitmap(bitmap, new Rectangle(0, 0, Width, Height)); bitmap.Save(output); } Close();
        }
    }
    static class Program
    {
        [STAThread] static int Main(string[] args)
        {
            try
            {
                if (args.Length == 3 && args[0] == "--test") return SaveTests.Run(args[1], args[2]);
                Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
                if (args.Length > 1 && args[0] == "--preview") { using (var form = new MainForm()) form.RenderPreview(args[1], args.Length > 2 ? args[2] : null); return 0; }
                Application.Run(new MainForm()); return 0;
            }
            catch (Exception error)
            {
                if (args.Length > 0) { Console.Error.WriteLine(error.ToString()); return 1; }
                MessageBox.Show(error.Message, "Stellar Blade · Collections", MessageBoxButtons.OK, MessageBoxIcon.Error); return 1;
            }
        }
    }
}
