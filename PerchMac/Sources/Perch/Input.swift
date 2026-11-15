import SwiftUI
import AppKit
import Carbon.HIToolbox
import ApplicationServices

// MARK: - Event tap: mouse and keyboard tweaks. Everything here is off until switched on, and the tap is only installed while a tweak needs it.
// Events Perch sends itself carry `Input.mark` in their user data, so the tap never treats them as yours.

enum Input {
    static let mark: Int64 = 0x50455243
    nonisolated(unsafe) static var tap: CFMachPort?
    nonisolated(unsafe) static var source: CFRunLoopSource?
    // snapshot of the settings, readable from the tap callback without hopping threads
    nonisolated(unsafe) static var cfg = Prefs()
    nonisolated(unsafe) static var lastDown: [Int64: Double] = [:], lastClick: Double = 0, swallowUp = false
    nonisolated(unsafe) static var typed = "", holdSince: Double = 0, holdFired = false, capsHeld = false, capsChord = false, quitPressedAt: Double = 0
    nonisolated(unsafe) static var cleaning = false, escSince: Double = 0
    nonisolated(unsafe) static var drag: (win: AXUIElement, start: CGPoint, origin: CGPoint, size: CGSize, resize: Bool)?
    nonisolated(unsafe) static var ignoredFront = false

    @MainActor static func refresh() {
        cfg = Settings.shared.p; let p = cfg
        let need = p.scrollInvert || p.scrollInvertH || p.scrollSideways || p.scrollSpeed != 100 || p.scrollLinear || p.clickFilter || p.keyDebounce || p.superKey || p.quitGuard || p.snippetsOn || p.gestureOn || cleaning
        if need && tap == nil { install() } else if !need && tap != nil { remove() }
        setSuperKeyMapping(p.superKey); setAcceleration(p.noMouseAccel)
        FocusFollows.set(p.focusFollowsMouse)
    }
    private static func install() {
        guard AXIsProcessTrusted() else { Task { @MainActor in Notices.shared.add("Allow Accessibility", "System Settings → Privacy & Security → Accessibility, then switch the tweak on again.", symbol: "hand.raised") }; let o = [kAXTrustedCheckOptionPrompt.takeUnretainedValue() as String: true] as CFDictionary; _ = AXIsProcessTrustedWithOptions(o); return }
        let types: [CGEventType] = [.scrollWheel, .keyDown, .keyUp, .leftMouseDown, .leftMouseUp, .leftMouseDragged, .rightMouseDown, .rightMouseUp, .rightMouseDragged, .flagsChanged, .mouseMoved, .otherMouseDown, .otherMouseUp]
        let mask = types.reduce(CGEventMask(0)) { $0 | (1 << $1.rawValue) }
        tap = CGEvent.tapCreate(tap: .cgSessionEventTap, place: .headInsertEventTap, options: .defaultTap, eventsOfInterest: mask, callback: { _, type, event, _ in Input.handle(type, event) }, userInfo: nil)
        guard let t = tap else { return }
        source = CFMachPortCreateRunLoopSource(kCFAllocatorDefault, t, 0); CFRunLoopAddSource(CFRunLoopGetMain(), source, .commonModes); CGEvent.tapEnable(tap: t, enable: true)
    }
    private static func remove() { if let t = tap { CGEvent.tapEnable(tap: t, enable: false) }; if let s = source { CFRunLoopRemoveSource(CFRunLoopGetMain(), s, .commonModes) }; tap = nil; source = nil }

