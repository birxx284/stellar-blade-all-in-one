using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace StellarAssist
{
    sealed class Feature
    {
        public string Name;
        public NumberKind Kind;
        public Candidate Location;
        public Label Current;
        public NumericUpDown Target;
        public CheckBox Hold;
        public Button Apply;
        public Feature(string name, NumberKind kind) { Name = name; Kind = kind; }
    }

    sealed class MainForm : Form
    {
        readonly Color ink = Color.FromArgb(25, 39, 58), muted = Color.FromArgb(83, 101, 120), accent = Color.FromArgb(25, 104, 94);
        readonly List<Feature> features = new List<Feature>();
        readonly Label connection = new Label(), status = new Label(), results = new Label();
        readonly Button connect = new Button(), firstExact = new Button(), firstUnknown = new Button(), filter = new Button(), cancel = new Button(), assign = new Button(), reset = new Button();
        readonly ComboBox featureBox = new ComboBox(), kindBox = new ComboBox(), matchBox = new ComboBox();
        readonly NumericUpDown exact = new NumericUpDown();
        readonly ListView list = new ListView();
        readonly TabControl tabs = new TabControl();
        readonly System.Windows.Forms.Timer timer = new System.Windows.Forms.Timer();
        GameMemory memory;
        ScanSession scan;
        CancellationTokenSource cancellation;
        bool busy, closingRequested, changing, previewMode;
        int scanFeature = -1;
        bool hotkey;

        [DllImport("user32.dll")] static extern bool RegisterHotKey(IntPtr hwnd, int id, uint modifiers, uint key);
        [DllImport("user32.dll")] static extern bool UnregisterHotKey(IntPtr hwnd, int id);

        public MainForm()
        {
            Text = "Stellar Blade · In-game controls · manual calibration";
            ClientSize = new Size(1030, 770); MinimumSize = new Size(940, 720);
            Font = new Font("Segoe UI", 10); ForeColor = ink; BackColor = Color.FromArgb(243, 246, 248);
            AutoScaleMode = AutoScaleMode.Dpi;
            StartPosition = FormStartPosition.CenterScreen;
            var root = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(22, 16, 22, 12), ColumnCount = 1, RowCount = 5 };
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 72)); root.RowStyles.Add(new RowStyle(SizeType.Absolute, 52));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 48)); root.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); root.RowStyles.Add(new RowStyle(SizeType.Absolute, 54));
            Controls.Add(root);
            var heading = new Panel { Dock = DockStyle.Fill };
            heading.Controls.Add(new Label { Text = "STELLAR BLADE", Font = new Font("Segoe UI", 23, FontStyle.Bold), AutoSize = true, Location = new Point(0, 0) });
            heading.Controls.Add(new Label { Text = "All in One 0.4.0 · in-game controls · manual calibration", AutoSize = true, ForeColor = muted, Location = new Point(3, 45) });
            root.Controls.Add(heading, 0, 0);
            var warning = new Label { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, Padding = new Padding(10, 0, 10, 0), BackColor = Color.FromArgb(255, 244, 213),
                Text = "MANUAL CALIBRATION · No verified automatic profile. Locate each value in this session before enabling it." };
            root.Controls.Add(warning, 0, 1);
            var bar = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 4, RowCount = 1 };
            bar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); bar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 146));
            bar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 146)); bar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 146));
            connection.Text = "Game not connected"; connection.Dock = DockStyle.Fill; connection.TextAlign = ContentAlignment.MiddleLeft;
            Configure(connect, "Connect to game", delegate { ConnectGame(); });
            var pause = NewButton("Stop all · F8", delegate { PauseAll(); SetStatus("Value holding stopped. Previously applied changes are not undone."); });
            var report = NewButton("Export report", delegate { ExportReport(); });
            bar.Controls.Add(connection, 0, 0); bar.Controls.Add(connect, 1, 0); bar.Controls.Add(pause, 2, 0); bar.Controls.Add(report, 3, 0); root.Controls.Add(bar, 0, 2);
            tabs.Dock = DockStyle.Fill; root.Controls.Add(tabs, 0, 3);
            BuildDashboard(); BuildSearch(); BuildHelp();
            status.Dock = DockStyle.Fill; status.TextAlign = ContentAlignment.MiddleLeft; status.ForeColor = muted;
            status.Text = "Load a save in game and click Connect to game. See Help to get started.";
            root.Controls.Add(status, 0, 4);
            timer.Interval = 250; timer.Tick += delegate { UpdateValues(); }; timer.Start();
            FormClosing += OnClosing;
            Shown += delegate { if (previewMode) return; hotkey = RegisterHotKey(Handle, 8, 0x4000, (uint)Keys.F8); if (!hotkey) SetStatus("F8 is unavailable. Use Stop all to stop holding values."); };
        }

        Button NewButton(string text, EventHandler action) { var button = new Button(); Configure(button, text, action); return button; }
        void Configure(Button button, string text, EventHandler action)
        {
            button.Text = text; button.Dock = DockStyle.Fill; button.Margin = new Padding(4); button.FlatStyle = FlatStyle.Flat;
            button.FlatAppearance.BorderColor = Color.FromArgb(199, 211, 220); button.BackColor = Color.White; button.ForeColor = ink;
            button.Click += action;
        }
        Label Caption(string text) { return new Label { Text = text, Dock = DockStyle.Fill, AutoSize = true, ForeColor = muted, TextAlign = ContentAlignment.MiddleLeft }; }
        void BuildDashboard()
        {
            var page = new TabPage("Controls") { BackColor = BackColor, Padding = new Padding(8) }; tabs.TabPages.Add(page);
            var grid = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 3 };
            for (int i = 0; i < 3; i++) grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.33f));
            grid.RowStyles.Add(new RowStyle(SizeType.Percent, 50)); grid.RowStyles.Add(new RowStyle(SizeType.Percent, 50)); grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
            page.Controls.Add(grid);
            features.Add(new Feature("Money", NumberKind.Integer)); features.Add(new Feature("Health", NumberKind.Float)); features.Add(new Feature("Consumable", NumberKind.Integer));
            features.Add(new Feature("Beta energy", NumberKind.Float)); features.Add(new Feature("Burst energy", NumberKind.Float)); features.Add(new Feature("Tachy energy", NumberKind.Float));
            for (int i = 0; i < features.Count; i++)
            {
                int index = i; Feature f = features[i];
                var group = new GroupBox { Text = f.Name, Dock = DockStyle.Fill, Padding = new Padding(10), BackColor = Color.White, Margin = new Padding(5) };
                var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 5 };
                layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50)); layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
                layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 23));
                layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 34)); layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 32)); layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
                f.Current = new Label { Text = "Not located", ForeColor = muted, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, AutoEllipsis = true };
                layout.Controls.Add(f.Current, 0, 0); layout.SetColumnSpan(f.Current, 2);
                layout.Controls.Add(Caption("Target value"), 0, 1); layout.SetColumnSpan(layout.GetControlFromPosition(0, 1), 2);
                f.Target = new NumericUpDown { Minimum = 0, Maximum = 1000000, DecimalPlaces = f.Kind == NumberKind.Float ? 2 : 0, ThousandsSeparator = true, Dock = DockStyle.Fill, Enabled = false };
                layout.Controls.Add(f.Target, 0, 2); layout.SetColumnSpan(f.Target, 2);
                f.Hold = new CheckBox { Text = "Hold this value", Dock = DockStyle.Fill, Enabled = false };
                f.Hold.CheckedChanged += delegate { if (f.Hold.Checked) TryApply(f); };
                layout.Controls.Add(f.Hold, 0, 3); layout.SetColumnSpan(f.Hold, 2);
                var locate = NewButton("Locate", delegate { if (!busy) { featureBox.SelectedIndex = index; tabs.SelectedIndex = 1; } });
                f.Apply = NewButton("Apply", delegate { TryApply(f); }); f.Apply.Enabled = false;
                layout.Controls.Add(locate, 0, 4); layout.Controls.Add(f.Apply, 1, 4); group.Controls.Add(layout); grid.Controls.Add(group, i % 3, i / 3);
            }
            var note = Caption("Locate each energy and consumable type separately. Recalibrate after loading another area or save.");
            grid.Controls.Add(note, 0, 2); grid.SetColumnSpan(note, 3);
        }
        void BuildSearch()
        {
            var page = new TabPage("Locate values") { Padding = new Padding(10), BackColor = BackColor }; tabs.TabPages.Add(page);
            var grid = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 7 };
            int[] heights = { 62, 42, 46, 46, 42 };
            foreach (int height in heights) grid.RowStyles.Add(new RowStyle(SizeType.Absolute, height));
            grid.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 43));
            page.Controls.Add(grid);
            var instructions = Caption("1. Search for the number shown in game, or use Unknown value for a bar.\n2. Change only that value in game and filter several times.  3. Test a candidate with a small change.");
            grid.Controls.Add(instructions, 0, 0);
            var selection = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 4 };
            selection.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 76)); selection.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 55));
            selection.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 48)); selection.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 45));
            featureBox.DropDownStyle = ComboBoxStyle.DropDownList; featureBox.Dock = DockStyle.Fill;
            foreach (Feature f in features) featureBox.Items.Add(f.Name);
            kindBox.DropDownStyle = ComboBoxStyle.DropDownList; kindBox.Dock = DockStyle.Fill; kindBox.Items.AddRange(new object[] { "4-byte integer", "4-byte float" });
            selection.Controls.Add(Caption("Control"), 0, 0); selection.Controls.Add(featureBox, 1, 0); selection.Controls.Add(Caption("Type"), 2, 0); selection.Controls.Add(kindBox, 3, 0);
            grid.Controls.Add(selection, 0, 1);
            var start = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 4 };
            start.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 100)); start.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 36));
            start.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 32)); start.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 32));
            exact.Maximum = 1000000; exact.DecimalPlaces = 3; exact.Dock = DockStyle.Fill;
            Configure(firstExact, "Search number", delegate { StartScan(false); }); Configure(firstUnknown, "Unknown value", delegate { StartScan(true); });
            start.Controls.Add(Caption("Current value"), 0, 0); start.Controls.Add(exact, 1, 0); start.Controls.Add(firstExact, 2, 0); start.Controls.Add(firstUnknown, 3, 0); grid.Controls.Add(start, 0, 2);
            var row = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 4 };
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50)); for (int i = 0; i < 3; i++) row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 16.66f));
            matchBox.DropDownStyle = ComboBoxStyle.DropDownList; matchBox.Dock = DockStyle.Fill;
            matchBox.Items.AddRange(new object[] { "Equals the number above", "Decreased", "Increased", "Changed", "Unchanged" }); matchBox.SelectedIndex = 0;
            Configure(filter, "Filter", delegate { FilterScan(); }); Configure(cancel, "Cancel", delegate { if (cancellation != null) cancellation.Cancel(); });
            Configure(reset, "Reset", delegate { ClearScan(); SetStatus("Scan reset. Enter the current value and start again."); });
            row.Controls.Add(matchBox, 0, 0); row.Controls.Add(filter, 1, 0); row.Controls.Add(cancel, 2, 0); row.Controls.Add(reset, 3, 0); grid.Controls.Add(row, 0, 3);
            results.Dock = DockStyle.Fill; results.TextAlign = ContentAlignment.MiddleLeft; results.Text = "No scan yet. At most 200 candidates are displayed."; grid.Controls.Add(results, 0, 4);
            list.View = View.Details; list.FullRowSelect = true; list.MultiSelect = false; list.HideSelection = false; list.Dock = DockStyle.Fill;
            list.Columns.Add("Candidate / temporary address", 320); list.Columns.Add("Value at last filter", 230);
            list.SelectedIndexChanged += delegate { assign.Enabled = !busy && scan != null && scan.Count <= 200 && list.SelectedItems.Count == 1; };
            grid.Controls.Add(list, 0, 5);
            Configure(assign, "Use selected candidate for this control", delegate { AssignCandidate(); });
            grid.Controls.Add(assign, 0, 6);
            featureBox.SelectedIndexChanged += delegate
            {
                if (changing || busy) return;
                changing = true; ClearScan(); kindBox.SelectedIndex = features[featureBox.SelectedIndex].Kind == NumberKind.Integer ? 0 : 1; exact.DecimalPlaces = kindBox.SelectedIndex == 0 ? 0 : 3; changing = false;
            };
            kindBox.SelectedIndexChanged += delegate { if (!changing && !busy) { ClearScan(); exact.DecimalPlaces = kindBox.SelectedIndex == 0 ? 0 : 3; } };
            featureBox.SelectedIndex = 0;
            SetBusy(false);
        }
        void BuildHelp()
        {
            var page = new TabPage("Help") { Padding = new Padding(16), BackColor = Color.White }; tabs.TabPages.Add(page);
            var text = new TextBox { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, BorderStyle = BorderStyle.None, BackColor = Color.White, Font = new Font("Segoe UI", 11) };
            text.Text = HelpText.Content.Replace("\r\n", "\n").Replace("\n", Environment.NewLine);
            page.Controls.Add(text);
        }

        void SetStatus(string text) { status.Text = text; }
        void PauseAll() { foreach (Feature f in features) f.Hold.Checked = false; }
        void ClearBindings()
        {
            PauseAll();
            foreach (Feature f in features) { f.Location = null; f.Current.Text = "Not located"; f.Target.Enabled = f.Apply.Enabled = f.Hold.Enabled = false; }
        }
        void ClearScan()
        {
            if (scan != null) { scan.Dispose(); scan = null; }
            scanFeature = -1; list.Items.Clear(); results.Text = "No scan yet. At most 200 candidates are displayed."; assign.Enabled = false; filter.Enabled = false;
        }
        void ConnectGame()
        {
            if (busy) return;
            try
            {
                ClearScan(); ClearBindings();
                if (memory != null) { memory.Dispose(); memory = null; }
                Process[] processes = Process.GetProcessesByName("SB-Win64-Shipping");
                if (processes.Length != 1)
                {
                    foreach (Process p in processes) p.Dispose();
                    connection.Text = "Game not connected";
                    SetStatus(processes.Length == 0 ? "Game not found. Start Stellar Blade and load a save." : "Multiple game instances found. Keep only one open."); return;
                }
                memory = new GameMemory(processes[0], false);
                connection.Text = "Connected · PID " + memory.Process.Id;
                SetStatus("Connected. Click Locate on the control you want to prepare.");
            }
            catch (Exception error) { connection.Text = "Game not connected"; SetStatus(error.Message); }
        }
        bool Ready()
        {
            if (memory == null || !memory.Alive) { SetStatus("Load a save and connect to the game first."); return false; }
            return true;
        }
        void SetBusy(bool value)
        {
            busy = value; connect.Enabled = !value; featureBox.Enabled = kindBox.Enabled = exact.Enabled = !value;
            firstExact.Enabled = firstUnknown.Enabled = reset.Enabled = !value; filter.Enabled = !value && scan != null;
            cancel.Enabled = value; assign.Enabled = !value && scan != null && scan.Count <= 200 && list.SelectedItems.Count == 1;
            foreach (Feature f in features) { f.Apply.Enabled = f.Hold.Enabled = f.Target.Enabled = !value && f.Location != null; }
        }
        void RunWork(Action<CancellationToken, Action<string>> work, Action failure)
        {
            PauseAll(); SetBusy(true); cancellation = new CancellationTokenSource();
            CancellationToken token = cancellation.Token; var watch = Stopwatch.StartNew();
            var progress = new Action<string>(delegate(string message)
            {
                if (watch.ElapsedMilliseconds < 200) return;
                watch.Restart();
                if (!IsDisposed) BeginInvoke((Action)delegate { SetStatus(message); });
            });
            Task.Factory.StartNew(delegate { work(token, progress); }, token).ContinueWith(delegate(Task task)
            {
                if (task.IsFaulted || task.IsCanceled)
                {
                    Exception error = task.Exception == null ? null : task.Exception.GetBaseException();
                    if (failure != null) failure();
                    SetStatus(task.IsCanceled || error is OperationCanceledException ? "Scan canceled." : "Not completed: " + error.Message);
                }
                else { SetStatus("Scan completed. Change the value in game and filter again to narrow the matches."); }
                cancellation.Dispose(); cancellation = null; SetBusy(false); ShowCandidates();
                if (closingRequested) Close();
            }, TaskScheduler.FromCurrentSynchronizationContext());
        }
        void StartScan(bool unknown)
        {
            if (!Ready() || busy) return;
            NumberKind kind = kindBox.SelectedIndex == 0 ? NumberKind.Integer : NumberKind.Float;
            double value = (double)exact.Value;
            if (kind == NumberKind.Integer && value != Math.Truncate(value)) { SetStatus("Enter a whole number to search for an integer."); return; }
            ClearScan(); scanFeature = featureBox.SelectedIndex;
            Feature f = features[scanFeature]; f.Hold.Checked = false; f.Location = null; f.Current.Text = "Not located";
            scan = new ScanSession(memory, kind); SetStatus("Preparing scan. You can cancel at any time.");
            RunWork(delegate(CancellationToken token, Action<string> progress) { scan.First(unknown, value, token, progress, null); }, delegate { ClearScan(); });
        }
        void FilterScan()
        {
            if (!Ready() || busy || scan == null) return;
            MatchKind kind = (MatchKind)matchBox.SelectedIndex; double value = (double)exact.Value;
            RunWork(delegate(CancellationToken token, Action<string> progress) { scan.Filter(kind, value, token, progress); }, null);
        }
        void ShowCandidates()
        {
            list.BeginUpdate(); list.Items.Clear();
            if (scan != null)
            {
                foreach (Candidate c in scan.Preview)
                {
                    var item = new ListViewItem("0x" + c.Address.ToString("X")); item.SubItems.Add(c.Value.ToString("0.###", CultureInfo.CurrentCulture)); item.Tag = c; list.Items.Add(item);
                }
                results.Text = String.Format("{0:N0} candidates · {1:N0} MB read · {2:N0} MB skipped after read failures. {3}", scan.Count, scan.BytesRead / 1048576, scan.BytesSkipped / 1048576,
                    scan.Count > 200 ? "Filter further before selecting a candidate." : "Verify the selected candidate in game.");
                if (scan.Count == 0) SetStatus("No candidates. Reset using the current value or try the other number type.");
            }
            list.EndUpdate(); assign.Enabled = false;
        }
        void AssignCandidate()
        {
            if (busy || scan == null || scan.Count > 200 || list.SelectedItems.Count != 1 || !Ready()) return;
            Candidate c = (Candidate)list.SelectedItems[0].Tag;
            foreach (Feature other in features)
                if (other.Location != null && other.Location.Address == c.Address) { SetStatus("This candidate is already assigned to " + other.Name + ". Locate a different address."); return; }
            Feature f = features[scanFeature]; double current;
            if (!memory.TryValue(c.Address, scan.Kind, out current) || current < 0 || current > 1000000) { SetStatus("This candidate is no longer valid. Repeat the scan."); return; }
            f.Location = c; f.Kind = scan.Kind; f.Target.DecimalPlaces = scan.Kind == NumberKind.Integer ? 0 : 2;
            f.Target.Value = (decimal)current; f.Target.Enabled = f.Apply.Enabled = f.Hold.Enabled = true;
            f.Current.Text = "Candidate assigned · verify in game"; tabs.SelectedIndex = 0;
            SetStatus("Assigned to " + f.Name + ". Apply a small change and verify its in-game effect before holding it.");
        }
        void TryApply(Feature f)
        {
            if (busy || f.Location == null || !Ready()) { f.Hold.Checked = false; return; }
            try { memory.Write(f.Location.Address, f.Location.Allocation, f.Kind, (double)f.Target.Value); SetStatus("Write sent to " + f.Name + ". Check the result in game."); }
            catch (Exception error) { f.Hold.Checked = false; SetStatus(error.Message); }
        }
        void UpdateValues()
        {
            if (busy || memory == null) return;
            if (!memory.Alive)
            {
                ClearBindings(); ClearScan(); memory.Dispose(); memory = null; connection.Text = "Game closed · disconnected";
                SetStatus("All addresses were cleared. Reconnect and recalibrate after loading another save."); return;
            }
            foreach (Feature f in features)
            {
                if (f.Location == null) continue;
                double value;
                if (!memory.TryValue(f.Location.Address, f.Kind, out value) || value < 0 || value > 1000000)
                {
                    f.Hold.Checked = false; f.Location = null; f.Current.Text = "Address lost · recalibrate"; f.Target.Enabled = f.Apply.Enabled = f.Hold.Enabled = false; continue;
                }
                f.Current.Text = "Read: " + value.ToString("N2", CultureInfo.CurrentCulture);
                if (f.Hold.Checked) TryApply(f);
            }
        }
        public string BuildReport()
        {
            var report = new StringBuilder(); report.AppendLine("Stellar Blade · All in One 0.4.0 (manual calibration)");
            report.AppendLine("UTC date: " + DateTime.UtcNow.ToString("u")); report.AppendLine("Verified automatic profile: NO");
            report.AppendLine("Connected process: " + (memory != null && memory.Alive ? "SB-Win64-Shipping" : "NO"));
            if (memory != null && memory.Alive)
            {
                try
                {
                    string path = memory.Process.MainModule.FileName;
                    report.AppendLine("Executable version: " + FileVersionInfo.GetVersionInfo(path).FileVersion);
                    DirectoryInfo parent = new FileInfo(path).Directory;
                    for (int i = 0; parent != null && i < 8; i++, parent = parent.Parent)
                    {
                        string manifest = Path.Combine(parent.FullName, "appmanifest_3489700.acf");
                        if (!File.Exists(manifest)) continue;
                        Match match = Regex.Match(File.ReadAllText(manifest), "\"buildid\"\\s+\"([0-9]+)\"", RegexOptions.IgnoreCase);
                        report.AppendLine("Steam Build ID: " + (match.Success ? match.Groups[1].Value : "Unavailable")); break;
                    }
                }
                catch { report.AppendLine("Version / Build ID: unavailable"); }
            }
            foreach (Feature f in features) report.AppendLine(f.Name + ": " + (f.Location == null ? "not calibrated" : "candidate assigned; in-game effect not verified") + "; hold=" + f.Hold.Checked);
            if (scan != null) report.AppendLine("Last scan: " + features[scanFeature].Name + "; type=" + scan.Kind + "; candidates=" + scan.Count + "; MB read=" + scan.BytesRead / 1048576);
            report.AppendLine("Status: " + status.Text); return report.ToString();
        }
        void ExportReport()
        {
            using (var dialog = new SaveFileDialog { Filter = "Text report|*.txt", FileName = "StellarBlade-report.txt" })
            {
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                try { File.WriteAllText(dialog.FileName, BuildReport(), new UTF8Encoding(true)); SetStatus("Report saved. Include it in a support request if needed."); }
                catch (Exception error) { SetStatus("Could not save: " + error.Message); }
            }
        }
        void OnClosing(object sender, FormClosingEventArgs e)
        {
            PauseAll();
            if (busy) { closingRequested = true; cancellation.Cancel(); e.Cancel = true; SetStatus("Canceling scan and removing its temporary files…"); return; }
            timer.Stop(); if (hotkey) UnregisterHotKey(Handle, 8); ClearScan();
            if (memory != null) { memory.Dispose(); memory = null; }
        }
        protected override void WndProc(ref Message message)
        {
            if (message.Msg == 0x0312 && message.WParam.ToInt32() == 8) { PauseAll(); SetStatus("F8: value holding stopped. Previously applied values are not restored."); }
            base.WndProc(ref message);
        }
        public void RenderPreview(string path, int tab)
        {
            previewMode = true; ShowInTaskbar = false; Opacity = 0; tabs.SelectedIndex = tab; Show(); Application.DoEvents();
            using (var bitmap = new Bitmap(Width, Height)) { DrawToBitmap(bitmap, new Rectangle(0, 0, Width, Height)); bitmap.Save(path); }
            Close();
        }
    }


}

