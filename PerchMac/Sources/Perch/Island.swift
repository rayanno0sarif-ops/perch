import SwiftUI
import AppKit

// MARK: - The island window

final class IslandPanel: NSPanel {
    override var canBecomeKey: Bool { true }
    override var canBecomeMain: Bool { false }
}

struct TabInfo: Identifiable { let id: String; let name: String; let symbol: String; let height: CGFloat; let blurb: String; let view: () -> AnyView }

@MainActor final class Island: ObservableObject {
    static let shared = Island()
    @Published var expanded = false
    @Published var pinned = false
    @Published var tab = "music"
    @Published var flashSymbol = "", flashText = ""
    var panel: IslandPanel!
    private var closeWork: DispatchWorkItem?, openWork: DispatchWorkItem?, flashWork: DispatchWorkItem?
    private var userHidden = false

    static let tabs: [TabInfo] = [
        TabInfo(id: "music", name: "Music", symbol: "music.note", height: 190, blurb: "Controls for the current track from any player.") { AnyView(MusicTab()) },
        TabInfo(id: "sites", name: "Sites", symbol: "link", height: 190, blurb: "Your bookmarks for daily pages.") { AnyView(SitesTab()) },
        TabInfo(id: "notices", name: "Notices", symbol: "bell", height: 220, blurb: "A short history of messages.") { AnyView(NoticesTab()) },
        TabInfo(id: "shelf", name: "Shelf", symbol: "tray", height: 200, blurb: "A temporary holding spot for files between apps.") { AnyView(ShelfTab()) },
        TabInfo(id: "clip", name: "Clipboard", symbol: "doc.on.clipboard", height: 270, blurb: "Recent copies. Click to reuse, pin to keep.") { AnyView(ClipTab()) },
        TabInfo(id: "widgets", name: "Widgets", symbol: "square.grid.2x2", height: 220, blurb: "Weather, load and network at a glance.") { AnyView(WidgetsTab()) },
        TabInfo(id: "controls", name: "Controls", symbol: "slider.horizontal.3", height: 230, blurb: "Volume, brightness, mute and shortcuts.") { AnyView(ControlsTab()) },
        TabInfo(id: "tools", name: "Tools", symbol: "wrench.and.screwdriver", height: 200, blurb: "Screenshot, record, camera and six launcher slots.") { AnyView(ToolsTab()) },
        TabInfo(id: "utils", name: "Utilities", symbol: "sparkles", height: 290, blurb: "One-click helpers and a calculator.") { AnyView(UtilsTab()) },
        TabInfo(id: "notes", name: "Notes", symbol: "note.text", height: 220, blurb: "A scratchpad that saves itself.") { AnyView(NotesTab()) },
        TabInfo(id: "reminders", name: "Reminders", symbol: "alarm", height: 240, blurb: "Set a reminder in a few clicks.") { AnyView(RemindersTab()) },
        TabInfo(id: "system", name: "System", symbol: "cpu", height: 280, blurb: "Ports, speed test, updates and drives.") { AnyView(SystemTab()) },
        TabInfo(id: "manage", name: "Manage", symbol: "externaldrive", height: 300, blurb: "Uninstall, devices, install, clean, pictures, processes.") { AnyView(ManageTab()) },
        TabInfo(id: "ai", name: "Assistant", symbol: "bubble.left.and.text.bubble.right", height: 300, blurb: "Chat with an assistant.") { AnyView(AssistantTab()) },
        TabInfo(id: "timer", name: "Timer", symbol: "timer", height: 232, blurb: "Timer, stopwatch and Pomodoro.") { AnyView(TimerTab()) },
        TabInfo(id: "calendar", name: "Calendar", symbol: "calendar", height: 292, blurb: "Month view and upcoming appointments.") { AnyView(CalendarTab()) },
        TabInfo(id: "lyrics", name: "Lyrics", symbol: "text.quote", height: 232, blurb: "Synced lyrics of the current song.") { AnyView(LyricsTab()) },
        TabInfo(id: "watch", name: "Watch", symbol: "eye", height: 250, blurb: "Watch part of the screen and say when it changes.") { AnyView(WatchTab()) },
        TabInfo(id: "agents", name: "AI agents", symbol: "sparkle", height: 312, blurb: "Claude Code and Codex usage on this Mac.") { AnyView(AgentsTab()) },
    ]
    var visibleTabs: [TabInfo] { Self.tabs.filter { !Settings.shared.p.hiddenTabs.contains($0.id) } }
    var current: TabInfo { visibleTabs.first { $0.id == tab } ?? visibleTabs.first ?? Self.tabs[0] }

