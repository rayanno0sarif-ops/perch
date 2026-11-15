import SwiftUI
import AppKit
import Vision
import AVFoundation
import UniformTypeIdentifiers

// MARK: - Tools (screenshots, text from screen, colour, system switches, processes)

@MainActor enum Tools {
    // MARK: running commands
    @discardableResult nonisolated static func run(_ exe: String, _ args: [String]) -> Int32 { let p = Process(); p.executableURL = URL(fileURLWithPath: exe); p.arguments = args; try? p.run(); p.waitUntilExit(); return p.terminationStatus }
    nonisolated static func runOut(_ exe: String, _ args: [String]) -> String {
        let p = Process(); p.executableURL = URL(fileURLWithPath: exe); p.arguments = args; let pipe = Pipe(); p.standardOutput = pipe; p.standardError = Pipe()
        var env = ProcessInfo.processInfo.environment; env["PATH"] = "/opt/homebrew/bin:/usr/local/bin:" + (env["PATH"] ?? ""); p.environment = env
        do { try p.run() } catch { return "" }
        let d = pipe.fileHandleForReading.readDataToEndOfFile(); p.waitUntilExit(); return String(data: d, encoding: .utf8) ?? ""
    }
    static func runTerminal(_ cmd: String) { _ = NSAppleScript(source: "tell application \"Terminal\" to do script \"\(cmd)\"\ntell application \"Terminal\" to activate")?.executeAndReturnError(nil) }
    static func osa(_ s: String) { var e: NSDictionary?; NSAppleScript(source: s)?.executeAndReturnError(&e) }
    static func defaultsBool(_ domain: String, _ key: String) -> Bool { runOut("/usr/bin/defaults", ["read", domain, key]).trimmingCharacters(in: .whitespacesAndNewlines) == "1" }

    // MARK: files
    static var picturesDir: URL { let s = Settings.shared.p.shotFolder; let d = s.isEmpty ? FileManager.default.urls(for: .picturesDirectory, in: .userDomainMask)[0].appendingPathComponent("Perch") : URL(fileURLWithPath: s); try? FileManager.default.createDirectory(at: d, withIntermediateDirectories: true); return d }
    static func newFile(_ ext: String, prefix: String = "Screenshot") -> URL {
        let f = DateFormatter(); f.dateFormat = "yyyy-MM-dd HH.mm.ss"; return picturesDir.appendingPathComponent("\(prefix) \(f.string(from: Date())).\(ext)")
    }
    static func pickApp(_ done: @escaping (String) -> Void) { let p = NSOpenPanel(); p.allowedContentTypes = [.application]; p.directoryURL = URL(fileURLWithPath: "/Applications"); if p.runModal() == .OK, let u = p.url { done(u.path) } }

    // MARK: screenshots (the system's own selector, so permissions and the look are the Mac's)
    static func screenshot(window: Bool = false) {
        Island.shared.pinned = false; Island.shared.close()
        let f = newFile("png"); let args = window ? ["-w", "-o", f.path] : ["-i", "-s", f.path]
        DispatchQueue.global().asyncAfter(deadline: .now() + 0.25) {
            let p = Process(); p.executableURL = URL(fileURLWithPath: "/usr/sbin/screencapture"); p.arguments = args; try? p.run(); p.waitUntilExit()
            Task { @MainActor in
                guard FileManager.default.fileExists(atPath: f.path), let img = NSImage(contentsOf: f) else { return }
                switch Settings.shared.p.shotAction {
                case "copy": NSPasteboard.general.clearContents(); NSPasteboard.general.writeObjects([img]); try? FileManager.default.removeItem(at: f); Notices.shared.add("Screenshot copied", symbol: "camera")
                case "save": Notices.shared.add("Screenshot saved", f.lastPathComponent, symbol: "camera"); Shelf.shared.add([f])
                case "both": NSPasteboard.general.clearContents(); NSPasteboard.general.writeObjects([img]); Notices.shared.add("Saved and copied", f.lastPathComponent, symbol: "camera"); Shelf.shared.add([f])
                default: EditorWindow.open(img, file: f)
                }
            }
        }
    }
    // MARK: recording (the system recorder; stops when you press its stop button or ⌃⌘Esc)
    private static var recProc: Process?
    static func toggleRecording() {
        if let p = recProc, p.isRunning { p.interrupt(); recProc = nil; Notices.shared.add("Recording saved", symbol: "record.circle"); return }
        let f = newFile("mov", prefix: "Recording"); let p = Process(); p.executableURL = URL(fileURLWithPath: "/usr/sbin/screencapture"); p.arguments = ["-v", "-V", "3600", "-i", f.path]
        p.terminationHandler = { _ in Task { @MainActor in if FileManager.default.fileExists(atPath: f.path) { Shelf.shared.add([f]); Notices.shared.add("Recording saved", f.lastPathComponent, symbol: "record.circle") } } }
        try? p.run(); recProc = p; Notices.shared.add("Recording", "Choose the area. Press the same shortcut to stop.", symbol: "record.circle")
    }

