import SwiftUI
import AppKit
import CryptoKit
import Security

// MARK: - Settings (one Codable value, saved as JSON in UserDefaults)

struct Site: Codable, Identifiable, Hashable { var id = UUID(); var name: String; var url: String }
struct Snippet: Codable, Identifiable, Hashable { var id = UUID(); var name = ""; var folder = ""; var trigger: String; var text: String; var inMenu = true }
struct Reminder: Codable, Identifiable, Hashable { var id = UUID(); var title: String; var due: Date; var done = false }

struct Prefs: Codable {
    // island
    var shape = "island"                 // island | notch
    var position = "center"              // center | left | right | free
    var freeX = 0.0, freeY = 0.0
    var lockPosition = false
    var hoverOpens = true
    var hideInFullscreen = true
    var clarity = 0.55                   // 0 frosted .. 1 clear
    var accentHex = "#FFFFFF"
    var hiddenTabs: [String] = ["timer", "calendar", "lyrics", "watch", "agents"]
    var language = "English"
    // content
    var sites: [Site] = []
    var slots: [String] = Array(repeating: "", count: 6)
    var city = "", lat = 0.0, lon = 0.0, fahrenheit = false
    // clipboard
    var clipMax = 100, clipKeepHours = 0, clipPersist = false, clipSkipSensitive = false, clipClearOnLock = false
    var clipIgnoredApps: [String] = []
    var cleanLinks = false
    // input (everything off until switched on)
    var scrollInvert = false, scrollInvertH = false, scrollSideways = false, scrollSpeed = 100.0, scrollLinear = false
    var noMouseAccel = false
    var clickFilter = false, clickFilterMs = 40, keyDebounce = false, keyDebounceMs = 45
    var superKey = false, superTap = "escape"       // escape | none | commandbar | island
    var quitGuard = false, quitGuardMode = "hold", quitGuardHoldMs = 900, quitGuardApps: [String] = []
    var snippetsOn = false, snippets: [Snippet] = []
    var inputIgnoredApps: [String] = []
    var cleaningStyle = "black", cleaningMaxSeconds = 300
    // windows
    var layoutGap = 0.0, layoutScreenGap = 0.0, layoutMargin = 5.0
    var gestureOn = false                               // hold ⌥ and drag to move, ⌥⇧ to resize
    var layoutIgnoredApps: [String] = []
    var focusFollowsMouse = false
    var switcherOn = true
    // energy
    var awakeApps: [String] = [], awakeCharger = false, awakeExternal = false, awakeJiggle = false
    var autoQuitApps: [String] = []
    // capture
    var shotFolder = "", shotAction = "editor"          // editor | copy | save | both
    var colourFormat = "hex"
    var recQuality = "balanced"
    var cameraMirror = true
    // alerts
    var alertCpu = false, alertCpuPct = 90, alertMem = false, alertMemPct = 90, alertDisk = false, alertDiskPct = 10, alertCooldown = 30
    // timer
    var pomoFocus = 25, pomoShort = 5, pomoLong = 15, pomoEvery = 4, pomoTotal = 8
    // agents, lyrics, calendar
    var agentsClaude = true, agentsCodex = true, agentsFinishAlert = false, agentsDailyBudget = 0.0
    var lyricsOnline = false, calendarSources: [String] = []
    var mascotOn = false, mascotShape = "ball", mascotSide = "left"
    var aiModel = "claude-sonnet-5-5"
    var hotkeys: [String: String] = Prefs.defaultHotkeys
    var reminders: [Reminder] = []
    var notes = ""
    var launchAtLogin = false
    var updateCheck = false, updateLast = "", updateSkipped = ""

    static let defaultHotkeys: [String: String] = [
        "toggle": "ctrl+opt+i", "commandbar": "ctrl+opt+k", "radial": "ctrl+opt+r", "screenshot": "ctrl+opt+s", "record": "ctrl+opt+v",
        "switcher": "ctrl+opt+tab", "snippets": "ctrl+opt+j", "clipboard": "ctrl+opt+c", "colour": "ctrl+opt+p", "ocr": "ctrl+opt+t",
        "left": "ctrl+opt+left", "right": "ctrl+opt+right", "top": "ctrl+opt+up", "bottom": "ctrl+opt+down", "max": "ctrl+opt+return", "center": "ctrl+opt+c2",
        "cleaning": "", "camera": "", "mic": "", "playpause": ""
    ]
}

