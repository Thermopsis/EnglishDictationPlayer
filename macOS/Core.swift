import Foundation
import AVFoundation

struct Phrase: Codable, Equatable, Identifiable {
    var start: Double
    var end: Double
    var id: Double { start }
    var duration: Double { end - start }
}

struct Preferences: Codable {
    var plays = 3
    var gap = 1.0
    var autoNext = false
    var playOnNext = false
    var minLength = 2.0
    var maxLength = 8.0
    var mergeShort = true
    var silence = 0.3
    var sensitivity = -26.0
    var leading = 0.3
    var speed = 1.0
    var dark = false
    var language = "zh"
    var teacher = false
    var boundary = false
    var adaptive = false
    var multiplier = 2.0
    static func load() -> Self {
        guard let data = UserDefaults.standard.data(forKey: "preferences"), let result = try? JSONDecoder().decode(Self.self, from: data) else { return Self() }
        return result
    }
    func save() { if let data = try? JSONEncoder().encode(self) { UserDefaults.standard.set(data, forKey: "preferences") } }
}

struct AudioAnalysis {
    var url: URL
    var duration: Double
    var step: Double
    var energy: [Float]
    var peaks: [Float]
    static func load(_ url: URL) throws -> Self {
        let file = try AVAudioFile(forReading: url, commonFormat: .pcmFormatFloat32, interleaved: false)
        let format = file.processingFormat
        let frameSize = max(1, Int(format.sampleRate / 100))
        guard let buffer = AVAudioPCMBuffer(pcmFormat: format, frameCapacity: AVAudioFrameCount(frameSize * 100)) else { throw Failure("无法分析音频。", "Cannot analyze audio.") }
        var energy = [Float](), peaks = [Float]()
        while file.framePosition < file.length {
            try file.read(into: buffer)
            guard buffer.frameLength > 0, let channels = buffer.floatChannelData else { break }
            let count = Int(buffer.frameLength)
            for start in stride(from: 0, to: count, by: frameSize) {
                let end = min(count, start + frameSize)
                var sum = 0.0, peak: Float = 0
                for i in start..<end {
                    var sample: Float = 0
                    for c in 0..<Int(format.channelCount) { sample += channels[c][i] }
                    sample /= Float(format.channelCount)
                    sum += Double(sample * sample); peak = max(peak, abs(sample))
                }
                energy.append(Float(sqrt(sum / Double(end - start)))); peaks.append(peak)
            }
        }
        let duration = Double(file.length) / format.sampleRate
        guard duration > 0 else { throw Failure("音频没有可播放的内容。", "The audio is empty.") }
        return Self(url: url, duration: duration, step: Double(frameSize) / format.sampleRate, energy: energy, peaks: peaks)
    }
}

struct Failure: LocalizedError {
    let zh: String, en: String
    init(_ zh: String, _ en: String) { self.zh = zh; self.en = en }
    var errorDescription: String? { "\(zh) / \(en)" }
}