    static func handle(_ type: CGEventType, _ e: CGEvent) -> Unmanaged<CGEvent>? {
        if type == .tapDisabledByTimeout || type == .tapDisabledByUserInput { if let t = tap { CGEvent.tapEnable(tap: t, enable: true) }; return Unmanaged.passUnretained(e) }
        if e.getIntegerValueField(.eventSourceUserData) == mark { return Unmanaged.passUnretained(e) }
        if cleaning {
            if type == .keyDown, e.getIntegerValueField(.keyboardEventKeycode) == 53 { if escSince == 0 { escSince = Date().timeIntervalSince1970 } } else if type == .keyUp, e.getIntegerValueField(.keyboardEventKeycode) == 53 { escSince = 0 }
            return nil
        }
        let now = Date().timeIntervalSince1970 * 1000; let c = cfg; let flags = e.flags
        switch type {
        case .scrollWheel:
            guard e.getIntegerValueField(.scrollWheelEventIsContinuous) == 0 else { break }      // trackpads keep their own behaviour
            var dy = e.getDoubleValueField(.scrollWheelEventPointDeltaAxis1), dx = e.getDoubleValueField(.scrollWheelEventPointDeltaAxis2)
            var fy = e.getIntegerValueField(.scrollWheelEventDeltaAxis1), fx = e.getIntegerValueField(.scrollWheelEventDeltaAxis2)
            if c.scrollSideways && flags.contains(.maskAlternate) { swap(&dy, &dx); swap(&fy, &fx) }
            if c.scrollInvert { dy = -dy; fy = -fy }; if c.scrollInvertH { dx = -dx; fx = -fx }
            if c.scrollLinear { fy = fy == 0 ? 0 : (fy > 0 ? 3 : -3); dy = Double(fy) * 10 }
            let k = c.scrollSpeed / 100; dy *= k; dx *= k; fy = Int64((Double(fy) * k).rounded(.toNearestOrAwayFromZero)) == 0 && fy != 0 ? (fy > 0 ? 1 : -1) : Int64((Double(fy) * k).rounded())
            e.setDoubleValueField(.scrollWheelEventPointDeltaAxis1, value: dy); e.setDoubleValueField(.scrollWheelEventPointDeltaAxis2, value: dx)
            e.setIntegerValueField(.scrollWheelEventDeltaAxis1, value: fy); e.setIntegerValueField(.scrollWheelEventDeltaAxis2, value: fx)
        case .leftMouseDown where c.clickFilter:
            if now - lastClick < Double(c.clickFilterMs) { swallowUp = true; return nil }; lastClick = now
        case .leftMouseUp where c.clickFilter && swallowUp: swallowUp = false; return nil
        case .keyDown:
            let code = e.getIntegerValueField(.keyboardEventKeycode); let repeatKey = e.getIntegerValueField(.keyboardEventAutorepeat) != 0
            // Caps Lock remapped to F18 (keycode 79) becomes a held modifier combination
            if c.superKey && code == 79 { capsHeld = true; return nil }
            if c.superKey && capsHeld { capsChord = true; e.flags = flags.union([.maskControl, .maskAlternate, .maskShift]) }
            if c.keyDebounce && !repeatKey { if let t = lastDown[code], now - t < Double(c.keyDebounceMs) { return nil }; lastDown[code] = now }
            if c.quitGuard, code == 12 || code == 13, flags.contains(.maskCommand), !flags.contains(.maskAlternate), !ignoredApp(c.quitGuardApps, invert: true) {
                if c.quitGuardMode == "double" { if now - quitPressedAt < 1500 { quitPressedAt = 0; return Unmanaged.passUnretained(e) }; quitPressedAt = now; hint("Press ⌘\(code == 12 ? "Q" : "W") again"); return nil }
                if !repeatKey { holdSince = now; holdFired = false; hint("Hold ⌘\(code == 12 ? "Q" : "W") to confirm") }
                if now - holdSince >= Double(c.quitGuardHoldMs) && !holdFired { holdFired = true; post(code: CGKeyCode(code), flags: .maskCommand) }
                return nil
            }
            if c.snippetsOn { snippetKey(e, flags) }
        case .keyUp:
            let code = e.getIntegerValueField(.keyboardEventKeycode)
            if c.superKey && code == 79 { let was = capsChord; capsHeld = false; capsChord = false; if !was { Task { @MainActor in Input.superTap() } }; return nil }
            if c.quitGuard, code == 12 || code == 13, flags.contains(.maskCommand), holdSince > 0 { holdSince = 0; return nil }
        case .leftMouseDown where c.gestureOn && flags.contains(.maskAlternate): return startDrag(e, resize: flags.contains(.maskShift))
        case .rightMouseDown where c.gestureOn && flags.contains(.maskAlternate): return startDrag(e, resize: true)
        case .leftMouseDragged, .rightMouseDragged: if drag != nil { moveDrag(e); return nil }
        case .leftMouseUp, .rightMouseUp: if drag != nil { endDrag(e); return nil }
        case .flagsChanged: break
        default: break
        }
        return Unmanaged.passUnretained(e)
    }
    static func ignoredApp(_ list: [String], invert: Bool) -> Bool {
        guard !list.isEmpty, let id = NSWorkspace.shared.frontmostApplication?.bundleIdentifier else { return false }
        return invert ? !list.contains(id) : list.contains(id)
    }
    static func hint(_ s: String) { Task { @MainActor in Island.shared.flash("keyboard", s) } }
    static func post(code: CGKeyCode, flags: CGEventFlags = []) {
        for down in [true, false] { if let e = CGEvent(keyboardEventSource: nil, virtualKey: code, keyDown: down) { e.flags = flags; e.setIntegerValueField(.eventSourceUserData, value: mark); e.post(tap: .cgSessionEventTap) } }
    }
    static func typeText(_ s: String) {
        for ch in s { for down in [true, false] { if let e = CGEvent(keyboardEventSource: nil, virtualKey: 0, keyDown: down) { var u = Array(String(ch).utf16); e.keyboardSetUnicodeString(stringLength: u.count, unicodeString: &u); e.setIntegerValueField(.eventSourceUserData, value: mark); e.post(tap: .cgSessionEventTap) } } }
    }
    @MainActor static func superTap() { switch cfg.superTap { case "escape": post(code: 53); case "commandbar": CommandBar.shared.toggle(); case "island": Island.shared.toggle(); default: break } }

