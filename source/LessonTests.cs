using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading;
using System.Xml.Serialization;

namespace PhrasePlayer {
    public static partial class Tests {
        static void LessonChecks(string directory, string wav) {
            Check("portable lesson keeps exact edits and original audio after relocation", delegate {
                string folder = Path.Combine(directory, "课件测试"); Directory.CreateDirectory(folder);
                string original = Path.Combine(folder, "原始 课堂.wav"), saved = Path.Combine(folder, "已编辑.edlesson"), moved = Path.Combine(folder, "移动后.edlesson");
                File.Copy(wav, original, true);
                var edited = new List<Phrase> { new Phrase(.72, 3.1), new Phrase(3.5, 5.6), new Phrase(6.2, 8.1) };
                PhraseEditor.Cut(edited, 0, 1.812345); PhraseEditor.Merge(edited, 2);
                LessonFile.Save(saved, original, Path.GetFileName(original), 10, edited, 1, edited[1], CancellationToken.None);
                File.Copy(saved, moved, true); File.Delete(saved); File.Delete(original);
                string extracted;
                using (var opened = LessonFile.Open(moved, CancellationToken.None)) {
                    extracted = opened.AudioPath;
                    Require(File.ReadAllBytes(extracted).SequenceEqual(File.ReadAllBytes(wav)), "original audio bytes changed");
                    Require(opened.Manifest.OriginalName == "原始 课堂.wav" && opened.Manifest.SelectedIndex == 1, "name or selection lost");
                    Require(opened.Manifest.Segments.Count == edited.Count, "phrase count changed");
                    for (int i = 0; i < edited.Count; i++) Require(opened.Manifest.Segments[i].Start == edited[i].Start && opened.Manifest.Segments[i].End == edited[i].End, "edited boundaries changed");
                    using (var decoded = AudioLoader.Load(extracted, CancellationToken.None, p => {})) Require(Math.Abs(decoded.Duration - 10) < .02, "embedded audio not playable");
                    // Save over an existing lesson while reading its extracted embedded audio.
                    LessonFile.Save(moved, extracted, opened.Manifest.OriginalName, 10, edited, -1, new Phrase(.12, .87), CancellationToken.None);
                }
                Require(!File.Exists(extracted) && !Directory.Exists(Path.GetDirectoryName(extracted)), "extracted audio leaked");
                using (var opened = LessonFile.Open(moved, CancellationToken.None)) Require(opened.Manifest.SelectedIndex == -1 && opened.Manifest.ManualSelection.Start == .12 && opened.Manifest.ManualSelection.End == .87, "manual selection lost");
            });
            Check("lesson validation rejects unsupported, overlapping and nonfinite ranges", delegate {
                Func<LessonManifest> valid = () => new LessonManifest { AudioEntry = "audio.wav", Duration = 10, SelectedIndex = 0, Segments = new List<LessonSegment> { new LessonSegment(new Phrase(1, 3)), new LessonSegment(new Phrase(4, 5)) } };
                var m = valid(); LessonFile.Validate(m);
                var bad = new List<LessonManifest>();
                m = valid(); m.Version = 2; bad.Add(m);
                m = valid(); m.AudioEntry = "../audio.wav"; bad.Add(m);
                m = valid(); m.Segments[0].Start = double.NaN; bad.Add(m);
                m = valid(); m.Segments[0].End = 11; bad.Add(m);
                m = valid(); m.Segments[1].Start = 2; bad.Add(m);
                m = valid(); m.SelectedIndex = 9; bad.Add(m);
                m = valid(); m.ManualSelection = new LessonSegment(new Phrase(-1, 2)); bad.Add(m);
                foreach (var invalid in bad) { bool rejected = false; try { LessonFile.Validate(invalid); } catch (InvalidDataException) { rejected = true; } Require(rejected, "invalid lesson accepted"); }
            });
            Check("cancelled lesson save preserves existing file and removes temporary package", delegate {
                string target = Path.Combine(directory, "cancel.edlesson"); File.WriteAllText(target, "existing lesson");
                using (var cancel = new CancellationTokenSource()) {
                    cancel.Cancel(); bool stopped = false;
                    try { LessonFile.Save(target, wav, "fixture.wav", 10, new List<Phrase> { new Phrase(1, 3) }, 0, null, cancel.Token); } catch (OperationCanceledException) { stopped = true; }
                    Require(stopped && File.ReadAllText(target) == "existing lesson", "previous file damaged");
                    Require(Directory.GetFiles(directory, "cancel.edlesson.*.tmp").Length == 0, "partial save leaked");
                }
            });
            Check("damaged lesson and XML external entities are rejected", delegate {
                string bad = Path.Combine(directory, "bad.edlesson"); File.WriteAllText(bad, "not a zip");
                bool rejected = false; try { using (var opened = LessonFile.Open(bad, CancellationToken.None)) {} } catch (InvalidDataException) { rejected = true; } Require(rejected, "damaged zip accepted");
                string dtd = Path.Combine(directory, "dtd.edlesson"); if (File.Exists(dtd)) File.Delete(dtd);
                using (var zip = ZipFile.Open(dtd, ZipArchiveMode.Create)) {
                    using (var writer = new StreamWriter(zip.CreateEntry("lesson.xml").Open())) writer.Write("<!DOCTYPE LessonManifest [<!ENTITY x SYSTEM 'file:///nonexistent'>]><LessonManifest><OriginalName>&x;</OriginalName></LessonManifest>");
                    using (var stream = zip.CreateEntry("audio.wav").Open()) stream.WriteByte(1);
                }
                rejected = false; try { using (var opened = LessonFile.Open(dtd, CancellationToken.None)) {} } catch (InvalidOperationException) { rejected = true; } catch (System.Xml.XmlException) { rejected = true; }
                Require(rejected, "DTD accepted");
            });
        }
    }
}
