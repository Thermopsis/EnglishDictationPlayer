import SwiftUI
import AppKit
import AVFoundation
import UniformTypeIdentifiers

final class Playback {
    let engine = AVAudioEngine(), node = AVAudioPlayerNode(), pitch = AVAudioUnitTimePitch()
    var file: AVAudioFile?
    var generation = 0
    var start = 0.0, end = 0.0
    var paused = false
    init() { engine.attach(node); engine.attach(pitch) }
    func stop() { generation += 1; node.stop(); engine.stop(); paused = false }
    var position: Double {
        guard let time = node.lastRenderTime, let playerTime = node.playerTime(forNodeTime: time) else { return start }
        return min(end, start + Double(playerTime.sampleTime) / playerTime.sampleRate)
    }
    func play(_ url: URL, range: Phrase, rate: Double, finished: @escaping () -> Void) throws {
        stop()
        let file = try AVAudioFile(forReading: url)
        self.file = file
        engine.disconnectNodeOutput(node); engine.disconnectNodeOutput(pitch)
        engine.connect(node, to: pitch, format: file.processingFormat)
        engine.connect(pitch, to: engine.mainMixerNode, format: file.processingFormat)
        pitch.rate = Float(rate); pitch.pitch = 0; engine.mainMixerNode.outputVolume = 1
        let first = max(0, min(file.length, AVAudioFramePosition(range.start * file.processingFormat.sampleRate)))
        let last = min(file.length, AVAudioFramePosition(range.end * file.processingFormat.sampleRate))
        guard last > first, last - first <= Int64(UInt32.max) else { throw Failure("选区过短或过长。", "The selected range is too short or too long.") }
        start = Double(first) / file.processingFormat.sampleRate; end = range.end
        let ticket = generation
        node.scheduleSegment(file, startingFrame: first, frameCount: AVAudioFrameCount(last - first), at: nil, completionCallbackType: .dataPlayedBack) { [weak self] _ in
            DispatchQueue.main.async { guard let self = self, self.generation == ticket else { return }; finished() }
        }
        try engine.start(); node.play()
    }
    func togglePause() { if paused { node.play() } else { node.pause() }; paused.toggle() }
}