enum Segmenter {
    static func split(_ audio: AudioAnalysis, _ p: Preferences) -> [Phrase] {
        let sorted = audio.energy.filter { $0 > 0.0001 }.sorted()
        guard !sorted.isEmpty else { return [] }
        let loud = Double(sorted[Int(Double(sorted.count - 1) * 0.8)])
        let threshold = max(0.0001, loud * pow(10, p.sensitivity / 20))
        let silenceFrames = max(1, Int(ceil(p.silence / audio.step)))
        var raw = [Phrase](), start = -1, quiet = 0, last = -1
        for i in audio.energy.indices {
            if Double(audio.energy[i]) >= threshold {
                if start < 0 { start = i }; last = i; quiet = 0
            } else if start >= 0 {
                quiet += 1
                if quiet >= silenceFrames {
                    raw.append(Phrase(start: max(0, Double(start) * audio.step - p.leading), end: min(audio.duration, Double(last + 1) * audio.step + 0.1)))
                    start = -1; quiet = 0
                }
            }
        }
        if start >= 0 { raw.append(Phrase(start: max(0, Double(start) * audio.step - p.leading), end: min(audio.duration, Double(last + 1) * audio.step + 0.1))) }
        for i in raw.indices.dropFirst() { raw[i].start = max(raw[i].start, raw[i - 1].end) }
        if p.mergeShort {
            var i = 0
            while i < raw.count {
                if raw[i].duration < p.minLength {
                    if i + 1 < raw.count && raw[i + 1].start - raw[i].end <= 1.5 {
                        raw[i].end = raw[i + 1].end; raw.remove(at: i + 1); continue
                    }
                    if i > 0 && raw[i].start - raw[i - 1].end <= 1.5 {
                        raw[i - 1].end = raw[i].end; raw.remove(at: i); i = max(0, i - 1); continue
                    }
                }
                i += 1
            }
        }
        var result = [Phrase]()
        for phrase in raw {
            var cursor = phrase.start
            while phrase.end - cursor > p.maxLength + 0.001 {
                let remaining = phrase.end - cursor
                let pieces = ceil(remaining / p.maxLength)
                let feasible = remaining >= pieces * p.minLength
                let left = cursor + (feasible ? max(p.minLength, remaining - (pieces - 1) * p.maxLength) : min(p.minLength, remaining / pieces))
                let right = cursor + (feasible ? min(p.maxLength, remaining - (pieces - 1) * p.minLength) : remaining / pieces)
                let first = max(0, Int(ceil(left / audio.step)))
                let last = min(audio.energy.count - 1, Int(right / audio.step))
                var best = last, score = Double.greatestFiniteMagnitude
                if first <= last {
                    for i in first...last {
                        var value = Double(audio.energy[i])
                        if i > 0 { value += Double(audio.energy[i - 1]) }
                        if i + 1 < audio.energy.count { value += Double(audio.energy[i + 1]) }
                        value += Double(last - i) * 0.00001
                        if value < score { score = value; best = i }
                    }
                }
                let cut = min(right, max(left, Double(best) * audio.step))
                result.append(Phrase(start: cursor, end: cut)); cursor = cut
            }
            if phrase.end - cursor > 0.001 { result.append(Phrase(start: cursor, end: phrase.end)) }
        }
        return result
    }
    static func next(completed: Int, plays: Int, auto: Bool, index: Int, count: Int) -> Int? {
        if plays == 0 || completed < plays { return index }
        return auto && index >= 0 && index + 1 < count ? index + 1 : nil
    }
    static func gap(_ duration: Double, _ p: Preferences) -> Double { p.adaptive ? min(120, duration * p.multiplier) : p.gap }
}

struct Lesson {
    var duration: Double
    var segments: [Phrase]
    var selected: Int
    var manual: Phrase?
    var audioEntry: String
    var originalName: String
    func validate() throws {
        let extensions = ["wav", "mp3", "m4a", "aac", "flac", "mp4", "wma", "aiff", "aif", "caf"]
        let ext = URL(fileURLWithPath: audioEntry).pathExtension.lowercased()
        guard duration.isFinite, duration > 0, segments.count <= 100000, extensions.contains(ext), audioEntry == "audio." + ext else { throw Failure("课件格式无效。", "Invalid lesson metadata.") }
        func check(_ p: Phrase) -> Bool { p.start.isFinite && p.end.isFinite && p.start >= 0 && p.end <= duration + 0.001 && p.duration >= 0.001 }
        var end = 0.0
        for phrase in segments {
            guard check(phrase), phrase.start >= end - 0.000001 else { throw Failure("课件分段越界或重叠。", "Invalid lesson boundaries.") }; end = phrase.end
        }
        guard selected >= -1, selected < segments.count, selected != -1 || manual != nil || segments.isEmpty else { throw Failure("课件选区无效。", "Invalid lesson selection.") }
        if let manual = manual, !check(manual) { throw Failure("课件手动选区无效。", "Invalid manual selection.") }
    }
    var xml: String {
        func escape(_ value: String) -> String { value.replacingOccurrences(of: "&", with: "&amp;").replacingOccurrences(of: "<", with: "&lt;").replacingOccurrences(of: ">", with: "&gt;").replacingOccurrences(of: "\"", with: "&quot;").replacingOccurrences(of: "'", with: "&apos;") }
        func range(_ p: Phrase) -> String { "<Start>\(p.start)</Start><End>\(p.end)</End>" }
        return "<?xml version=\"1.0\" encoding=\"utf-8\"?><LessonManifest><Version>1</Version><AudioEntry>\(escape(audioEntry))</AudioEntry><OriginalName>\(escape(originalName))</OriginalName><Duration>\(duration)</Duration><Segments>" + segments.map { "<LessonSegment>\(range($0))</LessonSegment>" }.joined() + "</Segments><SelectedIndex>\(selected)</SelectedIndex>" + (manual.map { "<ManualSelection>\(range($0))</ManualSelection>" } ?? "") + "</LessonManifest>"
    }
    static func parse(_ data: Data) throws -> Self {
        guard data.count <= 4 * 1024 * 1024, let text = String(data: data, encoding: .utf8), !text.contains("<!DOCTYPE"), !text.contains("<!ENTITY") else { throw Failure("课件描述无效。", "Invalid lesson XML.") }
        let delegate = LessonXML()
        let parser = XMLParser(data: data); parser.delegate = delegate; parser.shouldResolveExternalEntities = false
        guard parser.parse(), delegate.version == 1, let result = delegate.lesson else { throw Failure("不支持的课件格式。", "Unsupported lesson format.") }
        try result.validate(); return result
    }
}

