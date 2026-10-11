using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace PhrasePlayer {
    public sealed class Waveform : Control {
        public AudioAnalysis Audio;
        public List<Phrase> Phrases = new List<Phrase>();
        public Phrase Selected;
        public double CursorTime = -1, EditTime = -1;
        public double ViewStart, ViewLength = 24;
        public event Action<double, double> SelectRange;
        public event Action<double> SelectAt;
        public event Action ViewChanged;
        bool dragging;
        int downX;
        double dragStart, dragEnd;
        Color ink = Color.FromArgb(41, 79, 91), accent = Color.FromArgb(0, 125, 112);
        Color guideColor = Color.FromArgb(228, 235, 237), tickColor = Color.FromArgb(190, 205, 211), hintColor = Color.FromArgb(111, 128, 136);
        public string EmptyHint = "打开一段音频，开始逐段听写";
        public void ApplyTheme(bool dark) {
            BackColor = Appearance.Surface(dark);
            ink = dark ? Color.FromArgb(174, 207, 216) : Color.FromArgb(41, 79, 91);
            accent = Appearance.Accent(dark);
            guideColor = dark ? Color.FromArgb(58, 64, 69) : Color.FromArgb(228, 235, 237);
            tickColor = dark ? Color.FromArgb(87, 100, 109) : Color.FromArgb(190, 205, 211);
            hintColor = Appearance.Muted(dark); Invalidate();
        }
        public Waveform() {
            DoubleBuffered = true; BackColor = Color.White; TabStop = true;
            SetStyle(ControlStyles.ResizeRedraw, true);
            AccessibleName = "音频波形：单击选择短语，拖动选择片段，滚轮缩放";
            Cursor = Cursors.Cross;
        }
        double TimeAt(int x) { return Math.Max(0, Math.Min(Audio.Duration, ViewStart + (x - 18) * ViewLength / Math.Max(1, Width - 36))); }
        float XAt(double t) { return (float)(18 + (t - ViewStart) * (Width - 36) / ViewLength); }
        public void ShowPhrase(Phrase p) {
            if (Audio == null || p == null) return;
            if (p.Duration > ViewLength * 0.85) ViewLength = Math.Min(Audio.Duration, Math.Max(5, p.Duration * 1.3));
            if (p.Start < ViewStart || p.End > ViewStart + ViewLength) ViewStart = Math.Max(0, Math.Min(Audio.Duration - ViewLength, p.Start - ViewLength * 0.12));
            if (ViewChanged != null) ViewChanged(); Invalidate();
        }
        public void Zoom(double factor) {
            if (Audio == null) return;
            double anchor = EditTime >= ViewStart && EditTime <= ViewStart + ViewLength ? EditTime : ViewStart + ViewLength / 2;
            ViewLength = Math.Max(Math.Min(2, Audio.Duration), Math.Min(Audio.Duration, ViewLength * factor));
            ViewStart = Math.Max(0, Math.Min(Math.Max(0, Audio.Duration - ViewLength), anchor - ViewLength / 2));
            if (ViewChanged != null) ViewChanged(); Invalidate();
        }
        public void FitAll() { if (Audio == null) return; ViewStart = 0; ViewLength = Audio.Duration; if (ViewChanged != null) ViewChanged(); Invalidate(); }
        protected override void OnMouseDown(MouseEventArgs e) {
            base.OnMouseDown(e);
            if (Audio == null || e.Button != MouseButtons.Left) return;
            Focus(); Capture = true; dragging = true; downX = e.X; dragStart = dragEnd = TimeAt(e.X); Invalidate();
        }
        protected override void OnMouseMove(MouseEventArgs e) {
            base.OnMouseMove(e);
            if (!dragging || Audio == null) return;
            dragEnd = TimeAt(e.X); Invalidate();
        }
        protected override void OnMouseUp(MouseEventArgs e) {
            base.OnMouseUp(e);
            if (!dragging || Audio == null) return;
            dragging = false; Capture = false;
            dragEnd = TimeAt(e.X);
            if (Math.Abs(e.X - downX) < 5) { if (SelectAt != null) SelectAt(dragEnd); }
            else if (Math.Abs(dragEnd - dragStart) >= 0.1 && SelectRange != null) SelectRange(Math.Min(dragStart, dragEnd), Math.Max(dragStart, dragEnd));
            Invalidate();
        }
        protected override void OnMouseWheel(MouseEventArgs e) {
            base.OnMouseWheel(e); if (Audio == null) return;
            double anchor = TimeAt(e.X), fraction = (e.X - 18.0) / Math.Max(1, Width - 36);
            ViewLength = Math.Max(Math.Min(2, Audio.Duration), Math.Min(Audio.Duration, ViewLength * (e.Delta > 0 ? 0.8 : 1.25)));
            ViewStart = Math.Max(0, Math.Min(Math.Max(0, Audio.Duration - ViewLength), anchor - fraction * ViewLength));
            if (ViewChanged != null) ViewChanged(); Invalidate();
        }
        protected override void OnPaint(PaintEventArgs e) {
            base.OnPaint(e); var g = e.Graphics; g.SmoothingMode = SmoothingMode.None;
            int center = (Height - 33) / 2, half = Math.Max(12, center - 22);
            if (Audio == null) {
                TextRenderer.DrawText(g, EmptyHint, Font, new Rectangle(0, 0, Width, Height - 20), hintColor, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                return;
            }
            Rectangle plot = new Rectangle(18, 12, Math.Max(1, Width - 36), Math.Max(1, Height - 45));
            g.SetClip(plot);
            using (var guide = new Pen(guideColor)) {
                foreach (var p in Phrases) if (p.Start >= ViewStart && p.Start <= ViewStart + ViewLength) g.DrawLine(guide, XAt(p.Start), 12, XAt(p.Start), Height - 33);
                g.DrawLine(guide, 18, center, Width - 18, center);
            }
            if (Selected != null) PaintSelection(g, Selected.Start, Selected.End, Height - 33);
            if (dragging) PaintSelection(g, Math.Min(dragStart, dragEnd), Math.Max(dragStart, dragEnd), Height - 33);
            using (var pen = new Pen(ink)) {
                for (int x = 18; x < Width - 18; x++) {
                    double t = ViewStart + (x - 18.0) * ViewLength / Math.Max(1, Width - 36);
                    int a = Math.Max(0, (int)(t / Audio.Step));
                    int b = Math.Min(Audio.Peaks.Length, Math.Max(a + 1, (int)((t + ViewLength / Math.Max(1, Width - 36)) / Audio.Step)));
                    float peak = 0;
                    for (int i = a; i < b; i++) peak = Math.Max(peak, Audio.Peaks[i]);
                    float h = Math.Max(0.5f, (float)(Math.Sqrt(peak) * half));
                    g.DrawLine(pen, x, center - h, x, center + h);
                }
            }
            if (CursorTime >= ViewStart && CursorTime <= ViewStart + ViewLength) using (var pen = new Pen(Color.FromArgb(230, 118, 47), 2)) g.DrawLine(pen, XAt(CursorTime), 12, XAt(CursorTime), Height - 33);
            if (EditTime >= ViewStart && EditTime <= ViewStart + ViewLength) using (var pen = new Pen(hintColor, 1)) { pen.DashStyle = DashStyle.Dash; g.DrawLine(pen, XAt(EditTime), 12, XAt(EditTime), Height - 33); }
            g.ResetClip();
            double tick = ViewLength <= 8 ? 1 : ViewLength <= 40 ? 5 : ViewLength <= 160 ? 20 : Math.Ceiling(ViewLength / 8 / 60) * 60;
            using (var pen = new Pen(tickColor)) {
                for (double t = Math.Ceiling(ViewStart / tick) * tick; t <= ViewStart + ViewLength; t += tick) {
                    float x = XAt(t); g.DrawLine(pen, x, Height - 32, x, Height - 27);
                    TextRenderer.DrawText(g, FormatTime(t), Font, new Rectangle(Math.Max(0, Math.Min(Width - 60, (int)x - 30)), Height - 24, 60, 20), hintColor, TextFormatFlags.HorizontalCenter);
                }
            }
        }
        void PaintSelection(Graphics g, double start, double end, int bottom) {
            float x = XAt(start), y = XAt(end);
            using (var fill = new SolidBrush(Color.FromArgb(36, accent))) g.FillRectangle(fill, x, 12, Math.Max(1, y - x), bottom - 12);
            using (var pen = new Pen(accent, 2)) { g.DrawLine(pen, x, 12, x, bottom); g.DrawLine(pen, y, 12, y, bottom); }
        }
        public static string FormatTime(double seconds) {
            var t = TimeSpan.FromSeconds(Math.Max(0, seconds));
            return t.TotalHours >= 1 ? string.Format("{0}:{1:00}:{2:00}", (int)t.TotalHours, t.Minutes, t.Seconds) : string.Format("{0:00}:{1:00}", (int)t.TotalMinutes, t.Seconds);
        }
    }
}