    func setup() {
        let host = NSHostingView(rootView: IslandRoot())
        panel = IslandPanel(contentRect: frame(), styleMask: [.borderless, .nonactivatingPanel], backing: .buffered, defer: false)
        panel.contentView = host; panel.isOpaque = false; panel.backgroundColor = .clear; panel.hasShadow = true
        panel.level = .statusBar; panel.collectionBehavior = [.canJoinAllSpaces, .stationary, .fullScreenAuxiliary, .ignoresCycle]
        panel.isMovableByWindowBackground = !Settings.shared.p.lockPosition; panel.hidesOnDeactivate = false
        panel.orderFrontRegardless()
        NotificationCenter.default.addObserver(forName: NSApplication.didChangeScreenParametersNotification, object: nil, queue: .main) { _ in Task { @MainActor in self.reframe(animated: false) } }
        NotificationCenter.default.addObserver(forName: NSWindow.didMoveNotification, object: panel, queue: .main) { _ in
            Task { @MainActor in
                guard !Settings.shared.p.lockPosition, self.panel.isMovableByWindowBackground, let s = self.panel.screen else { return }
                Settings.shared.p.position = "free"; Settings.shared.p.freeX = Double(self.panel.frame.midX - s.frame.minX); Settings.shared.p.freeY = Double(s.frame.maxY - self.panel.frame.maxY)
            }
        }
        // hide over full-screen apps when asked
        NSWorkspace.shared.notificationCenter.addObserver(forName: NSWorkspace.activeSpaceDidChangeNotification, object: nil, queue: .main) { _ in Task { @MainActor in self.checkFullscreen() } }
    }
    private func checkFullscreen() {
        guard Settings.shared.p.hideInFullscreen else { return }
        let full = NSWorkspace.shared.frontmostApplication.map { app in
            (CGWindowListCopyWindowInfo([.optionOnScreenOnly], kCGNullWindowID) as? [[String: Any]] ?? []).contains { ($0[kCGWindowOwnerPID as String] as? pid_t) == app.processIdentifier && ($0[kCGWindowBounds as String] as? [String: CGFloat])?["Height"] == NSScreen.main?.frame.height && ($0[kCGWindowLayer as String] as? Int) == 0 }
        } ?? false
        panel.alphaValue = full && !userHidden ? 0.0 : 1
    }
    func frame() -> NSRect {
        let s = NSScreen.main ?? NSScreen.screens[0]; let p = Settings.shared.p
        let w: CGFloat = expanded ? 460 : 214, h: CGFloat = expanded ? current.height + 74 : 40
        let hasNotch = s.safeAreaInsets.top > 0
        var x = s.frame.midX - w / 2
        switch p.position { case "left": x = s.frame.minX + 24; case "right": x = s.frame.maxX - w - 24; case "free": x = s.frame.minX + CGFloat(p.freeX) - w / 2; default: break }
        let gap: CGFloat = (p.shape == "notch" || hasNotch) ? 0 : 6
        var y = s.frame.maxY - h - gap
        if p.position == "free" { y = s.frame.maxY - CGFloat(p.freeY) - h }
        return NSRect(x: x, y: y, width: w, height: h)
    }
    func reframe(animated: Bool = true) { panel?.setFrame(frame(), display: true, animate: animated && !NSWorkspace.shared.accessibilityDisplayShouldReduceMotion) }
    func open() { closeWork?.cancel(); guard !expanded else { return }; withAnimation(.spring(response: 0.38, dampingFraction: 0.82)) { expanded = true }; reframe(); panel.makeKey() }
    func close() { guard expanded, !pinned else { return }; withAnimation(.spring(response: 0.34, dampingFraction: 0.9)) { expanded = false }; reframe() }
    func toggle() { if expanded { pinned = false; close() } else { pinned = true; open() } }
    func hoverChanged(_ inside: Bool) {
        let p = Settings.shared.p; guard p.hoverOpens else { return }
        closeWork?.cancel(); openWork?.cancel()
        if inside { let w = DispatchWorkItem { Task { @MainActor in self.open() } }; openWork = w; DispatchQueue.main.asyncAfter(deadline: .now() + 0.12, execute: w) }
        else { let w = DispatchWorkItem { Task { @MainActor in self.close() } }; closeWork = w; DispatchQueue.main.asyncAfter(deadline: .now() + 0.45, execute: w) }
    }
    func select(_ id: String) { withAnimation(.easeInOut(duration: 0.2)) { tab = id }; reframe() }
    func show(_ id: String) { select(id); pinned = true; open() }
    func hideToggle() { userHidden.toggle(); panel.alphaValue = userHidden ? 0 : 1 }
    func flash(_ symbol: String, _ text: String) {
        flashSymbol = symbol; flashText = text; flashWork?.cancel()
        let w = DispatchWorkItem { Task { @MainActor in self.flashText = "" } }; flashWork = w; DispatchQueue.main.asyncAfter(deadline: .now() + 1.6, execute: w)
    }
    func applyLock() { panel.isMovableByWindowBackground = !Settings.shared.p.lockPosition }
}

