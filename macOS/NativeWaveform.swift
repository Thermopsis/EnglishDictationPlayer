import AppKit

final class NativeWaveform: NSView {
    var model: PlayerModel?
    private var downX = 0.0
    private var dragging: (Double, Double)?
    override var isFlipped: Bool { true }
    override var acceptsFirstResponder: Bool { true }
    override var isOpaque: Bool { true }
    func time(_ x: Double) -> Double {
        guard let model = model else { return 0 }
        return min(model.audio?.duration ?? 0, max(0, model.viewStart + (x - 18) * model.viewLength / max(1, bounds.width - 36)))
    }
    override func draw(_ dirtyRect: NSRect) {
        guard let model = model else { return }
        let background = model.prefs.dark ? NSColor.controlBackgroundColor : NSColor.white
        background.setFill(); bounds.fill()
        guard let audio = model.audio else {
            let string = NSAttributedString(string: model.l("打开一段音频，开始逐段听写", "Open audio to start phrase dictation"), attributes: [.foregroundColor: NSColor.secondaryLabelColor, .font: NSFont.systemFont(ofSize: 14)])
            let size = string.size(); string.draw(at: NSPoint(x: (bounds.width - size.width) / 2, y: (bounds.height - size.height) / 2)); return
        }
        let width = bounds.width - 36, bottom = bounds.height - 30, center = bottom / 2 + 5
        func x(_ t: Double) -> Double { 18 + (t - model.viewStart) * width / model.viewLength }
        NSGraphicsContext.saveGraphicsState()
        NSBezierPath(rect: NSRect(x: 18, y: 10, width: width, height: bottom - 10)).addClip()
        let guide = NSBezierPath()
        guide.move(to: NSPoint(x: 18, y: center)); guide.line(to: NSPoint(x: bounds.width - 18, y: center))
        for p in model.segments where p.start >= model.viewStart && p.start <= model.viewStart + model.viewLength { guide.move(to: NSPoint(x: x(p.start), y: 10)); guide.line(to: NSPoint(x: x(p.start), y: bottom)) }
        NSColor.secondaryLabelColor.withAlphaComponent(0.17).setStroke(); guide.stroke()
        func highlight(_ p: Phrase) {
            NSColor.systemTeal.withAlphaComponent(0.15).setFill()
            NSBezierPath(rect: NSRect(x: x(p.start), y: 10, width: max(1, x(p.end) - x(p.start)), height: bottom - 10)).fill()
            let line = NSBezierPath(); line.lineWidth = 1.5
            for t in [p.start, p.end] { line.move(to: NSPoint(x: x(t), y: 10)); line.line(to: NSPoint(x: x(t), y: bottom)) }
            NSColor.systemTeal.setStroke(); line.stroke()
        }
        if let range = model.selection { highlight(range) }
        if let range = dragging { highlight(Phrase(start: min(range.0, range.1), end: max(range.0, range.1))) }
        let path = NSBezierPath()
        for pixel in 0..<max(1, Int(width)) {
            let t = model.viewStart + Double(pixel) * model.viewLength / width
            let a = max(0, min(audio.peaks.count, Int(t / audio.step)))
            let b = min(audio.peaks.count, max(a + 1, Int((t + model.viewLength / width) / audio.step)))
            var peak: Float = 0
            if a < b { for i in a..<b { peak = max(peak, audio.peaks[i]) } }
            let height = max(0.4, sqrt(Double(peak)) * max(5, center - 22))
            path.move(to: NSPoint(x: 18 + Double(pixel), y: center - height)); path.line(to: NSPoint(x: 18 + Double(pixel), y: center + height))
        }
        (model.prefs.dark ? NSColor(red: 0.68, green: 0.81, blue: 0.85, alpha: 1) : NSColor(red: 0.16, green: 0.31, blue: 0.36, alpha: 1)).setStroke(); path.stroke()
        if let t = model.editCursor {
            let path = NSBezierPath(); path.move(to: NSPoint(x: x(t), y: 10)); path.line(to: NSPoint(x: x(t), y: bottom)); path.setLineDash([3], count: 1, phase: 0); NSColor.secondaryLabelColor.setStroke(); path.stroke()
        }
        if let t = model.cursor { let path = NSBezierPath(); path.lineWidth = 2; path.move(to: NSPoint(x: x(t), y: 10)); path.line(to: NSPoint(x: x(t), y: bottom)); NSColor.systemOrange.setStroke(); path.stroke() }
        NSGraphicsContext.restoreGraphicsState()
        let tick = model.viewLength <= 8 ? 1.0 : model.viewLength <= 40 ? 5.0 : model.viewLength <= 160 ? 20.0 : ceil(model.viewLength / 480) * 60
        let attributes: [NSAttributedString.Key: Any] = [.foregroundColor: NSColor.secondaryLabelColor, .font: NSFont.monospacedSystemFont(ofSize: 11, weight: .regular)]
        for t in stride(from: ceil(model.viewStart / tick) * tick, through: model.viewStart + model.viewLength, by: tick) {
            let text = NSAttributedString(string: timeLabel(t), attributes: attributes)
            text.draw(at: NSPoint(x: min(bounds.width - text.size().width - 4, max(4, x(t) - text.size().width / 2)), y: bounds.height - 21))
        }
    }
    override func mouseDown(with event: NSEvent) {
        guard model?.ready == true else { return }
        window?.makeFirstResponder(self)
        downX = convert(event.locationInWindow, from: nil).x
        dragging = (time(downX), time(downX)); needsDisplay = true
    }
    override func mouseDragged(with event: NSEvent) {
        guard let range = dragging else { return }
        dragging = (range.0, time(convert(event.locationInWindow, from: nil).x)); needsDisplay = true
    }
    override func mouseUp(with event: NSEvent) {
        guard let range = dragging, let model = model else { return }
        let x = convert(event.locationInWindow, from: nil).x
        if abs(x - downX) < 5 { model.click(time(x)) } else { model.select(range.0, time(x)) }
        dragging = nil; needsDisplay = true
    }
    override func magnify(with event: NSEvent) { model?.zoom(max(0.25, 1 - event.magnification)) }
    override func scrollWheel(with event: NSEvent) {
        guard let model = model, let audio = model.audio else { return }
        if abs(event.scrollingDeltaX) > abs(event.scrollingDeltaY) { model.viewStart = max(0, min(audio.duration - model.viewLength, model.viewStart + event.scrollingDeltaX * model.viewLength / max(1, bounds.width))) }
        else if !event.hasPreciseScrollingDeltas { model.zoom(event.scrollingDeltaY > 0 ? 0.8 : 1.25) }
        else { super.scrollWheel(with: event) }
    }
}
