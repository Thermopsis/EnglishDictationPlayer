using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using NAudio.Wave;

namespace PhrasePlayer {
    public sealed class Phrase {
        public double Start, End;
        public bool Manual;
        public double Duration { get { return End - Start; } }
        public Phrase(double start, double end) { Start = start; End = end; }
    }

    public sealed class AudioAnalysis : IDisposable {
        public string CachePath;
        public float[] Peaks, Energy;
        public double Step, Duration;
        public void Dispose() {
            if (CachePath != null) { try { File.Delete(CachePath); } catch (IOException) {} catch (UnauthorizedAccessException) {} CachePath = null; }
        }
    }

    public static class AudioLoader {
        public static AudioAnalysis Load(string path, CancellationToken cancel, Action<int> progress) {
            string cache = Path.Combine(Path.GetTempPath(), "PhrasePlayer-" + Guid.NewGuid().ToString("N") + ".wav");
            try {
                var peaks = new List<float>();
                var energy = new List<float>();
                const int sampleRate = 22050, samples = 220;
                var format = new WaveFormat(sampleRate, 16, 1);
                using (var source = new MediaFoundationReader(path))
                using (var convert = new MediaFoundationResampler(source, format))
                using (var writer = new WaveFileWriter(cache, format)) {
                    convert.ResamplerQuality = 60;
                    var buffer = new byte[samples * 2];
                    long written = 0;
                    double estimate = source.TotalTime.TotalSeconds * format.AverageBytesPerSecond;
                    int lastProgress = -1;
                    while (true) {
                        cancel.ThrowIfCancellationRequested();
                        int count = 0;
                        while (count < buffer.Length) {
                            int n = convert.Read(buffer, count, buffer.Length - count);
                            if (n == 0) break;
                            count += n;
                        }
                        if (count == 0) break;
                        writer.Write(buffer, 0, count);
                        double sum = 0, peak = 0;
                        for (int i = 0; i + 1 < count; i += 2) {
                            double v = (short)(buffer[i] | buffer[i + 1] << 8) / 32768.0;
                            sum += v * v; peak = Math.Max(peak, Math.Abs(v));
                        }
                        energy.Add((float)Math.Sqrt(sum / Math.Max(1, count / 2)));
                        peaks.Add((float)peak);
                        written += count;
                        int percent = estimate > 0 ? Math.Min(99, (int)(100 * written / estimate)) : 0;
                        if (percent != lastProgress) { lastProgress = percent; progress(percent); }
                    }
                    if (written == 0) throw new InvalidDataException("音频中没有可播放的数据。");
                    cancel.ThrowIfCancellationRequested();
                    progress(100);
                    return new AudioAnalysis { CachePath = cache, Peaks = peaks.ToArray(), Energy = energy.ToArray(), Step = (double)samples / sampleRate, Duration = (double)written / format.AverageBytesPerSecond };
                }
            } catch {
                try { File.Delete(cache); } catch (IOException) {} catch (UnauthorizedAccessException) {}
                throw;
            }
        }
    }

