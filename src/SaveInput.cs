using System;
using System.Drawing;
using System.IO;
using System.Text;
using System.Windows.Forms;

namespace StellarAdvanced
{
    static class SaveInput
    {
        public static string Normalize(string input)
        {
            string path = (input ?? "").Trim();
            if (path.Length >= 2 && ((path[0] == '"' && path[path.Length - 1] == '"') || (path[0] == '\'' && path[path.Length - 1] == '\''))) path = path.Substring(1, path.Length - 2).Trim();
            if (path.Length == 0) throw new InvalidDataException("Choose the .sav file or paste its full path, including the file name.");
            Uri uri;
            if (path.StartsWith("file:", StringComparison.OrdinalIgnoreCase) && Uri.TryCreate(path, UriKind.Absolute, out uri) && uri.IsFile) path = uri.LocalPath;
            return Path.GetFullPath(Environment.ExpandEnvironmentVariables(path));
        }
        public static string Validate(string input)
        {
            string path = Normalize(input);
            if (Directory.Exists(path)) throw new InvalidDataException("You selected a folder. Open it and select a save file, such as StellarBladeSave00.sav.");
            if (!File.Exists(path)) throw new InvalidDataException("The file was not found at that path. Check your USB drive and select the actual .sav file, not a shortcut.");
            if (!String.Equals(Path.GetExtension(path), ".sav", StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("The file does not end in .sav. In File Explorer, enable View > Show > File name extensions and check that it is not .sav.txt, a ZIP or a shortcut.");
            long length = new FileInfo(path).Length;
            if (length < 8) throw new InvalidDataException("The file is empty or incomplete (less than 8 bytes). Copy the save again from your gaming PC.");
            if (length > 128 * 1024 * 1024) throw new InvalidDataException("The save exceeds the 128 MB size limit.");
            return path;
        }
        public static string Report(string input, Exception error, SaveEditor editor)
        {
            var report = new StringBuilder(); report.AppendLine("STELLAR BLADE · SAVE OPENING REPORT · 0.4.0");
            report.AppendLine(editor == null ? "RESULT: SAVE NOT LOADED" : "RESULT: SAVE LOADED");
            try
            {
                string path = Normalize(input); report.AppendLine("File: " + Path.GetFileName(path));
                if (File.Exists(path))
                {
                    report.AppendLine("Size: " + new FileInfo(path).Length + " bytes");
                    byte[] header = new byte[8]; int count;
                    using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite)) count = stream.Read(header, 0, header.Length);
                    report.AppendLine("Header (up to 8 bytes): " + BitConverter.ToString(header, 0, count));
                }
                else report.AppendLine("The file does not exist at the specified path.");
            }
            catch (Exception probeError) { report.AppendLine("File check: " + probeError.Message); }
            if (error != null)
            {
                report.AppendLine(); report.AppendLine("Reason: " + error.Message); report.AppendLine("Error type: " + error.GetType().Name);
                report.AppendLine(); report.AppendLine("Select a main save, normally StellarBladeSave00.sav, StellarBladeSave01.sav, etc. Settings files do not contain inventory.");
                report.AppendLine("If using a USB drive, select the save copied to that drive. The game installation and save folders are different.");
                report.AppendLine("This report identifies unsupported formats or structures. Renaming or moving a file does not convert its format.");
            }
            if (editor != null)
            {
                report.AppendLine("Format: " + (editor.Archive.HasEvas ? "EVAS v1; CRC32 verified" : "GVAS"));
                report.AppendLine("Inventory read: " + editor.Inventory.Count + " entries; editable items: " + editor.Stacks().Count + "; cans: " + editor.Cans() + "/49.");
            }
            report.AppendLine(); report.AppendLine("This check does not modify or upload your save.");
            return report.ToString();
        }
    }
    sealed class SaveDiagnosticForm : Form
    {
        public SaveDiagnosticForm(string report)
        {
            Text = "Save not loaded · error details"; Font = new Font("Segoe UI", 10);
            ClientSize = new Size(720, 470); MinimumSize = new Size(570, 390); StartPosition = FormStartPosition.CenterParent;
            var root = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(14), ColumnCount = 1, RowCount = 3 };
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 52)); root.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); root.RowStyles.Add(new RowStyle(SizeType.Absolute, 46)); Controls.Add(root);
            root.Controls.Add(new Label { Text = "The save could not be opened. The reason is shown below.\nCopy this report to include it in a support request.", Dock = DockStyle.Fill }, 0, 0);
            root.Controls.Add(new TextBox { Text = report, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Both, Dock = DockStyle.Fill, BackColor = Color.White }, 0, 1);
            var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft };
            var close = new Button { Text = "Close", Width = 110, Height = 34, DialogResult = DialogResult.OK }; buttons.Controls.Add(close); AcceptButton = close;
            var copy = new Button { Text = "Copy report", Width = 150, Height = 34 };
            copy.Click += delegate { try { Clipboard.SetText(report); copy.Text = "Report copied"; } catch (Exception e) { MessageBox.Show(this, e.Message, "Could not copy"); } }; buttons.Controls.Add(copy);
            var save = new Button { Text = "Save report…", Width = 160, Height = 34 };
            save.Click += delegate
            {
                using (var dialog = new SaveFileDialog { Filter = "Text report|*.txt", FileName = "StellarBlade-error-apertura.txt", DefaultExt = "txt", AddExtension = true })
                {
                    if (dialog.ShowDialog(this) != DialogResult.OK) return;
                    try { File.WriteAllText(dialog.FileName, report, new UTF8Encoding(true)); } catch (Exception e) { MessageBox.Show(this, e.Message, "Could not save the report"); }
                }
            }; buttons.Controls.Add(save); root.Controls.Add(buttons, 0, 2);
        }
    }
}
