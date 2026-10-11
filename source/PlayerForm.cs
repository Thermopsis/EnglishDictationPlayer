using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Xml.Serialization;
using NAudio.Wave;

namespace PhrasePlayer {
    public sealed class Preferences {
        public int Plays = 3;
        public decimal Gap = 1;
        public bool AutoNext = false;
        public decimal MaxLength = 8;
        public decimal MinLength = 2;
        public bool MergeShort = true;
        public double Speed = 1;
        public decimal SilenceMs = 300;
        public int Sensitivity = -26;
        public decimal LeadingPadding = 0.3m; public bool ShowBoundaryControls = false; public bool AdaptiveGap = false; public decimal GapMultiplier = 2;
        public bool DarkMode = false;
        public string Language = "zh";
    }
    public sealed partial class PlayerForm : Form {
        readonly Color background = Color.FromArgb(245, 248, 249), accent = Color.FromArgb(0, 125, 112), muted = Color.FromArgb(99, 118, 127);
        readonly Waveform wave = new Waveform();
        readonly Label file = new Label(), selected = new Label(), status = new Label();
        readonly Button open = new Button(), previous = new Button(), play = new Button(), next = new Button(), split = new Button(), whole = new Button(), settings = new Button();
        readonly ComboBox repeat = new ComboBox(), phrases = new ComboBox(), speed = new ComboBox();
        static readonly double[] Speeds = { 0.5, 0.8, 1, 1.2, 1.5, 2 };
        readonly NumericUpDown gap = new NumericUpDown();
        readonly CheckBox autoNext = new CheckBox();
        readonly HScrollBar scroll = new HScrollBar();
        readonly System.Windows.Forms.Timer clock = new System.Windows.Forms.Timer();
        readonly string preferencesPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PhrasePlayer", "settings.xml");
        Preferences prefs = new Preferences();
        AudioAnalysis audio;
        List<Phrase> segments = new List<Phrase>();
        Phrase selection;
        Phrase playbackRange;
        bool wholeFile;
        double activeSpeed = 1;
        int index = -1, completed, generation;
        WaveOutEvent output;
        RangeProvider range;
        CancellationTokenSource loadCancellation;
        bool loading, running, waiting, closing, updating;
        DateTime continueAt;
        readonly string startupFile;
        readonly bool silentTest;
        public PlayerForm(string input, string testPreferences) {
            startupFile = input;
            silentTest = testPreferences != null;
            if (silentTest) preferencesPath = testPreferences;
            try { if (File.Exists(preferencesPath)) using (var stream = File.OpenRead(preferencesPath)) prefs = (Preferences)new XmlSerializer(typeof(Preferences)).Deserialize(stream); } catch { prefs = new Preferences(); }
            prefs.MaxLength = Math.Max(2, Math.Min(30, prefs.MaxLength));
            prefs.MinLength = Math.Max(0.2m, Math.Min(prefs.MaxLength, prefs.MinLength));
            prefs.SilenceMs = Math.Max(150, Math.Min(1500, prefs.SilenceMs));
            prefs.LeadingPadding = Math.Max(0, Math.Min(1, prefs.LeadingPadding)); prefs.GapMultiplier = Math.Max(.5m, Math.Min(5, prefs.GapMultiplier));
            prefs.Sensitivity = Math.Max(-45, Math.Min(-10, prefs.Sensitivity));
            Text = L("英语听写播放器") + (silentTest ? L(" [静音测试]") : ""); Name = "PhrasePlayer";
            using (var iconStream = typeof(PlayerForm).Assembly.GetManifestResourceStream("PhrasePlayer.AppIcon.ico"))
            using (var embeddedIcon = new Icon(iconStream)) Icon = (Icon)embeddedIcon.Clone();
            Font = new Font("Microsoft YaHei UI", 10f);
            BackColor = background; ForeColor = Color.FromArgb(36, 53, 63);
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(960, 570); MinimumSize = new Size(840, 530);
            AutoScaleMode = AutoScaleMode.Dpi; KeyPreview = true; AllowDrop = true;
            var root = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(24, 18, 24, 12), ColumnCount = 1, RowCount = 9 };
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 52));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 24));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 42)); root.RowStyles.Add(new RowStyle(SizeType.Absolute, 0));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 114));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
            Controls.Add(root);
            var head = new Panel { Dock = DockStyle.Fill };
            var title = new Label { Text = L("英语听写播放器"), AutoSize = true, Font = new Font(Font.FontFamily, 19f, FontStyle.Bold), Location = new Point(0, 4) };
            StyleButton(open, L("打开音频"), true); open.Size = new Size(114, 38); open.Dock = DockStyle.Right;
            StyleButton(settings, L("设置"), false); settings.Size = new Size(96, 38); settings.Dock = DockStyle.Right;
            head.Controls.Add(title); head.Controls.Add(settings); head.Controls.Add(open); root.Controls.Add(head, 0, 0);
            file.Text = L("MP3 / WAV / M4A / AAC / WMA · 也可以把音频拖进窗口");
            file.Dock = DockStyle.Fill; file.TextAlign = ContentAlignment.MiddleLeft; file.AutoEllipsis = true; file.ForeColor = muted;
            root.Controls.Add(file, 0, 1);
            wave.Dock = DockStyle.Fill; wave.Margin = new Padding(0, 3, 0, 2); root.Controls.Add(wave, 0, 2);
            scroll.Dock = DockStyle.Fill; scroll.Margin = new Padding(0, 2, 0, 5); scroll.Enabled = false; root.Controls.Add(scroll, 0, 3);
            var selectionRow = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, Margin = new Padding(0) };
            selectionRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 206)); selectionRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); selectionRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 110));
            phrases.DropDownStyle = ComboBoxStyle.DropDownList; phrases.Dock = DockStyle.Fill; phrases.Margin = new Padding(0, 7, 10, 0); phrases.AccessibleName = L("当前短语");
            selected.Text = L("单击选短语 · 拖动选片段 · 滚轮缩放"); selected.Dock = DockStyle.Fill; selected.TextAlign = ContentAlignment.MiddleLeft; selected.ForeColor = muted; selected.AutoEllipsis = true;
            StyleButton(split, L("分段设置"), false); split.Dock = DockStyle.Fill; split.Margin = new Padding(0, 3, 0, 3);
            selectionRow.Controls.Add(phrases, 0, 0); selectionRow.Controls.Add(selected, 1, 0); selectionRow.Controls.Add(split, 2, 0); root.Controls.Add(selectionRow, 0, 4);
            var controlRows = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, Margin = new Padding(0) };
            controlRows.RowStyles.Add(new RowStyle(SizeType.Absolute, 62)); controlRows.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            var controls = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, Padding = new Padding(0, 15, 0, 0), Margin = new Padding(0) };
            StyleButton(previous, L("上一段"), false); StyleButton(play, L("播放"), true); StyleButton(next, L("下一段"), false);
            foreach (var b in new[] { previous, play, next }) { b.Size = new Size(90, 38); b.Margin = new Padding(0, 0, 8, 0); controls.Controls.Add(b); }
            controls.Controls.Add(InlineLabel(L("播放次数"), 76));
            repeat.DropDownStyle = ComboBoxStyle.DropDownList; repeat.Items.AddRange(new object[] { L("1 次"), L("2 次"), L("3 次"), L("5 次"), L("10 次"), L("无限") }); repeat.Width = 74; repeat.Margin = new Padding(0, 5, 10, 0); repeat.AccessibleName = L("播放次数"); repeat.SelectedIndex = Array.IndexOf(new[] { 1, 2, 3, 5, 10, 0 }, prefs.Plays); if (repeat.SelectedIndex < 0) repeat.SelectedIndex = 2; controls.Controls.Add(repeat);
            controls.Controls.Add(InlineLabel(L("间隔"), 43));
            gap.Minimum = 0; gap.Maximum = 30; gap.DecimalPlaces = 1; gap.Increment = 0.5m; gap.Value = Math.Max(0, Math.Min(30, prefs.Gap)); gap.Width = 62; gap.Margin = new Padding(0, 5, 0, 0); gap.AccessibleName = L("重复间隔秒数"); controls.Controls.Add(gap); controls.Controls.Add(InlineLabel(L("秒"), 29));
            autoNext.Text = L("播完进入下一段"); autoNext.AutoSize = true; autoNext.Checked = prefs.AutoNext; autoNext.Margin = new Padding(8, 9, 0, 0); controls.Controls.Add(autoNext);
            var extra = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, Margin = new Padding(0) };
            StyleButton(whole, L("从头播放到尾"), false); whole.Size = new Size(140, 36); whole.Margin = new Padding(0, 0, 12, 0); extra.Controls.Add(whole);
            extra.Controls.Add(InlineLabel(L("倍速"), 43));
            speed.DropDownStyle = ComboBoxStyle.DropDownList; speed.Items.AddRange(new object[] { L("0.5 倍"), L("0.8 倍"), L("1.0 倍"), L("1.2 倍"), L("1.5 倍"), L("2.0 倍") }); speed.Width = 94; speed.Margin = new Padding(0, 4, 0, 0); speed.AccessibleName = L("播放倍速"); speed.SelectedIndex = Array.IndexOf(Speeds, prefs.Speed); if (speed.SelectedIndex < 0) speed.SelectedIndex = 2; extra.Controls.Add(speed);
            controlRows.Controls.Add(controls, 0, 0); controlRows.Controls.Add(extra, 0, 1); root.Controls.Add(controlRows, 0, 7); BuildEditing(root);
            status.Text = L("空格：播放 / 暂停    ← →：切换片段    Enter：从头重播");
            status.Dock = DockStyle.Fill; status.ForeColor = muted; status.TextAlign = ContentAlignment.MiddleLeft; status.AutoEllipsis = true; root.Controls.Add(status, 0, 8);
            open.Click += delegate { using (var dialog = new OpenFileDialog { Title = L("打开音频"), Filter = L("音频文件|*.mp3;*.wav;*.m4a;*.aac;*.wma;*.flac;*.mp4|所有文件|*.*") }) if (dialog.ShowDialog(this) == DialogResult.OK) LoadFile(dialog.FileName); };
            previous.Click += delegate { MovePhrase(-1); }; next.Click += delegate { MovePhrase(1); }; play.Click += delegate { Toggle(); };
            whole.Click += delegate { if (audio == null || loading) return; Stop(); wholeFile = true; running = true; StartClip(); };
            speed.SelectedIndexChanged += delegate {
                if (updating) return;
                if (running && !waiting && output != null) {
                    double position = CurrentTime;
                    bool paused = output.PlaybackState == PlaybackState.Paused;
                    StartClip(position);
                    if (paused && output != null) { output.Pause(); play.Text = L("继续"); status.Text = L("已暂停"); }
                }
                SavePreferences();
            };
            phrases.SelectedIndexChanged += delegate { if (!updating && phrases.SelectedIndex >= 0) Choose(phrases.SelectedIndex); };
            split.Click += delegate { ShowSettings(); };
            settings.Click += delegate { ShowApplicationSettings(); };
            wave.SelectAt += delegate(double t) { if (segments.Count > 0) { Choose(Nearest(t)); wave.EditTime = t; UpdateEnabled(); wave.Invalidate(); } };
            wave.SelectRange += delegate(double a, double b) { Stop(); index = -1; selection = new Phrase(a, b) { Manual = true }; UpdateSelection(); };
            wave.ViewChanged += UpdateScroll;
            scroll.Scroll += delegate { if (!updating && audio != null) { wave.ViewStart = scroll.Value / 1000.0; wave.Invalidate(); } };
            repeat.SelectedIndexChanged += delegate { SavePreferences(); }; gap.ValueChanged += delegate { SavePreferences(); }; autoNext.CheckedChanged += delegate { SavePreferences(); };
            DragEnter += delegate(object sender, DragEventArgs e) { e.Effect = !loading && e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None; };
            DragDrop += delegate(object sender, DragEventArgs e) { var paths = e.Data.GetData(DataFormats.FileDrop) as string[]; if (paths != null && paths.Length > 0) LoadFile(paths[0]); };
            clock.Interval = 30; clock.Tick += Tick; clock.Start();
            Shown += delegate { if (startupFile != null && File.Exists(startupFile)) LoadFile(startupFile); };
            FormClosing += delegate(object sender, FormClosingEventArgs e) {
                closing = true;
                if (loading) { e.Cancel = true; if (loadCancellation != null) loadCancellation.Cancel(); Stop(); status.Text = L("正在关闭…"); return; }
                Stop(); clock.Stop(); clock.Dispose(); SavePreferences(); if (audio != null) audio.Dispose();
            };
            RefreshLanguage(); ApplyAppearance(); UpdateEnabled();
        }
        Label InlineLabel(string text, int width) { return new Label { Text = text, Width = width, Height = 38, TextAlign = ContentAlignment.MiddleCenter, ForeColor = muted, Margin = new Padding(0) }; }
        void StyleButton(Button b, string text, bool primary) {
            b.Tag = primary ? "primary" : "secondary";
            b.Text = text; b.FlatStyle = FlatStyle.Flat; b.FlatAppearance.BorderSize = primary ? 0 : 1;
            b.FlatAppearance.BorderColor = Color.FromArgb(210, 221, 225); b.BackColor = primary ? accent : Color.White;
            b.ForeColor = primary ? Color.White : ForeColor; b.Cursor = Cursors.Hand; b.UseVisualStyleBackColor = false;
        }
        async void LoadFile(string path) {
            if (loading || closing) return;
            Stop(); loading = true; UpdateEnabled(); status.Text = L("正在分析音频…");
            loadCancellation = new CancellationTokenSource(); var token = loadCancellation.Token;
            try {
                AudioAnalysis result = await Task.Run(() => AudioLoader.Load(path, token, percent => {
                    if (!closing && IsHandleCreated) try { BeginInvoke((Action)(() => { if (loading && !closing) status.Text = L("正在分析音频… ") + percent + "%"; })); } catch (InvalidOperationException) {}
                }));
                if (closing || token.IsCancellationRequested) { result.Dispose(); return; }
                if (audio != null) audio.Dispose(); audio = result;
                wave.Audio = audio; wave.ViewStart = 0; wave.ViewLength = Math.Min(120, audio.Duration); wave.CursorTime = -1;
                file.Text = Path.GetFileName(path) + "   ·   " + Waveform.FormatTime(audio.Duration);
                Resegment();
            } catch (OperationCanceledException) {} catch (Exception e) {
                if (!closing) { status.Text = L("打开失败，原有音频仍可使用。"); MessageBox.Show(this, L("无法打开这段音频。请确认文件有效且系统支持其格式。\n\n") + e.Message, L("打开音频"), MessageBoxButtons.OK, MessageBoxIcon.Warning); }
            } finally {
                loading = false; loadCancellation.Dispose(); loadCancellation = null;
                if (closing && !IsDisposed) Close(); else if (!closing) UpdateEnabled();
            }
        }
        void Resegment() {
            Stop(); editHistory.Clear(); wave.EditTime = -1; segments = Segmenter.Split(audio, (double)prefs.MinLength, (double)prefs.MaxLength, (double)prefs.SilenceMs / 1000, prefs.Sensitivity, prefs.MergeShort, (double)prefs.LeadingPadding);
            wave.Phrases = segments;
            updating = true; phrases.Items.Clear();
            for (int i = 0; i < segments.Count; i++) phrases.Items.Add(string.Format(L("第 {0} 段   {1}"), i + 1, Waveform.FormatTime(segments[i].Start)));
            updating = false;
            if (segments.Count > 0) { Choose(0); status.Text = L("已自动分为 ") + segments.Count + L(" 段。单击选段，拖动可自由选取片段。"); }
            else { index = -1; selection = null; wave.Selected = null; selected.Text = L("没有检测到语音，可以拖动手动选段。"); status.Text = L("未找到明显声音。可调整分段设置或手动选段。"); }
            UpdateScroll(); UpdateEnabled(); wave.Invalidate();
        }
        void Choose(int i) {
            if (audio == null || segments.Count == 0 || i < 0 || i >= segments.Count) return;
            Stop(); index = i; selection = segments[i]; UpdateSelection(); wave.ShowPhrase(selection);
            status.Text = L("空格：播放 / 暂停    ← →：切换片段    Enter：从头重播");
        }
        int Nearest(double t) {
            int best = 0; double distance = double.MaxValue;
            for (int i = 0; i < segments.Count; i++) {
                double d = t < segments[i].Start ? segments[i].Start - t : t > segments[i].End ? t - segments[i].End : 0;
                if (d < distance) { distance = d; best = i; }
            }
            return best;
        }
        void MovePhrase(int direction) {
            if (loading || selection == null || segments.Count == 0) return;
            int target;
            if (index >= 0) target = index + direction;
            else if (direction > 0) { target = segments.FindIndex(p => p.Start >= selection.End - 0.001); if (target < 0) target = segments.Count - 1; }
            else { target = segments.FindLastIndex(p => p.End <= selection.Start + 0.001); if (target < 0) target = 0; }
            Choose(Math.Max(0, Math.Min(segments.Count - 1, target)));
        }
        void UpdateSelection() {
            wave.Selected = selection; wave.CursorTime = -1; wave.EditTime = -1;
            updating = true; phrases.SelectedIndex = index; updating = false;
            selected.Text = (index < 0 ? L("手动片段") : string.Format(L("{0} / {1} 段"), index + 1, segments.Count)) + string.Format(L("   {0:0.00} — {1:0.00} 秒   ·   {2:0.0} 秒"), selection.Start, selection.End, selection.Duration);
            wave.Invalidate(); UpdateEnabled();
        }
        void UpdateScroll() {
            if (audio == null) return;
            updating = true;
            scroll.Minimum = 0; scroll.LargeChange = Math.Max(1, (int)(wave.ViewLength * 1000));
            scroll.Maximum = Math.Max(scroll.LargeChange - 1, (int)Math.Ceiling(audio.Duration * 1000) - 1);
            scroll.SmallChange = Math.Max(1, (int)(wave.ViewLength * 100));
            scroll.Value = Math.Max(0, Math.Min(scroll.Maximum - scroll.LargeChange + 1, (int)(wave.ViewStart * 1000)));
            scroll.Enabled = !loading && audio.Duration > wave.ViewLength; updating = false;
        }
        int TotalPlays { get { return new[] { 1, 2, 3, 5, 10, 0 }[Math.Max(0, repeat.SelectedIndex)]; } }
        void Toggle() {
            if (loading || (!wholeFile && selection == null)) return;
            if (running) {
                if (waiting) Stop();
                else if (output != null && output.PlaybackState == PlaybackState.Playing) { output.Pause(); play.Text = L("继续"); status.Text = L("已暂停 · Enter 从片段开头重播"); }
                else if (output != null) { output.Play(); play.Text = L("暂停"); }
            } else { completed = 0; running = true; StartClip(); }
        }
        double CurrentTime { get { return playbackRange == null || output == null ? 0 : Math.Min(playbackRange.End, playbackRange.Start + output.GetPosition() / (double)range.WaveFormat.AverageBytesPerSecond * activeSpeed); } }
        void StartClip(double resumeAt = -1) {
            ReleaseOutput(); waiting = false;
            int version = generation;
            try {
                var clip = wholeFile ? new Phrase(0, audio.Duration) : selection;
                playbackRange = new Phrase(resumeAt >= 0 ? Math.Min(resumeAt, clip.End) : clip.Start, clip.End);
                activeSpeed = Speeds[Math.Max(0, speed.SelectedIndex)];
                range = new RangeProvider(audio.CachePath, playbackRange);
                output = new WaveOutEvent { DesiredLatency = 80, NumberOfBuffers = 2 };
                output.Init(activeSpeed == 1 ? (IWaveProvider)range : new TempoProvider(range, activeSpeed));
                output.Volume = silentTest ? 0 : 1;
                output.PlaybackStopped += delegate(object sender, StoppedEventArgs e) {
                    if (closing || version != generation || !running) return;
                    if (InvokeRequired) { try { BeginInvoke((Action)(() => ClipEnded(version, e.Exception))); } catch (InvalidOperationException) {} }
                    else ClipEnded(version, e.Exception);
                };
                output.Play(); UpdateEnabled(); play.Text = L("暂停"); status.Text = wholeFile ? L("全文连续播放 · ") + activeSpeed + L(" 倍速 · Esc 停止") : string.Format(L("正在播放 · 第 {0} 遍{1} · {2} 倍速"), completed + 1, TotalPlays == 0 ? L(" / 无限循环") : " / " + TotalPlays, activeSpeed);
            } catch (Exception e) { Stop(); status.Text = L("播放失败：") + e.Message; }
        }
        void ClipEnded(int version, Exception error) {
            if (closing || version != generation || !running) return;
            if (error != null) { Stop(); status.Text = L("播放失败：") + error.Message; return; }
            if (wholeFile) { Stop(); status.Text = L("全文播放完成"); return; }
            completed++;
            int target = RepeatPlan.Next(completed, TotalPlays, autoNext.Checked && index >= 0, index, segments.Count);
            // A manual range has index -1 but still supports unlimited / counted replay.
            bool replay = TotalPlays == 0 || completed < TotalPlays;
            if (!replay && target < 0) { Stop(); status.Text = L("本段播放完成 · Enter 重播，方向键切换片段"); return; }
            waiting = true; continueAt = DateTime.UtcNow.AddSeconds(PhraseEditor.WaitSeconds(prefs.AdaptiveGap, selection.Duration, (double)prefs.GapMultiplier, (double)gap.Value));
            play.Text = L("停止");
            wave.CursorTime = selection.End; wave.Invalidate();
        }
        void Tick(object sender, EventArgs e) {
            if (!running || (!wholeFile && selection == null)) return;
            if (waiting) {
                double remaining = (continueAt - DateTime.UtcNow).TotalSeconds;
                if (remaining > 0) { status.Text = string.Format(L("间隔 {0:0.0} 秒 · 点击播放按钮可停止"), remaining); return; }
                int target = RepeatPlan.Next(completed, TotalPlays, autoNext.Checked && index >= 0, index, segments.Count);
                bool replay = TotalPlays == 0 || completed < TotalPlays;
                if (!replay) {
                    if (target < 0) { Stop(); return; }
                    // Change selection without Choose(), which would end the playback sequence.
                    index = target; selection = segments[index]; completed = 0; UpdateSelection(); wave.ShowPhrase(selection);
                }
                StartClip();
            } else if (output != null) {
                try {
                    wave.CursorTime = CurrentTime;
                    if (wholeFile && (wave.CursorTime < wave.ViewStart || wave.CursorTime > wave.ViewStart + wave.ViewLength)) { wave.ViewStart = Math.Min(Math.Max(0, audio.Duration - wave.ViewLength), Math.Max(0, wave.CursorTime)); UpdateScroll(); }
                    wave.Invalidate();
                } catch (ObjectDisposedException) {}
            }
        }
        void ReleaseOutput() {
            generation++;
            if (output != null) { output.Stop(); output.Dispose(); output = null; }
            if (range != null) { range.Dispose(); range = null; }
        }
        void Stop() { running = false; waiting = false; wholeFile = false; ReleaseOutput(); play.Text = L("播放"); wave.CursorTime = -1; wave.Invalidate(); }
        void UpdateEnabled() {
            bool ready = audio != null && !loading;
            open.Enabled = !loading; play.Enabled = ready && (selection != null || wholeFile); previous.Enabled = next.Enabled = ready && segments.Count > 0;
            split.Enabled = ready; whole.Enabled = ready; phrases.Enabled = ready && segments.Count > 0; wave.Enabled = !loading;
            repeat.Enabled = gap.Enabled = autoNext.Enabled = speed.Enabled = !loading;
            UpdateEditingEnabled(ready); if (audio != null) UpdateScroll();
        }
        void SavePreferences() {
            if (updating || repeat.SelectedIndex < 0) return;
            prefs.Plays = TotalPlays; prefs.Gap = gap.Value; prefs.AutoNext = autoNext.Checked;
            if (speed.SelectedIndex >= 0) prefs.Speed = Speeds[speed.SelectedIndex];
            try {
                Directory.CreateDirectory(Path.GetDirectoryName(preferencesPath));
                string temp = preferencesPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
                try {
                    using (var stream = File.Create(temp)) new XmlSerializer(typeof(Preferences)).Serialize(stream, prefs);
                    if (File.Exists(preferencesPath)) File.Replace(temp, preferencesPath, null); else File.Move(temp, preferencesPath);
                } finally { if (File.Exists(temp)) File.Delete(temp); }
            } catch (IOException) {} catch (UnauthorizedAccessException) {}
        }
        void ShowSettings() {
            using (var dialog = new Form { Text = L("自动分段设置"), Font = Font, ClientSize = new Size(560, 478), FormBorderStyle = FormBorderStyle.FixedDialog, StartPosition = FormStartPosition.CenterParent, MaximizeBox = false, MinimizeBox = false, BackColor = background }) {
                var preset = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Location = new Point(218, 24), Width = 310, AccessibleName = L("常用分段推荐") };
                preset.Items.AddRange(new object[] { L("自定义"), L("短语练习：1–5 秒"), L("常规听写：2–8 秒（推荐）"), L("长句练习：4–15 秒") }); preset.SelectedIndex = 0;
                var minimum = new NumericUpDown { Minimum = 0.2m, Maximum = 30, DecimalPlaces = 1, Increment = 0.5m, Value = prefs.MinLength, Location = new Point(218, 72), Width = 220, AccessibleName = L("片段最短秒数") };
                var maximum = new NumericUpDown { Minimum = 2, Maximum = 30, DecimalPlaces = 1, Increment = 0.5m, Value = prefs.MaxLength, Location = new Point(218, 120), Width = 220, AccessibleName = L("片段最长秒数") };
                var merge = new CheckBox { Text = L("合并过短片段（优先连接下一段）"), AutoSize = true, Checked = prefs.MergeShort, Location = new Point(24, 168), AccessibleName = L("合并过短片段") };
                var silence = new NumericUpDown { Minimum = 150, Maximum = 1500, Increment = 50, Value = prefs.SilenceMs, Location = new Point(218, 261), Width = 220, AccessibleName = L("分段停顿毫秒") };
                var sensitivity = new NumericUpDown { Minimum = -45, Maximum = -10, Value = prefs.Sensitivity, Location = new Point(218, 306), Width = 220, AccessibleName = L("静音阈值") };
                var leading = new NumericUpDown { Minimum = 0, Maximum = 1, DecimalPlaces = 2, Increment = .1m, Value = prefs.LeadingPadding, Location = new Point(218, 213), Width = 220, AccessibleName = L("开头预留（秒）") };
                string[] labels = { L("常用分段推荐"), L("最短片段（秒）"), L("最长片段（秒）"), L("开头预留（秒）"), L("分段停顿（毫秒）"), L("静音阈值（相对 dB）") };
                int[] positions = { 28, 76, 124, 217, 265, 310 };
                for (int i = 0; i < labels.Length; i++) dialog.Controls.Add(new Label { Text = labels[i], AutoSize = true, Location = new Point(24, positions[i]) });
                dialog.Controls.AddRange(new Control[] { preset, minimum, maximum, merge, leading, silence, sensitivity });
                dialog.Controls.Add(new Label { Text = L("最短值用于合并短段；关闭合并会保留短词。\n长停顿不强行连接；尾段或孤立短词可能不足最短值。\n按停顿分段，不识别文字。分得太碎可增大停顿。"), Size = new Size(512, 66), ForeColor = muted, Location = new Point(24, 351) });
                bool applyingPreset = false;
                EventHandler custom = delegate { if (!applyingPreset) preset.SelectedIndex = 0; };
                minimum.ValueChanged += custom; maximum.ValueChanged += custom; silence.ValueChanged += custom; sensitivity.ValueChanged += custom; merge.CheckedChanged += custom;
                preset.SelectedIndexChanged += delegate {
                    if (preset.SelectedIndex <= 0) return;
                    applyingPreset = true;
                    decimal[] minima = { 0, 1, 2, 4 }, maxima = { 0, 5, 8, 15 }, pauses = { 0, 250, 300, 500 };
                    int pick = preset.SelectedIndex; minimum.Value = minima[pick]; maximum.Value = maxima[pick]; silence.Value = pauses[pick]; sensitivity.Value = -26; merge.Checked = true;
                    applyingPreset = false;
                };
                var apply = new Button { Text = L("重新分段"), Location = new Point(328, 430), Size = new Size(110, 32) };
                var cancel = new Button { Text = L("取消"), DialogResult = DialogResult.Cancel, Location = new Point(222, 430), Size = new Size(90, 32) };
                apply.Click += delegate {
                    if (minimum.Value > maximum.Value) { MessageBox.Show(dialog, L("最短片段不能大于最长片段。"), L("分段区间"), MessageBoxButtons.OK, MessageBoxIcon.Information); return; }
                    dialog.DialogResult = DialogResult.OK;
                };
                dialog.Controls.Add(apply); dialog.Controls.Add(cancel); dialog.AcceptButton = apply; dialog.CancelButton = cancel;
                ApplyDialogAppearance(dialog);
                if (dialog.ShowDialog(this) == DialogResult.OK) { prefs.LeadingPadding = leading.Value; prefs.MinLength = minimum.Value; prefs.MaxLength = maximum.Value; prefs.MergeShort = merge.Checked; prefs.SilenceMs = silence.Value; prefs.Sensitivity = (int)sensitivity.Value; SavePreferences(); Resegment(); }
            }
        }
        protected override bool ProcessCmdKey(ref Message msg, Keys keyData) {
            if (keyData == Keys.F1) { ShowInformation(this, true); return true; }
            // Keep normal editing/navigation in comboboxes and numeric controls.
            bool editing = ActiveControl is NumericUpDown || ActiveControl is ComboBox;
            if (!editing && keyData == Keys.Space) { Toggle(); return true; }
            if (!editing && keyData == Keys.Left) { MovePhrase(-1); return true; }
            if (!editing && keyData == Keys.Right) { MovePhrase(1); return true; }
            if (!editing && keyData == Keys.Enter && !loading && selection != null) { Stop(); completed = 0; running = true; StartClip(); return true; }
            if (keyData == Keys.Escape) { Stop(); status.Text = L("已停止"); return true; }
            return base.ProcessCmdKey(ref msg, keyData);
        }
    }
}
