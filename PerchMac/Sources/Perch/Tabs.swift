import SwiftUI
import AppKit
import UserNotifications

// MARK: - Music

struct MusicTab: View {
    @ObservedObject var m = Media.shared
    var body: some View {
        if !m.hasSession { Empty(symbol: "music.note", title: "Nothing playing", text: "Start a song in Music, Spotify or a browser and it shows up here.") }
        else {
            HStack(spacing: 14) {
                Group { if let a = m.art { Image(nsImage: a).resizable() } else { Rectangle().fill(Look.card).overlay(Image(systemName: "music.note")) } }.frame(width: 92, height: 92).clipShape(RoundedRectangle(cornerRadius: 14, style: .continuous))
                VStack(alignment: .leading, spacing: 4) {
                    Text(m.title).font(.system(size: 15, weight: .semibold)).lineLimit(1); Text(m.artist).font(.system(size: 12)).foregroundStyle(Look.dim).lineLimit(1)
                    if m.duration > 0 { ProgressView(value: min(1, m.position / m.duration)).tint(Look.accent()).padding(.vertical, 4) }
                    HStack(spacing: 18) {
                        Button { m.previous() } label: { Image(systemName: "backward.fill") }; Button { m.toggle() } label: { Image(systemName: m.playing ? "pause.fill" : "play.fill").font(.system(size: 20)) }; Button { m.next() } label: { Image(systemName: "forward.fill") }
                    }.buttonStyle(.plain).font(.system(size: 15))
                }
            }.padding(.horizontal, 20)
        }
    }
}
struct Empty: View {
    let symbol: String, title: String, text: String
    var body: some View { VStack(spacing: 6) { Image(systemName: symbol).font(.system(size: 22)).foregroundStyle(Look.dim); Label_(title, size: 12.5, bold: true); Label_(text, size: 11, dim: true).multilineTextAlignment(.center).frame(maxWidth: 260) }.frame(maxWidth: .infinity, maxHeight: .infinity) }
}

// MARK: - Sites

struct SitesTab: View {
    @ObservedObject var s = Settings.shared
    @State private var name = "", url = ""
    var body: some View {
        VStack(alignment: .leading, spacing: 8) {
            ScrollView { LazyVGrid(columns: Array(repeating: GridItem(.flexible(), spacing: 6), count: 3), spacing: 6) {
                ForEach(s.p.sites) { site in Button { if let u = URL(string: site.url.contains("://") ? site.url : "https://" + site.url) { NSWorkspace.shared.open(u) } } label: { GlassCard { Label_(site.name, size: 11.5, bold: true).frame(maxWidth: .infinity) } }.buttonStyle(.plain).contextMenu { Button("Remove") { s.p.sites.removeAll { $0 == site } } } }
            } }
            HStack { TextField(L("Name"), text: $name).textFieldStyle(.roundedBorder).frame(width: 90); TextField("example.com", text: $url).textFieldStyle(.roundedBorder)
                Pill("Add") { guard !name.isEmpty, !url.isEmpty else { return }; s.p.sites.append(Site(name: name, url: url)); name = ""; url = "" } }
        }.padding(.horizontal, 16)
    }
}

// MARK: - Notices

struct NoticesTab: View {
    @ObservedObject var n = Notices.shared
    var body: some View {
        if n.items.isEmpty { Empty(symbol: "bell", title: "No notices yet", text: "Timers, screenshots and alerts collect here.") }
        else { VStack(alignment: .trailing, spacing: 4) {
            ScrollView { VStack(spacing: 5) { ForEach(n.items) { i in GlassCard(pad: 8) { HStack { Image(systemName: i.symbol).frame(width: 22); VStack(alignment: .leading) { Label_(i.title, size: 12, bold: true); if !i.body.isEmpty { Label_(i.body, size: 10.5, dim: true) } }; Spacer(); Text(i.at, style: .time).font(.system(size: 10)).foregroundStyle(Look.dim) } } } }.padding(.horizontal, 14) }
            Pill("Clear all") { n.clear() }.padding(.trailing, 14) } }
    }
}

// MARK: - Shelf

