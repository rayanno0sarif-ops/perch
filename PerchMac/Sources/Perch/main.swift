import AppKit
import SwiftUI
import Carbon.HIToolbox

// Perch for Mac. A menu-bar-less accessory app: the island is the interface, with a status item as a way back to settings and Quit.

@MainActor func registerHotKeys() {
    let st = Island.shared
    HotKeys.shared.register([
        "toggle": { st.toggle() }, "commandbar": { CommandBar.shared.toggle() }, "radial": { Radial.toggle() }, "screenshot": { Tools.screenshot() }, "record": { Tools.toggleRecording() },
        "switcher": { if Settings.shared.p.switcherOn { Switcher.shared.toggle() } }, "snippets": { Pickers.snippets() }, "clipboard": { Pickers.clipboard() }, "colour": { Tools.pickColour() }, "ocr": { Tools.copyTextFromScreen() },
        "left": { WindowLayout.apply("left") }, "right": { WindowLayout.apply("right") }, "top": { WindowLayout.apply("top") }, "bottom": { WindowLayout.apply("bottom") }, "max": { WindowLayout.apply("max") }, "center": { WindowLayout.apply("center") },
        "cleaning": { CleaningMode.shared.start() }, "camera": { CameraWindow.toggle() }, "mic": { Audio.toggleMic() }, "playpause": { Media.shared.toggle() },
    ])
}

final class AppDelegate: NSObject, NSApplicationDelegate {
    var item: NSStatusItem?
    private var btWasOn = false
    @MainActor func applicationDidFinishLaunching(_ n: Notification) {
        NSApp.setActivationPolicy(.accessory)
        Loc.load(); Clip.shared.start(); Media.shared.start(); Stats.shared.start(); KeepAwake.shared.start(); AgentModel.shared.start()
        Island.shared.setup(); Mascot.shared.sync(); Reminders.ask(); registerHotKeys(); Input.refresh()
        for r in Settings.shared.p.reminders where r.due > Date() { Reminders.schedule(r) }
        item = NSStatusBar.system.statusItem(withLength: NSStatusItem.squareLength)
        item?.button?.image = NSImage(systemSymbolName: "capsule.fill", accessibilityDescription: "Perch")
        let m = NSMenu(); m.addItem(withTitle: "Open or close Perch", action: #selector(toggle), keyEquivalent: "").target = self
        m.addItem(withTitle: "Settings…", action: #selector(settings), keyEquivalent: ",").target = self; m.addItem(.separator())
        m.addItem(withTitle: "Quit Perch", action: #selector(quit), keyEquivalent: "q").target = self; item?.menu = m
        // Bluetooth off during sleep, back on at wake only if Perch switched it off (needs `brew install blueutil`)
        NSWorkspace.shared.notificationCenter.addObserver(forName: NSWorkspace.willSleepNotification, object: nil, queue: .main) { [weak self] _ in
            guard Settings.shared.p.hotkeys["btsleep"] == "on" else { return }
            let on = Tools.runOut("/usr/bin/env", ["blueutil", "--power"]).trimmingCharacters(in: .whitespacesAndNewlines) == "1"; self?.btWasOn = on; if on { Tools.run("/usr/bin/env", ["blueutil", "--power", "0"]) } }
        NSWorkspace.shared.notificationCenter.addObserver(forName: NSWorkspace.didWakeNotification, object: nil, queue: .main) { [weak self] _ in if self?.btWasOn == true { Tools.run("/usr/bin/env", ["blueutil", "--power", "1"]); self?.btWasOn = false } }
    }
    func applicationWillTerminate(_ n: Notification) { Input.setSuperKeyMapping(false); Input.setAcceleration(false) }
    @MainActor @objc func toggle() { Island.shared.toggle() }
    @MainActor @objc func settings() { SettingsWindow.show() }
    @objc func quit() { NSApp.terminate(nil) }
}

let app = NSApplication.shared
let delegate = AppDelegate()
app.delegate = delegate
app.run()
