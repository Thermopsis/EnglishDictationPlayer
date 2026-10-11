using System;
using NAudio.Wave;
using SoundTouch;

namespace PhrasePlayer {
    // Stream small blocks through SoundTouch; do not render a second copy of the entire file.
    public sealed class TempoProvider : IWaveProvider {
        readonly IWaveProvider source;
        readonly SoundTouchProcessor processor;
        readonly byte[] input = new byte[4096];
        readonly float[] samples = new float[2048];
        readonly float[] processed = new float[2048];
        bool flushed;
        public WaveFormat WaveFormat { get { return source.WaveFormat; } }
        public TempoProvider(IWaveProvider source, double speed) {
            if (speed < 0.5 || speed > 2) throw new ArgumentOutOfRangeException("speed");
            if (source.WaveFormat.BitsPerSample != 16 || source.WaveFormat.Channels != 1) throw new ArgumentException("需要单声道 16 位 PCM。");
            this.source = source;
            processor = new SoundTouchProcessor { SampleRate = source.WaveFormat.SampleRate, Channels = 1, Tempo = speed };
        }
        public int Read(byte[] buffer, int offset, int count) {
            int written = 0; count -= count % 2;
            while (written < count) {
                int requested = Math.Min(processed.Length, (count - written) / 2);
                var outputSpan = new Span<float>(processed);
                int got = processor.ReceiveSamples(in outputSpan, requested);
                if (got > 0) {
                    for (int i = 0; i < got; i++) {
                        short value = (short)Math.Max(short.MinValue, Math.Min(short.MaxValue, Math.Round(processed[i] * 32768)));
                        buffer[offset + written++] = (byte)value; buffer[offset + written++] = (byte)(value >> 8);
                    }
                    continue;
                }
                if (flushed) break;
                int n = source.Read(input, 0, input.Length);
                if (n == 0) { processor.Flush(); flushed = true; continue; }
                for (int i = 0; i < n / 2; i++) samples[i] = (short)(input[2 * i] | input[2 * i + 1] << 8) / 32768f;
                var inputSpan = new ReadOnlySpan<float>(samples, 0, n / 2);
                processor.PutSamples(in inputSpan, n / 2);
            }
            return written;
        }
    }
}
