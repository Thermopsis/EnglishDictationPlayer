using System;
using System.Drawing;
using System.Windows.Forms;

namespace PhrasePlayer {
    public static class Appearance {
        public static Color Background(bool dark) { return dark ? Color.FromArgb(24, 25, 27) : Color.FromArgb(245, 248, 249); }
        public static Color Surface(bool dark) { return dark ? Color.FromArgb(37, 39, 42) : Color.White; }
        public static Color Ink(bool dark) { return dark ? Color.FromArgb(232, 235, 238) : Color.FromArgb(36, 53, 63); }
        public static Color Muted(bool dark) { return dark ? Color.FromArgb(172, 183, 191) : Color.FromArgb(99, 118, 127); }
        public static Color Accent(bool dark) { return dark ? Color.FromArgb(65, 178, 156) : Color.FromArgb(0, 125, 112); }
        public static void Apply(Control control, bool dark) {
            control.BackColor = Background(dark); control.ForeColor = Ink(dark);
            var button = control as Button;
            if (button != null) {
                bool primary = button.Tag as string == "primary";
                button.BackColor = primary ? Accent(dark) : Surface(dark);
                button.ForeColor = primary ? (dark ? Color.FromArgb(12, 30, 27) : Color.White) : Ink(dark);
                button.FlatStyle = FlatStyle.Flat; button.FlatAppearance.BorderSize = primary ? 0 : 1;
                button.FlatAppearance.BorderColor = dark ? Color.FromArgb(74, 80, 86) : Color.FromArgb(210, 221, 225);
                button.FlatAppearance.MouseOverBackColor = primary ? (dark ? Color.FromArgb(84, 198, 176) : Color.FromArgb(0, 145, 128)) : (dark ? Color.FromArgb(55, 59, 64) : Color.FromArgb(235, 241, 243));
            } else if (control is ComboBox || control is NumericUpDown || control is TextBox) {
                control.BackColor = Surface(dark);
                var combo = control as ComboBox; if (combo != null) combo.FlatStyle = FlatStyle.Flat;
            }
            foreach (Control child in control.Controls) Apply(child, dark);
            var wave = control as Waveform; if (wave != null) wave.ApplyTheme(dark);
        }
    }