final class PlayerModel: ObservableObject {
    @Published var prefs = Preferences.load() { didSet { prefs.save() } }
    @Published var audio: AudioAnalysis?
    @Published var segments = [Phrase]()
    @Published var index = -1
    @Published var manual: Phrase?
    @Published var filename = ""
    @Published var status = ""
    @Published var busy = false
    @Published var playing = false
    @Published var paused = false
    @Published var waiting = false
    @Published var cursor: Double? = nil
    @Published var editCursor: Double? = nil
    @Published var viewStart = 0.0
    @Published var viewLength = 120.0
    @Published var history = [[Phrase]]()
    @Published var dirty = false
    @Published var showSettings = false
    @Published var showSegmentation = false
    @Published var showHelp = false
    @Published var showAbout = false
    private let playback = Playback()
    private var timer: Timer?
    private var completed = 0
    private var wholeFile = false
    private var waitUntil = Date()
    private var extractedDirectory: URL?
    private var lessonURL: URL?
    private var originalName = ""
    private var monitor: Any?
    var selection: Phrase? { manual ?? (segments.indices.contains(index) ? segments[index] : nil) }
    var ready: Bool { audio != nil && !busy }
    var canCut: Bool { guard let p = selection, let t = editCursor else { return false }; return ready && index >= 0 && t - p.start >= 0.1 && p.end - t >= 0.1 }
    func l(_ zh: String, _ en: String) -> String { prefs.language == "en" ? en : zh }
    init() {
        timer = Timer.scheduledTimer(withTimeInterval: 1.0 / 30, repeats: true) { [weak self] _ in self?.tick() }
        if let timer = timer { RunLoop.main.add(timer, forMode: .common) }
    }
    deinit {
        timer?.invalidate(); playback.stop()
        if let monitor = monitor { NSEvent.removeMonitor(monitor) }
        if let dir = extractedDirectory { try? FileManager.default.removeItem(at: dir) }
    }
    func installShortcuts() {
        guard monitor == nil else { return }
        monitor = NSEvent.addLocalMonitorForEvents(matching: .keyDown) { [weak self] event in
            guard let self = self, !self.showSettings, !self.showSegmentation, !self.showHelp, !self.showAbout, !self.busy, NSApp.modalWindow == nil else { return event }
            if NSApp.keyWindow?.firstResponder is NSTextView { return event }
            guard event.modifierFlags.intersection([.command, .control, .option]).isEmpty else { return event }
            switch event.keyCode {
            case 49: self.toggle(); return nil
            case 123: self.move(-1); return nil
            case 124: self.move(1); return nil
            case 36, 76: self.stop(); self.begin(); return nil
            case 53: self.stop(); return nil
            case 122: self.showHelp = true; return nil
            default: return event
            }
        }
    }
    func confirmDiscard() -> Bool {
        guard dirty && (prefs.teacher || lessonURL != nil) else { return true }
        let alert = NSAlert(); alert.messageText = l("课件修改尚未保存", "Unsaved lesson edits")
        alert.informativeText = l("继续会丢失这些分段修改。请取消后保存课件，或放弃修改。", "Continuing discards these edits. Cancel to save the lesson first, or discard the edits.")
        alert.addButton(withTitle: l("取消", "Cancel")); alert.addButton(withTitle: l("放弃修改", "Discard edits"))
        return alert.runModal() == .alertSecondButtonReturn
    }
    func report(_ error: Error) {
        let alert = NSAlert(); alert.messageText = l("操作失败", "Operation failed"); alert.informativeText = error.localizedDescription; alert.runModal()
    }
    func openPanel() {
        guard !busy else { return }
        let panel = NSOpenPanel(); panel.allowsMultipleSelection = false
        panel.allowedContentTypes = ["mp3", "wav", "m4a", "aac", "flac", "mp4", "aiff", "aif", "caf", "edlesson"].compactMap { UTType(filenameExtension: $0) }
        panel.title = l("打开音频或课件", "Open audio or lesson")
        if panel.runModal() == .OK, let url = panel.url { load(url) }
    }
    func load(_ url: URL) {
        guard !busy, confirmDiscard() else { return }
        stop(); busy = true; status = l("正在分析音频…", "Analyzing audio...")
        DispatchQueue.global(qos: .userInitiated).async {
            var opened: LessonFile.Opened?
            do {
                if url.pathExtension.lowercased() == "edlesson" { opened = try LessonFile.open(url) }
                let result = try AudioAnalysis.load(opened?.audio ?? url)
                if let opened = opened, abs(result.duration - opened.manifest.duration) > 0.1 { throw Failure("课件音频长度不匹配。", "Lesson duration does not match the audio.") }
                let lesson = opened
                DispatchQueue.main.async {
                    if let dir = self.extractedDirectory { try? FileManager.default.removeItem(at: dir) }
                    self.extractedDirectory = lesson?.directory
                    self.audio = result; self.filename = url.lastPathComponent
                    self.originalName = lesson?.manifest.originalName ?? url.lastPathComponent
                    self.lessonURL = lesson == nil ? nil : url
                    self.viewStart = 0; self.viewLength = min(120, result.duration); self.history = []; self.busy = false
                    if let lesson = lesson {
                        self.segments = lesson.manifest.segments; self.index = lesson.manifest.selected; self.manual = lesson.manifest.manual
                        self.status = self.l("已恢复课件：\(self.segments.count) 段，未重新自动分段。", "Restored \(self.segments.count) phrases without resegmenting.")
                    } else {
                        self.segments = Segmenter.split(result, self.prefs); self.index = self.segments.isEmpty ? -1 : 0; self.manual = nil
                        self.status = self.l("已分为 \(self.segments.count) 段。单击选段，拖动选区。", "Split into \(self.segments.count) phrases. Click to select; drag for a manual range.")
                    }
                    self.dirty = false; self.showSelection()
                }
            } catch {
                if let dir = opened?.directory { try? FileManager.default.removeItem(at: dir) }
                DispatchQueue.main.async { self.busy = false; self.status = self.l("打开失败，原有音频仍可使用。", "Could not open; previous audio is still available."); self.report(error) }
            }
        }
    }
    func saveLesson() {
        guard ready, let audio = audio else { return }
        let panel = NSSavePanel(); panel.allowedContentTypes = [UTType(filenameExtension: "edlesson")!]
        panel.nameFieldStringValue = lessonURL?.lastPathComponent ?? (URL(fileURLWithPath: originalName).deletingPathExtension().lastPathComponent + ".edlesson")
        panel.title = l("保存课件", "Save lesson")
        guard panel.runModal() == .OK, let url = panel.url else { return }
        let manifest = Lesson(duration: audio.duration, segments: segments, selected: index, manual: manual, audioEntry: "audio." + audio.url.pathExtension.lowercased(), originalName: originalName)
        stop(); busy = true; status = l("正在保存课件…", "Saving lesson...")
        DispatchQueue.global(qos: .userInitiated).async {
            do {
                try LessonFile.save(url, audio: audio.url, manifest: manifest)
                DispatchQueue.main.async { self.busy = false; self.lessonURL = url; self.dirty = false; self.status = self.l("课件已保存：", "Lesson saved: ") + url.lastPathComponent }
            } catch { DispatchQueue.main.async { self.busy = false; self.report(error) } }
        }
    }
    func resegment() {
        guard let audio = audio, !busy else { return }
        stop(); busy = true
        let p = prefs
        DispatchQueue.global(qos: .userInitiated).async {
            let phrases = Segmenter.split(audio, p)
            DispatchQueue.main.async { self.segments = phrases; self.index = phrases.isEmpty ? -1 : 0; self.manual = nil; self.history = []; self.editCursor = nil; self.busy = false; self.dirty = true; self.showSelection() }
        }
    }
    func choose(_ i: Int) {
        guard !busy, segments.indices.contains(i) else { return }
        stop(); index = i; manual = nil; editCursor = nil; showSelection()
    }
    func move(_ direction: Int) {
        guard ready, !segments.isEmpty else { return }
        var target: Int
        if index >= 0 { target = index + direction }
        else if direction > 0 { target = segments.firstIndex { $0.start >= (manual?.end ?? 0) - 0.001 } ?? segments.count - 1 }
        else { target = segments.lastIndex { $0.end <= (manual?.start ?? 0) + 0.001 } ?? 0 }
        target = min(segments.count - 1, max(0, target))
        let changed = target != index; choose(target)
        if changed && direction > 0 && prefs.playOnNext { begin() }
    }
    func click(_ t: Double) {
        guard ready, !segments.isEmpty else { return }
        let nearest = segments.indices.min { distance(t, segments[$0]) < distance(t, segments[$1]) } ?? 0
        choose(nearest); editCursor = t
    }
    private func distance(_ t: Double, _ p: Phrase) -> Double { t < p.start ? p.start - t : t > p.end ? t - p.end : 0 }
    func select(_ a: Double, _ b: Double) {
        guard ready, abs(a - b) >= 0.1 else { return }
        stop(); manual = Phrase(start: min(a, b), end: max(a, b)); index = -1; editCursor = nil; dirty = true
    }
    func remember() { stop(); if history.count == 20 { history.removeFirst() }; history.append(segments); dirty = true }
    func merge() { guard ready, index >= 0, index + 1 < segments.count else { return }; remember(); segments[index].end = segments[index + 1].end; segments.remove(at: index + 1); editCursor = nil }
    func cut() { guard canCut, let t = editCursor else { return }; remember(); let old = segments[index]; segments[index].end = t; segments.insert(Phrase(start: t, end: old.end), at: index + 1); editCursor = nil }
    func undo() { guard ready, let previous = history.popLast() else { return }; stop(); segments = previous; index = min(max(0, index), segments.count - 1); manual = nil; editCursor = nil; dirty = true }
    func nudge(_ isStart: Bool, _ amount: Double) {
        guard ready, var p = selection, let audio = audio else { return }
        if index >= 0 { remember() } else { stop(); dirty = true }
        let lower = index > 0 ? segments[index - 1].end : 0
        let upper = index >= 0 && index + 1 < segments.count ? segments[index + 1].start : audio.duration
        if isStart { p.start = max(lower, min(p.end - 0.1, p.start + amount)) } else { p.end = min(upper, max(p.start + 0.1, p.end + amount)) }
        if index >= 0 { segments[index] = p } else { manual = p }; editCursor = nil
    }
    func showSelection() {
        guard let audio = audio, let p = selection else { return }
        if p.duration > viewLength * 0.85 { viewLength = min(audio.duration, max(5, p.duration * 1.3)) }
        if p.start < viewStart || p.end > viewStart + viewLength { viewStart = max(0, min(audio.duration - viewLength, p.start - viewLength * 0.12)) }
    }
    func zoom(_ factor: Double) {
        guard let audio = audio else { return }
        let anchor = editCursor.flatMap { $0 >= viewStart && $0 <= viewStart + viewLength ? $0 : nil } ?? (viewStart + viewLength / 2)
        viewLength = min(audio.duration, max(min(2, audio.duration), viewLength * factor)); viewStart = max(0, min(audio.duration - viewLength, anchor - viewLength / 2))
    }
    func fit() { if let audio = audio { viewStart = 0; viewLength = audio.duration } }
    func stop() { playback.stop(); playing = false; waiting = false; paused = false; wholeFile = false; cursor = nil }
    func toggle() {
        guard ready else { return }
        if waiting { stop() }
        else if playing { playback.togglePause(); paused = playback.paused }
        else { begin() }
    }
    func begin(whole: Bool = false) { guard ready, whole || selection != nil else { return }; stop(); completed = 0; wholeFile = whole; playing = true; startClip() }
    func changeRate(_ rate: Double) {
        let current = playback.position, wasPaused = paused
        prefs.speed = rate
        if playing && !waiting { startClip(resume: current); if wasPaused { playback.togglePause(); paused = true } }
    }
    private func startClip(resume: Double? = nil) {
        guard let audio = audio, let range = wholeFile ? Phrase(start: 0, end: audio.duration) : selection else { stop(); return }
        waiting = false; paused = false
        do {
            try playback.play(audio.url, range: Phrase(start: resume ?? range.start, end: range.end), rate: prefs.speed) { [weak self] in self?.ended() }
            status = wholeFile ? l("全文连续播放", "Playing entire audio") : l("正在播放 · 第 \(completed + 1) 遍", "Playing · repeat \(completed + 1)")
        } catch { stop(); report(error) }
    }
    private func ended() {
        guard playing else { return }
        if wholeFile { stop(); status = l("全文播放完成", "Entire audio finished"); return }
        completed += 1
        if Segmenter.next(completed: completed, plays: prefs.plays, auto: prefs.autoNext, index: index, count: segments.count) == nil { stop(); status = l("本段播放完成", "Phrase finished"); return }
        waiting = true; waitUntil = Date().addingTimeInterval(Segmenter.gap(selection?.duration ?? 0, prefs)); cursor = selection?.end
    }
    private func tick() {
        guard playing else { return }
        if waiting {
            let seconds = waitUntil.timeIntervalSinceNow
            if seconds > 0 { status = l(String(format: "间隔 %.1f 秒 · 点击停止可结束", seconds), String(format: "Gap %.1f s · Stop to end", seconds)); return }
            if prefs.plays > 0 && completed >= prefs.plays {
                guard let target = Segmenter.next(completed: completed, plays: prefs.plays, auto: prefs.autoNext, index: index, count: segments.count), segments.indices.contains(target) else { stop(); return }
                index = target; manual = nil; completed = 0; showSelection()
            }
            startClip()
        } else {
            cursor = playback.position
            if wholeFile, let cursor = cursor, let audio = audio, cursor > viewStart + viewLength { viewStart = min(max(0, audio.duration - viewLength), cursor) }
        }
    }
    func cleanup() { stop(); if let dir = extractedDirectory { try? FileManager.default.removeItem(at: dir); extractedDirectory = nil } }
}

final class WindowDelegate: NSObject, NSWindowDelegate {
    weak var model: PlayerModel?
    func windowShouldClose(_ sender: NSWindow) -> Bool {
        guard let model = model else { return true }
        if model.busy { NSSound.beep(); return false }
        if model.confirmDiscard() { model.dirty = false; return true }; return false
    }
}

final class AppDelegate: NSObject, NSApplicationDelegate {
    weak var model: PlayerModel?
    var pending: URL?
    func application(_ application: NSApplication, open urls: [URL]) { if let url = urls.first { if let model = model { model.load(url) } else { pending = url } } }
    func applicationShouldTerminate(_ sender: NSApplication) -> NSApplication.TerminateReply {
        if let model = model { if model.busy || !model.confirmDiscard() { return .terminateCancel }; model.cleanup() }
        return .terminateNow
    }
}
