import Foundation
import AVFoundation

enum MacTests {
    static func run() -> Int32 {
        var failures = 0, passed = 0
        func require(_ condition: @autoclosure () -> Bool, _ reason: String) throws { if !condition() { throw Failure(reason, reason) } }
        func check(_ name: String, _ body: () throws -> Void) { do { try body(); passed += 1; print("PASS \(name)") } catch { failures += 1; print("FAIL \(name): \(error)") } }
        let output = URL(fileURLWithPath: ProcessInfo.processInfo.environment["TEST_OUTPUT"] ?? FileManager.default.temporaryDirectory.appendingPathComponent("DictationMacTests").path, isDirectory: true)
        try? FileManager.default.createDirectory(at: output, withIntermediateDirectories: true)
        func synthetic(_ seconds: Double, _ amplitude: (Double) -> Float) -> AudioAnalysis {
            let values = (0..<Int(seconds * 100)).map { amplitude(Double($0) * 0.01) }
            return AudioAnalysis(url: output.appendingPathComponent("fixture.wav"), duration: seconds, step: 0.01, energy: values, peaks: values)
        }
        func fixture(_ url: URL, _ seconds: Double = 10) throws {
            let format = AVAudioFormat(standardFormatWithSampleRate: 44100, channels: 1)!
            let file = try AVAudioFile(forWriting: url, settings: format.settings)
            let buffer = AVAudioPCMBuffer(pcmFormat: format, frameCapacity: 44100)!
            for second in 0..<Int(seconds) {
                buffer.frameLength = 44100
                for i in 0..<44100 {
                    let t = Double(second) + Double(i) / 44100
                    let voice = t >= 1 && t < 3 || t >= 3.8 && t < 5.5 || t >= 6.5 && t < 8
                    buffer.floatChannelData![0][i] = voice ? Float(0.3 * sin(2 * .pi * 440 * t)) : 0
                }
                try file.write(from: buffer)
            }
        }
        let wav = output.appendingPathComponent("fixture.wav")
        check("WAV decode with bounded memory, energy analysis and default segmentation") {
            try fixture(wav)
            let audio = try AudioAnalysis.load(wav)
            try require(abs(audio.duration - 10) < 0.001 && audio.energy.count == 1000, "WAV analysis")
            let phrases = Segmenter.split(audio, Preferences())
            try require(phrases.count >= 2 && phrases.first!.start <= 0.71 && phrases.allSatisfy { $0.duration <= 8.001 }, "default phrases")
        }
        check("leading padding and quiet recordings retain beginnings without overlap") {
            let audio = synthetic(5) { t in t >= 1 && t < 2 || t >= 2.4 && t < 3.4 ? 0.003 : 0 }
            var prefs = Preferences(); prefs.mergeShort = false
            let phrases = Segmenter.split(audio, prefs)
            try require(phrases.count == 2 && abs(phrases[0].start - 0.7) < 0.011 && phrases[1].start >= phrases[0].end, "padding or overlap")
        }
        check("short introductory words merge forward and toggle off preserves them") {
            let audio = synthetic(10) { t in t < 2.5 || t >= 3.5 && t < 3.8 || t >= 4.3 && t < 7 ? 0.3 : 0 }
            var prefs = Preferences(); prefs.leading = 0.08
            let on = Segmenter.split(audio, prefs); prefs.mergeShort = false; let off = Segmenter.split(audio, prefs)
            try require(on.count == 2 && off.count == 3 && on[1].start < 3.5 && on[1].end > 7, "short merge")
        }
        check("long speech balances duration interval and preserves full coverage") {
            for length in [9.0, 17, 27, 60, 600] {
                var prefs = Preferences(); prefs.minLength = 4; prefs.maxLength = 8
                let phrases = Segmenter.split(synthetic(length) { _ in 0.3 }, prefs)
                try require(phrases.allSatisfy { $0.duration >= 3.999 && $0.duration <= 8.001 }, "duration interval")
                try require(phrases.first!.start == 0 && abs(phrases.last!.end - length) < 0.001, "coverage")
                for i in phrases.indices.dropFirst() { try require(phrases[i - 1].end == phrases[i].start, "missing samples") }
            }
        }
        check("silence and impossible intervals are handled without content loss") {
            try require(Segmenter.split(synthetic(10) { _ in 0 }, Preferences()).isEmpty, "silence")
            var prefs = Preferences(); prefs.minLength = 7; prefs.maxLength = 8
            let phrases = Segmenter.split(synthetic(9) { _ in 0.3 }, prefs)
            try require(phrases.count == 2 && phrases.allSatisfy { $0.duration <= 8.001 } && phrases.last!.end == 9, "impossible interval")
        }
        check("100 playback sequences, repeat totals, auto-next and manual infinite") {
            for _ in 0..<100 {
                var i = 0, completed = 0, total = 0
                while i < 8 {
                    total += 1; completed += 1
                    guard let next = Segmenter.next(completed: completed, plays: 3, auto: true, index: i, count: 8) else { break }
                    if next != i { completed = 0 }; i = next
                }
                try require(total == 24, "repeat count")
            }
            try require(Segmenter.next(completed: 100, plays: 0, auto: true, index: -1, count: 3) == -1, "manual infinite")
            var p = Preferences(); p.adaptive = true; p.multiplier = 2
            try require(Segmenter.gap(8, p) == 16 && Segmenter.gap(80, p) == 120, "adaptive gap")
        }
        check("preferences including teacher and play-on-next round-trip") {
            var p = Preferences(); p.teacher = true; p.playOnNext = true; p.language = "en"; p.dark = true
            let restored = try JSONDecoder().decode(Preferences.self, from: JSONEncoder().encode(p))
            try require(restored.teacher && restored.playOnNext && restored.dark && restored.language == "en", "preferences lost")
        }
        check("lesson portable round-trip, manual selection, overwrite and original bytes") {
            let file = output.appendingPathComponent("课堂.edlesson")
            let manifest = Lesson(duration: 10, segments: [Phrase(start: 0.712345, end: 3.1), Phrase(start: 3.5, end: 8.1)], selected: -1, manual: Phrase(start: 0.1, end: 0.8), audioEntry: "audio.wav", originalName: "课堂 & English.wav")
            try LessonFile.save(file, audio: wav, manifest: manifest)
            let moved = output.appendingPathComponent("移动后.edlesson"); try? FileManager.default.removeItem(at: moved); try FileManager.default.moveItem(at: file, to: moved)
            let opened = try LessonFile.open(moved); defer { try? FileManager.default.removeItem(at: opened.directory) }
            try require(opened.manifest.segments == manifest.segments && opened.manifest.manual == manifest.manual && opened.manifest.originalName == manifest.originalName, "lesson metadata")
            let embeddedBytes = try Data(contentsOf: opened.audio), sourceBytes = try Data(contentsOf: wav)
            try require(embeddedBytes == sourceBytes, "audio bytes changed")
            try LessonFile.save(moved, audio: opened.audio, manifest: manifest)
            let reopened = try LessonFile.open(moved); defer { try? FileManager.default.removeItem(at: reopened.directory) }
            try require(reopened.manifest.segments == manifest.segments, "overwrite changed ranges")
        }
        check("lesson validation rejects nonfinite, overlapping, traversal and external entities") {
            let valid = Lesson(duration: 10, segments: [Phrase(start: 1, end: 3), Phrase(start: 4, end: 5)], selected: 0, manual: nil, audioEntry: "audio.wav", originalName: "a.wav")
            var bad = [Lesson](); var p = valid; p.audioEntry = "../audio.wav"; bad.append(p)
            p = valid; p.segments[0].start = .nan; bad.append(p)
            p = valid; p.segments[1].start = 2; bad.append(p)
            p = valid; p.selected = 99; bad.append(p)
            for item in bad { var rejected = false; do { try item.validate() } catch { rejected = true }; try require(rejected, "invalid metadata accepted") }
            var rejected = false
            do { _ = try Lesson.parse(Data("<!DOCTYPE X [<!ENTITY x SYSTEM 'file:///tmp/x'>]><LessonManifest/>".utf8)) } catch { rejected = true }
            try require(rejected, "DTD accepted")
        }
        if let fixturePath = ProcessInfo.processInfo.environment["WINDOWS_LESSON"] {
            check("Windows v1.1 .edlesson opens with exact stored boundaries") {
                let opened = try LessonFile.open(URL(fileURLWithPath: fixturePath)); defer { try? FileManager.default.removeItem(at: opened.directory) }
                let audio = try AudioAnalysis.load(opened.audio)
                try require(opened.manifest.segments.count == 2 && abs(opened.manifest.segments[0].end - 6.921981766117) < 0.02, "Windows ranges not restored")
                try require(abs(audio.duration - opened.manifest.duration) < 0.05, "Windows duration")
            }
        }
        for rate in [0.5, 0.8, 1.0, 1.2, 1.5, 2.0] {
            check("AVAudioEngine offline \(rate)x: audible data, correct duration, preserved pitch") {
                let samples = try renderTone(rate)
                let audible = samples.indices.filter { abs(samples[$0]) > 0.01 }
                try require(audible.count > 4000, "silent output")
                let actual = Double(audible.last! - audible.first!) / 44100
                try require(abs(actual - 1 / rate) < 0.18, "tempo duration \(actual)")
                let a = audible.first! + 2048, b = min(audible.last! - 2048, a + 22050)
                var crossings = 0
                if a < b { for i in (a + 1)..<b { if samples[i - 1] <= 0 && samples[i] > 0 { crossings += 1 } } }
                let frequency = Double(crossings) * 44100 / Double(max(1, b - a))
                try require(abs(frequency - 440) < 20, "pitch changed \(frequency)")
            }
        }
        print("RESULT \(passed) passed, \(failures) failed")
        return failures == 0 ? 0 : 1
    }
    static func renderTone(_ rate: Double) throws -> [Float] {
        let engine = AVAudioEngine()
        let player = AVAudioPlayerNode()
        let pitch = AVAudioUnitTimePitch()
        let format = AVAudioFormat(standardFormatWithSampleRate: 44100, channels: 1)!
        engine.attach(player); engine.attach(pitch)
        engine.connect(player, to: pitch, format: format)
        engine.connect(pitch, to: engine.mainMixerNode, format: format)
        pitch.rate = Float(rate); pitch.pitch = 0
        try engine.enableManualRenderingMode(.offline, format: format, maximumFrameCount: 1024)
        let input = AVAudioPCMBuffer(pcmFormat: format, frameCapacity: 44100)!
        input.frameLength = 44100
        let angularStep: Double = 2.0 * Double.pi * 440.0 / 44100.0
        for i in 0..<44100 { input.floatChannelData![0][i] = Float(0.3 * sin(angularStep * Double(i))) }
        player.scheduleBuffer(input)
        try engine.start(); player.play()
        defer { player.stop(); engine.stop() }
        let buffer = AVAudioPCMBuffer(pcmFormat: engine.manualRenderingFormat, frameCapacity: 1024)!
        var samples = [Float]()
        let expectedFrames = Int(44100.0 * (1.0 / rate + 0.5))
        var attempts = 0
        while samples.count < expectedFrames && attempts < 10000 {
            attempts += 1
            let status = try engine.renderOffline(1024, to: buffer)
            if status == .success {
                let chunk = UnsafeBufferPointer<Float>(start: buffer.floatChannelData![0], count: Int(buffer.frameLength))
                samples.append(contentsOf: chunk)
            }
        }
        return samples
    }
}