struct ShelfTab: View {
    @ObservedObject var shelf = Shelf.shared
    var body: some View {
        if shelf.files.isEmpty { Empty(symbol: "tray", title: "The shelf is empty", text: "Drop files on the island to hold them here, then drag them out again.") }
        else { ScrollView(.horizontal) { HStack(spacing: 8) { ForEach(shelf.files, id: \.self) { u in
            VStack(spacing: 4) { Image(nsImage: NSWorkspace.shared.icon(forFile: u.path)).resizable().frame(width: 44, height: 44); Label_(u.lastPathComponent, size: 10).frame(width: 74) }
                .padding(8).background(RoundedRectangle(cornerRadius: 12).fill(Look.card)).onDrag { NSItemProvider(contentsOf: u) ?? NSItemProvider() }
                .contextMenu { Button("Show in Finder") { NSWorkspace.shared.activateFileViewerSelecting([u]) }; Button("Remove") { shelf.remove(u) } }
        } }.padding(.horizontal, 16) } }
    }
}

// MARK: - Clipboard

struct ClipTab: View {
    @ObservedObject var c = Clip.shared
    @State private var q = ""
    var body: some View {
        VStack(spacing: 6) {
            TextField(L("Search copied text"), text: $q).textFieldStyle(.roundedBorder).padding(.horizontal, 14)
            ScrollView { VStack(spacing: 4) {
                ForEach(c.items.filter { q.isEmpty || ($0.text ?? "").localizedCaseInsensitiveContains(q) }.sorted { $0.pinned && !$1.pinned }) { i in
                    HStack {
                        Button { c.copyBack(i); Notices.shared.add("Copied again", symbol: "doc.on.doc") } label: {
                            if let p = i.imagePNG, let img = NSImage(data: p) { Image(nsImage: img).resizable().scaledToFit().frame(height: 44) } else { Text((i.text ?? "").replacingOccurrences(of: "\n", with: " ")).font(.system(size: 11.5)).lineLimit(2).frame(maxWidth: .infinity, alignment: .leading) }
                        }.buttonStyle(.plain)
                        Button { c.pin(i) } label: { Image(systemName: i.pinned ? "pin.fill" : "pin").font(.system(size: 10)) }.buttonStyle(.plain).foregroundStyle(Look.dim)
                    }.padding(8).background(RoundedRectangle(cornerRadius: 10).fill(Look.card))
                }
            }.padding(.horizontal, 14) }
            HStack { Spacer(); Pill("Clear unpinned") { c.clearUnpinned() } }.padding(.horizontal, 14)
        }
    }
}

// MARK: - Widgets

struct WidgetsTab: View {
    @ObservedObject var st = Stats.shared, w = Weather.shared
    var body: some View {
        VStack(spacing: 8) {
            HStack(spacing: 8) {
                GlassCard { VStack(alignment: .leading) { Label_(w.temp.isEmpty ? "–" : w.temp, size: 26, bold: true); Label_(w.summary.isEmpty ? "Set a city in Settings" : w.summary, size: 10.5, dim: true) } }.frame(maxWidth: .infinity)
                GlassCard { VStack(alignment: .leading) { Label_(Date.now.formatted(.dateTime.weekday(.wide).day().month()), size: 12, bold: true); Label_(Date.now.formatted(.dateTime.hour().minute()), size: 22, bold: true) } }.frame(maxWidth: .infinity)
            }
            HStack(spacing: 8) { Meter("CPU", st.cpu); Meter("Memory", st.mem); if st.battery >= 0 { Meter(st.charging ? "Battery ⚡" : "Battery", Double(st.battery) / 100) } }
            GlassCard { HStack { Label_("↓ " + Stats.rate(st.down), size: 11.5, bold: true); Spacer(); Label_("↑ " + Stats.rate(st.up), size: 11.5, bold: true) } }
        }.padding(.horizontal, 16).task { await Weather.shared.refresh() }
    }
}
struct Meter: View {
    let t: String, v: Double
    init(_ t: String, _ v: Double) { self.t = t; self.v = v }
    var body: some View { GlassCard(pad: 8) { VStack(alignment: .leading, spacing: 4) { Label_(t, size: 10.5, dim: true); Text(String(format: "%.0f%%", v * 100)).font(.system(size: 15, weight: .semibold)); ProgressView(value: min(1, max(0, v))).tint(Look.accent()) } }.frame(maxWidth: .infinity) }
}

// MARK: - Controls

