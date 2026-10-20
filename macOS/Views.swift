import SwiftUI
import AppKit
import UniformTypeIdentifiers

struct WaveformView: NSViewRepresentable {
    @ObservedObject var model: PlayerModel
    func makeNSView(context: Context) -> NativeWaveform {
        let view = NativeWaveform(); view.model = model
        view.setAccessibilityLabel(model.l("音频波形，单击选段，拖动选区", "Audio waveform: click a phrase or drag a range"))
        return view
    }
    func updateNSView(_ view: NativeWaveform, context: Context) { view.model = model; view.needsDisplay = true }
}
struct MainView: View {
    @ObservedObject var model: PlayerModel
    var body: some View {
        VStack(spacing: 14) {
            header
            HStack { Text(model.filename.isEmpty ? model.l("MP3 / WAV / M4A / AAC / FLAC · 也可拖入课件", "MP3 / WAV / M4A / AAC / FLAC · Drop audio or lessons here") : model.filename + " · " + timeLabel(model.audio?.duration ?? 0)).foregroundStyle(.secondary).lineLimit(1); Spacer(); if model.busy { ProgressView().controlSize(.small) } }
            WaveformView(model: model).frame(minHeight: 150, idealHeight: 190, maxHeight: .infinity)
            Slider(value: $model.viewStart, in: 0...max(0.001, (model.audio?.duration ?? 0) - model.viewLength)).disabled(!model.ready || model.viewLength >= (model.audio?.duration ?? 0)).accessibilityLabel(model.l("音频时间位置", "Waveform time position"))
            selectionRow
            editingRow
            if model.prefs.boundary { boundaryRow }
            playbackRow
            extraRow
            Text(model.status.isEmpty ? model.l("空格：播放 / 暂停    ← →：切换片段    Return：从头重播", "Space: play / pause    ← →: change phrase    Return: replay") : model.status).font(.callout).foregroundStyle(.secondary).frame(maxWidth: .infinity, alignment: .leading).lineLimit(2).frame(height: 34, alignment: .topLeading)
        }
        .padding(24).frame(minWidth: 970, minHeight: model.prefs.boundary ? 630 : 590)
        .background(Color(nsColor: .windowBackgroundColor))
        .tint(.teal).preferredColorScheme(model.prefs.dark ? .dark : .light)
        .sheet(isPresented: $model.showSettings) { SettingsView(model: model) }
        .sheet(isPresented: $model.showSegmentation) { SegmentationView(model: model) }
        .sheet(isPresented: $model.showHelp) { InformationView(model: model, help: true) }
        .sheet(isPresented: $model.showAbout) { InformationView(model: model, help: false) }
        .onDrop(of: [.fileURL], isTargeted: nil) { providers in
            guard !model.busy, let provider = providers.first else { return false }
            _ = provider.loadObject(ofClass: URL.self) { url, _ in if let url = url { DispatchQueue.main.async { model.load(url) } } }; return true
        }
        .onAppear {
            model.installShortcuts()
        }
    }
    var header: some View {
        HStack {
            Text(model.l("英语听写播放器", "English Dictation Player")).font(.system(size: 26, weight: .semibold))
            Spacer()
            Button(model.l("设置", "Settings")) { model.showSettings = true }.controlSize(.large)
            Button(model.l("打开音频 / 课件", "Open audio / lesson")) { model.openPanel() }.buttonStyle(.borderedProminent).controlSize(.large).disabled(model.busy)
        }
    }
    var selectionRow: some View {
        HStack {
            Picker("", selection: Binding(get: { model.index }, set: { model.choose($0) })) {
                if model.index < 0 { Text(model.l("手动片段", "Manual range")).tag(-1) }
                ForEach(model.segments.indices, id: \.self) { i in Text(model.l("第 \(i + 1) 段", "Phrase \(i + 1)") + " · " + timeLabel(model.segments[i].start)).tag(i) }
            }.labelsHidden().frame(width: 190).disabled(!model.ready || model.segments.isEmpty)
            if let range = model.selection {
                Text((model.index < 0 ? model.l("手动选区", "Manual range") : "\(model.index + 1) / \(model.segments.count)") + String(format: "   %.2f — %.2f s   ·   %.1f s", range.start, range.end, range.duration)).font(.system(.callout, design: .monospaced)).foregroundStyle(.secondary).lineLimit(1)
            } else { Text(model.l("单击选短语 · 拖动选片段 · 双指捏合缩放", "Click a phrase · Drag a range · Pinch to zoom")).foregroundStyle(.secondary) }
            Spacer(minLength: 0)
            Button(model.l("分段设置", "Segmentation")) { model.showSegmentation = true }.disabled(!model.ready)
        }
    }
    var editingRow: some View {
        HStack(spacing: 10) {
            Button(model.l("合并下一段", "Merge next")) { model.merge() }.disabled(!model.ready || model.index < 0 || model.index + 1 >= model.segments.count)
            Button(model.l("在此拆分", "Split here")) { model.cut() }.disabled(!model.canCut)
            Button(model.l("撤销", "Undo")) { model.undo() }.disabled(!model.ready || model.history.isEmpty)
            Divider().frame(height: 20)
            Button(model.l("放大 +", "Zoom +")) { model.zoom(0.8) }.disabled(!model.ready)
            Button(model.l("缩小 −", "Zoom −")) { model.zoom(1.25) }.disabled(!model.ready)
            Button(model.l("显示全部", "Fit all")) { model.fit() }.disabled(!model.ready)
            Spacer()
        }.controlSize(.large)
    }
    var boundaryRow: some View {
        HStack {
            Button(model.l("起点 −0.1s", "Start −0.1s")) { model.nudge(true, -0.1) }
            Button(model.l("起点 +0.1s", "Start +0.1s")) { model.nudge(true, 0.1) }
            Button(model.l("终点 −0.1s", "End −0.1s")) { model.nudge(false, -0.1) }
            Button(model.l("终点 +0.1s", "End +0.1s")) { model.nudge(false, 0.1) }
            Spacer()
        }.disabled(!model.ready || model.selection == nil)
    }
    var playbackRow: some View {
        HStack(spacing: 12) {
            Button(model.l("上一段", "Previous")) { model.move(-1) }.disabled(!model.ready || model.segments.isEmpty)
            Button(model.waiting ? model.l("停止", "Stop") : model.playing ? (model.paused ? model.l("继续", "Resume") : model.l("暂停", "Pause")) : model.l("播放", "Play")) { model.toggle() }.buttonStyle(.borderedProminent).frame(minWidth: 65).disabled(!model.ready || (model.selection == nil && !model.playing))
            Button(model.l("下一段", "Next")) { model.move(1) }.disabled(!model.ready || model.segments.isEmpty)
            Picker(model.l("播放次数", "Repeats"), selection: $model.prefs.plays) {
                ForEach([1, 2, 3, 5, 10, 0], id: \.self) { n in Text(n == 0 ? model.l("无限", "Infinite") : "\(n)").tag(n) }
            }.frame(width: 145)
            Text(model.l("间隔", "Gap"))
            TextField("", value: Binding(get: { model.prefs.gap }, set: { model.prefs.gap = $0.isFinite ? min(30, max(0, $0)) : 1 }), format: .number.precision(.fractionLength(1))).frame(width: 45).disabled(model.prefs.adaptive)
            Text("s").foregroundStyle(.secondary)
            Toggle(model.l("播完进入下一段", "Auto next"), isOn: $model.prefs.autoNext)
            Spacer(minLength: 0)
        }.controlSize(.large).disabled(model.busy)
    }
    var extraRow: some View {
        HStack(spacing: 16) {
            Button(model.l("从头播放到尾", "Play entire audio")) { model.begin(whole: true) }.disabled(!model.ready)
            Picker(model.l("倍速", "Speed"), selection: Binding(get: { model.prefs.speed }, set: { model.changeRate($0) })) {
                ForEach([0.5, 0.8, 1, 1.2, 1.5, 2], id: \.self) { rate in Text(String(format: "%.1f×", rate)).tag(rate) }
            }.frame(width: 150)
            Toggle(model.l("教师模式", "Teacher mode"), isOn: $model.prefs.teacher)
            if model.prefs.teacher { Button(model.l("保存课件", "Save lesson")) { model.saveLesson() }.disabled(!model.ready) }
            if model.dirty && model.prefs.teacher { Text(model.l("未保存", "Unsaved")).foregroundStyle(.orange).font(.callout) }
            Spacer(minLength: 0)
        }.controlSize(.large).disabled(model.busy)
    }
}