    // typed triggers become their text: the trigger is deleted with backspaces, then the text is typed
    static func snippetKey(_ e: CGEvent, _ flags: CGEventFlags) {
        if flags.contains(.maskCommand) || flags.contains(.maskControl) { typed = ""; return }
        var len = 0; var u = [UniChar](repeating: 0, count: 4); e.keyboardGetUnicodeString(maxStringLength: 4, actualStringLength: &len, unicodeString: &u)
        let code = e.getIntegerValueField(.keyboardEventKeycode)
        if code == 51 { if !typed.isEmpty { typed.removeLast() }; return }
        guard len > 0, let ch = String(utf16CodeUnits: u, count: len).first, !ch.isNewline else { typed = ""; return }
        typed.append(ch); if typed.count > 40 { typed.removeFirst() }
        for s in cfg.snippets where !s.trigger.isEmpty && typed.hasSuffix(s.trigger) {
            let n = s.trigger.count - 1; typed = ""
            DispatchQueue.main.async { for _ in 0..<n { post(code: 51) }; post(code: 51); typeText(Input.fill(s.text)) }   // the last trigger key is swallowed below
            return
        }
    }
    static func fill(_ body: String) -> String {
        var s = body; let f = DateFormatter()
        for (name, style) in [("date", "yyyy-MM-dd"), ("time", "HH:mm"), ("datetime", "yyyy-MM-dd HH:mm")] {
            let rx = try! NSRegularExpression(pattern: "\\{\\{\(name)(?::([^}]+))?\\}\\}"); let ns = s as NSString
            for m in rx.matches(in: s, range: NSRange(location: 0, length: ns.length)).reversed() { f.dateFormat = m.range(at: 1).location != NSNotFound ? ns.substring(with: m.range(at: 1)) : style; s = (s as NSString).replacingCharacters(in: m.range, with: f.string(from: Date())) }
        }
        return s.replacingOccurrences(of: "{{clipboard}}", with: NSPasteboard.general.string(forType: .string) ?? "")
    }

    // MARK: ⌥-drag to move, ⌥⇧-drag (or right button) to resize any window
    static func startDrag(_ e: CGEvent, resize: Bool) -> Unmanaged<CGEvent>? {
        let pt = e.location; var el: AXUIElement?; let sys = AXUIElementCreateSystemWide(); var hit: AXUIElement?
        guard AXUIElementCopyElementAtPosition(sys, Float(pt.x), Float(pt.y), &hit) == .success, let h = hit else { return Unmanaged.passUnretained(e) }; el = h
        var win: AnyObject?; if AXUIElementCopyAttributeValue(h, kAXWindowAttribute as CFString, &win) == .success { el = (win as! AXUIElement) }
        guard let w = el else { return Unmanaged.passUnretained(e) }
        var pid: pid_t = 0; AXUIElementGetPid(w, &pid); if pid == getpid() { return Unmanaged.passUnretained(e) }
        if let id = NSRunningApplication(processIdentifier: pid)?.bundleIdentifier, cfg.layoutIgnoredApps.contains(id) { return Unmanaged.passUnretained(e) }
        guard let o = WindowLayout.position(w), let s = WindowLayout.size(w) else { return Unmanaged.passUnretained(e) }
        drag = (w, pt, o, s, resize); NSRunningApplication(processIdentifier: pid)?.activate(); return nil
    }
    static func moveDrag(_ e: CGEvent) {
        guard let d = drag else { return }; let dx = e.location.x - d.start.x, dy = e.location.y - d.start.y
        if d.resize { WindowLayout.set(d.win, size: CGSize(width: max(160, d.size.width + dx), height: max(100, d.size.height + dy))) } else { WindowLayout.set(d.win, position: CGPoint(x: d.origin.x + dx, y: d.origin.y + dy)) }
    }
    static func endDrag(_ e: CGEvent) {
        guard let d = drag else { return }; drag = nil
        guard !d.resize else { return }
        let p = e.location; let scr = NSScreen.screens.first { $0.frame.contains(CGPoint(x: p.x, y: NSScreen.screens[0].frame.height - p.y)) } ?? NSScreen.main!; let top = NSScreen.screens[0].frame.height - p.y; let fr = scr.frame
        let l = p.x <= fr.minX + 6, r = p.x >= fr.maxX - 6, t = top >= fr.maxY - 6
        let mode = (t && l) ? "tl" : (t && r) ? "tr" : l ? "left" : r ? "right" : t ? "max" : ""
        if !mode.isEmpty { DispatchQueue.main.async { WindowLayout.apply(mode, to: d.win) } }
    }