@MainActor final class Settings: ObservableObject {
    static let shared = Settings()
    @Published var p: Prefs { didSet { save() } }
    private let key = "perch.prefs"
    init() {
        if let d = UserDefaults.standard.data(forKey: key), let v = try? JSONDecoder().decode(Prefs.self, from: d) { p = v } else { p = Prefs() }
    }
    func save() { if let d = try? JSONEncoder().encode(p) { UserDefaults.standard.set(d, forKey: key) } }
    func exportJSON() -> Data? { let e = JSONEncoder(); e.outputFormatting = .prettyPrinted; return try? e.encode(p) }
    func importJSON(_ d: Data) -> Bool { guard let v = try? JSONDecoder().decode(Prefs.self, from: d) else { return false }; p = v; return true }
}

// MARK: - Notices

struct Notice: Identifiable { let id = UUID(); let title: String; let body: String; let symbol: String; let at = Date() }
@MainActor final class Notices: ObservableObject {
    static let shared = Notices()
    @Published var items: [Notice] = []
    @Published var flash: Notice?
    func add(_ title: String, _ body: String = "", symbol: String = "bell") {
        let n = Notice(title: title, body: body, symbol: symbol)
        items.insert(n, at: 0); if items.count > 30 { items.removeLast() }
        flash = n
        Task { try? await Task.sleep(nanoseconds: 3_500_000_000); if self.flash?.id == n.id { self.flash = nil } }
        Mascot.shared.react(n)
    }
    func clear() { items.removeAll() }
}

// MARK: - Keychain + encryption helpers

enum Secret {
    static func get(_ account: String) -> String? {
        let q: [String: Any] = [kSecClass as String: kSecClassGenericPassword, kSecAttrService as String: "app.perch.mac", kSecAttrAccount as String: account, kSecReturnData as String: true]
        var out: AnyObject?; guard SecItemCopyMatching(q as CFDictionary, &out) == errSecSuccess, let d = out as? Data else { return nil }
        return String(data: d, encoding: .utf8)
    }
    static func set(_ account: String, _ value: String) {
        let base: [String: Any] = [kSecClass as String: kSecClassGenericPassword, kSecAttrService as String: "app.perch.mac", kSecAttrAccount as String: account]
        SecItemDelete(base as CFDictionary)
        if value.isEmpty { return }
        var q = base; q[kSecValueData as String] = Data(value.utf8); SecItemAdd(q as CFDictionary, nil)
    }
    static func box(_ data: Data) -> Data? {
        var k = get("clip-key").flatMap { Data(base64Encoded: $0) }
        if k == nil { let nk = SymmetricKey(size: .bits256).withUnsafeBytes { Data($0) }; set("clip-key", nk.base64EncodedString()); k = nk }
        return try? AES.GCM.seal(data, using: SymmetricKey(data: k!)).combined
    }
    static func open(_ data: Data) -> Data? {
        guard let k = get("clip-key").flatMap({ Data(base64Encoded: $0) }), let b = try? AES.GCM.SealedBox(combined: data) else { return nil }
        return try? AES.GCM.open(b, using: SymmetricKey(data: k))
    }
}

// MARK: - Language (the assistant translates the words the screens use)