struct ControlsTab: View {
    @State private var vol: Double = Double(Audio.volume()); @State private var bright: Double = 1
    @ObservedObject var b = Brightness.shared
    var body: some View {
        VStack(spacing: 10) {
            HStack { Image(systemName: "speaker.wave.2"); Slider(value: $vol, in: 0...1) { _ in } .onChange(of: vol) { _, v in Audio.setVolume(Float(v)) }; Text("\(Int(vol * 100))").font(.system(size: 11, design: .monospaced)).frame(width: 30) }
            HStack { Image(systemName: "sun.max"); Slider(value: $bright, in: 0.05...1).onChange(of: bright) { _, v in Brightness.shared.set(Float(v)) }; Text("\(Int(bright * 100))").font(.system(size: 11, design: .monospaced)).frame(width: 30) }
            HStack(spacing: 8) {
                Tile("speaker.slash", "Mute") { Audio.muted.toggle() }; Tile("mic.slash", "Mic") { Audio.toggleMic() }; Tile("hifispeaker", "Output") { Audio.nextOutput() }
                Tile("moon.fill", "Dark mode") { Tools.toggleDarkMode() }; Tile("lock", "Lock") { Tools.lockScreen() }
            }
        }.padding(.horizontal, 18).onAppear { Brightness.shared.read(); bright = Double(Brightness.shared.level); vol = Double(Audio.volume()) }
    }
}
struct Tile: View {
    let symbol: String, title: String, action: () -> Void
    @MainActor init(_ s: String, _ t: String, _ a: @escaping () -> Void) { symbol = s; title = L(t); action = a }
    var body: some View { Button(action: action) { VStack(spacing: 4) { Image(systemName: symbol).font(.system(size: 15)); Text(title).font(.system(size: 9.5, weight: .semibold)).lineLimit(1) }.frame(maxWidth: .infinity).padding(.vertical, 9).background(RoundedRectangle(cornerRadius: 12).fill(Look.card)) }.buttonStyle(.plain) }
}

// MARK: - Tools

struct ToolsTab: View {
    @ObservedObject var s = Settings.shared
    var body: some View {
        VStack(spacing: 12) {
            HStack(spacing: 8) { Tile("camera.viewfinder", "Screenshot") { Tools.screenshot() }; Tile("record.circle", "Record") { Tools.toggleRecording() }; Tile("web.camera", "Camera") { CameraWindow.toggle() }; Tile("rectangle.3.group", "Switcher") { Switcher.shared.toggle() } }
            LazyVGrid(columns: Array(repeating: GridItem(.flexible()), count: 6), spacing: 6) {
                ForEach(0..<6, id: \.self) { i in
                    let path = s.p.slots[i]
                    Button { if !path.isEmpty { NSWorkspace.shared.open(URL(fileURLWithPath: path)) } else { Tools.pickApp { s.p.slots[i] = $0 } } } label: {
                        Group { if path.isEmpty { Image(systemName: "plus").foregroundStyle(Look.dim) } else { Image(nsImage: NSWorkspace.shared.icon(forFile: path)).resizable() } }.frame(width: 40, height: 40).background(RoundedRectangle(cornerRadius: 11).fill(path.isEmpty ? Color.clear : Look.card).overlay(RoundedRectangle(cornerRadius: 11).strokeBorder(.white.opacity(path.isEmpty ? 0.25 : 0))))
                    }.buttonStyle(.plain).contextMenu { Button("Clear") { s.p.slots[i] = "" } }
                }
            }
        }.padding(.horizontal, 16)
    }
}

// MARK: - Utilities

struct UtilsTab: View {
    @ObservedObject var kw = KeepAwake.shared
    @State private var expr = ""
    var body: some View {
        VStack(spacing: 8) {
            let cols = Array(repeating: GridItem(.flexible(), spacing: 6), count: 5)
            LazyVGrid(columns: cols, spacing: 6) {
                Tile("moon.zzz", kw.on ? "Awake ✓" : "Keep awake") { kw.cycle() }; Tile("eyedropper", "Colour") { Tools.pickColour() }; Tile("text.viewfinder", "Copy text") { Tools.copyTextFromScreen() }
                Tile("textformat", "Plain text") { Tools.plainClipboard() }; Tile("link", "Clean link") { Tools.cleanClipboardLink() }
                Tile("circle.lefthalf.filled", "Light/dark") { Tools.toggleDarkMode() }; Tile("menubar.dock.rectangle", "Desktop icons") { Tools.toggleDesktopIcons() }; Tile("lock", "Lock") { Tools.lockScreen() }
                Tile("trash", "Empty Trash") { Tools.emptyTrash() }; Tile("network", "My IP") { Tools.copyIP() }
                Tile("eye.slash", "Hidden files") { Tools.toggleHiddenFiles() }; Tile("display", "Display off") { Tools.displayOff() }; Tile("sparkles.tv", "Screen saver") { Tools.screenSaver() }
                Tile("xmark.app", "Quit others") { Tools.quitOthers() }; Tile("bubbles.and.sparkles", "Clean keys") { CleaningMode.shared.start() }
            }
            GlassCard(pad: 8) { HStack { TextField(L("Calculate or convert: 18 * 4.5"), text: $expr).textFieldStyle(.plain); if let r = Calc.eval(expr) { Text("= " + r).font(.system(size: 12, weight: .semibold)).onTapGesture { NSPasteboard.general.clearContents(); NSPasteboard.general.setString(r, forType: .string) } } } }
        }.padding(.horizontal, 14)
    }
}