    // MARK: Caps Lock as a super key: the system maps it to F18; the tap above turns F18 into the held combination
    static func setSuperKeyMapping(_ on: Bool) {
        let json = on ? "{\"UserKeyMapping\":[{\"HIDKeyboardModifierMappingSrc\":0x700000039,\"HIDKeyboardModifierMappingDst\":0x70000006D}]}" : "{\"UserKeyMapping\":[]}"
        let p = Process(); p.executableURL = URL(fileURLWithPath: "/usr/bin/hidutil"); p.arguments = ["property", "--set", json]; try? p.run()
    }
    // MARK: pointer acceleration
    static func setAcceleration(_ off: Bool) {
        Tools.run("/usr/bin/defaults", off ? ["write", "-g", "com.apple.mouse.scaling", "-1"] : ["delete", "-g", "com.apple.mouse.scaling"])
    }
    // MARK: quit programs whose last window closed
    @MainActor static func autoQuit(bundleID: String) {
        guard let app = NSRunningApplication.runningApplications(withBundleIdentifier: bundleID).first else { seen.remove(bundleID); return }
        let windows = (CGWindowListCopyWindowInfo([.optionOnScreenOnly], kCGNullWindowID) as? [[String: Any]] ?? []).filter { ($0[kCGWindowOwnerPID as String] as? pid_t) == app.processIdentifier && ($0[kCGWindowLayer as String] as? Int) == 0 }
        if !windows.isEmpty { seen.insert(bundleID); bare[bundleID] = nil; return }
        guard seen.contains(bundleID) else { return }
        if let t = bare[bundleID] { if Date().timeIntervalSince(t) > 4 { app.terminate(); bare[bundleID] = nil; seen.remove(bundleID); Notices.shared.add("Asked \(app.localizedName ?? "the app") to quit", "Its last window was closed.", symbol: "xmark.app") } } else { bare[bundleID] = Date() }
    }
    @MainActor static var seen = Set<String>(); @MainActor static var bare: [String: Date] = [:]
}

// MARK: - Cleaning mode

@MainActor final class CleaningMode {
    static let shared = CleaningMode(); private var wins: [NSWindow] = [], timer: Timer?, started = Date()
    func start() {
        guard wins.isEmpty else { return }; Island.shared.pinned = false; Island.shared.close()
        Input.cleaning = true; Input.escSince = 0; Input.refresh(); started = Date()
        for s in NSScreen.screens { let w = NSWindow(contentRect: s.frame, styleMask: .borderless, backing: .buffered, defer: false); w.backgroundColor = Settings.shared.p.cleaningStyle == "black" ? .black : .clear; w.isOpaque = Settings.shared.p.cleaningStyle == "black"; w.level = .screenSaver; w.ignoresMouseEvents = false
            w.contentView = NSHostingView(rootView: Text(L("Keyboard and mouse are off. Hold Esc for 3 seconds to finish.")).font(.system(size: 18, weight: .medium)).foregroundStyle(.white.opacity(0.85)).frame(maxWidth: .infinity, maxHeight: .infinity)); w.orderFrontRegardless(); wins.append(w) }
        timer = Timer.scheduledTimer(withTimeInterval: 0.1, repeats: true) { _ in Task { @MainActor in
            let es = Input.escSince; if es > 0 && Date().timeIntervalSince1970 - es >= 3 { self.stop() }
            if Date().timeIntervalSince(self.started) > Double(Settings.shared.p.cleaningMaxSeconds) { self.stop() } } }
    }
    func stop() { timer?.invalidate(); wins.forEach { $0.close() }; wins = []; Input.cleaning = false; Input.escSince = 0; Input.refresh() }
}

// MARK: - Focus follows the mouse