    public sealed partial class PlayerForm {
        string L(string key) { return Localization.Get(key, prefs.Language); }
        void ApplyAppearance() {
            Appearance.Apply(this, prefs.DarkMode);
            file.ForeColor = selected.ForeColor = status.ForeColor = Appearance.Muted(prefs.DarkMode);
            Invalidate(true);
        }
        void ApplyDialogAppearance(Form dialog) { Appearance.Apply(dialog, prefs.DarkMode); }
        void LocalizeControls(Control root) {
            if (!(root is ComboBox) && !(root is TextBox) && !(root is NumericUpDown)) root.Text = Localization.ConvertExisting(root.Text, prefs.Language);
            if (root.AccessibleName != null) root.AccessibleName = Localization.ConvertExisting(root.AccessibleName, prefs.Language);
            foreach (Control child in root.Controls) LocalizeControls(child);
        }
        void RefreshLanguage() {
            SuspendLayout();
            try {
                LocalizeControls(this);
                Text = L("英语听写播放器") + (silentTest ? L(" [静音测试]") : "");
                bool english = prefs.Language == "en";
                ((TableLayoutPanel)split.Parent).ColumnStyles[2].Width = english ? 156 : 110;
                whole.Width = english ? 190 : 140; repeat.Width = english ? 96 : 74;
                foreach (Control sibling in speed.Parent.Controls) if (sibling is Label) sibling.Width = english ? 65 : 43;
                int repeatIndex = repeat.SelectedIndex, speedIndex = speed.SelectedIndex;
                updating = true;
                try {
                    repeat.Items.Clear(); foreach (string key in new[] { "1 次", "2 次", "3 次", "5 次", "10 次", "无限" }) repeat.Items.Add(L(key));
                    repeat.SelectedIndex = Math.Max(0, repeatIndex);
                    speed.Items.Clear(); foreach (string key in new[] { "0.5 倍", "0.8 倍", "1.0 倍", "1.2 倍", "1.5 倍", "2.0 倍" }) speed.Items.Add(L(key));
                    speed.SelectedIndex = Math.Max(0, speedIndex);
                    phrases.BeginUpdate();
                    try {
                        phrases.Items.Clear();
                        for (int i = 0; i < segments.Count; i++) phrases.Items.Add(string.Format(L("第 {0} 段   {1}"), i + 1, Waveform.FormatTime(segments[i].Start)));
                        phrases.SelectedIndex = index;
                    } finally { phrases.EndUpdate(); }
                } finally { updating = false; }
                double cursor = wave.CursorTime, editCursor = wave.EditTime;
                if (selection != null) UpdateSelection();
                wave.CursorTime = cursor; wave.EditTime = editCursor;
                wave.EmptyHint = L("打开一段音频，开始逐段听写");
                wave.AccessibleName = L("音频波形：单击选择短语，拖动选择片段，滚轮缩放");
                if (audio == null) {
                    file.Text = L("MP3 / WAV / M4A / AAC / WMA · 也可以把音频拖进窗口");
                    selected.Text = L("单击选短语 · 拖动选片段 · 滚轮缩放");
                }
                status.Text = L("空格：播放 / 暂停    ← →：切换片段    Enter：从头重播");
                wave.Invalidate();
            } finally { ResumeLayout(true); }
        }
        void ShowApplicationSettings() {
            using (var dialog = new Form { Text = L("设置"), Font = Font, ClientSize = new Size(540, 410), FormBorderStyle = FormBorderStyle.FixedDialog, StartPosition = FormStartPosition.CenterParent, MaximizeBox = false, MinimizeBox = false, Icon = Icon }) {
                var theme = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Location = new Point(160, 28), Width = 350, AccessibleName = L("皮肤") };
                theme.Items.AddRange(new object[] { L("默认模式"), L("黑色模式") }); theme.SelectedIndex = prefs.DarkMode ? 1 : 0;
                var language = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Location = new Point(160, 82), Width = 350, AccessibleName = L("语言") };
                language.Items.AddRange(new object[] { "中文", "English" }); language.SelectedIndex = prefs.Language == "en" ? 1 : 0;
                dialog.Controls.Add(new Label { Text = L("皮肤"), AutoSize = true, Location = new Point(26, 32) });
                dialog.Controls.Add(new Label { Text = L("语言"), AutoSize = true, Location = new Point(26, 86) });
                var hint = new Label { Text = L("皮肤与语言将在保存后立即生效，下次打开时保留。"), Location = new Point(26, 139), Size = new Size(484, 44) };
                var boundary = new CheckBox { Text = L("显示边界微调（±0.1 秒）"), AutoSize = true, Checked = prefs.ShowBoundaryControls, Location = new Point(26, 188) };
                var adaptive = new CheckBox { Text = L("按片段长度自动留白"), AutoSize = true, Checked = prefs.AdaptiveGap, Location = new Point(26, 224) };
                var ratio = new NumericUpDown { Minimum = .5m, Maximum = 5, DecimalPlaces = 1, Increment = .5m, Value = prefs.GapMultiplier, Location = new Point(360, 224), Width = 80, AccessibleName = L("留白倍数") };
                ratio.Enabled = adaptive.Checked; adaptive.CheckedChanged += delegate { ratio.Enabled = adaptive.Checked; };
                var formula = new Label { Text = L("留白 = 片段时长 × 倍数，最多 120 秒"), AutoSize = true, Location = new Point(26, 266) };
                dialog.Controls.AddRange(new Control[] { boundary, adaptive, ratio, formula });
                var about = new Button { Text = L("关于"), Location = new Point(26, 306), Size = new Size(110, 34) };
                var help = new Button { Text = L("帮助"), Location = new Point(148, 306), Size = new Size(110, 34) };
                var save = new Button { Text = L("保存设置"), Location = new Point(384, 362), Size = new Size(126, 34), DialogResult = DialogResult.OK, Tag = "primary" };
                var cancel = new Button { Text = L("取消"), Location = new Point(276, 362), Size = new Size(96, 34), DialogResult = DialogResult.Cancel };
                about.Click += delegate { ShowInformation(dialog, false); }; help.Click += delegate { ShowInformation(dialog, true); };
                dialog.Controls.AddRange(new Control[] { theme, language, hint, about, help, save, cancel });
                dialog.AcceptButton = save; dialog.CancelButton = cancel; ApplyDialogAppearance(dialog);
                if (dialog.ShowDialog(this) == DialogResult.OK) {
                    prefs.ShowBoundaryControls = boundary.Checked; prefs.AdaptiveGap = adaptive.Checked; prefs.GapMultiplier = ratio.Value; ApplyEditingOptions(); UpdateEnabled(); prefs.DarkMode = theme.SelectedIndex == 1; prefs.Language = language.SelectedIndex == 1 ? "en" : "zh";
                    SavePreferences(); RefreshLanguage(); ApplyAppearance();
                    status.Text = loading ? L("正在分析音频…") : running ? (waiting ? L("停止") : output != null && output.PlaybackState == NAudio.Wave.PlaybackState.Paused ? L("已暂停") : L("播放")) : L("设置已保存");
                }
            }
        }
        void ShowInformation(Form owner, bool help) {
            using (var dialog = new Form { Text = L(help ? "帮助" : "关于"), Font = Font, ClientSize = new Size(620, help ? 550 : 300), MinimumSize = new Size(500, 280), StartPosition = FormStartPosition.CenterParent, MaximizeBox = false, MinimizeBox = false, Icon = Icon, Padding = new Padding(22) }) {
                var close = new Button { Text = L("关闭"), Dock = DockStyle.Bottom, Height = 36, DialogResult = DialogResult.OK };
                var text = new TextBox { Multiline = true, ReadOnly = true, BorderStyle = BorderStyle.None, ScrollBars = ScrollBars.Vertical, Dock = DockStyle.Fill, Text = help ? HelpText.Get(prefs.Language) : HelpText.About(prefs.Language) };
                dialog.Controls.Add(text); dialog.Controls.Add(close); dialog.AcceptButton = close; dialog.CancelButton = close;
                dialog.Shown += delegate { text.SelectionStart = 0; text.SelectionLength = 0; close.Focus(); };
                ApplyDialogAppearance(dialog); dialog.ShowDialog(owner);
            }
        }
    }
}