    public static class Segmenter {
        // Identify audible bursts, combine brief gaps, then split long bursts at low-energy points.
        // This is pause-based phrase selection, not grammatical sentence recognition.
        public static List<Phrase> Split(AudioAnalysis audio, double maxSeconds, double silenceSeconds, int sensitivityDb) {
            return Split(audio, 1, maxSeconds, silenceSeconds, sensitivityDb, true);
        }
        public static List<Phrase> Split(AudioAnalysis audio, double minSeconds, double maxSeconds, double silenceSeconds, int sensitivityDb, bool mergeShort) {
            return Split(audio, minSeconds, maxSeconds, silenceSeconds, sensitivityDb, mergeShort, .08);
        }
        public static List<Phrase> Split(AudioAnalysis audio, double minSeconds, double maxSeconds, double silenceSeconds, int sensitivityDb, bool mergeShort, double leadingPadding) {
            if (leadingPadding < 0 || leadingPadding > 1) throw new ArgumentOutOfRangeException("leadingPadding");
            if (minSeconds <= 0 || maxSeconds < minSeconds) throw new ArgumentException("最短长度必须大于零，且不能大于最长长度。");
            if (audio.Energy.Length == 0 || audio.Duration <= 0) return new List<Phrase>();
            float[] sorted = audio.Energy.Where(v => v > 0.0001f).OrderBy(v => v).ToArray();
            if (sorted.Length == 0) return new List<Phrase>();
            double loud = sorted[(int)((sorted.Length - 1) * 0.8)];
            double threshold = Math.Max(0.0001, loud * Math.Pow(10, sensitivityDb / 20.0));
            int silenceFrames = Math.Max(1, (int)Math.Ceiling(silenceSeconds / audio.Step));
            var raw = new List<Phrase>();
            int start = -1, quiet = 0, lastVoice = -1;
            for (int i = 0; i < audio.Energy.Length; i++) {
                if (audio.Energy[i] >= threshold) {
                    if (start < 0) start = i;
                    lastVoice = i; quiet = 0;
                } else if (start >= 0 && ++quiet >= silenceFrames) {
                    raw.Add(new Phrase(Math.Max(0, start * audio.Step - leadingPadding), Math.Min(audio.Duration, (lastVoice + 1) * audio.Step + 0.1)));
                    start = -1; quiet = 0;
                }
            }
            if (start >= 0) raw.Add(new Phrase(Math.Max(0, start * audio.Step - leadingPadding), Math.Min(audio.Duration, (lastVoice + 1) * audio.Step + 0.1)));
            for (int i = 1; i < raw.Count; i++) raw[i].Start = Math.Max(raw[i].Start, raw[i - 1].End);
            // Prefer the next phrase for short introductory words. Never bridge a long silence.
            var joined = raw;
            if (mergeShort) for (int i = 0; i < joined.Count; i++) {
                var p = joined[i];
                if (p.Duration >= minSeconds) continue;
                if (i + 1 < joined.Count && joined[i + 1].Start - p.End <= 1.5) {
                    p.End = joined[i + 1].End; joined.RemoveAt(i + 1); i--; continue;
                }
                if (i > 0 && p.Start - joined[i - 1].End <= 1.5) {
                    joined[i - 1].End = p.End; joined.RemoveAt(i); i -= 2;
                }
            }
            var result = new List<Phrase>();
            foreach (var p in joined) {
                double cursor = p.Start;
                while (p.End - cursor > maxSeconds + 0.001) {
                    // Choose a feasible cut so the tail can still satisfy the requested interval.
                    double remaining = p.End - cursor;
                    int pieces = (int)Math.Ceiling(remaining / maxSeconds);
                    bool feasible = remaining >= pieces * minSeconds;
                    double left = cursor + (feasible ? Math.Max(minSeconds, remaining - (pieces - 1) * maxSeconds) : Math.Min(minSeconds, remaining / pieces));
                    double right = cursor + (feasible ? Math.Min(maxSeconds, remaining - (pieces - 1) * minSeconds) : remaining / pieces);
                    int first = Math.Max(0, (int)Math.Ceiling(left / audio.Step));
                    int last = Math.Min(audio.Energy.Length - 1, (int)(right / audio.Step));
                    int best = last; double score = double.MaxValue;
                    for (int i = first; i <= last; i++) {
                        double local = audio.Energy[i];
                        if (i > 0) local += audio.Energy[i - 1];
                        if (i + 1 < audio.Energy.Length) local += audio.Energy[i + 1];
                        // Among similarly quiet locations, prefer a longer phrase.
                        double value = local + (last - i) * 0.00001;
                        if (value < score) { best = i; score = value; }
                    }
                    double cut = Math.Min(right, Math.Max(left, best * audio.Step));
                    result.Add(new Phrase(cursor, cut)); cursor = cut;
                }
                if (p.End - cursor > 0.001) result.Add(new Phrase(cursor, p.End));
            }
            return result;
        }
    }

    public sealed class RangeProvider : IWaveProvider, IDisposable {
        readonly WaveFileReader reader;
        readonly long end;
        public WaveFormat WaveFormat { get { return reader.WaveFormat; } }
        public RangeProvider(string wav, Phrase phrase) {
            reader = new WaveFileReader(wav);
            int align = reader.WaveFormat.BlockAlign;
            long begin = (long)(phrase.Start * reader.WaveFormat.AverageBytesPerSecond) / align * align;
            end = Math.Min(reader.Length, (long)(phrase.End * reader.WaveFormat.AverageBytesPerSecond) / align * align);
            reader.Position = Math.Max(0, Math.Min(begin, end));
        }
        public int Read(byte[] buffer, int offset, int count) {
            int remaining = (int)Math.Min(count, Math.Max(0, end - reader.Position));
            remaining -= remaining % WaveFormat.BlockAlign;
            return remaining > 0 ? reader.Read(buffer, offset, remaining) : 0;
        }
        public void Dispose() { reader.Dispose(); }
    }

    public static class RepeatPlan {
        // totalPlays == 0 means repeat the current phrase indefinitely.
        public static int Next(int completed, int totalPlays, bool autoNext, int current, int count) {
            if (totalPlays == 0 || completed < totalPlays) return current;
            return autoNext && current + 1 < count ? current + 1 : -1;
        }
    }
}