@MainActor enum FocusFollows {
    private static var monitor: Any?, timer: Timer?, last = CGPoint.zero
    static func set(_ on: Bool) {
        timer?.invalidate(); timer = nil; guard on else { return }
        timer = Timer.scheduledTimer(withTimeInterval: 0.4, repeats: true) { _ in
            let p = NSEvent.mouseLocation; guard abs(p.x - last.x) < 2, abs(p.y - last.y) < 2 else { last = p; return }
            let y = (NSScreen.screens.first?.frame.height ?? 0) - p.y
            let info = CGWindowListCopyWindowInfo([.optionOnScreenOnly, .excludeDesktopElements], kCGNullWindowID) as? [[String: Any]] ?? []
            guard let w = info.first(where: { ($0[kCGWindowLayer as String] as? Int) == 0 && CGRect(dictionaryRepresentation: $0[kCGWindowBounds as String] as! CFDictionary)?.contains(CGPoint(x: p.x, y: y)) == true }), let pid = w[kCGWindowOwnerPID as String] as? pid_t, pid != getpid(), pid != NSWorkspace.shared.frontmostApplication?.processIdentifier else { return }
            if let id = NSRunningApplication(processIdentifier: pid)?.bundleIdentifier, Settings.shared.p.inputIgnoredApps.contains(id) { return }
            NSRunningApplication(processIdentifier: pid)?.activate()
        }
    }
}

// MARK: - Window layout through the Accessibility API

@MainActor enum WindowLayout {
    static func position(_ w: AXUIElement) -> CGPoint? { var v: AnyObject?; guard AXUIElementCopyAttributeValue(w, kAXPositionAttribute as CFString, &v) == .success else { return nil }; var p = CGPoint.zero; AXValueGetValue(v as! AXValue, .cgPoint, &p); return p }
    static func size(_ w: AXUIElement) -> CGSize? { var v: AnyObject?; guard AXUIElementCopyAttributeValue(w, kAXSizeAttribute as CFString, &v) == .success else { return nil }; var s = CGSize.zero; AXValueGetValue(v as! AXValue, .cgSize, &s); return s }
    nonisolated static func set(_ w: AXUIElement, position: CGPoint) { var p = position; if let v = AXValueCreate(.cgPoint, &p) { AXUIElementSetAttributeValue(w, kAXPositionAttribute as CFString, v) } }
    nonisolated static func set(_ w: AXUIElement, size: CGSize) { var s = size; if let v = AXValueCreate(.cgSize, &s) { AXUIElementSetAttributeValue(w, kAXSizeAttribute as CFString, v) } }
    static var saved: [pid_t: (CGPoint, CGSize)] = [:]
    static func frontWindow() -> (AXUIElement, pid_t)? {
        guard let app = NSWorkspace.shared.frontmostApplication, app.processIdentifier != getpid() else { return nil }
        if let id = app.bundleIdentifier, Settings.shared.p.layoutIgnoredApps.contains(id) { return nil }
        let a = AXUIElementCreateApplication(app.processIdentifier); var w: AnyObject?
        guard AXUIElementCopyAttributeValue(a, kAXFocusedWindowAttribute as CFString, &w) == .success else { return nil }; return (w as! AXUIElement, app.processIdentifier)
    }
    // Where each mode lands, as a share of the display: x, y, width, height
    static let fractions: [String: (Double, Double, Double, Double)] = [
        "left": (0, 0, 0.5, 1), "right": (0.5, 0, 0.5, 1), "top": (0, 0, 1, 0.5), "bottom": (0, 0.5, 1, 0.5), "centerhalf": (0.25, 0, 0.5, 1),
        "thirdl": (0, 0, 1.0 / 3, 1), "thirdc": (1.0 / 3, 0, 1.0 / 3, 1), "thirdr": (2.0 / 3, 0, 1.0 / 3, 1), "twothirdsl": (0, 0, 2.0 / 3, 1), "twothirdsr": (1.0 / 3, 0, 2.0 / 3, 1),
        "tl": (0, 0, 0.5, 0.5), "tr": (0.5, 0, 0.5, 0.5), "bl": (0, 0.5, 0.5, 0.5), "br": (0.5, 0.5, 0.5, 0.5),
        "tl6": (0, 0, 1.0 / 3, 0.5), "tc6": (1.0 / 3, 0, 1.0 / 3, 0.5), "tr6": (2.0 / 3, 0, 1.0 / 3, 0.5), "bl6": (0, 0.5, 1.0 / 3, 0.5), "bc6": (1.0 / 3, 0.5, 1.0 / 3, 0.5), "br6": (2.0 / 3, 0.5, 1.0 / 3, 0.5),
        "rowq1": (0, 0, 1, 0.25), "rowq2": (0, 0.25, 1, 0.25), "rowq3": (0, 0.5, 1, 0.25), "rowq4": (0, 0.75, 1, 0.25), "colq1": (0, 0, 0.25, 1), "colq2": (0.25, 0, 0.25, 1), "colq3": (0.5, 0, 0.25, 1), "colq4": (0.75, 0, 0.25, 1),
        "center": (1.0 / 6, 1.0 / 6, 2.0 / 3, 2.0 / 3), "max": (0, 0, 1, 1),
    ]
    static func apply(_ mode: String, to window: AXUIElement? = nil) {
        guard let (w, pid) = window.map({ ($0, NSWorkspace.shared.frontmostApplication?.processIdentifier ?? 0) }) ?? frontWindow() else { return }
        if mode == "restore" { if let s = saved[pid] { set(w, size: s.1); set(w, position: s.0) }; return }
        guard let pos = position(w), let size = size(w) else { return }; saved[pid] = (pos, size)
        let screens = NSScreen.screens; let primaryH = screens[0].frame.height
        // the display the window is mostly on, in AX coordinates (origin top left)
        func axRect(_ s: NSScreen) -> CGRect { CGRect(x: s.visibleFrame.minX, y: primaryH - s.visibleFrame.maxY, width: s.visibleFrame.width, height: s.visibleFrame.height) }
        let centre = CGPoint(x: pos.x + size.width / 2, y: pos.y + size.height / 2)
        let idx = screens.firstIndex { axRect($0).contains(centre) } ?? 0
        var target = idx
        if mode == "nextdisplay" { target = (idx + 1) % screens.count } else if mode == "prevdisplay" { target = (idx + screens.count - 1) % screens.count }
        let area = axRect(screens[target]); let p = Settings.shared.p
        if mode == "nextdisplay" || mode == "prevdisplay" { let from = axRect(screens[idx]); set(w, position: CGPoint(x: area.minX + (pos.x - from.minX) / from.width * area.width, y: area.minY + (pos.y - from.minY) / from.height * area.height)); return }
        if mode == "marginmax" { let m = min(area.width, area.height) * p.layoutMargin / 100; set(w, position: CGPoint(x: area.minX + m, y: area.minY + m)); set(w, size: CGSize(width: area.width - 2 * m, height: area.height - 2 * m)); return }
        guard let f = fractions[mode] else { return }
        let sg = p.layoutScreenGap, g = p.layoutGap; let ax = area.minX + sg, ay = area.minY + sg, aw = area.width - 2 * sg, ah = area.height - 2 * sg
        var x0 = ax + aw * f.0, y0 = ay + ah * f.1, x1 = ax + aw * (f.0 + f.2), y1 = ay + ah * (f.1 + f.3)
        if f.0 > 0.001 { x0 += g / 2 }; if f.0 + f.2 < 0.999 { x1 -= g / 2 }; if f.1 > 0.001 { y0 += g / 2 }; if f.1 + f.3 < 0.999 { y1 -= g / 2 }
        set(w, size: CGSize(width: x1 - x0, height: y1 - y0)); set(w, position: CGPoint(x: x0, y: y0)); set(w, size: CGSize(width: x1 - x0, height: y1 - y0))
    }
    static func pointerToNextDisplay() { let s = NSScreen.screens; guard s.count > 1 else { return }; let cur = s.firstIndex { $0.frame.contains(NSEvent.mouseLocation) } ?? 0; let n = s[(cur + 1) % s.count].frame; CGWarpMouseCursorPosition(CGPoint(x: n.midX, y: s[0].frame.height - n.midY)) }
}