final class LessonXML: NSObject, XMLParserDelegate {
    var version = 0
    var values = [String: String](), stack = [String](), text = "", segments = [Phrase](), manual: Phrase?
    var begin = 0.0, end = 0.0
    var lesson: Lesson? {
        guard let duration = Double(values["Duration"] ?? ""), let selected = Int(values["SelectedIndex"] ?? "") else { return nil }
        return Lesson(duration: duration, segments: segments, selected: selected, manual: manual, audioEntry: values["AudioEntry"] ?? "", originalName: values["OriginalName"] ?? "audio")
    }
    func parser(_ parser: XMLParser, didStartElement name: String, namespaceURI: String?, qualifiedName: String?, attributes: [String: String]) { stack.append(name); text = ""; if name == "LessonSegment" || name == "ManualSelection" { begin = .nan; end = .nan } }
    func parser(_ parser: XMLParser, foundCharacters string: String) { text += string }
    func parser(_ parser: XMLParser, didEndElement name: String, namespaceURI: String?, qualifiedName: String?) {
        let value = text.trimmingCharacters(in: .whitespacesAndNewlines)
        if name == "Start" { begin = Double(value) ?? .nan }
        else if name == "End" { end = Double(value) ?? .nan }
        else if name == "LessonSegment" { segments.append(Phrase(start: begin, end: end)) }
        else if name == "ManualSelection", !attributesNil { manual = Phrase(start: begin, end: end) }
        else if name == "Version" { version = Int(value) ?? 0 }
        else if ["Duration", "SelectedIndex", "AudioEntry", "OriginalName"].contains(name) { values[name] = value }
        _ = stack.popLast(); text = ""
    }
    // XmlSerializer can omit the manual range or emit an xsi:nil empty element.
    var attributesNil: Bool { !begin.isFinite && !end.isFinite }
}

