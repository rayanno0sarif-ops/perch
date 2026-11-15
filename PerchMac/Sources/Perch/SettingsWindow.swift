import SwiftUI
import AppKit
import ServiceManagement

@MainActor enum SettingsWindow {
    static var window: NSWindow?
    static func show(_ page: String = "General") {
        if window == nil {
            let w = NSWindow(contentRect: NSRect(x: 0, y: 0, width: 820, height: 680), styleMask: [.titled, .closable, .miniaturizable, .resizable, .fullSizeContentView], backing: .buffered, defer: false)
            w.title = "Perch settings"; w.titlebarAppearsTransparent = true; w.contentView = NSHostingView(rootView: SettingsRoot()); w.center(); w.isReleasedWhenClosed = false; window = w
        }
        NSApp.activate(ignoringOtherApps: true); window?.makeKeyAndOrderFront(nil)
    }
}

struct SettingsRoot: View {
    @ObservedObject var s = Settings.shared
    @State private var page = "General"
    static let pages: [(String, String)] = [("General", "gearshape"), ("Appearance", "paintbrush"), ("Panels", "square.grid.2x2"), ("Input", "computermouse"), ("Windows", "macwindow"), ("Keys", "keyboard"), ("Clipboard", "doc.on.clipboard"), ("Capture", "camera"), ("Energy", "bolt"), ("Alerts", "bell"), ("More tabs", "square.stack"), ("Assistant", "sparkles"), ("Language", "globe"), ("About", "info.circle")]
    var body: some View {
        NavigationSplitView {
            List(Self.pages, id: \.0, selection: Binding(get: { page }, set: { page = $0 ?? "General" })) { p in Label(L(p.0), systemImage: p.1).tag(p.0) }.navigationSplitViewColumnWidth(min: 170, ideal: 190)
        } detail: {
            ScrollView { VStack(alignment: .leading, spacing: 4) { Text(L(page)).font(.system(size: 26, weight: .bold)).padding(.bottom, 6); PageBody(page: page) }.padding(24).frame(maxWidth: .infinity, alignment: .leading) }
        }.preferredColorScheme(.dark).frame(minWidth: 760, minHeight: 560)
    }
}

struct Row<C: View>: View {
    let title: String, desc: String; @ViewBuilder var control: C
    init(_ t: String, _ d: String = "", @ViewBuilder control: () -> C) { title = t; desc = d; self.control = control() }
    var body: some View { VStack(spacing: 0) { Divider().padding(.vertical, 8); HStack(alignment: .top, spacing: 18) { VStack(alignment: .leading, spacing: 3) { Text(L(title)).font(.system(size: 13, weight: .semibold)); if !desc.isEmpty { Text(L(desc)).font(.system(size: 11.5)).foregroundStyle(.secondary).fixedSize(horizontal: false, vertical: true) } }; Spacer(minLength: 12); control.fixedSize() }.padding(.bottom, 6) } }
}
struct Section_: View { let t: String; init(_ t: String) { self.t = t }; var body: some View { Text(L(t).uppercased()).font(.system(size: 10, design: .monospaced)).foregroundStyle(.secondary).padding(.top, 20) } }