    // MARK: text, codes and colours from the screen (all on this Mac)
    static func copyTextFromScreen() {
        let f = FileManager.default.temporaryDirectory.appendingPathComponent("perch-ocr.png"); try? FileManager.default.removeItem(at: f)
        Island.shared.pinned = false; Island.shared.close()
        DispatchQueue.global().asyncAfter(deadline: .now() + 0.25) {
            let p = Process(); p.executableURL = URL(fileURLWithPath: "/usr/sbin/screencapture"); p.arguments = ["-i", "-x", f.path]; try? p.run(); p.waitUntilExit()
            guard let img = NSImage(contentsOf: f), let cg = img.cgImage(forProposedRect: nil, context: nil, hints: nil) else { return }
            let text = VNRecognizeTextRequest(); text.recognitionLevel = .accurate; text.usesLanguageCorrection = true
            let codes = VNDetectBarcodesRequest()
            try? VNImageRequestHandler(cgImage: cg).perform([codes, text])
            var out = ""; if let c = codes.results?.first?.payloadStringValue { out = c } else { out = (text.results ?? []).compactMap { $0.topCandidates(1).first?.string }.joined(separator: "\n") }
            Task { @MainActor in
                if out.isEmpty { Notices.shared.add("No text found", symbol: "text.viewfinder") } else { NSPasteboard.general.clearContents(); NSPasteboard.general.setString(out, forType: .string); Notices.shared.add("Text copied", String(out.prefix(60)), symbol: "text.viewfinder") }
            }
        }
    }
    static func ocr(_ img: NSImage) {
        guard let cg = img.cgImage(forProposedRect: nil, context: nil, hints: nil) else { return }
        let t = VNRecognizeTextRequest(); try? VNImageRequestHandler(cgImage: cg).perform([t])
        let s = (t.results ?? []).compactMap { $0.topCandidates(1).first?.string }.joined(separator: "\n")
        NSPasteboard.general.clearContents(); NSPasteboard.general.setString(s, forType: .string); Notices.shared.add(s.isEmpty ? "No text found" : "Text copied", symbol: "text.viewfinder")
    }
    static func pickColour() {
        NSColorSampler().show { c in
            guard let c = c?.usingColorSpace(.sRGB) else { return }
            let r = Int(c.redComponent * 255), g = Int(c.greenComponent * 255), b = Int(c.blueComponent * 255)
            let s: String
            switch Settings.shared.p.colourFormat {
            case "rgb": s = "rgb(\(r), \(g), \(b))"
            case "swiftui": s = String(format: "Color(red: %.3f, green: %.3f, blue: %.3f)", c.redComponent, c.greenComponent, c.blueComponent)
            case "hsl": var h: CGFloat = 0, sat: CGFloat = 0, br: CGFloat = 0; c.getHue(&h, saturation: &sat, brightness: &br, alpha: nil); let l = br * (1 - sat / 2); let s2 = (l == 0 || l == 1) ? 0 : (br - l) / min(l, 1 - l); s = String(format: "hsl(%.0f, %.0f%%, %.0f%%)", h * 360, s2 * 100, l * 100)
            default: s = String(format: "#%02X%02X%02X", r, g, b)
            }
            Task { @MainActor in NSPasteboard.general.clearContents(); NSPasteboard.general.setString(s, forType: .string); Notices.shared.add("Colour copied", s, symbol: "eyedropper") }
        }
    }