// MARK: - Window switcher (⌃⌥Tab): live thumbnails, search, arrows, Return

struct WinItem: Identifiable { let id: CGWindowID; let pid: pid_t; let app: String; let title: String; let icon: NSImage? }

@MainActor final class Switcher: ObservableObject {
    static let shared = Switcher()
    @Published var items: [WinItem] = []; @Published var sel = 0; @Published var query = ""
    var panel: IslandPanel?
    var filtered: [WinItem] { query.isEmpty ? items : items.filter { ($0.app + " " + $0.title).localizedCaseInsensitiveContains(query) } }
    func toggle() { if panel != nil { close() } else { open() } }
    func open() {
        let info = CGWindowListCopyWindowInfo([.optionOnScreenOnly, .excludeDesktopElements], kCGNullWindowID) as? [[String: Any]] ?? []
        items = info.compactMap { d in
            guard (d[kCGWindowLayer as String] as? Int) == 0, let pid = d[kCGWindowOwnerPID as String] as? pid_t, pid != getpid(), let id = d[kCGWindowNumber as String] as? CGWindowID, let b = d[kCGWindowBounds as String] as? [String: CGFloat], (b["Width"] ?? 0) > 80 else { return nil }
            let app = NSRunningApplication(processIdentifier: pid); return WinItem(id: id, pid: pid, app: app?.localizedName ?? "", title: d[kCGWindowName as String] as? String ?? "", icon: app?.icon)
        }
        guard !items.isEmpty else { return }; sel = items.count > 1 ? 1 : 0; query = ""
        let p = IslandPanel(contentRect: NSRect(x: 0, y: 0, width: 880, height: 420), styleMask: [.borderless, .nonactivatingPanel], backing: .buffered, defer: false)
        p.contentView = NSHostingView(rootView: SwitcherView()); p.isOpaque = false; p.backgroundColor = .clear; p.level = .popUpMenu; p.hasShadow = true; p.center(); NSApp.activate(ignoringOtherApps: true); p.makeKeyAndOrderFront(nil); panel = p
    }
    func close() { panel?.close(); panel = nil }
    func commit() { let l = filtered; guard sel >= 0, sel < l.count else { close(); return }; let w = l[sel]; close(); NSRunningApplication(processIdentifier: w.pid)?.activate(); raise(w) }
    private func raise(_ w: WinItem) {
        let a = AXUIElementCreateApplication(w.pid); var arr: AnyObject?; guard AXUIElementCopyAttributeValue(a, kAXWindowsAttribute as CFString, &arr) == .success, let list = arr as? [AXUIElement] else { return }
        if let m = list.first(where: { var t: AnyObject?; AXUIElementCopyAttributeValue($0, kAXTitleAttribute as CFString, &t); return (t as? String) == w.title }) ?? list.first { AXUIElementPerformAction(m, kAXRaiseAction as CFString) }
    }
}
struct SwitcherView: View {
    @ObservedObject var sw = Switcher.shared
    var body: some View {
        VStack(spacing: 10) {
            TextField(L("Type to search windows"), text: $sw.query).textFieldStyle(.roundedBorder).frame(width: 300)
            ScrollView { LazyVGrid(columns: Array(repeating: GridItem(.flexible(), spacing: 8), count: 4), spacing: 8) {
                ForEach(Array(sw.filtered.enumerated()), id: \.element.id) { i, w in
                    VStack(spacing: 4) {
                        HStack { if let ic = w.icon { Image(nsImage: ic).resizable().frame(width: 16, height: 16) }; Text(w.title.isEmpty ? w.app : w.title).font(.system(size: 11, weight: .semibold)).lineLimit(1); Spacer() }
                        Thumb(id: w.id).frame(height: 100).clipShape(RoundedRectangle(cornerRadius: 8))
                    }.padding(8).background(RoundedRectangle(cornerRadius: 12).fill(i == sw.sel ? Color.white.opacity(0.3) : Look.card)).onTapGesture { sw.sel = i; sw.commit() }
                }
            }.padding(.horizontal, 12) }
        }.padding(.vertical, 14).background(Blur(material: .hudWindow)).clipShape(RoundedRectangle(cornerRadius: 22, style: .continuous))
            .onKeyPress(.escape) { sw.close(); return .handled }.onKeyPress(.return) { sw.commit(); return .handled }
            .onKeyPress(.rightArrow) { sw.sel = min(sw.sel + 1, sw.filtered.count - 1); return .handled }.onKeyPress(.leftArrow) { sw.sel = max(sw.sel - 1, 0); return .handled }
            .onKeyPress(.downArrow) { sw.sel = min(sw.sel + 4, sw.filtered.count - 1); return .handled }.onKeyPress(.upArrow) { sw.sel = max(sw.sel - 4, 0); return .handled }
            .onKeyPress(.tab) { sw.sel = (sw.sel + 1) % max(1, sw.filtered.count); return .handled }
    }
}
struct Thumb: View {
    let id: CGWindowID; @State private var img: NSImage?
    var body: some View { Group { if let i = img { Image(nsImage: i).resizable().scaledToFill() } else { Rectangle().fill(Look.card) } }.task { if let cg = CGWindowListCreateImage(.null, .optionIncludingWindow, id, [.boundsIgnoreFraming, .nominalResolution]) { img = NSImage(cgImage: cg, size: .zero) } } }
}