// MARK: - Look

enum Look {
    static func accent() -> Color { Color(hex: Settings.shared.p.accentHex) }
    static let card = Color.white.opacity(0.10), cardHi = Color.white.opacity(0.2), dim = Color.white.opacity(0.55)
}
extension Color {
    init(hex: String) {
        let h = hex.trimmingCharacters(in: CharacterSet(charactersIn: "#")); var v: UInt64 = 0; Scanner(string: h).scanHexInt64(&v)
        self.init(red: Double((v >> 16) & 255) / 255, green: Double((v >> 8) & 255) / 255, blue: Double(v & 255) / 255)
    }
}
struct GlassCard<Content: View>: View {
    var radius: CGFloat = 12; var pad: CGFloat = 10; @ViewBuilder var content: Content
    var body: some View { content.padding(pad).background(RoundedRectangle(cornerRadius: radius, style: .continuous).fill(Look.card).overlay(RoundedRectangle(cornerRadius: radius, style: .continuous).strokeBorder(.white.opacity(0.12), lineWidth: 0.6))) }
}
struct Pill: View {
    let title: String; var primary = false; let action: () -> Void
    @MainActor init(_ t: String, primary: Bool = false, action: @escaping () -> Void) { title = L(t); self.primary = primary; self.action = action }
    var body: some View {
        Button(action: action) { Text(title).font(.system(size: 11, weight: .semibold)).foregroundStyle(primary ? .black : .white).padding(.horizontal, 11).padding(.vertical, 5).background(Capsule().fill(primary ? Color.white : Look.cardHi)) }.buttonStyle(.plain)
    }
}
struct Blur: NSViewRepresentable {
    var material: NSVisualEffectView.Material = .hudWindow
    func makeNSView(context: Context) -> NSVisualEffectView { let v = NSVisualEffectView(); v.material = material; v.blendingMode = .behindWindow; v.state = .active; return v }
    func updateNSView(_ v: NSVisualEffectView, context: Context) { v.material = material }
}
struct Label_: View { let t: String; var size: CGFloat = 12; var bold = false; var dim = false
    @MainActor init(_ t: String, size: CGFloat = 12, bold: Bool = false, dim: Bool = false) { self.t = L(t); self.size = size; self.bold = bold; self.dim = dim }
    var body: some View { Text(t).font(.system(size: size, weight: bold ? .semibold : .regular)).foregroundStyle(dim ? Look.dim : .white).lineLimit(2) } }

// MARK: - Root view

struct IslandRoot: View {
    @ObservedObject var island = Island.shared
    @ObservedObject var settings = Settings.shared
    @ObservedObject var media = Media.shared
    @ObservedObject var notices = Notices.shared
    var body: some View {
        let shape = UnevenRoundedRectangle(topLeadingRadius: settings.p.shape == "notch" ? 0 : (island.expanded ? 26 : 20), bottomLeadingRadius: island.expanded ? 26 : 20, bottomTrailingRadius: island.expanded ? 26 : 20, topTrailingRadius: settings.p.shape == "notch" ? 0 : (island.expanded ? 26 : 20), style: .continuous)
        ZStack {
            Blur(material: settings.p.clarity > 0.6 ? .underWindowBackground : .hudWindow)
            Color.black.opacity(0.35 + (1 - settings.p.clarity) * 0.3)
            LinearGradient(colors: [.white.opacity(0.14), .clear], startPoint: .top, endPoint: .center)
            if island.expanded { Expanded() } else { Collapsed() }
        }
        .clipShape(shape).overlay(shape.strokeBorder(.white.opacity(0.18), lineWidth: 0.8))
        .onHover { island.hoverChanged($0) }
        .animation(.spring(response: 0.38, dampingFraction: 0.82), value: island.expanded)
        .preferredColorScheme(.dark)
    }
}