    // MARK: links and text
    static func cleanLink(_ t: String) -> String? {
        guard let u = URL(string: t.trimmingCharacters(in: .whitespacesAndNewlines)), var c = URLComponents(url: u, resolvingAgainstBaseURL: false), c.scheme?.hasPrefix("http") == true, let items = c.queryItems else { return nil }
        let junk = Set(["fbclid", "gclid", "mc_cid", "mc_eid", "igshid", "yclid", "msclkid", "ref_src", "_ga", "si"]); let keep = items.filter { !($0.name.hasPrefix("utm_") || junk.contains($0.name)) }
        c.queryItems = keep.isEmpty ? nil : keep; return c.string
    }
    static func cleanClipboardLink() {
        guard let t = NSPasteboard.general.string(forType: .string), let c = cleanLink(t), c != t else { Notices.shared.add("Nothing to clean", symbol: "link"); return }
        NSPasteboard.general.clearContents(); NSPasteboard.general.setString(c, forType: .string); Notices.shared.add("Link cleaned", c, symbol: "link")
    }
    static func plainClipboard() { if let t = NSPasteboard.general.string(forType: .string) { NSPasteboard.general.clearContents(); NSPasteboard.general.setString(t, forType: .string); Notices.shared.add("Formatting removed", symbol: "textformat") } }

    // MARK: system switches
    static func toggleDarkMode() { osa("tell application \"System Events\" to tell appearance preferences to set dark mode to not dark mode") }
    static func lockScreen() { run("/usr/bin/pmset", ["displaysleepnow"]) }
    static func displayOff() { run("/usr/bin/pmset", ["displaysleepnow"]) }
    static func screenSaver() { NSWorkspace.shared.open(URL(fileURLWithPath: "/System/Library/CoreServices/ScreenSaverEngine.app")) }
    static func toggleDesktopIcons() { let on = defaultsBool("com.apple.finder", "CreateDesktop") ; run("/usr/bin/defaults", ["write", "com.apple.finder", "CreateDesktop", on ? "false" : "true"]); run("/usr/bin/killall", ["Finder"]) }
    static func toggleHiddenFiles() { let on = defaultsBool("com.apple.finder", "AppleShowAllFiles"); run("/usr/bin/defaults", ["write", "com.apple.finder", "AppleShowAllFiles", on ? "false" : "true"]); run("/usr/bin/killall", ["Finder"]) }
    static func emptyTrash() {
        let a = NSAlert(); a.messageText = L("Empty the Trash?"); a.informativeText = L("All items in the Trash will be removed. This cannot be undone."); a.addButton(withTitle: L("Empty the Trash")); a.addButton(withTitle: L("Cancel"))
        if a.runModal() == .alertFirstButtonReturn { osa("tell application \"Finder\" to empty trash") }
    }
    static func copyIP() {
        var addr = ""; var ifa: UnsafeMutablePointer<ifaddrs>?
        if getifaddrs(&ifa) == 0 { var p = ifa; while let c = p { if c.pointee.ifa_addr.pointee.sa_family == UInt8(AF_INET), String(cString: c.pointee.ifa_name).hasPrefix("en") { var h = [CChar](repeating: 0, count: Int(NI_MAXHOST)); getnameinfo(c.pointee.ifa_addr, socklen_t(c.pointee.ifa_addr.pointee.sa_len), &h, socklen_t(h.count), nil, 0, NI_NUMERICHOST); addr = String(cString: h); break }; p = c.pointee.ifa_next }; freeifaddrs(ifa) }
        NSPasteboard.general.clearContents(); NSPasteboard.general.setString(addr, forType: .string); Notices.shared.add("Address copied", addr, symbol: "network")
    }
    static func quitOthers() {
        let front = NSWorkspace.shared.frontmostApplication; let me = Bundle.main.bundleIdentifier
        let targets = NSWorkspace.shared.runningApplications.filter { $0.activationPolicy == .regular && $0 != front && $0.bundleIdentifier != me && $0.bundleIdentifier != "com.apple.finder" }
        guard !targets.isEmpty else { Notices.shared.add("Nothing else to close", symbol: "xmark.app"); return }
        let a = NSAlert(); a.messageText = L("Quit \(targets.count) other apps?"); a.informativeText = targets.prefix(12).compactMap(\.localizedName).joined(separator: "\n"); a.addButton(withTitle: L("Quit them")); a.addButton(withTitle: L("Cancel"))
        if a.runModal() == .alertFirstButtonReturn { targets.forEach { $0.terminate() } }
    }
    static func volumes() -> [URL] { FileManager.default.mountedVolumeURLs(includingResourceValuesForKeys: nil, options: [.skipHiddenVolumes]) ?? [] }
    static func parseLsof(_ out: String) -> [(String, String, String)] {
        var res: [(String, String, String)] = []; var pid = "", cmd = ""
        for l in out.split(separator: "\n") { let s = String(l); if s.hasPrefix("p") { pid = String(s.dropFirst()) } else if s.hasPrefix("c") { cmd = String(s.dropFirst()) } else if s.hasPrefix("n"), let port = s.split(separator: ":").last { if !res.contains(where: { $0.1 == String(port) }) { res.append((cmd, String(port), pid)) } } }
        return res.sorted { (Int($0.1) ?? 0) < (Int($1.1) ?? 0) }
    }
    static func speedTest() async -> String {
        guard let u = URL(string: "https://speed.cloudflare.com/__down?bytes=25000000") else { return "Could not start." }
        let t = Date(); guard let (d, _) = try? await URLSession.shared.data(from: u) else { return "The test could not finish. Check your connection." }
        let s = Date().timeIntervalSince(t); return String(format: "Download: %.0f Mbit/s (%.1f MB in %.1f s)", Double(d.count) * 8 / s / 1e6, Double(d.count) / 1e6, s)
    }
}