@MainActor final class Loc {
    static var map: [String: String] = [:]
    static var seen = Set<String>()
    static let languages = ["English", "Español", "Français", "Deutsch", "Italiano", "Português (Brasil)", "日本語", "한국어", "简体中文", "繁體中文", "Русский", "Türkçe", "Українська", "Slovenčina"]
    static var file: URL { dir.appendingPathComponent(Settings.shared.p.language.filter { $0.isLetter || $0.isNumber } + ".json") }
    static var dir: URL { let d = FileManager.default.urls(for: .applicationSupportDirectory, in: .userDomainMask)[0].appendingPathComponent("Perch/lang"); try? FileManager.default.createDirectory(at: d, withIntermediateDirectories: true); return d }
    static func load() { map = [:]; if Settings.shared.p.language != "English", let d = try? Data(contentsOf: file), let m = try? JSONDecoder().decode([String: String].self, from: d) { map = m } }
    static func tr(_ s: String) -> String { if Settings.shared.p.language == "English" { if seen.count < 20000 { seen.insert(s) }; return s }; return map[s] ?? s }
    static func translate(_ strings: [String], progress: @escaping (String) -> Void) async -> String {
        guard let key = Secret.get("anthropic"), !key.isEmpty else { return "Add your API key under Assistant first." }
        let todo = Array(Set(strings.filter { $0.count > 1 && $0.contains(where: \.isLetter) && map[$0] == nil }))
        var result = map; let lang = Settings.shared.p.language
        for start in stride(from: 0, to: todo.count, by: 60) {
            let batch = Array(todo[start..<min(start + 60, todo.count)]); progress("Translating \(min(start + 60, todo.count)) of \(todo.count)…")
            let prompt = "Translate these user interface strings from English into \(lang). Reply with ONLY a JSON object whose keys are the exact English strings and whose values are the translations. Keep key names (⌘, ⌥, ⌃, ⇧, Esc, Return), product names (Perch, macOS, Bluetooth), numbers, units, placeholders and symbols unchanged.\n\n" + ((try? String(data: JSONEncoder().encode(batch), encoding: .utf8)) ?? "[]")
            guard let text = await Claude.ask(key: key, model: Settings.shared.p.aiModel, system: nil, messages: [["role": "user", "content": prompt]], maxTokens: 8000) else { return "The request was refused." }
            if let a = text.firstIndex(of: "{"), let b = text.lastIndex(of: "}"), let d = String(text[a...b]).data(using: .utf8), let part = try? JSONDecoder().decode([String: String].self, from: d) { for (k, v) in part { result[k] = v } }
        }
        if let d = try? JSONEncoder().encode(result) { try? d.write(to: file) }
        load(); return ""
    }
}
@MainActor func L(_ s: String) -> String { Loc.tr(s) }

// MARK: - Anthropic client (used by the assistant and by translation)

enum Claude {
    static func ask(key: String, model: String, system: String?, messages: [[String: String]], maxTokens: Int = 2048) async -> String? {
        var req = URLRequest(url: URL(string: "https://api.anthropic.com/v1/messages")!)
        req.httpMethod = "POST"; req.timeoutInterval = 120
        req.setValue(key, forHTTPHeaderField: "x-api-key"); req.setValue("2023-06-01", forHTTPHeaderField: "anthropic-version"); req.setValue("application/json", forHTTPHeaderField: "content-type")
        var body: [String: Any] = ["model": model, "max_tokens": maxTokens, "messages": messages]; if let s = system { body["system"] = s }
        req.httpBody = try? JSONSerialization.data(withJSONObject: body)
        guard let (d, r) = try? await URLSession.shared.data(for: req), (r as? HTTPURLResponse)?.statusCode == 200,
              let j = try? JSONSerialization.jsonObject(with: d) as? [String: Any], let c = j["content"] as? [[String: Any]] else { return nil }
        return c.compactMap { $0["text"] as? String }.joined()
    }
}

// MARK: - Clipboard history

struct ClipItem: Identifiable, Codable { var id = UUID(); var text: String?; var imagePNG: Data?; var files: [String]?; var at = Date(); var pinned = false }

@MainActor final class Clip: ObservableObject {
    static let shared = Clip()
    @Published var items: [ClipItem] = []
    private var last = NSPasteboard.general.changeCount
    private var ignoreUntil = Date.distantPast
    private var timer: Timer?
    private var store: URL { FileManager.default.urls(for: .applicationSupportDirectory, in: .userDomainMask)[0].appendingPathComponent("Perch/clip.dat") }

