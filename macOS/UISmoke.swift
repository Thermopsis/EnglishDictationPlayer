import AppKit
import Foundation
import CoreGraphics

enum UISmoke {
    static var output: URL {
        let args = CommandLine.arguments
        if let i = args.firstIndex(of: "--ui-output"), i + 1 < args.count { return URL(fileURLWithPath: args[i + 1], isDirectory: true) }
        return FileManager.default.temporaryDirectory.appendingPathComponent("DictationUISmoke", isDirectory: true)
    }
    static func write(_ message: String) {
        try? FileManager.default.createDirectory(at: output, withIntermediateDirectories: true)
        try? Data(message.utf8).write(to: output.appendingPathComponent("UI_TEST.txt"))
    }
    static func fail(_ message: String) { write("FAIL \(message)\n"); exit(1) }
    static func note(_ message: String) { print("UI: \(message)"); fflush(stdout) }
    static func snapshot(_ name: String) {
        note("Snapshot \(name)")
        guard let main = NSApp.windows.first(where: { $0.isVisible && $0.frame.width > 700 }) else { fail("No visible application window"); return }
        let needsSheet = name.hasPrefix("settings-") || name.hasPrefix("segmentation-")
        if needsSheet && main.sheets.isEmpty { fail("Expected settings sheet was not presented"); return }
        let window = needsSheet ? main.sheets[0] : main
        window.displayIfNeeded()
        if let view = window.contentView, let bitmap = view.bitmapImageRepForCachingDisplay(in: view.bounds) {
            view.cacheDisplay(in: view.bounds, to: bitmap)
            if let png = bitmap.representation(using: .png, properties: [:]) { try? png.write(to: output.appendingPathComponent(name + ".png")) }
        }
    }
    static func run(_ model: PlayerModel) {
        note("Run model checks")
        let original = model.segments
        let preferences = model.prefs
        guard original.count == 2, let audio = model.audio, !model.busy else { fail("Lesson did not restore before UI test"); return }
        model.prefs.playOnNext = false
        model.choose(0); model.move(1)
        guard model.index == 1, !model.playing else { fail("Next played when disabled"); return }
        model.move(-1)
        model.editCursor = (original[0].start + original[0].end) / 2
        model.cut()
        guard model.segments.count == 3 else { fail("UI split failed"); return }
        model.merge()
        guard model.segments == original else { fail("UI merge changed ranges"); return }
        model.nudge(true, -0.1); model.undo()
        guard model.segments == original else { fail("UI boundary undo failed"); return }
        model.select(0.2, 0.8)
        guard model.index == -1, model.manual == Phrase(start: 0.2, end: 0.8) else { fail("UI manual range failed"); return }
        model.choose(0); model.zoom(0.8)
        guard model.viewLength < audio.duration else { fail("UI zoom failed"); return }
        model.fit(); model.history = []; model.dirty = false
        model.prefs.dark = false; model.prefs.language = "zh"
        DispatchQueue.main.asyncAfter(deadline: .now() + 0.8) {
            snapshot("main-light-zh")
            model.prefs.dark = true; model.prefs.language = "en"
            DispatchQueue.main.asyncAfter(deadline: .now() + 0.8) {
                snapshot("main-dark-en"); model.showSettings = true
                DispatchQueue.main.asyncAfter(deadline: .now() + 0.8) {
                    snapshot("settings-dark-en"); model.showSettings = false
                    DispatchQueue.main.asyncAfter(deadline: .now() + 0.5) {
                        model.showSegmentation = true
                        DispatchQueue.main.asyncAfter(deadline: .now() + 0.8) {
                            snapshot("segmentation-dark-en"); model.showSegmentation = false
                            model.prefs = preferences; model.dirty = false
                            write("PASS native window launch\nPASS Windows lesson restored without resegmentation\nPASS Next with autoplay disabled\nPASS split and merge preserve ranges\nPASS boundary adjustment and undo\nPASS manual selection\nPASS zoom and fit\nPASS Chinese/light and English/dark rendering\nPASS settings and segmentation sheets\n")
                            model.cleanup(); note("Complete"); exit(0)
                        }
                    }
                }
            }
        }
    }
}