// MARK: - Search-and-pick palette: snippets menu, clipboard window, command bar

struct PickItem: Identifiable { let id = UUID(); let title: String; let sub: String; let symbol: String; let run: () -> Void }

@MainActor final class Palette: ObservableObject {
    @Published var q = "" { didSet { sel = 0 } }; @Published var sel = 0; @Published var digits = false; var source: (String) -> [PickItem] = { _ in [] }; var placeholder = ""; var footer = ""
    var items: [PickItem] { source(q) }
    var panel: IslandPanel?
    func show(placeholder: String, footer: String, digits: Bool = false, source: @escaping (String) -> [PickItem]) {
        close(); self.placeholder = placeholder; self.footer = footer; self.digits = digits; self.source = source; q = ""; sel = 0
        let p = IslandPanel(contentRect: NSRect(x: 0, y: 0, width: 540, height: 420), styleMask: [.borderless, .nonactivatingPanel], backing: .buffered, defer: false)
        p.contentView = NSHostingView(rootView: PaletteView(pal: self)); p.isOpaque = false; p.backgroundColor = .clear; p.level = .popUpMenu; p.hasShadow = true
        if let s = NSScreen.main { p.setFrameOrigin(NSPoint(x: s.frame.midX - 270, y: s.frame.maxY - 140 - 420)) }
        NSApp.activate(ignoringOtherApps: true); p.makeKeyAndOrderFront(nil); panel = p
    }
    func close() { panel?.close(); panel = nil }
    func run(_ i: Int) { let l = items; guard i >= 0, i < l.count else { return }; let r = l[i].run; close(); r() }
}
struct PaletteView: View {
    @ObservedObject var pal: Palette
    var body: some View {
        let list = pal.items
        VStack(spacing: 0) {
            TextField(L(pal.placeholder), text: $pal.q).textFieldStyle(.plain).font(.system(size: 17)).padding(16)
            Divider().overlay(.white.opacity(0.15))
            ScrollView { VStack(spacing: 2) { ForEach(Array(list.prefix(40).enumerated()), id: \.element.id) { i, it in
                HStack(spacing: 10) { if pal.digits { Text(i < 9 ? "\(i + 1)" : "").font(.system(size: 11, design: .monospaced)).foregroundStyle(Look.dim).frame(width: 16) } else { Image(systemName: it.symbol).frame(width: 24) }
                    VStack(alignment: .leading, spacing: 1) { Text(it.title).font(.system(size: 13, weight: .semibold)).lineLimit(1); if !it.sub.isEmpty { Text(it.sub).font(.system(size: 10.5)).foregroundStyle(Look.dim).lineLimit(1) } }; Spacer() }
                    .padding(.horizontal, 10).padding(.vertical, 6).background(RoundedRectangle(cornerRadius: 10).fill(i == pal.sel ? Color.white.opacity(0.25) : .clear)).contentShape(Rectangle()).onTapGesture { pal.run(i) }
            } }.padding(8) }
            Text(L(pal.footer)).font(.system(size: 10.5)).foregroundStyle(Look.dim).padding(10)
        }.background(Blur(material: .hudWindow)).clipShape(RoundedRectangle(cornerRadius: 18, style: .continuous))
            .onKeyPress(.escape) { pal.close(); return .handled }.onKeyPress(.return) { pal.run(pal.sel); return .handled }
            .onKeyPress(.downArrow) { pal.sel = min(pal.sel + 1, max(0, list.count - 1)); return .handled }.onKeyPress(.upArrow) { pal.sel = max(pal.sel - 1, 0); return .handled }
            .onKeyPress(characters: .decimalDigits) { k in if pal.digits, NSEvent.modifierFlags.contains(.control), let n = Int(k.characters), n >= 1 { pal.run(n - 1); return .handled }; return .ignored }
    }
}