    func start() {
        load()
        timer = Timer.scheduledTimer(withTimeInterval: 0.6, repeats: true) { _ in Task { @MainActor in self.poll() } }
        DistributedNotificationCenter.default().addObserver(forName: Notification.Name("com.apple.screenIsLocked"), object: nil, queue: .main) { _ in
            Task { @MainActor in if Settings.shared.p.clipClearOnLock { NSPasteboard.general.clearContents() } }
        }
    }
    private func poll() {
        let pb = NSPasteboard.general; guard pb.changeCount != last else { trim(); return }; last = pb.changeCount
        guard Date() > ignoreUntil else { return }
        let s = Settings.shared.p
        if let app = NSWorkspace.shared.frontmostApplication?.bundleIdentifier, s.clipIgnoredApps.contains(app) { return }
        // password managers mark their copies as concealed
        if pb.types?.contains(NSPasteboard.PasteboardType("org.nspasteboard.ConcealedType")) == true || pb.types?.contains(NSPasteboard.PasteboardType("org.nspasteboard.TransientType")) == true { return }
        if let urls = pb.readObjects(forClasses: [NSURL.self], options: [.urlReadingFileURLsOnly: true]) as? [URL], !urls.isEmpty {
            let paths = urls.map(\.path); items.removeAll { $0.files == paths }
            items.insert(ClipItem(text: "Files: " + urls[0].lastPathComponent + (urls.count > 1 ? " +\(urls.count - 1) more" : ""), files: paths), at: 0)
        } else if let t = pb.string(forType: .string), !t.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty {
            if s.clipSkipSensitive && Self.looksSensitive(t) { return }
            if s.cleanLinks, let c = Tools.cleanLink(t), c != t { ignoreUntil = Date().addingTimeInterval(1); pb.clearContents(); pb.setString(c, forType: .string); last = pb.changeCount; items.removeAll { $0.text == c }; items.insert(ClipItem(text: c), at: 0) }
            else { items.removeAll { $0.text == t && !$0.pinned }; items.insert(ClipItem(text: t), at: 0) }
        } else if let img = NSImage(pasteboard: pb), let tiff = img.tiffRepresentation, let png = NSBitmapImageRep(data: tiff)?.representation(using: .png, properties: [:]) {
            items.insert(ClipItem(imagePNG: png), at: 0)
            for old in items.filter({ $0.imagePNG != nil }).dropFirst(8) { items.removeAll { $0.id == old.id } }
        } else { return }
        while items.count > max(10, s.clipMax) { if let i = items.lastIndex(where: { !$0.pinned }) { items.remove(at: i) } else { break } }
        saveSoon()
    }
    func trim() {
        let h = Settings.shared.p.clipKeepHours; guard h > 0 else { return }
        let n = items.count; items.removeAll { !$0.pinned && Date().timeIntervalSince($0.at) > Double(h) * 3600 }; if items.count != n { saveSoon() }
    }
    func copyBack(_ c: ClipItem) {
        ignoreUntil = Date().addingTimeInterval(1); let pb = NSPasteboard.general; pb.clearContents()
        if let f = c.files { pb.writeObjects(f.map { URL(fileURLWithPath: $0) as NSURL }) }
        else if let t = c.text { pb.setString(t, forType: .string) }
        else if let p = c.imagePNG, let i = NSImage(data: p) { pb.writeObjects([i]) }
        last = pb.changeCount
    }
    func pin(_ c: ClipItem) { if let i = items.firstIndex(where: { $0.id == c.id }) { items[i].pinned.toggle(); saveSoon() } }
    func clearUnpinned() { items.removeAll { !$0.pinned }; saveSoon() }
    static func looksSensitive(_ t: String) -> Bool {
        let s = t.trimmingCharacters(in: .whitespacesAndNewlines); guard s.count >= 8, s.count <= 200 else { return false }
        for p in ["sk-", "ghp_", "github_pat_", "xox", "AKIA", "eyJ", "-----BEGIN"] where s.hasPrefix(p) { return true }
        if s.contains(" ") || s.contains("\n") || s.contains("/") || s.contains("@") { return false }
        let kinds = [s.contains { $0.isUppercase }, s.contains { $0.isLowercase }, s.contains { $0.isNumber }, s.contains { !$0.isLetter && !$0.isNumber }].filter { $0 }.count
        return kinds == 4 && s.count >= 10
    }
    private var pending: DispatchWorkItem?
    private func saveSoon() {
        guard Settings.shared.p.clipPersist else { return }
        pending?.cancel(); let w = DispatchWorkItem { [items] in
            if let d = try? JSONEncoder().encode(items), let b = Secret.box(d) { try? FileManager.default.createDirectory(at: self.store.deletingLastPathComponent(), withIntermediateDirectories: true); try? b.write(to: self.store) }
        }
        pending = w; DispatchQueue.global().asyncAfter(deadline: .now() + 4, execute: w)
    }
    private func load() {
        guard Settings.shared.p.clipPersist, let d = try? Data(contentsOf: store), let o = Secret.open(d), let v = try? JSONDecoder().decode([ClipItem].self, from: o) else { return }
        items = v
    }
    func forget() { try? FileManager.default.removeItem(at: store) }
}

// MARK: - Shelf

@MainActor final class Shelf: ObservableObject {
    static let shared = Shelf()
    @Published var files: [URL] = []
    func add(_ urls: [URL]) { for u in urls where !files.contains(u) { files.append(u) } }
    func remove(_ u: URL) { files.removeAll { $0 == u } }
}