struct Collapsed: View {
    @ObservedObject var island = Island.shared, media = Media.shared, notices = Notices.shared, stats = Stats.shared, kw = KeepAwake.shared, timer = Pomodoro.shared
    var body: some View {
        HStack(spacing: 8) {
            if !island.flashText.isEmpty { Image(systemName: island.flashSymbol).font(.system(size: 12)); Text(island.flashText).font(.system(size: 11.5, weight: .semibold)).lineLimit(1) }
            else if let n = notices.flash { Image(systemName: n.symbol).font(.system(size: 12)); Text(n.title).font(.system(size: 11.5, weight: .semibold)).lineLimit(1) }
            else if timer.running { Image(systemName: "timer").font(.system(size: 11)); Text(timer.text).font(.system(size: 12, weight: .semibold, design: .monospaced)) }
            else if media.playing, media.hasSession {
                if let a = media.art { Image(nsImage: a).resizable().frame(width: 24, height: 24).clipShape(RoundedRectangle(cornerRadius: 6)) }
                Text(media.title).font(.system(size: 11.5, weight: .semibold)).lineLimit(1)
                Spacer(minLength: 2); Bars()
            } else if kw.on { Image(systemName: "moon.zzz.fill").font(.system(size: 11)); Text(kw.until.map { $0.formatted(date: .omitted, time: .shortened) } ?? "∞").font(.system(size: 11.5, weight: .semibold)) }
            else { Text(Date.now, format: .dateTime.hour().minute()).font(.system(size: 12, weight: .semibold)) }
        }.foregroundStyle(.white).padding(.horizontal, 14).frame(maxWidth: .infinity, maxHeight: .infinity)
    }
}
struct Bars: View {
    @State private var on = false
    var body: some View { HStack(spacing: 2) { ForEach(0..<4) { i in RoundedRectangle(cornerRadius: 1).fill(Look.accent()).frame(width: 2.5, height: on ? CGFloat([14, 8, 16, 10][i]) : CGFloat([6, 12, 7, 13][i])).animation(.easeInOut(duration: 0.4 + Double(i) * 0.08).repeatForever(autoreverses: true), value: on) } }.frame(height: 18).onAppear { on = true } }
}

struct Expanded: View {
    @ObservedObject var island = Island.shared, settings = Settings.shared
    var body: some View {
        VStack(spacing: 0) {
            let tabs = island.visibleTabs
            ScrollView(.horizontal, showsIndicators: false) {
                HStack(spacing: 4) {
                    ForEach(tabs) { t in
                        Button { island.select(t.id) } label: { Image(systemName: t.symbol).font(.system(size: 12.5)).foregroundStyle(island.current.id == t.id ? .black : .white).frame(width: 30, height: 28).background(Capsule().fill(island.current.id == t.id ? Color.white : Color.clear)) }
                            .buttonStyle(.plain).help(L(t.name))
                    }
                }.padding(.horizontal, 12)
            }.frame(height: 40).padding(.top, 2)
            island.current.view().frame(maxWidth: .infinity, maxHeight: .infinity)
            HStack {
                Button { island.pinned.toggle() } label: { Image(systemName: island.pinned ? "pin.fill" : "pin").font(.system(size: 10)).foregroundStyle(Look.dim) }.buttonStyle(.plain).help(L("Keep open"))
                Spacer()
                Button { SettingsWindow.show() } label: { Image(systemName: "gearshape").font(.system(size: 10.5)).foregroundStyle(Look.dim) }.buttonStyle(.plain).help(L("Settings"))
            }.padding(.horizontal, 14).padding(.bottom, 8).frame(height: 24)
        }
        .onDrop(of: [.fileURL], isTargeted: nil) { providers in
            for p in providers { _ = p.loadObject(ofClass: URL.self) { u, _ in if let u { Task { @MainActor in Shelf.shared.add([u]); Island.shared.select("shelf") } } } }; return true
        }
    }
}

// MARK: - Volume / brightness HUD is shown as a flash in the closed island (see Island.flash).