@MainActor enum Pickers {
    static let pal = Palette()
    static func snippets() {
        let prev = NSWorkspace.shared.frontmostApplication
        pal.show(placeholder: "Search snippets", footer: "Return types it where your cursor is · Esc closes") { q in
            Settings.shared.p.snippets.filter { $0.inMenu && (q.isEmpty || ($0.name + $0.trigger + $0.folder + $0.text).localizedCaseInsensitiveContains(q)) }.map { s in
                PickItem(title: (s.name.isEmpty ? s.trigger : s.name) + (s.folder.isEmpty ? "" : "   ·   " + s.folder), sub: s.text.replacingOccurrences(of: "\n", with: " "), symbol: "text.append") { prev?.activate(); DispatchQueue.main.asyncAfter(deadline: .now() + 0.15) { Input.typeText(Input.fill(s.text)) } } }
        }
    }
    static func clipboard() {
        let prev = NSWorkspace.shared.frontmostApplication
        pal.show(placeholder: "Search what you copied", footer: "Return pastes · ⌃1–⌃9 paste that row · Esc closes", digits: true) { q in
            Clip.shared.items.filter { q.isEmpty || ($0.text ?? "").localizedCaseInsensitiveContains(q) }.prefix(40).map { c in
                PickItem(title: String((c.text ?? "Image").replacingOccurrences(of: "\n", with: " ").prefix(90)), sub: (c.pinned ? "Pinned · " : "") + c.at.formatted(date: .omitted, time: .shortened), symbol: "doc") { Clip.shared.copyBack(c); prev?.activate(); DispatchQueue.main.asyncAfter(deadline: .now() + 0.15) { Input.post(code: 9, flags: .maskCommand) } } }
        }
    }
}