struct NumberRow: View {
    let title: String
    @Binding var value: Double
    let range: ClosedRange<Double>
    var step = 0.1
    var body: some View {
        HStack { Text(title); Spacer(); TextField("", value: $value, format: .number.precision(.fractionLength(0...2))).multilineTextAlignment(.trailing).frame(width: 90); Stepper("", value: $value, in: range, step: step).labelsHidden() }
    }
}

struct SettingsView: View {
    @ObservedObject var model: PlayerModel
    @State var draft: Preferences
    @Environment(\.dismiss) var dismiss
    init(model: PlayerModel) { self.model = model; _draft = State(initialValue: model.prefs) }
    var body: some View {
        VStack(alignment: .leading, spacing: 20) {
            Text(model.l("设置", "Settings")).font(.title2.bold())
            Form {
                Picker(model.l("皮肤", "Theme"), selection: $draft.dark) { Text(model.l("默认模式", "Default")).tag(false); Text(model.l("黑色模式", "Dark")).tag(true) }
                Picker(model.l("语言", "Language"), selection: $draft.language) { Text("中文").tag("zh"); Text("English").tag("en") }
                Toggle(model.l("显示边界微调（±0.1 秒）", "Show boundary controls (±0.1 s)"), isOn: $draft.boundary)
                Toggle(model.l("按片段长度自动留白", "Adaptive gap based on phrase duration"), isOn: $draft.adaptive)
                NumberRow(title: model.l("留白倍数（最多 120 秒）", "Gap multiplier (up to 120 s)"), value: $draft.multiplier, range: 0.5...5, step: 0.5).disabled(!draft.adaptive)
                Toggle(model.l("切换下一段时自动播放（含右方向键）", "Play on Next (including Right arrow)"), isOn: $draft.playOnNext)
            }.formStyle(.grouped)
            HStack { Button(model.l("关于", "About")) { dismiss(); DispatchQueue.main.asyncAfter(deadline: .now() + 0.2) { model.showAbout = true } }; Button(model.l("帮助", "Help")) { dismiss(); DispatchQueue.main.asyncAfter(deadline: .now() + 0.2) { model.showHelp = true } }; Spacer(); Button(model.l("取消", "Cancel")) { dismiss() }.keyboardShortcut(.cancelAction); Button(model.l("保存设置", "Save settings")) { draft.multiplier = min(5, max(0.5, draft.multiplier)); model.prefs = draft; dismiss() }.buttonStyle(.borderedProminent).keyboardShortcut(.defaultAction) }
        }.padding(24).frame(width: 550, height: 430).background(Color(nsColor: .windowBackgroundColor)).preferredColorScheme(model.prefs.dark ? .dark : .light)
    }
}