struct PageBody: View {
    let page: String; @ObservedObject var s = Settings.shared
    @State private var status = ""
    func tog(_ b: Binding<Bool>, after: (() -> Void)? = nil) -> some View { Toggle("", isOn: b).labelsHidden().toggleStyle(.switch).onChange(of: b.wrappedValue) { _, _ in after?() } }
    func num(_ b: Binding<Double>, _ r: ClosedRange<Double>, _ unit: String) -> some View { HStack { Slider(value: b, in: r).frame(width: 150); Text("\(Int(b.wrappedValue)) \(unit)").font(.system(size: 11, design: .monospaced)).frame(width: 60, alignment: .trailing) } }
    func int(_ b: Binding<Int>, _ r: ClosedRange<Int>, _ unit: String) -> some View { num(Binding(get: { Double(b.wrappedValue) }, set: { b.wrappedValue = Int($0) }), Double(r.lowerBound)...Double(r.upperBound), unit) }
    func apps(_ title: String, _ desc: String, _ list: Binding<[String]>) -> some View {
        VStack(alignment: .leading, spacing: 0) {
            Row(title, desc) { Pill("Add app…") { Tools.pickApp { p in if let id = Bundle(path: p)?.bundleIdentifier, !list.wrappedValue.contains(id) { list.wrappedValue.append(id) } } } }
            ForEach(list.wrappedValue, id: \.self) { id in Row(id) { Pill("Remove") { list.wrappedValue.removeAll { $0 == id } } } }
        }
    }
    var body: some View {
        switch page {
        case "General":
            Section_("General")
            Row("Open at login", "Starts Perch when you log in.") { tog($s.p.launchAtLogin) { if s.p.launchAtLogin { try? SMAppService.mainApp.register() } else { try? SMAppService.mainApp.unregister() } } }
            Row("Open when the pointer rests on it", "Off, the island opens only from its shortcut or a click.") { tog($s.p.hoverOpens) }
            Row("Shape", "A floating pill, or a notch that sits flush with the top edge.") { Picker("", selection: $s.p.shape) { Text(L("Island")).tag("island"); Text(L("Notch")).tag("notch") }.pickerStyle(.segmented).frame(width: 170).onChange(of: s.p.shape) { _, _ in Island.shared.reframe() } }
            Row("Position", "Dragging the island moves it; Lock keeps it where it is.") { Picker("", selection: $s.p.position) { Text(L("Left")).tag("left"); Text(L("Centre")).tag("center"); Text(L("Right")).tag("right") }.pickerStyle(.segmented).frame(width: 220).onChange(of: s.p.position) { _, _ in Island.shared.reframe() } }
            Row("Lock position") { tog($s.p.lockPosition) { Island.shared.applyLock() } }
            Row("Hide over full-screen apps") { tog($s.p.hideInFullscreen) }
            Section_("Weather")
            Row("City", "Only the name and its coordinates are sent to Open-Meteo.") { HStack { TextField("City", text: $s.p.city).textFieldStyle(.roundedBorder).frame(width: 150).onSubmit { s.p.lat = 0; s.p.lon = 0; Task { await Weather.shared.refresh() } }; Toggle("°F", isOn: $s.p.fahrenheit).onChange(of: s.p.fahrenheit) { _, _ in Task { await Weather.shared.refresh() } } } }
            Section_("Settings backup")
            Row("Export or import", "Your key and clipboard history are never included.") { HStack {
                Pill("Export…") { let p = NSSavePanel(); p.nameFieldStringValue = "perch-settings.json"; if p.runModal() == .OK, let u = p.url, let d = s.exportJSON() { try? d.write(to: u) } }
                Pill("Import…") { let p = NSOpenPanel(); p.allowedContentTypes = [.json]; if p.runModal() == .OK, let u = p.url, let d = try? Data(contentsOf: u) { status = s.importJSON(d) ? "Imported." : "That file is not a Perch backup."; Island.shared.reframe() } }; Text(status).font(.caption) } }
        case "Appearance":
            Section_("Glass")
            Row("Clarity", "0 is fully frosted, 1 almost clear.") { num(Binding(get: { s.p.clarity * 100 }, set: { s.p.clarity = $0 / 100 }), 0...100, "%") }
            Row("Accent colour", "The bars, progress and levels.") { ColorPicker("", selection: Binding(get: { Color(hex: s.p.accentHex) }, set: { c in if let ns = NSColor(c).usingColorSpace(.sRGB) { s.p.accentHex = String(format: "#%02X%02X%02X", Int(ns.redComponent * 255), Int(ns.greenComponent * 255), Int(ns.blueComponent * 255)) } })).labelsHidden() }
            Section_("Companion")
            Row("A little friend beside the island", "It rests when nothing happens, hops over when something does, and likes being petted.") { tog($s.p.mascotOn) { Mascot.shared.sync() } }
            if s.p.mascotOn {
                Row("Shape") { Picker("", selection: $s.p.mascotShape) { Text(L("Ball")).tag("ball"); Text(L("Pill")).tag("pill"); Text(L("Square")).tag("square") }.pickerStyle(.segmented).frame(width: 200) }
                Row("Side") { Picker("", selection: $s.p.mascotSide) { Text(L("Left")).tag("left"); Text(L("Right")).tag("right") }.pickerStyle(.segmented).frame(width: 140).onChange(of: s.p.mascotSide) { _, _ in Mascot.shared.sync() } }
            }
        case "Panels":
            Section_("Bundles")
            Row("Essentials", "Music, controls, mixer-free essentials and nothing that listens to the keyboard or mouse.") { Pill("Apply") { bundle(["music", "notices", "clip", "controls", "utils", "shelf", "sites", "tools", "notes"], quiet: true) } }
            Row("Windows", "Essentials plus the switcher and the window layout shortcuts.") { Pill("Apply") { bundle(["music", "notices", "clip", "controls", "utils", "shelf", "tools"], quiet: false); s.p.switcherOn = true; s.p.gestureOn = true; Input.refresh() } }
            Row("Battery and quiet", "Only music, notices and controls. Every input tweak is switched off.") { Pill("Apply") { bundle(["music", "notices", "controls"], quiet: true) } }
            Row("Everything") { Pill("Apply") { bundle(Island.tabs.map(\.id), quiet: false) } }
            Section_("What is listening")
            Row("Keyboard and mouse", Input.tap == nil ? "Nothing. No input tweak is on." : "The event tap is active for the tweaks you switched on. Nothing typed is stored or sent.") { EmptyView() }
            Section_("Tabs")
            ForEach(Island.tabs) { t in Row(t.name, t.blurb) { tog(Binding(get: { !s.p.hiddenTabs.contains(t.id) }, set: { on in if on { s.p.hiddenTabs.removeAll { $0 == t.id } } else if !s.p.hiddenTabs.contains(t.id) { s.p.hiddenTabs.append(t.id) } })) } }
        case "Input":
            Section_("Mouse wheel")
            Row("Reverse the wheel", "A mouse wheel scrolls the other way; the trackpad keeps its direction.") { tog($s.p.scrollInvert) { Input.refresh() } }
            Row("Reverse the tilt wheel") { tog($s.p.scrollInvertH) { Input.refresh() } }
            Row("Scroll sideways while holding ⌥", "Hold Option and the wheel scrolls left and right.") { tog($s.p.scrollSideways) { Input.refresh() } }
            Row("Scroll speed", "100 is the system's own.") { num($s.p.scrollSpeed, 25...400, "%").onChange(of: s.p.scrollSpeed) { _, _ in Input.refresh() } }
            Row("Same distance every notch") { tog($s.p.scrollLinear) { Input.refresh() } }
            Row("Turn pointer acceleration off", "Restored when you turn this off.") { tog($s.p.noMouseAccel) { Input.refresh() } }
            Section_("Worn hardware")
            Row("Extra click filter", "Ignore a click that follows another too quickly.") { tog($s.p.clickFilter) { Input.refresh() } }
            Row("Keyboard debounce", "Ignore a letter that repeats too quickly.") { tog($s.p.keyDebounce) { Input.refresh() } }
            Row("Shortest gap, clicks") { int($s.p.clickFilterMs, 10...120, "ms").onChange(of: s.p.clickFilterMs) { _, _ in Input.refresh() } }
            Row("Shortest gap, keys") { int($s.p.keyDebounceMs, 10...120, "ms").onChange(of: s.p.keyDebounceMs) { _, _ in Input.refresh() } }
            Section_("Keyboard")
            Row("Caps Lock as a super key", "Hold it with another key to press ⌃⌥⇧ and that key. Uses the system's key remapping and is undone when you turn it off.") { tog($s.p.superKey) { Input.refresh() } }
            Row("A quick tap does") { Picker("", selection: $s.p.superTap) { Text(L("Nothing")).tag("none"); Text("Escape").tag("escape"); Text(L("Command bar")).tag("commandbar"); Text(L("Open Perch")).tag("island") }.frame(width: 150).onChange(of: s.p.superTap) { _, _ in Input.refresh() } }
            Row("Protect ⌘Q and ⌘W", "They act only after a hold or a second press.") { tog($s.p.quitGuard) { Input.refresh() } }
            Row("How to confirm") { Picker("", selection: $s.p.quitGuardMode) { Text(L("Hold")).tag("hold"); Text(L("Press twice")).tag("double") }.pickerStyle(.segmented).frame(width: 170).onChange(of: s.p.quitGuardMode) { _, _ in Input.refresh() } }
            apps("Only these apps", "Leave empty to protect every app.", $s.p.quitGuardApps)
            Section_("Text snippets")
            Row("Expand snippets as you type", "Type a trigger anywhere and it becomes its text. Variables: {{date}}, {{time}}, {{datetime}} with an optional :format, and {{clipboard}}.") { tog($s.p.snippetsOn) { Input.refresh() } }
            ForEach($s.p.snippets) { $sn in Row(sn.name.isEmpty ? sn.trigger : sn.name) { HStack { TextField("Name", text: $sn.name).frame(width: 90); TextField(";trigger", text: $sn.trigger).frame(width: 80); TextField("Text", text: $sn.text).frame(width: 170); Toggle("Menu", isOn: $sn.inMenu); Pill("Remove") { s.p.snippets.removeAll { $0.id == sn.id } } }.textFieldStyle(.roundedBorder) } }
            Row("Add a snippet", "Opens from a searchable menu too (see Keys).") { Pill("Add snippet") { s.p.snippets.append(Snippet(trigger: ";sig", text: "Best regards")) } }
            Section_("Apps to leave alone")
            apps("Apps the input tweaks skip", "Focus follows mouse never switches to these.", $s.p.inputIgnoredApps)
            Section_("Cleaning mode")
            Row("Switch the keyboard and mouse off", "Wipe the keys without typing. Hold Esc for 3 seconds to finish.") { Pill("Start", primary: true) { CleaningMode.shared.start() } }
            Row("Screen while cleaning") { Picker("", selection: $s.p.cleaningStyle) { Text(L("Black screen")).tag("black"); Text(L("Clear")).tag("clear") }.pickerStyle(.segmented).frame(width: 190) }
        case "Windows":
            Section_("Window switcher")
            Row("Switcher", "⌃⌥Tab opens a grid of windows with live previews. Type to search, arrows to move, Return to switch.") { tog($s.p.switcherOn) { registerHotKeys() } }
            Section_("Layouts")
            Row("Window gap", "Pixels between snapped windows.") { num($s.p.layoutGap, 0...40, "px") }
            Row("Screen gap") { num($s.p.layoutScreenGap, 0...40, "px") }
            Row("Margin for maximise with margin") { num($s.p.layoutMargin, 0...25, "%") }
            Row("Shortcuts", "Halves, thirds, two-thirds, quarters, sixths, corners, centre, maximise, next and previous display, restore are bound under Keys.") { EmptyView() }
            apps("Leave these apps alone", "Layouts and drag gestures skip their windows.", $s.p.layoutIgnoredApps)
            Section_("Drag to move and resize")
            Row("Hold ⌥ and drag anywhere in a window", "Add ⇧ (or use the right button) to resize. Let go at a screen edge to snap.") { tog($s.p.gestureOn) { Input.refresh() } }
            Row("Focus follows the mouse", "Rest the pointer on a window and it comes to the front.") { tog($s.p.focusFollowsMouse) { Input.refresh() } }
            Row("Quit apps when their last window closes", "") { EmptyView() }
            apps("Apps to quit", "Asked politely a few seconds after their last window goes.", $s.p.autoQuitApps)
        case "Keys":
            Section_("Shortcuts")
            ForEach(Array(Prefs.defaultHotkeys.keys.sorted()), id: \.self) { k in Row(Self.keyLabels[k] ?? k.capitalized, "Written like ctrl+opt+i. Leave empty to unbind.") { TextField("", text: Binding(get: { s.p.hotkeys[k] ?? "" }, set: { s.p.hotkeys[k] = $0 })).textFieldStyle(.roundedBorder).frame(width: 170).onSubmit { registerHotKeys() } } }
            Row("Apply", "") { Pill("Register shortcuts", primary: true) { registerHotKeys() } }
        case "Clipboard":
            Section_("History")
            Row("Keep it between launches", "Encrypted with a key in your Keychain.") { tog($s.p.clipPersist) { if !s.p.clipPersist { Clip.shared.forget() } } }
            Row("How many clips") { int($s.p.clipMax, 20...500, "") }
            Row("Forget after", "0 keeps them until you clear them.") { int($s.p.clipKeepHours, 0...168, "h") }
            Row("Skip text that looks sensitive", "Passwords and tokens. Items password managers mark concealed are always skipped.") { tog($s.p.clipSkipSensitive) }
            Row("Clear the clipboard when the screen locks") { tog($s.p.clipClearOnLock) }
            Row("Strip tracking tags from copied links") { tog($s.p.cleanLinks) }
            apps("Apps whose copies are never saved", "A password manager, say.", $s.p.clipIgnoredApps)
        case "Capture":
            Section_("Screenshot")
            Row("What happens next") { Picker("", selection: $s.p.shotAction) { Text(L("Open the editor")).tag("editor"); Text(L("Copy")).tag("copy"); Text(L("Save")).tag("save"); Text(L("Save and copy")).tag("both") }.frame(width: 170) }
            Row("Save to", s.p.shotFolder.isEmpty ? "Pictures/Perch" : s.p.shotFolder) { Pill("Choose…") { let p = NSOpenPanel(); p.canChooseDirectories = true; p.canChooseFiles = false; if p.runModal() == .OK, let u = p.url { s.p.shotFolder = u.path } } }
            Row("Take one now") { HStack { Pill("Area", primary: true) { SettingsWindow.window?.close(); Tools.screenshot() }; Pill("Window") { SettingsWindow.window?.close(); Tools.screenshot(window: true) } } }
            Section_("Other")
            Row("Copy colours as") { Picker("", selection: $s.p.colourFormat) { Text("#RRGGBB").tag("hex"); Text("rgb()").tag("rgb"); Text("hsl()").tag("hsl"); Text("SwiftUI").tag("swiftui") }.frame(width: 140) }
            Row("Camera mirror", "Flip it like a mirror.") { HStack { Toggle("", isOn: $s.p.cameraMirror).labelsHidden(); Pill("Open") { CameraWindow.toggle() } } }
        case "Energy":
            Section_("Keep awake rules")
            Row("While the charger is in") { tog($s.p.awakeCharger) }
            Row("While another display is connected") { tog($s.p.awakeExternal) }
            Row("Nudge the pointer now and then", "So chat apps do not show you as away.") { tog($s.p.awakeJiggle) }
            apps("While these apps run", "Stay awake as long as any is open.", $s.p.awakeApps)
            Section_("Display")
            Row("Brightness", Brightness.shared.builtIn ? "Controls the built-in display; other displays are dimmed with a dark layer." : "Dims the picture with a dark layer.") { Pill("Lower") { Brightness.shared.nudge(-0.1) } }
        case "Alerts":
            Section_("Warnings")
            Row("The processor is busy") { tog($s.p.alertCpu) }; Row("At or above") { int($s.p.alertCpuPct, 50...100, "%") }
            Row("Memory is nearly full") { tog($s.p.alertMem) }; Row("At or above") { int($s.p.alertMemPct, 60...99, "%") }
            Row("The disk is nearly full") { tog($s.p.alertDisk) }; Row("At or below (free)") { int($s.p.alertDiskPct, 2...40, "%") }
            Row("Tell me again after") { int($s.p.alertCooldown, 5...240, "min") }
        case "More tabs":
            Section_("Calendar")
            Row("Calendar files and links", "Your Calendar app is read through EventKit; add .ics files or links too.") { HStack { Pill("Add file…") { let p = NSOpenPanel(); if p.runModal() == .OK, let u = p.url { s.p.calendarSources.append(u.path) } } } }
            ForEach(s.p.calendarSources, id: \.self) { c in Row(String(c.suffix(50))) { Pill("Remove") { s.p.calendarSources.removeAll { $0 == c } } } }
            Section_("AI agents")
            Row("What Perch reads", "The session files Claude Code (~/.claude) and Codex (~/.codex) keep: dates, models, project folders and token counts. Never prompts or replies. Nothing is written or sent.") { EmptyView() }
            Row("Claude Code", AgentModel.shared.found("Claude") ? "Found." : "Not found.") { tog($s.p.agentsClaude) }
            Row("Codex", AgentModel.shared.found("Codex") ? "Found." : "Not found.") { tog($s.p.agentsCodex) }
            Row("Tell me when a task finishes") { tog($s.p.agentsFinishAlert) }
            Row("Daily budget", "API value, 0 is off.") { num($s.p.agentsDailyBudget, 0...200, "$") }
            Section_("Lyrics")
            Row("Find lyrics online", "Sends the song, artist and length to lrclib.net.") { tog($s.p.lyricsOnline) }
            Section_("Timer")
            Row("Pomodoro lengths", "Focus \(s.p.pomoFocus)m, break \(s.p.pomoShort)m, long break \(s.p.pomoLong)m every \(s.p.pomoEvery) sessions.") { Stepper("Sessions \(s.p.pomoTotal)", value: $s.p.pomoTotal, in: 1...16) }
        case "Assistant":
            Section_("Your assistant")
            Row("API key", "Kept in the macOS Keychain and sent only to Anthropic.") { SecureField("sk-ant-…", text: Binding(get: { "" }, set: { if !$0.isEmpty { Secret.set("anthropic", $0) } })).textFieldStyle(.roundedBorder).frame(width: 220) }
            Row("Model") { TextField("", text: $s.p.aiModel).textFieldStyle(.roundedBorder).frame(width: 220) }
        case "Language":
            Section_("Language")
            Row("Show Perch in", "Perch's words are English. Pick another language and the assistant translates the words the screens use into a file on this Mac. Only interface words are sent.") { Picker("", selection: $s.p.language) { ForEach(Loc.languages, id: \.self) { Text($0).tag($0) } }.frame(width: 190).onChange(of: s.p.language) { _, _ in Loc.load() } }
            if s.p.language != "English" { Row("Translate", Loc.map.isEmpty ? "Nothing translated yet. Visit a few pages first so their words are known." : "\(Loc.map.count) phrases.") { Pill("Translate now", primary: true) { Task { let r = await Loc.translate(Array(Loc.seen)) { status = $0 }; status = r.isEmpty ? "Done. Reopen Perch to see it everywhere." : r } } }; Text(status).font(.caption) }
        default:
            Section_("About")
            Row("Perch for Mac 1.0", "An original notch for macOS. Written from the Windows version; not affiliated with any other app.") { EmptyView() }
            Row("Your data", "Settings live in your user defaults; the clipboard history and translations in ~/Library/Application Support/Perch.") { Pill("Open folder") { NSWorkspace.shared.open(FileManager.default.urls(for: .applicationSupportDirectory, in: .userDomainMask)[0].appendingPathComponent("Perch")) } }
            Row("Remove Perch's data", "Deletes settings, history and translations.") { Pill("Remove…") { UserDefaults.standard.removeObject(forKey: "perch.prefs"); try? FileManager.default.removeItem(at: FileManager.default.urls(for: .applicationSupportDirectory, in: .userDomainMask)[0].appendingPathComponent("Perch")); Input.setSuperKeyMapping(false); NSApp.terminate(nil) } }
            Row("Quit Perch") { Pill("Quit") { NSApp.terminate(nil) } }
        }
    }
    static let keyLabels = ["toggle": "Open or close the island", "commandbar": "Command bar", "radial": "Radial menu", "screenshot": "Screenshot", "record": "Record the screen", "switcher": "Window switcher", "snippets": "Snippet menu", "clipboard": "Clipboard history window", "colour": "Pick a colour", "ocr": "Copy text from the screen", "left": "Snap left half", "right": "Snap right half", "top": "Snap top half", "bottom": "Snap bottom half", "max": "Maximise", "center": "Centre", "cleaning": "Cleaning mode", "camera": "Camera mirror", "mic": "Mute the microphone", "playpause": "Play or pause"]
    func bundle(_ keep: [String], quiet: Bool) {
        s.p.hiddenTabs = Island.tabs.map(\.id).filter { !keep.contains($0) }
        if quiet { s.p.switcherOn = false; s.p.gestureOn = false; s.p.scrollInvert = false; s.p.scrollInvertH = false; s.p.scrollSideways = false; s.p.scrollLinear = false; s.p.clickFilter = false; s.p.keyDebounce = false; s.p.quitGuard = false; s.p.snippetsOn = false; s.p.superKey = false; s.p.focusFollowsMouse = false; Input.refresh() }
        Notices.shared.add("Bundle applied", "Nothing was deleted. Switch anything back on under Panels.", symbol: "checkmark")
    }
}
