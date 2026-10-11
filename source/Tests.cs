using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using NAudio.Wave;

namespace PhrasePlayer {
    public static class Tests {
        static readonly List<string> results = new List<string>();
        static void Require(bool condition, string reason) { if (!condition) throw new Exception(reason); }
        static void Check(string title, Action test) {
            try { test(); results.Add("PASS " + title); } catch (Exception e) { results.Add("FAIL " + title + ": " + e); }
        }
        static AudioAnalysis Synthetic(int frames, Func<double, double> amplitude) {
            var energy = new float[frames]; for (int i = 0; i < frames; i++) energy[i] = (float)amplitude(i * 0.01);
            return new AudioAnalysis { Step = 0.01, Duration = frames * 0.01, Energy = energy, Peaks = energy };
        }
        static void MakeFixture(string path, int seconds) {
            var format = new WaveFormat(44100, 16, 2);
            using (var writer = new WaveFileWriter(path, format)) {
                byte[] buffer = new byte[44100 * 4];
                for (int sec = 0; sec < seconds; sec++) {
                    for (int i = 0; i < 44100; i++) {
                        double t = sec + i / 44100.0;
                        bool voice = (t >= 1 && t < 3) || (t >= 3.8 && t < 5.5) || (t >= 6.5 && t < 8);
                        if (seconds > 10) voice = t % 4 < 3;
                        short sample = voice ? (short)(10000 * Math.Sin(2 * Math.PI * 440 * t)) : (short)0;
                        int offset = i * 4; buffer[offset] = buffer[offset + 2] = (byte)sample; buffer[offset + 1] = buffer[offset + 3] = (byte)(sample >> 8);
                    }
                    writer.Write(buffer, 0, buffer.Length);
                }
            }
        }
        public static int Run(string directory) {
            Directory.CreateDirectory(directory);
            string wav = Path.Combine(directory, "分段测试.wav"), mp3 = Path.Combine(directory, "分段测试.mp3"), aac = Path.Combine(directory, "分段测试.m4a");
            MakeFixture(wav, 10);
            Check("merge, cursor split, undo copy and boundary clamps", delegate {
                var list = new List<Phrase> { new Phrase(1, 2), new Phrase(3, 5) };
                var copy = PhraseEditor.Copy(list);
                Require(PhraseEditor.Merge(list, 0) && list.Count == 1 && list[0].Start == 1 && list[0].End == 5, "merge lost range");
                Require(!PhraseEditor.Cut(list, 0, 1.05) && list.Count == 1, "edge split allowed");
                Require(PhraseEditor.Cut(list, 0, 3) && list.Count == 2 && list[0].End == list[1].Start, "split lost audio");
                Require(copy.Count == 2 && copy[0].End == 2, "undo snapshot changed");
                PhraseEditor.Nudge(list[1], true, -5, 3, 10); Require(list[1].Start == 3, "crossed neighbor");
                PhraseEditor.Nudge(list[1], false, -10, 3, 10); Require(Math.Abs(list[1].Duration - .1) < .0001, "collapsed range");
                Require(!PhraseEditor.Merge(list, 1) && !PhraseEditor.Cut(list, -1, 2), "invalid index accepted");
            });
            Check("adaptive gaps and generous non-overlapping leading padding", delegate {
                Require(PhraseEditor.WaitSeconds(false, 8, 2, 1) == 1 && PhraseEditor.WaitSeconds(true, 8, 2, 1) == 16 && PhraseEditor.WaitSeconds(true, 80, 2, 1) == 120, "gap calculation");
                using (var fixture = Synthetic(500, t => t >= 1 && t < 2 || t >= 2.4 && t < 3.4 ? .5 : 0)) {
                    var ranges = Segmenter.Split(fixture, .2, 8, .3, -26, false, .3);
                    Require(ranges.Count == 2 && Math.Abs(ranges[0].Start - .7) < .011 && ranges[1].Start >= ranges[0].End, "padding missing or overlaps");
                }
                var defaults = new Preferences(); Require(defaults.LeadingPadding == .3m && !defaults.AdaptiveGap && !defaults.ShowBoundaryControls, "new defaults");
            });
            Check("old preferences retain defaults and new appearance preferences round-trip", delegate {
                var serializer = new System.Xml.Serialization.XmlSerializer(typeof(Preferences));
                Preferences old;
                using (var reader = new StringReader("<Preferences><Plays>5</Plays><MaxLength>12</MaxLength></Preferences>")) old = (Preferences)serializer.Deserialize(reader);
                Require(old.Plays == 5 && old.MaxLength == 12 && old.Language == "zh" && !old.DarkMode, "legacy defaults");
                old.Language = "en"; old.DarkMode = true;
                using (var memory = new MemoryStream()) {
                    serializer.Serialize(memory, old); memory.Position = 0;
                    var saved = (Preferences)serializer.Deserialize(memory);
                    Require(saved.DarkMode && saved.Language == "en" && saved.Plays == 5 && saved.MaxLength == 12, "new preferences lost");
                }
            });
            Check("Chinese and English labels, format placeholders, author and help", delegate {
                foreach (var entry in Localization.English) {
                    Require(!string.IsNullOrWhiteSpace(entry.Value), "empty translation");
                    Require(Localization.Get(entry.Key, "zh") == entry.Key && Localization.Get(entry.Key, "en") == entry.Value, "language mapping");
                    string.Format(entry.Key, 1, 2, 3); string.Format(entry.Value, 1, 2, 3);
                }
                foreach (string language in new[] { "zh", "en" }) {
                    Require(HelpText.About(language).Contains("ThermoXu"), "author missing");
                    var help = HelpText.Get(language);
                    Require(help.Contains("Enter") && help.Contains("Esc") && help.Contains("F1") && help.Contains("0.5"), "help incomplete");
                }
                Require(Appearance.Background(true).GetBrightness() < .15f && Appearance.Ink(true).GetBrightness() > .8f, "dark contrast");
            });
            Check("short introductory phrase merges forward only when enabled", delegate {
                var a = Synthetic(1000, t => t < 2.5 || t >= 3.5 && t < 3.8 || t >= 4.3 && t < 7 ? 0.3 : 0);
                var on = Segmenter.Split(a, 2, 8, 0.3, -26, true);
                var off = Segmenter.Split(a, 2, 8, 0.3, -26, false);
                Require(on.Count == 2 && off.Count == 3, "merge switch");
                Require(on[1].Start < 3.5 && on[1].End > 7 && on[0].End < 3, "did not merge forward");
                var longer = Segmenter.Split(Synthetic(2000, t => t < .3 || t >= .8 ? .3 : 0), 2, 8, .3, -26, true);
                Require(longer[0].Duration >= 2 && longer.All(p => p.Duration <= 8.001) && longer[0].Start == 0, "short word before long speech");
            });
            Check("duration interval balances tail and preserves coverage", delegate {
                foreach (double duration in new[] { 9.0, 17.0, 27.0, 60.0 }) {
                    var p = Segmenter.Split(Synthetic((int)(duration * 100), t => .4), 4, 8, .3, -26, true);
                    Require(p.All(v => v.Duration >= 3.999 && v.Duration <= 8.001), "outside interval");
                    Require(p[0].Start == 0 && Math.Abs(p.Last().End - duration) < .001, "lost tail");
                    for (int i = 1; i < p.Count; i++) Require(Math.Abs(p[i].Start - p[i - 1].End) < .001, "gap");
                }
            });
            Check("impossible minimum retains short content and maximum", delegate {
                Require(Segmenter.Split(Synthetic(50, t => .3), 2, 8, .3, -26, true).Single().Duration == .5, "short lost");
                var p = Segmenter.Split(Synthetic(900, t => .3), 8, 8, .3, -26, true);
                Require(p.All(v => v.Duration <= 8.001) && Math.Abs(p.Sum(v => v.Duration) - 9) < .001, "impossible interval");
                bool failed = false; try { Segmenter.Split(Synthetic(100, t => .3), 9, 8, .3, -26, true); } catch (ArgumentException) { failed = true; } Require(failed, "invalid interval accepted");
            });
            Check("three phrases, trim leading/trailing silence", delegate {
                var a = Synthetic(1000, t => t >= 1 && t < 3 || t >= 3.8 && t < 5.5 || t >= 6.5 && t < 8 ? 0.3 : 0);
                var p = Segmenter.Split(a, 8, 0.3, -26);
                Require(p.Count == 3, "count " + p.Count);
                Require(Math.Abs(p[0].Start - 0.92) < 0.02 && Math.Abs(p[2].End - 8.1) < 0.02, "edges");
            });
            Check("long continuous speech preserves full coverage and maximum duration", delegate {
                var a = Synthetic(2700, t => 0.4); var p = Segmenter.Split(a, 8, 0.3, -26);
                Require(p.Count >= 4 && p.All(v => v.Duration <= 8.001 && v.Duration > 0), "length bound");
                Require(p[0].Start == 0 && Math.Abs(p.Last().End - 27) < 0.001, "coverage");
                for (int i = 1; i < p.Count; i++) Require(Math.Abs(p[i].Start - p[i - 1].End) < 0.001, "gap or overlap");
            });
            Check("silence does not generate fake phrases", delegate { Require(Segmenter.Split(Synthetic(1000, t => 0), 8, 0.3, -26).Count == 0, "silence"); });
            Check("quiet recording is segmented by relative energy", delegate {
                var a = Synthetic(1000, t => t >= 1 && t < 3 || t >= 3.8 && t < 5.5 || t >= 6.5 && t < 8 ? 0.002 : 0);
                Require(Segmenter.Split(a, 8, 0.3, -26).Count == 3, "quiet");
            });
            Check("long silence is skipped", delegate { var a = Synthetic(3000, t => t < 2 || t >= 28 ? 0.2 : 0); var p = Segmenter.Split(a, 8, 0.3, -26); Require(p.Count == 2 && p[1].Start > 27, "long silence"); });
            Check("short intra-phrase pause does not split", delegate { var a = Synthetic(400, t => t >= 1.8 && t < 1.95 ? 0 : 0.3); Require(Segmenter.Split(a, 8, 0.3, -26).Count == 1, "short gap"); });
            Check("counted repeats, auto-next, infinite and last phrase", delegate {
                Require(RepeatPlan.Next(1, 3, false, 0, 3) == 0, "early stop");
                Require(RepeatPlan.Next(3, 3, false, 0, 3) == -1, "counted stop");
                Require(RepeatPlan.Next(3, 3, true, 0, 3) == 1, "auto next");
                Require(RepeatPlan.Next(3, 3, true, 2, 3) == -1, "end of file");
                for (int i = 1; i <= 1000; i++) Require(RepeatPlan.Next(i, 0, true, 0, 3) == 0, "infinite advanced");
            });
            Check("100 phrase sequences with exact repeat totals", delegate {
                for (int round = 0; round < 100; round++) {
                    int current = 0, plays = 0, completed = 0;
                    while (current >= 0) { plays++; completed++; int target = RepeatPlan.Next(completed, 3, true, current, 20); if (target != current) completed = 0; current = target; Require(plays <= 60, "never stopped"); }
                    Require(plays == 60, "wrong count");
                }
            });
            AudioAnalysis decoded = null;
            Check("WAV decode, resampling and segmentation", delegate {
                decoded = AudioLoader.Load(wav, CancellationToken.None, p => {});
                Require(Math.Abs(decoded.Duration - 10) < 0.02 && decoded.Peaks.Length > 1000, "duration");
                Require(Segmenter.Split(decoded, 8, 0.3, -26).Count == 3, "decoded phrases");
            });
            Check("MP3 encode and decode through Windows codecs", delegate {
                using (var reader = new WaveFileReader(wav)) MediaFoundationEncoder.EncodeToMp3(reader, mp3, 128000);
                using (var a = AudioLoader.Load(mp3, CancellationToken.None, p => {})) { Require(a.Duration > 9.9 && a.Duration < 10.3, "mp3 duration"); Require(Segmenter.Split(a, 8, 0.3, -26).Count == 3, "mp3 phrases"); }
            });
            Check("M4A AAC encode and decode", delegate {
                using (var reader = new WaveFileReader(wav)) MediaFoundationEncoder.EncodeToAac(reader, aac, 128000);
                using (var a = AudioLoader.Load(aac, CancellationToken.None, p => {})) Require(a.Duration > 9.9 && a.Duration < 10.3 && Segmenter.Split(a, 8, 0.3, -26).Count == 3, "aac phrases");
            });
            Check("all six speeds preserve pitch, duration and nonzero audio", delegate {
                Require(decoded != null, "no decoded audio");
                foreach (double speed in new[] { .5, .8, 1, 1.2, 1.5, 2 }) {
                    using (var source = new RangeProvider(decoded.CachePath, new Phrase(1, 3))) {
                        IWaveProvider tempo = speed == 1 ? (IWaveProvider)source : new TempoProvider(source, speed);
                        using (var memory = new MemoryStream()) {
                            byte[] block = new byte[4096]; int n; int calls = 0;
                            while ((n = tempo.Read(block, 0, block.Length)) > 0) { memory.Write(block, 0, n); Require(++calls < 200, "never finished"); }
                            byte[] data = memory.ToArray(); double duration = data.Length / 44100.0;
                            Require(Math.Abs(duration - 2 / speed) < .025, "duration at " + speed + ": " + duration);
                            int crossings = 0, previous = 0; double sum = 0; int first = 2205, last = data.Length / 2 - 2205;
                            for (int i = first; i < last; i++) { short value = (short)(data[i * 2] | data[i * 2 + 1] << 8); if (previous <= 0 && value > 0) crossings++; previous = value; sum += (double)value * value; }
                            double hz = crossings / ((last - first) / 22050.0);
                            Require(sum > 1000000 && Math.Abs(hz - 440) < 12, "silent or pitch shifted at " + speed + ": " + hz);
                        }
                    }
                }
            });
            Check("manual selection byte-exact, no audio beyond B", delegate {
                Require(decoded != null, "no decoded file");
                var p = new Phrase(1.125, 1.875);
                byte[] got;
                using (var source = new RangeProvider(decoded.CachePath, p)) using (var memory = new MemoryStream()) {
                    var block = new byte[2048]; int n;
                    while ((n = source.Read(block, 0, block.Length)) > 0) memory.Write(block, 0, n);
                    got = memory.ToArray(); Require(source.Read(block, 0, block.Length) == 0, "read beyond end");
                }
                using (var reader = new WaveFileReader(decoded.CachePath)) {
                    long start = (long)(p.Start * 44100) / 2 * 2, end = (long)(p.End * 44100) / 2 * 2;
                    reader.Position = start; var expected = new byte[end - start]; reader.Read(expected, 0, expected.Length);
                    Require(got.SequenceEqual(expected), "sample mismatch");
                }
            });
            Check("cancel decode and remove temporary cache", delegate {
                var before = Directory.GetFiles(Path.GetTempPath(), "PhrasePlayer-*.wav");
                bool cancelled = false; using (var c = new CancellationTokenSource()) { c.Cancel(); try { using (var a = AudioLoader.Load(wav, c.Token, p => {})) {} } catch (OperationCanceledException) { cancelled = true; } }
                Require(cancelled, "not cancelled"); Require(Directory.GetFiles(Path.GetTempPath(), "PhrasePlayer-*.wav").Except(before).Count() == 0, "cache leaked");
            });
            Check("invalid file fails safely", delegate {
                string broken = Path.Combine(directory, "broken.mp3"); File.WriteAllText(broken, "not an audio file"); bool failed = false;
                try { using (var a = AudioLoader.Load(broken, CancellationToken.None, p => {})) {} } catch { failed = true; }
                Require(failed, "accepted invalid audio");
            });
            Check("real WaveOut playback completes 10 bounded clips", delegate {
                Require(decoded != null, "no decoded file");
                for (int i = 0; i < 10; i++) using (var end = new AutoResetEvent(false)) using (var provider = new RangeProvider(decoded.CachePath, new Phrase(1.1, 1.22))) using (var device = new WaveOutEvent { DesiredLatency = 60 }) {
                    Exception error = null; device.Init(provider); device.Volume = 0;
                    device.PlaybackStopped += delegate(object sender, StoppedEventArgs e) { error = e.Exception; end.Set(); };
                    device.Play(); Require(end.WaitOne(3000), "playback timeout"); Require(error == null && device.PlaybackState == PlaybackState.Stopped, "device failure");
                }
            });
            Check("10-minute file analysis and cache cleanup", delegate {
                string longPath = Path.Combine(directory, "long.wav"); MakeFixture(longPath, 600); var timer = Stopwatch.StartNew(); string cache;
                using (var a = AudioLoader.Load(longPath, CancellationToken.None, p => {})) { cache = a.CachePath; Require(Math.Abs(a.Duration - 600) < 0.02, "long duration"); Require(Segmenter.Split(a, 8, 0.3, -26).Count == 150, "long phrase count"); Require(a.Peaks.Length < 61000, "unbounded waveform storage"); }
                results.Add("METRIC 10-minute decode ms=" + timer.ElapsedMilliseconds); Require(!File.Exists(cache), "cleanup");
                File.Delete(longPath);
            });
            if (decoded != null) decoded.Dispose();
            int failures = results.Count(s => s.StartsWith("FAIL"));
            results.Add("RESULT " + (results.Count(s => s.StartsWith("PASS"))) + " passed, " + failures + " failed");
            File.WriteAllLines(Path.Combine(directory, "SELF_TEST.txt"), results.ToArray(), System.Text.Encoding.UTF8);
            return failures == 0 ? 0 : 1;
        }
    }
}