struct SegmentationView: View {
    @ObservedObject var model: PlayerModel
    @State var draft: Preferences
    @State var preset = 0
    @Environment(\.dismiss) var dismiss
    init(model: PlayerModel) { self.model = model; _draft = State(initialValue: model.prefs) }
    var body: some View {
        VStack(alignment: .leading, spacing: 14) {
            Text(model.l("自动分段设置", "Segmentation settings")).font(.title2.bold())
            Form {
                Picker(model.l("常用分段推荐", "Presets"), selection: $preset) {
                    Text(model.l("自定义", "Custom")).tag(0); Text(model.l("短语练习：1–5 秒", "Short phrases: 1–5 s")).tag(1); Text(model.l("常规听写：2–8 秒", "Dictation: 2–8 s")).tag(2); Text(model.l("长句练习：4–15 秒", "Long phrases: 4–15 s")).tag(3)
                }.onChange(of: preset) { value in
                    if value > 0 { draft.minLength = [0, 1, 2, 4][value]; draft.maxLength = [0, 5, 8, 15][value]; draft.silence = [0, 0.25, 0.3, 0.5][value]; draft.mergeShort = true; draft.sensitivity = -26 }
                }
                NumberRow(title: model.l("最短片段（秒）", "Minimum phrase (s)"), value: $draft.minLength, range: 0.2...30, step: 0.5)
                NumberRow(title: model.l("最长片段（秒）", "Maximum phrase (s)"), value: $draft.maxLength, range: 2...30, step: 0.5)
                Toggle(model.l("合并过短片段（优先连接下一段）", "Merge short phrases into the next phrase"), isOn: $draft.mergeShort)
                NumberRow(title: model.l("开头预留（秒）", "Leading padding (s)"), value: $draft.leading, range: 0...1)
                NumberRow(title: model.l("分段停顿（秒）", "Pause threshold (s)"), value: $draft.silence, range: 0.15...1.5, step: 0.05)
                NumberRow(title: model.l("静音阈值（相对 dB）", "Silence threshold (relative dB)"), value: $draft.sensitivity, range: -45 ... -10, step: 1)
            }.formStyle(.grouped)
            Text(model.l("按声音和停顿分段，不识别文字。短词可合并，长停顿不强行连接。重新分段会替换当前片段，请先保存课件。", "Splits by energy and pauses, without speech recognition. Short phrases can be merged; long silences stay separate. Resegment replaces current phrases: save your lesson first.")).font(.callout).foregroundStyle(.secondary)
            HStack { Spacer(); Button(model.l("取消", "Cancel")) { dismiss() }.keyboardShortcut(.cancelAction); Button(model.l("重新分段", "Resegment")) {
                guard draft.minLength.isFinite, draft.maxLength.isFinite, draft.minLength >= 0.2, draft.maxLength >= 2, draft.maxLength <= 30, draft.minLength <= draft.maxLength, (0...1).contains(draft.leading), (0.15...1.5).contains(draft.silence), (-45 ... -10).contains(draft.sensitivity) else { model.report(Failure("请检查时长区间和分段参数。", "Please check the duration interval and segmentation values.")); return }
                model.prefs = draft; model.resegment(); dismiss()
            }.buttonStyle(.borderedProminent).keyboardShortcut(.defaultAction) }
        }.padding(24).frame(width: 570, height: 640).background(Color(nsColor: .windowBackgroundColor)).preferredColorScheme(model.prefs.dark ? .dark : .light)
    }
}