enum Calc {
    static func eval(_ s: String) -> String? {
        let t = s.trimmingCharacters(in: .whitespaces); guard !t.isEmpty, t.contains(where: \.isNumber) else { return nil }
        // a unit conversion like "12 km in mi"
        let parts = t.lowercased().components(separatedBy: " in "); if parts.count == 2, let r = convert(parts[0], parts[1]) { return r }
        var p = Parser(Array(t.filter { !$0.isWhitespace })); guard let d = p.expr(), p.done, d.isFinite else { return nil }
        return d == d.rounded() && abs(d) < 1e15 ? String(Int(d)) : String(format: "%.6g", d)
    }
    struct Parser {
        var c: [Character]; var i = 0
        init(_ c: [Character]) { self.c = c }
        var done: Bool { i >= c.count }
        mutating func expr() -> Double? { guard var v = term() else { return nil }; while i < c.count, c[i] == "+" || c[i] == "-" { let op = c[i]; i += 1; guard let r = term() else { return nil }; v = op == "+" ? v + r : v - r }; return v }
        mutating func term() -> Double? { guard var v = power() else { return nil }; while i < c.count, c[i] == "*" || c[i] == "/" { let op = c[i]; i += 1; guard let r = power() else { return nil }; v = op == "*" ? v * r : v / r }; return v }
        mutating func power() -> Double? { guard let b = atom() else { return nil }; if i < c.count, c[i] == "^" { i += 1; guard let e = power() else { return nil }; return pow(b, e) }; return b }
        mutating func atom() -> Double? {
            guard i < c.count else { return nil }
            if c[i] == "-" { i += 1; return atom().map { -$0 } }
            if c[i] == "(" { i += 1; guard let v = expr(), i < c.count, c[i] == ")" else { return nil }; i += 1; return postfix(v) }
            let s = i; while i < c.count, c[i].isNumber || c[i] == "." { i += 1 }; guard i > s, let v = Double(String(c[s..<i])) else { return nil }; return postfix(v)
        }
        mutating func postfix(_ v: Double) -> Double { if i < c.count, c[i] == "%" { i += 1; return v / 100 }; return v }
    }
    static let units: [String: (kind: String, f: Double)] = ["km": ("l", 1000), "m": ("l", 1), "cm": ("l", 0.01), "mm": ("l", 0.001), "mi": ("l", 1609.344), "ft": ("l", 0.3048), "in": ("l", 0.0254), "yd": ("l", 0.9144),
        "kg": ("m", 1), "g": ("m", 0.001), "lb": ("m", 0.45359237), "oz": ("m", 0.0283495), "l": ("v", 1), "ml": ("v", 0.001), "gal": ("v", 3.78541), "cup": ("v", 0.236588)]
    static func convert(_ a: String, _ to: String) -> String? {
        let bits = a.split(separator: " "); guard bits.count == 2, let v = Double(bits[0]), let u = units[String(bits[1])], let t = units[to.trimmingCharacters(in: .whitespaces)], u.kind == t.kind else {
            if let m = a.split(separator: " ").first.flatMap({ Double($0) }), a.hasSuffix("c") || a.hasSuffix("f") { return a.hasSuffix("c") ? String(format: "%.1f f", m * 9 / 5 + 32) : String(format: "%.1f c", (m - 32) * 5 / 9) }
            return nil }
        return String(format: "%.6g %@", v * u.f / t.f, to.trimmingCharacters(in: .whitespaces))
    }
}

// MARK: - Notes, Reminders

