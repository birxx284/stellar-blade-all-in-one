using System;
using System.Drawing;
using System.Windows.Forms;

namespace StellarUnified
{
    static class Ui
    {
        public static readonly Color Background = Color.FromArgb(242, 245, 247), Ink = Color.FromArgb(28, 42, 51), Muted = Color.FromArgb(91, 108, 119), Accent = Color.FromArgb(12, 105, 91);
        public static void Style(Form form, string title, Size size, Size minimum)
        {
            form.Text = title; form.ClientSize = size; form.MinimumSize = minimum;
            form.Font = new Font("Segoe UI", 9.5f); form.ForeColor = Ink; form.BackColor = Background;
            form.AutoScaleMode = AutoScaleMode.Dpi; form.StartPosition = FormStartPosition.CenterParent;
        }
        public static TableLayoutPanel Row(params int[] widths)
        {
            var row = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = widths.Length, RowCount = 1, Margin = Padding.Empty };
            foreach (int width in widths) row.ColumnStyles.Add(width > 0 ? new ColumnStyle(SizeType.Absolute, width) : new ColumnStyle(SizeType.Percent, 100));
            row.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); return row;
        }
        public static Label Label(string text)
        { return new Label { Text = text, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, AutoEllipsis = true }; }
        public static Button Button(string text, EventHandler click, bool primary)
        {
            var button = new Button { Text = text, AccessibleName = text, Dock = DockStyle.Fill, Margin = new Padding(3), FlatStyle = FlatStyle.Flat, BackColor = primary ? Accent : Color.White, ForeColor = primary ? Color.White : Ink, UseVisualStyleBackColor = false };
            button.FlatAppearance.BorderColor = primary ? Accent : Color.FromArgb(204, 214, 220); button.Click += click;
            if (primary) button.EnabledChanged += delegate { button.BackColor = button.Enabled ? Accent : Color.FromArgb(220, 228, 231); button.ForeColor = button.Enabled ? Color.White : Muted; };
            return button;
        }
        public static void Review(IWin32Window owner, string text, string title)
        {
            using (var dialog = new Form())
            {
                Style(dialog, title, new Size(650, 440), new Size(520, 360));
                var root = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(12), ColumnCount = 1, RowCount = 2 };
                root.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); root.RowStyles.Add(new RowStyle(SizeType.Absolute, 40)); dialog.Controls.Add(root);
                root.Controls.Add(new TextBox { Text = text, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, Dock = DockStyle.Fill, BackColor = Color.White }, 0, 0);
                var close = Button("Back to editor", delegate { dialog.Close(); }, false); root.Controls.Add(close, 0, 1); dialog.AcceptButton = close; dialog.CancelButton = close;
                dialog.ShowDialog(owner);
            }
        }
    }
}