enum LessonFile {
    struct Opened {
        var manifest: Lesson
        var directory: URL
        var audio: URL { directory.appendingPathComponent(manifest.audioEntry) }
    }
    @discardableResult static func run(_ executable: String, _ arguments: [String], output: URL? = nil, directory: URL? = nil) throws -> Data {
        let process = Process(); process.executableURL = URL(fileURLWithPath: executable); process.arguments = arguments; process.currentDirectoryURL = directory
        let errorFile = FileManager.default.temporaryDirectory.appendingPathComponent(UUID().uuidString)
        FileManager.default.createFile(atPath: errorFile.path, contents: nil)
        let errors = try FileHandle(forWritingTo: errorFile)
        defer { try? errors.close(); try? FileManager.default.removeItem(at: errorFile) }
        process.standardError = errors
        let pipe = Pipe(); var target: FileHandle?
        if let output = output {
            FileManager.default.createFile(atPath: output.path, contents: nil); target = try FileHandle(forWritingTo: output); process.standardOutput = target
        } else { process.standardOutput = pipe }
        try process.run()
        let data = output == nil ? pipe.fileHandleForReading.readDataToEndOfFile() : Data()
        process.waitUntilExit(); try? target?.close()
        guard process.terminationStatus == 0 else { throw Failure("课件读写失败，请检查文件。", "Could not read or write the lesson.") }
        return data
    }
    static func open(_ url: URL) throws -> Opened {
        let listing = try run("/usr/bin/unzip", ["-Z1", url.path])
        let entries = String(decoding: listing, as: UTF8.self).split(separator: "\n").map(String.init)
        guard entries.count == 2, entries.filter({ $0 == "lesson.xml" }).count == 1 else { throw Failure("课件压缩包无效。", "Invalid lesson archive.") }
        // Read the small manifest first; never extract archive-provided paths.
        let sizeList = try run("/usr/bin/unzip", ["-l", url.path])
        let listingText = String(decoding: sizeList, as: UTF8.self)
        for line in listingText.split(separator: "\n") {
            let fields = line.split(whereSeparator: { $0 == " " || $0 == "\t" })
            if fields.count >= 4, let length = Int64(fields[0]), fields.last == "lesson.xml", length > 4 * 1024 * 1024 { throw Failure("课件描述过大。", "Lesson metadata is too large.") }
            if fields.count >= 4, let length = Int64(fields[0]), fields.last != "lesson.xml", length > 2 * 1024 * 1024 * 1024 { throw Failure("课件音频超过 2 GB。", "Lesson audio exceeds 2 GB.") }
        }
        let data = try run("/usr/bin/unzip", ["-p", url.path, "lesson.xml"])
        let manifest = try Lesson.parse(data)
        guard entries.contains(manifest.audioEntry) else { throw Failure("课件中缺少音频。", "Lesson audio is missing.") }
        let directory = FileManager.default.temporaryDirectory.appendingPathComponent("DictationLesson-" + UUID().uuidString, isDirectory: true)
        try FileManager.default.createDirectory(at: directory, withIntermediateDirectories: true)
        do {
            let audio = directory.appendingPathComponent(manifest.audioEntry)
            try run("/usr/bin/unzip", ["-p", url.path, manifest.audioEntry], output: audio)
            return Opened(manifest: manifest, directory: directory)
        } catch { try? FileManager.default.removeItem(at: directory); throw error }
    }
    static func save(_ destination: URL, audio: URL, manifest: Lesson) throws {
        try manifest.validate()
        guard destination.standardizedFileURL != audio.standardizedFileURL else { throw Failure("不能覆盖原音频。", "Cannot overwrite the source audio.") }
        let size = try audio.resourceValues(forKeys: [.fileSizeKey]).fileSize ?? 0
        guard size > 0, size <= 2 * 1024 * 1024 * 1024 else { throw Failure("音频大小无效或超过 2 GB。", "Invalid audio size or larger than 2 GB.") }
        let dir = FileManager.default.temporaryDirectory.appendingPathComponent("DictationSave-" + UUID().uuidString, isDirectory: true)
        try FileManager.default.createDirectory(at: dir, withIntermediateDirectories: true)
        let temp = destination.deletingLastPathComponent().appendingPathComponent("." + UUID().uuidString + ".edlesson")
        defer { try? FileManager.default.removeItem(at: dir); try? FileManager.default.removeItem(at: temp) }
        try Data(manifest.xml.utf8).write(to: dir.appendingPathComponent("lesson.xml"))
        try FileManager.default.copyItem(at: audio, to: dir.appendingPathComponent(manifest.audioEntry))
        try run("/usr/bin/zip", ["-q", "-1", temp.path, "lesson.xml", manifest.audioEntry], directory: dir)
        if FileManager.default.fileExists(atPath: destination.path) { _ = try FileManager.default.replaceItemAt(destination, withItemAt: temp) }
        else { try FileManager.default.moveItem(at: temp, to: destination) }
    }
}

func timeLabel(_ seconds: Double) -> String {
    let total = Int(max(0, seconds))
    return total >= 3600 ? String(format: "%d:%02d:%02d", total / 3600, total / 60 % 60, total % 60) : String(format: "%02d:%02d", total / 60, total % 60)
}