// MARK: - Screenshot editor (arrow, rectangle, pen, text, number, solid block, pixelate-style blur, crop-free; copy/save/pin)

struct Mark: Identifiable { enum Kind { case arrow, rect, pen, text, number, redact }; let id = UUID(); var kind: Kind; var a: CGPoint; var b: CGPoint; var pts: [CGPoint] = []; var text = ""; var colour: Color }

struct EditorView: View {
    let image: NSImage; let file: URL
    @State private var marks: [Mark] = []; @State private var live: Mark?; @State private var tool: Mark.Kind = .arrow; @State private var colour = Color.red; @State private var count = 1; @State private var backdrop = false; @State private var canvasSize = CGSize(width: 1, height: 1)
    var body: some View {
        VStack(spacing: 8) {
            HStack {
                ForEach([("arrow", "arrow.up.right", Mark.Kind.arrow), ("rect", "rectangle", .rect), ("pen", "pencil.tip", .pen), ("text", "textformat", .text), ("num", "1.circle", .number), ("block", "square.fill", .redact)], id: \.0) { t in
                    Button { tool = t.2 } label: { Image(systemName: t.1).frame(width: 30, height: 26).background(Capsule().fill(tool == t.2 ? Look.cardHi : .clear)) }.buttonStyle(.plain) }
                ColorPicker("", selection: $colour).labelsHidden(); Toggle(L("Background"), isOn: $backdrop).toggleStyle(.checkbox).font(.system(size: 11))
                Spacer(); Button(L("Undo")) { _ = marks.popLast() }.buttonStyle(.plain)
            }.padding(.horizontal, 12).padding(.top, 10)
            Canvas { ctx, size in
                ctx.draw(Image(nsImage: image), in: CGRect(origin: .zero, size: size))
                for m in marks + (live.map { [$0] } ?? []) { draw(m, &ctx, size) }
            }.aspectRatio(image.size, contentMode: .fit).padding(.horizontal, 12).background(GeometryReader { g in Color.clear.onAppear { canvasSize = g.size }.onChange(of: g.size) { _, n in canvasSize = n } })
                .gesture(DragGesture(minimumDistance: 0).onChanged { v in
                    if live == nil { live = Mark(kind: tool, a: v.startLocation, b: v.location, pts: [v.startLocation], colour: colour); if tool == .number { live?.text = "\(count)" } }
                    live?.b = v.location; live?.pts.append(v.location)
                }.onEnded { v in
                    guard var m = live else { return }; live = nil; m.b = v.location
                    if m.kind == .text { m.text = prompt("Text") ?? ""; if m.text.isEmpty { return } }
                    if m.kind == .number { count += 1 }
                    marks.append(m) })
            HStack { Pill("Copy", primary: true) { let r = render(); NSPasteboard.general.clearContents(); NSPasteboard.general.writeObjects([r]); Notices.shared.add("Screenshot copied", symbol: "camera") }
                Pill("Save") { if let d = pngData(render()) { try? d.write(to: file); Shelf.shared.add([file]); Notices.shared.add("Saved", file.lastPathComponent, symbol: "camera") } }
                Pill("Pin to screen") { PinWindow.pin(render()) }; Pill("Copy text") { Tools.ocr(image) }; Spacer() }.padding(12)
        }.frame(minWidth: 560, minHeight: 440)
    }
    func prompt(_ title: String) -> String? { let a = NSAlert(); a.messageText = L(title); let f = NSTextField(frame: NSRect(x: 0, y: 0, width: 240, height: 24)); a.accessoryView = f; a.addButton(withTitle: "OK"); a.addButton(withTitle: L("Cancel")); return a.runModal() == .alertFirstButtonReturn ? f.stringValue : nil }
    func draw(_ m: Mark, _ ctx: inout GraphicsContext, _ size: CGSize) {
        switch m.kind {
        case .rect: ctx.stroke(Path(CGRect(x: min(m.a.x, m.b.x), y: min(m.a.y, m.b.y), width: abs(m.a.x - m.b.x), height: abs(m.a.y - m.b.y))), with: .color(m.colour), lineWidth: 3)
        case .redact: ctx.fill(Path(CGRect(x: min(m.a.x, m.b.x), y: min(m.a.y, m.b.y), width: abs(m.a.x - m.b.x), height: abs(m.a.y - m.b.y))), with: .color(.black))
        case .pen: var p = Path(); p.addLines(m.pts); ctx.stroke(p, with: .color(m.colour), style: StrokeStyle(lineWidth: 3, lineCap: .round, lineJoin: .round))
        case .arrow:
            var p = Path(); p.move(to: m.a); p.addLine(to: m.b); let ang = atan2(m.b.y - m.a.y, m.b.x - m.a.x)
            for s in [CGFloat.pi * 0.85, -CGFloat.pi * 0.85] { p.move(to: m.b); p.addLine(to: CGPoint(x: m.b.x + 16 * cos(ang + s), y: m.b.y + 16 * sin(ang + s))) }
            ctx.stroke(p, with: .color(m.colour), style: StrokeStyle(lineWidth: 3.5, lineCap: .round, lineJoin: .round))
        case .text: ctx.draw(Text(m.text).font(.system(size: 20, weight: .semibold)).foregroundColor(m.colour), at: m.a, anchor: .topLeading)
        case .number: ctx.fill(Path(ellipseIn: CGRect(x: m.a.x - 14, y: m.a.y - 14, width: 28, height: 28)), with: .color(m.colour)); ctx.draw(Text(m.text).font(.system(size: 14, weight: .bold)).foregroundColor(.white), at: m.a)
        }
    }
    @MainActor func render() -> NSImage {
        let cs = canvasSize
        let v = Canvas { ctx, size in ctx.draw(Image(nsImage: image), in: CGRect(origin: .zero, size: size)); ctx.scaleBy(x: size.width / max(1, cs.width), y: size.height / max(1, cs.height)); for m in marks { draw(m, &ctx, size) } }.frame(width: image.size.width, height: image.size.height)
        // marks were drawn in the on-screen canvas size; rescale them to the picture's size
        let r = ImageRenderer(content: backdrop ? AnyView(v.padding(48).background(LinearGradient(colors: [Color(hex: "#FF7E5F"), Color(hex: "#FEB47B")], startPoint: .topLeading, endPoint: .bottomTrailing))) : AnyView(v)); r.scale = 2
        return r.nsImage ?? image
    }
    func pngData(_ i: NSImage) -> Data? { i.tiffRepresentation.flatMap { NSBitmapImageRep(data: $0)?.representation(using: .png, properties: [:]) } }
}
@MainActor enum EditorWindow {
    static var wins: [NSWindow] = []
    static func open(_ img: NSImage, file: URL) {
        let w = NSWindow(contentRect: NSRect(x: 0, y: 0, width: 760, height: 580), styleMask: [.titled, .closable, .resizable], backing: .buffered, defer: false); w.title = "Perch screenshot"; w.contentView = NSHostingView(rootView: EditorView(image: img, file: file)); w.center(); w.isReleasedWhenClosed = false
        NSApp.activate(ignoringOtherApps: true); w.makeKeyAndOrderFront(nil); wins.append(w)
    }
}
@MainActor enum PinWindow {
    static var pins: [NSPanel] = []
    static func pin(_ img: NSImage) {
        let p = NSPanel(contentRect: NSRect(origin: NSPoint(x: 200, y: 200), size: NSSize(width: min(img.size.width, 700), height: min(img.size.height, 520))), styleMask: [.borderless, .nonactivatingPanel, .resizable], backing: .buffered, defer: false)
        p.contentView = NSHostingView(rootView: Image(nsImage: img).resizable().scaledToFit().onTapGesture(count: 2) { p.close() }); p.level = .floating; p.isMovableByWindowBackground = true; p.hasShadow = true; p.isReleasedWhenClosed = false; p.orderFrontRegardless(); pins.append(p)
    }
}

