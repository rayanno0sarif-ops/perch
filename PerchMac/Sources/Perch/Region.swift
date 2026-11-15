import AppKit
import SwiftUI

// A dim full-screen layer where you drag a rectangle. Returns it in global screen coordinates with the origin at the top left, which is what CGWindowListCreateImage expects.
@MainActor final class RegionPicker: NSObject {
    private var window: NSWindow?
    private var done: ((CGRect?) -> Void)?
    static var current: RegionPicker?

    static func pick(_ done: @escaping (CGRect?) -> Void) {
        let p = RegionPicker(); current = p; p.done = done
        guard let s = NSScreen.main else { done(nil); return }
        let w = NSWindow(contentRect: s.frame, styleMask: .borderless, backing: .buffered, defer: false)
        w.backgroundColor = NSColor.black.withAlphaComponent(0.25); w.isOpaque = false; w.level = .screenSaver; w.ignoresMouseEvents = false
        let v = RegionView(frame: NSRect(origin: .zero, size: s.frame.size)); v.onDone = { r in
            p.window?.close(); p.window = nil; current = nil
            guard let r, r.width > 8, r.height > 8 else { done(nil); return }
            done(CGRect(x: s.frame.minX + r.minX, y: s.frame.height - r.maxY, width: r.width, height: r.height))
        }
        w.contentView = v; w.makeKeyAndOrderFront(nil); w.makeFirstResponder(v); NSApp.activate(ignoringOtherApps: true); p.window = w; NSCursor.crosshair.push()
    }
}
final class RegionView: NSView {
    var onDone: ((CGRect?) -> Void)?; private var from: NSPoint?; private var rect: NSRect?
    override var acceptsFirstResponder: Bool { true }
    override func mouseDown(with e: NSEvent) { from = convert(e.locationInWindow, from: nil); rect = nil; needsDisplay = true }
    override func mouseDragged(with e: NSEvent) { guard let f = from else { return }; let p = convert(e.locationInWindow, from: nil); rect = NSRect(x: min(f.x, p.x), y: min(f.y, p.y), width: abs(f.x - p.x), height: abs(f.y - p.y)); needsDisplay = true }
    override func mouseUp(with e: NSEvent) { NSCursor.pop(); onDone?(rect) }
    override func keyDown(with e: NSEvent) { if e.keyCode == 53 { NSCursor.pop(); onDone?(nil) } }
    override func draw(_ dirty: NSRect) { guard let r = rect else { return }; NSColor.clear.setFill(); r.fill(using: .copy); NSColor.white.setStroke(); let p = NSBezierPath(rect: r); p.lineWidth = 1.5; p.stroke() }
}
