using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading;
using System.Xml;
using System.Xml.Serialization;

namespace PhrasePlayer {
    public sealed class LessonSegment {
        public double Start, End;
        public LessonSegment() { }
        public LessonSegment(Phrase p) { Start = p.Start; End = p.End; }
        public Phrase ToPhrase() { return new Phrase(Start, End); }
    }
    public sealed class LessonManifest {
        public int Version = 1;
        public string AudioEntry, OriginalName;
        public double Duration;
        public List<LessonSegment> Segments = new List<LessonSegment>();
        public int SelectedIndex;
        public LessonSegment ManualSelection;
    }
    public sealed class OpenLesson : IDisposable {
        public LessonManifest Manifest;
        public string AudioPath;
        public void Dispose() {
            if (AudioPath == null) return;
            string dir = Path.GetDirectoryName(AudioPath);
            try { File.Delete(AudioPath); Directory.Delete(dir); } catch (IOException) {} catch (UnauthorizedAccessException) {}
            AudioPath = null;
        }
    }
    public static class LessonFile {
        const long MaxAudioBytes = 2L * 1024 * 1024 * 1024;
        static readonly string[] Extensions = { ".mp3", ".wav", ".m4a", ".aac", ".wma", ".flac", ".mp4" };
        public static bool IsLesson(string path) { return string.Equals(Path.GetExtension(path), ".edlesson", StringComparison.OrdinalIgnoreCase); }
        static bool Finite(double x) { return !double.IsNaN(x) && !double.IsInfinity(x); }
        static void ValidateRange(LessonSegment s, double duration) {
            if (s == null || !Finite(s.Start) || !Finite(s.End) || s.Start < 0 || s.End > duration + .001 || s.End - s.Start < .001) throw new InvalidDataException("Invalid lesson range.");
        }
        public static void Validate(LessonManifest m) {
            if (m == null || m.Version != 1 || !Finite(m.Duration) || m.Duration <= 0 || m.Segments == null || m.Segments.Count > 100000) throw new InvalidDataException("Invalid lesson version or metadata.");
            string extension = Path.GetExtension(m.AudioEntry ?? "").ToLowerInvariant();
            if (!Extensions.Contains(extension) || m.AudioEntry != "audio" + extension) throw new InvalidDataException("Invalid lesson audio entry.");
            double end = 0;
            foreach (var s in m.Segments) { ValidateRange(s, m.Duration); if (s.Start < end - .000001) throw new InvalidDataException("Overlapping lesson ranges."); end = s.End; }
            if (m.SelectedIndex < -1 || m.SelectedIndex >= m.Segments.Count || (m.SelectedIndex == -1 && m.ManualSelection == null && m.Segments.Count > 0)) throw new InvalidDataException("Invalid selected phrase.");
            if (m.ManualSelection != null) ValidateRange(m.ManualSelection, m.Duration);
        }
        static void Copy(Stream source, Stream target, CancellationToken cancel) {
            byte[] buffer = new byte[65536]; int n; long total = 0;
            while ((n = source.Read(buffer, 0, buffer.Length)) > 0) { cancel.ThrowIfCancellationRequested(); total += n; if (total > MaxAudioBytes) throw new InvalidDataException("Audio exceeds 2 GB."); target.Write(buffer, 0, n); }
        }
        public static void Save(string destination, string audioPath, string originalName, double duration, List<Phrase> segments, int selectedIndex, Phrase selection, CancellationToken cancel) {
            string extension = Path.GetExtension(audioPath).ToLowerInvariant();
            var manifest = new LessonManifest { AudioEntry = "audio" + extension, OriginalName = Path.GetFileName(originalName), Duration = duration, SelectedIndex = selectedIndex, ManualSelection = selectedIndex < 0 && selection != null ? new LessonSegment(selection) : null, Segments = segments.Select(p => new LessonSegment(p)).ToList() };
            Validate(manifest);
            destination = Path.GetFullPath(destination);
            if (string.Equals(destination, Path.GetFullPath(audioPath), StringComparison.OrdinalIgnoreCase)) throw new IOException("Cannot overwrite source audio.");
            string temp = destination + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try {
                using (var input = File.OpenRead(audioPath)) {
                    if (input.Length <= 0 || input.Length > MaxAudioBytes) throw new InvalidDataException("Invalid audio size.");
                    using (var zip = ZipFile.Open(temp, ZipArchiveMode.Create)) {
                        using (var metadata = zip.CreateEntry("lesson.xml").Open()) new XmlSerializer(typeof(LessonManifest)).Serialize(metadata, manifest);
                        using (var output = zip.CreateEntry(manifest.AudioEntry, CompressionLevel.Fastest).Open()) Copy(input, output, cancel);
                    }
                }
                cancel.ThrowIfCancellationRequested();
                if (File.Exists(destination)) File.Replace(temp, destination, null); else File.Move(temp, destination);
            } finally { if (File.Exists(temp)) File.Delete(temp); }
        }
        public static OpenLesson Open(string path, CancellationToken cancel) {
            var result = new OpenLesson();
            try {
                using (var zip = ZipFile.OpenRead(path)) {
                    var metadata = zip.GetEntry("lesson.xml");
                    if (metadata == null || metadata.Length > 4 * 1024 * 1024 || zip.Entries.Count != 2 || zip.Entries.Count(e => e.FullName == "lesson.xml") != 1) throw new InvalidDataException("Invalid lesson package.");
                    using (var input = metadata.Open()) using (var reader = XmlReader.Create(input, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 4 * 1024 * 1024 })) result.Manifest = (LessonManifest)new XmlSerializer(typeof(LessonManifest)).Deserialize(reader);
                    Validate(result.Manifest);
                    var audio = zip.GetEntry(result.Manifest.AudioEntry);
                    if (audio == null || audio.Length <= 0 || audio.Length > MaxAudioBytes) throw new InvalidDataException("Missing or invalid lesson audio.");
                    string directory = Path.Combine(Path.GetTempPath(), "DictationLesson-" + Guid.NewGuid().ToString("N"));
                    Directory.CreateDirectory(directory); result.AudioPath = Path.Combine(directory, result.Manifest.AudioEntry);
                    using (var input = audio.Open()) using (var output = File.Create(result.AudioPath)) Copy(input, output, cancel);
                }
                cancel.ThrowIfCancellationRequested(); return result;
            } catch { result.Dispose(); throw; }
        }
    }
}
