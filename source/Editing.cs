using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace PhrasePlayer {
    public static class PhraseEditor {
        public static List<Phrase> Copy(List<Phrase> list) { return list.Select(p => new Phrase(p.Start, p.End) { Manual = p.Manual }).ToList(); }
        public static bool Merge(List<Phrase> list, int i) {
            if (i < 0 || i + 1 >= list.Count) return false;
            list[i] = new Phrase(list[i].Start, list[i + 1].End); list.RemoveAt(i + 1); return true;
        }
        public static bool Cut(List<Phrase> list, int i, double time) {
            if (i < 0 || i >= list.Count || time - list[i].Start < .1 - 1e-9 || list[i].End - time < .1 - 1e-9) return false;
            double end = list[i].End; list[i] = new Phrase(list[i].Start, time); list.Insert(i + 1, new Phrase(time, end)); return true;
        }
        public static double WaitSeconds(bool adaptive, double duration, double multiplier, double fixedGap) { return adaptive ? Math.Min(120, Math.Max(0, duration * multiplier)) : fixedGap; }
        public static void Nudge(Phrase p, bool start, double amount, double lower, double upper) {
            if (start) p.Start = Math.Max(lower, Math.Min(p.End - .1, p.Start + amount));
            else p.End = Math.Min(upper, Math.Max(p.Start + .1, p.End + amount));
        }
    }
    public sealed partial class PlayerForm {
        readonly Button mergeNext = new Button(), cutHere = new Button(), undoEdit = new Button(), zoomIn = new Button(), zoomOut = new Button(), fitAudio = new Button();
        readonly FlowLayoutPanel nudges = new FlowLayoutPanel();
        readonly List<List<Phrase>> editHistory = new List<List<Phrase>>();
        RowStyle nudgeHeight;
        void BuildEditing(TableLayoutPanel root) {
            var row = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, Margin = new Padding(0), Padding = new Padding(0, 3, 0, 0) };
            var buttons = new[] { mergeNext, cutHere, undoEdit, zoomIn, zoomOut, fitAudio };
            var labels = new[] { "合并下一段", "在此拆分", "撤销", "放大 +", "缩小 −", "显示全部" };
            int[] widths = { 150, 135, 80, 105, 105, 110 };
            for (int i = 0; i < buttons.Length; i++) { StyleButton(buttons[i], L(labels[i]), false); buttons[i].Size = new Size(widths[i], 32); buttons[i].Margin = new Padding(0, 0, 7, 0); row.Controls.Add(buttons[i]); }
            root.Controls.Add(row, 0, 5);
            nudges.Dock = DockStyle.Fill; nudges.WrapContents = false; nudges.Margin = new Padding(0);
            string[] names = { "起点 −0.1s", "起点 +0.1s", "终点 −0.1s", "终点 +0.1s" };
            for (int i = 0; i < 4; i++) {
                int pick = i; var b = new Button(); StyleButton(b, L(names[i]), false); b.Size = new Size(151, 32); b.Margin = new Padding(0, 0, 8, 0);
                b.Click += delegate {
                    if (selection == null || audio == null || loading) return;
                    Stop(); if (index >= 0) RememberEdit();
                    PhraseEditor.Nudge(selection, pick < 2, pick % 2 == 0 ? -.1 : .1, index > 0 ? segments[index - 1].End : 0, index >= 0 && index + 1 < segments.Count ? segments[index + 1].Start : audio.Duration);
                    wave.EditTime = -1; UpdateSelection();
                }; nudges.Controls.Add(b);
            }
            root.Controls.Add(nudges, 0, 6); nudgeHeight = root.RowStyles[6]; ApplyEditingOptions();
            mergeNext.Click += delegate { RememberEdit(); PhraseEditor.Merge(segments, index); RebuildEdited(index); };
            cutHere.Click += delegate { RememberEdit(); PhraseEditor.Cut(segments, index, wave.EditTime); RebuildEdited(index); };
            undoEdit.Click += delegate { if (editHistory.Count == 0) return; segments = editHistory[editHistory.Count - 1]; editHistory.RemoveAt(editHistory.Count - 1); RebuildEdited(Math.Max(0, Math.Min(index, segments.Count - 1))); };
            zoomIn.Click += delegate { wave.Zoom(.8); }; zoomOut.Click += delegate { wave.Zoom(1.25); }; fitAudio.Click += delegate { wave.FitAll(); };
        }
        void RememberEdit() { Stop(); if (editHistory.Count == 20) editHistory.RemoveAt(0); editHistory.Add(PhraseEditor.Copy(segments)); }
        void RebuildEdited(int target) {
            Stop(); wave.Phrases = segments; updating = true; phrases.Items.Clear();
            for (int i = 0; i < segments.Count; i++) phrases.Items.Add(string.Format(L("第 {0} 段   {1}"), i + 1, Waveform.FormatTime(segments[i].Start)));
            updating = false; Choose(target); UpdateEnabled();
        }
        void ApplyEditingOptions() { if (nudgeHeight != null) { nudgeHeight.Height = prefs.ShowBoundaryControls ? 38 : 0; nudges.Visible = prefs.ShowBoundaryControls; } }
        void UpdateEditingEnabled(bool ready) {
            mergeNext.Enabled = ready && index >= 0 && index + 1 < segments.Count;
            cutHere.Enabled = ready && index >= 0 && selection != null && wave.EditTime - selection.Start >= .1 && selection.End - wave.EditTime >= .1;
            undoEdit.Enabled = ready && editHistory.Count > 0;
            zoomIn.Enabled = zoomOut.Enabled = fitAudio.Enabled = ready;
            nudges.Enabled = ready && selection != null;
            gap.Enabled = !loading && !prefs.AdaptiveGap;
        }
    }
}