struct InformationView: View {
    @ObservedObject var model: PlayerModel
    let help: Bool
    @Environment(\.dismiss) var dismiss
    var body: some View {
        VStack(alignment: .leading, spacing: 18) {
            Text(model.l(help ? "帮助" : "关于", help ? "Help" : "About")).font(.title2.bold())
            ScrollView { Text(help ? helpText : aboutText).textSelection(.enabled).frame(maxWidth: .infinity, alignment: .leading).lineSpacing(5) }
            Button(model.l("关闭", "Close")) { dismiss() }.keyboardShortcut(.cancelAction).frame(maxWidth: .infinity, alignment: .trailing)
        }.padding(24).frame(width: 600, height: help ? 600 : 350).background(Color(nsColor: .windowBackgroundColor)).preferredColorScheme(model.prefs.dark ? .dark : .light)
    }
    var aboutText: String { model.l("英语听写播放器 · Mac 版 1.1\n\n轻便的离线英语听写工具，支持分段、重复练习和教师课件。\n\n作者：ThermoXu\n免费使用。\n\n使用 macOS 原生 SwiftUI 与 AVFoundation，无需 Python、Node 或联网。", "English Dictation Player · Mac 1.1\n\nA lightweight offline dictation player with phrase practice, repeats and portable teacher lessons.\n\nAuthor: ThermoXu\nFree to use.\n\nBuilt with native macOS SwiftUI and AVFoundation. No Python, Node or Internet connection required.") }
    var helpText: String { model.l("1. 打开音频或拖入文件，自动按停顿分段。支持 MP3、WAV、M4A、AAC、FLAC、AIFF 等系统支持格式；WMA 请先转换。\n\n2. 单击波形选择短语；拖动可选任意范围。使用放大、缩小、显示全部和下方时间滑块浏览，也可双指捏合缩放。\n\n3. 播放次数支持 1 / 2 / 3 / 5 / 10 或无限，间隔 0–30 秒。勾选“播完进入下一段”会在重复完成后切换；无限重复停留当前段。\n\n4. 倍速 0.5 / 0.8 / 1.0 / 1.2 / 1.5 / 2.0，保持音调。全文播放忽略分段和重复间隔。\n\n5. 单击段内放置虚线光标，再点“在此拆分”；两侧至少各 0.1 秒。支持合并下一段和最多 20 次撤销。边界微调可在设置开启。\n\n6. 分段设置支持时长区间、常用推荐、短词合并和 0–1 秒句首预留。按声音判断，不保证语法完整。\n\n7. 教师模式：分段调整后保存 .edlesson 课件，内含原音频与分段，下次打开直接恢复。可打开 Windows v1.1 课件，常见格式的 Mac 课件也可回到 Windows 使用。AIFF / CAF 课件仅 Mac 支持。学生也需要本播放器。\n\n8. 课件手动保存，不是自动保存。未保存修改在关闭或换文件前提示。播放次数、倍速和皮肤等使用各自本机设置。\n\n9. 设置可选“切换下一段时自动播放”、按片段时长自动留白（0.5–5 倍，上限 120 秒）、中英文、默认 / 黑色模式。\n\n快捷键\n空格：播放 / 暂停 / 继续，间隔时停止\n← / →：上一段 / 下一段\nReturn：从片段开头重播\nEsc：停止 / 关闭弹窗\n⌘O：打开音频或课件\n⌘S：保存课件\n⌘,：设置\nF1：帮助\n\n在文字和数值输入框中优先使用正常编辑按键。原音频不会被改写。", "1. Open audio or drop a file to split it at pauses. Supports MP3, WAV, M4A, AAC, FLAC, AIFF and system-supported formats. Convert WMA first.\n\n2. Click a phrase or drag any range. Zoom + / −, Fit all, the time slider and pinch gestures adjust the view.\n\n3. Repeat 1 / 2 / 3 / 5 / 10 times or infinitely, with a 0–30 s gap. Auto next advances after the repeats. Infinite keeps the current phrase.\n\n4. Speeds: 0.5 / 0.8 / 1.0 / 1.2 / 1.5 / 2.0× with original pitch. Play entire audio ignores phrase repeats and gaps.\n\n5. Click inside a phrase to place the dashed edit cursor, then Split here (at least 0.1 s on either side). Merge next and Undo (20 steps) are available. Optional boundary controls adjust ±0.1 s.\n\n6. Segmentation offers min/max duration, presets, short-phrase merging and 0–1 s leading padding. It uses energy and pauses, without speech recognition.\n\n7. Teacher mode saves .edlesson files containing the original audio and edited phrases. Reopen without automatic resegmentation. Windows v1.1 lessons are supported; common-format Mac lessons also work on Windows. AIFF / CAF lessons are Mac-only. Students need the player.\n\n8. Lessons require manual saving. Unsaved edits are warned before closing or opening another file. Playback preferences are local to each user.\n\n9. Settings offers Play on Next, adaptive gaps (0.5–5× original duration, capped at 120 s), Chinese / English and Default / Dark mode.\n\nSHORTCUTS\nSpace: Play / Pause / Resume, or stop during a gap\n← / →: Previous / Next\nReturn: Replay phrase from its beginning\nEsc: Stop / Close dialog\n⌘O: Open audio or lesson\n⌘S: Save lesson\n⌘,: Settings\nF1: Help\n\nText and number fields keep normal editing keys. Source audio is never modified.") }
}

@main
struct DictationLauncher {
    static func main() {
        if CommandLine.arguments.contains("--self-test") { exit(MacTests.run()) }
        let application = NSApplication.shared
        application.setActivationPolicy(.regular)
        let delegate = AppDelegate()
        application.delegate = delegate
        application.run()
        withExtendedLifetime(delegate) {}
    }
}