// MARK: - Camera mirror

struct CameraPreview: NSViewRepresentable {
    let session: AVCaptureSession; let mirror: Bool
    func makeNSView(context: Context) -> NSView { let v = NSView(); v.wantsLayer = true; let l = AVCaptureVideoPreviewLayer(session: session); l.videoGravity = .resizeAspectFill; l.frame = v.bounds; l.autoresizingMask = [.layerWidthSizable, .layerHeightSizable]; v.layer = CALayer(); v.layer?.addSublayer(l); if mirror { l.setAffineTransform(CGAffineTransform(scaleX: -1, y: 1)) }; return v }
    func updateNSView(_ v: NSView, context: Context) {}
}
@MainActor final class CameraWindow {
    static var panel: NSPanel?; static var session: AVCaptureSession?
    static func toggle() {
        if let p = panel { p.close(); session?.stopRunning(); panel = nil; session = nil; return }
        AVCaptureDevice.requestAccess(for: .video) { ok in DispatchQueue.main.async {
            guard ok, let dev = AVCaptureDevice.default(for: .video), let input = try? AVCaptureDeviceInput(device: dev) else { Notices.shared.add("The camera could not start", "Allow it in System Settings → Privacy & Security → Camera.", symbol: "web.camera"); return }
            let s = AVCaptureSession(); s.sessionPreset = .medium; s.addInput(input); DispatchQueue.global().async { s.startRunning() }; session = s
            let p = NSPanel(contentRect: NSRect(x: 100, y: 100, width: 340, height: 260), styleMask: [.borderless, .nonactivatingPanel], backing: .buffered, defer: false)
            p.contentView = NSHostingView(rootView: CameraPreview(session: s, mirror: Settings.shared.p.cameraMirror).clipShape(RoundedRectangle(cornerRadius: 22)).overlay(RoundedRectangle(cornerRadius: 22).strokeBorder(.white.opacity(0.3), lineWidth: 1.5)).padding(8))
            p.isOpaque = false; p.backgroundColor = .clear; p.level = .floating; p.isMovableByWindowBackground = true; p.hasShadow = true; p.orderFrontRegardless(); panel = p
        } }
    }
}