struct NotesTab: View {
    @ObservedObject var s = Settings.shared
    var body: some View { VStack(alignment: .trailing) { TextEditor(text: $s.p.notes).font(.system(size: 12)).scrollContentBackground(.hidden).padding(8).background(RoundedRectangle(cornerRadius: 12).fill(Look.card))
        Pill("Copy all") { NSPasteboard.general.clearContents(); NSPasteboard.general.setString(s.p.notes, forType: .string) } }.padding(.horizontal, 16) }
}
struct RemindersTab: View {
    @ObservedObject var s = Settings.shared
    @State private var title = "", when = Date().addingTimeInterval(600)
    var body: some View {
        VStack(spacing: 8) {
            HStack { TextField(L("Remind me to…"), text: $title).textFieldStyle(.roundedBorder); DatePicker("", selection: $when).labelsHidden().frame(width: 180)
                Pill("Add", primary: true) { guard !title.isEmpty else { return }; let r = Reminder(title: title, due: when); s.p.reminders.append(r); Reminders.schedule(r); title = "" } }
            ScrollView { VStack(spacing: 5) { ForEach(s.p.reminders.sorted { $0.due < $1.due }) { r in GlassCard(pad: 8) { HStack { VStack(alignment: .leading) { Label_(r.title, size: 12, bold: true); Text(r.due, format: .dateTime.day().month().hour().minute()).font(.system(size: 10)).foregroundStyle(Look.dim) }; Spacer(); Pill("Done") { s.p.reminders.removeAll { $0.id == r.id }; Reminders.cancel(r) } } } } } }
        }.padding(.horizontal, 14)
    }
}
enum Reminders {
    static func ask() { UNUserNotificationCenter.current().requestAuthorization(options: [.alert, .sound]) { _, _ in } }
    static func schedule(_ r: Reminder) {
        let c = UNMutableNotificationContent(); c.title = "Reminder"; c.body = r.title; c.sound = .default
        let t = UNCalendarNotificationTrigger(dateMatching: Calendar.current.dateComponents([.year, .month, .day, .hour, .minute], from: r.due), repeats: false)
        UNUserNotificationCenter.current().add(UNNotificationRequest(identifier: r.id.uuidString, content: c, trigger: t))
    }
    static func cancel(_ r: Reminder) { UNUserNotificationCenter.current().removePendingNotificationRequests(withIdentifiers: [r.id.uuidString]) }
}

// MARK: - System: ports, speed test, updates

struct SystemTab: View {
    @State private var page = "Ports"; @State private var ports: [(String, String, String)] = []; @State private var speed = "Press Run to measure your connection."; @State private var updates = "Looking for updates…"
    var body: some View {
        VStack(spacing: 8) {
            HStack { ForEach(["Ports", "Speed", "Updates", "Drives"], id: \.self) { p in Button { page = p; load() } label: { Text(L(p)).font(.system(size: 11, weight: .semibold)).foregroundStyle(page == p ? .black : .white).padding(.horizontal, 10).padding(.vertical, 5).background(Capsule().fill(page == p ? Color.white : Look.card)) }.buttonStyle(.plain) }; Spacer() }
            ScrollView { VStack(spacing: 5) {
                switch page {
                case "Ports": ForEach(ports, id: \.1) { p in GlassCard(pad: 8) { HStack { VStack(alignment: .leading) { Label_("Port " + p.1, size: 12, bold: true); Label_(p.0 + " · pid " + p.2, size: 10.5, dim: true) }; Spacer(); Pill("Free it") { Tools.run("/bin/kill", [p.2]); load() } } } }
                case "Speed": GlassCard { Text(speed).font(.system(size: 12)).frame(maxWidth: .infinity, alignment: .leading) }; Pill("Run", primary: true) { speed = "Measuring…"; Task { speed = await Tools.speedTest() } }
                case "Updates": GlassCard { Text(updates).font(.system(size: 11, design: .monospaced)).frame(maxWidth: .infinity, alignment: .leading) }; Pill("Update all with Homebrew") { Tools.runTerminal("brew upgrade") }
                default: ForEach(Tools.volumes(), id: \.self) { v in GlassCard(pad: 8) { HStack { Label_(v.lastPathComponent, size: 12, bold: true); Spacer(); if v.path != "/" { Pill("Eject") { try? NSWorkspace.shared.unmountAndEjectDevice(at: v) } } } } }
                }
            } }
        }.padding(.horizontal, 14).onAppear { load() }
    }
    func load() {
        if page == "Ports" { Task.detached { let out = Tools.runOut("/usr/sbin/lsof", ["-iTCP", "-sTCP:LISTEN", "-nP", "-F", "pcn"]); await MainActor.run { ports = Tools.parseLsof(out) } } }
        if page == "Updates" { Task.detached { let o = Tools.runOut("/usr/bin/env", ["brew", "outdated"]); await MainActor.run { updates = o.isEmpty ? "Everything Homebrew manages is up to date." : o } } }
    }
}
