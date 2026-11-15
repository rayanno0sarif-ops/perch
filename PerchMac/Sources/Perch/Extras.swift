import SwiftUI
import AppKit
import EventKit
import Vision

// MARK: - Timer, stopwatch, Pomodoro

@MainActor final class Pomodoro: ObservableObject {
    static let shared = Pomodoro()
    @Published var mode = "timer"
    @Published var phase = "focus"
    @Published var running = false
    @Published var left = 600.0
    @Published var total = 600.0
    @Published var up = 0.0
    @Published var session = 1
    @Published var timerMinutes = 10
    private var t: Timer?
    var text: String { let s = Int(mode == "stopwatch" ? up : left); return s >= 3600 ? String(format: "%d:%02d:%02d", s / 3600, s % 3600 / 60, s % 60) : String(format: "%02d:%02d", s / 60, s % 60) }
    func reset() {
        running = false; t?.invalidate(); up = 0; let p = Settings.shared.p
        total = mode == "pomodoro" ? Double((phase == "focus" ? p.pomoFocus : phase == "short" ? p.pomoShort : p.pomoLong) * 60) : Double(timerMinutes * 60); left = total
    }
    func toggle() {
        if running { running = false; t?.invalidate(); return }
        if mode != "stopwatch" && left <= 0 { reset() }
        running = true; t = Timer.scheduledTimer(withTimeInterval: 0.25, repeats: true) { _ in Task { @MainActor in self.step() } }
    }
    private func step() {
        if mode == "stopwatch" { up += 0.25; return }
        left -= 0.25; if left <= 0 { left = 0; running = false; t?.invalidate(); finish() }
    }
    private func finish() {
        let p = Settings.shared.p
        if mode == "pomodoro" {
            let focus = phase == "focus"; Notices.shared.add(focus ? "Focus session done" : "Break over", focus ? "Session \(session) of \(p.pomoTotal). Time for a break." : "Ready for the next focus session.", symbol: "timer")
            if focus { if session >= p.pomoTotal { Notices.shared.add("Pomodoro complete", "\(p.pomoTotal) sessions.", symbol: "checkmark.circle"); session = 1; phase = "focus" } else { phase = session % max(1, p.pomoEvery) == 0 ? "long" : "short" } } else { phase = "focus"; session += 1 }
            reset()
        } else { Notices.shared.add("Time is up", symbol: "timer") }
        NSSound(named: "Glass")?.play()
    }
}
struct TimerTab: View {
    @ObservedObject var t = Pomodoro.shared
    @ObservedObject var s = Settings.shared
    var body: some View {
        VStack(spacing: 8) {
            HStack { ForEach(["timer", "stopwatch", "pomodoro"], id: \.self) { m in Button { t.mode = m; t.reset() } label: { Text(L(m.capitalized)).font(.system(size: 11, weight: .semibold)).foregroundStyle(t.mode == m ? .black : .white).padding(.horizontal, 11).padding(.vertical, 5).background(Capsule().fill(t.mode == m ? Color.white : Look.card)) }.buttonStyle(.plain) } }
            Text(t.text).font(.system(size: 38, weight: .semibold, design: .monospaced))
            Text(t.mode == "pomodoro" ? "\(t.phase == "focus" ? L("Focus") : L("Break")) · \(t.session)/\(s.p.pomoTotal)" : (t.running ? L("Running") : "")).font(.system(size: 11.5)).foregroundStyle(Look.dim)
            ProgressView(value: t.mode == "stopwatch" ? t.up.truncatingRemainder(dividingBy: 60) / 60 : (t.total > 0 ? 1 - t.left / t.total : 0)).tint(Look.accent()).padding(.horizontal, 40)
            if t.mode == "timer" { HStack { ForEach([1, 5, 10, 15, 30, 60], id: \.self) { m in Pill("\(m)m") { t.timerMinutes = m; t.reset() } } } }
            if t.mode == "pomodoro" { HStack { Stepper("\(L("Focus")) \(s.p.pomoFocus)m", value: $s.p.pomoFocus, in: 5...90, step: 5).font(.system(size: 11)); Stepper("\(L("Break")) \(s.p.pomoShort)m", value: $s.p.pomoShort, in: 1...30).font(.system(size: 11)) }.onChange(of: s.p.pomoFocus) { _, _ in t.reset() } }
            HStack { Pill(t.running ? "Pause" : "Start", primary: true) { t.toggle() }; Pill("Reset") { t.reset() } }
        }
    }
}

// MARK: - Calendar (EventKit, plus .ics files and links)

@MainActor final class CalendarStore: ObservableObject {
    static let shared = CalendarStore()
    @Published var events: [(start: Date, end: Date, title: String, allDay: Bool)] = []
    private let store = EKEventStore()
    func refresh() async {
        var out: [(Date, Date, String, Bool)] = []; let from = Calendar.current.date(byAdding: .day, value: -35, to: Date())!, to = Calendar.current.date(byAdding: .day, value: 120, to: Date())!
        if (try? await store.requestFullAccessToEvents()) == true { for e in store.events(matching: store.predicateForEvents(withStart: from, end: to, calendars: nil)) { out.append((e.startDate, e.endDate, e.title ?? "Untitled event", e.isAllDay)) } }
        for src in Settings.shared.p.calendarSources {
            let text = src.hasPrefix("http") ? ((try? await URLSession.shared.data(from: URL(string: src)!)).flatMap { String(data: $0.0, encoding: .utf8) } ?? "") : ((try? String(contentsOfFile: src)) ?? "")
            out += Self.parseICS(text)
        }
        events = out.sorted { $0.0 < $1.0 }.map { (start: $0.0, end: $0.1, title: $0.2, allDay: $0.3) }
    }
    static func parseICS(_ text: String) -> [(Date, Date, String, Bool)] {
        var res: [(Date, Date, String, Bool)] = []; var ev: [String: String]?; var lines: [String] = []
        for raw in text.replacingOccurrences(of: "\r\n", with: "\n").split(separator: "\n", omittingEmptySubsequences: false) { if (raw.hasPrefix(" ") || raw.hasPrefix("\t")), !lines.isEmpty { lines[lines.count - 1] += raw.dropFirst() } else { lines.append(String(raw)) } }
        func date(_ s: String) -> (Date, Bool)? { let f = DateFormatter(); f.locale = Locale(identifier: "en_US_POSIX"); if s.count == 8 { f.dateFormat = "yyyyMMdd"; return f.date(from: s).map { ($0, true) } }; f.dateFormat = s.hasSuffix("Z") ? "yyyyMMdd'T'HHmmss'Z'" : "yyyyMMdd'T'HHmmss"; if s.hasSuffix("Z") { f.timeZone = TimeZone(identifier: "UTC") }; return f.date(from: s).map { ($0, false) } }
        for l in lines {
            if l == "BEGIN:VEVENT" { ev = [:]; continue }
            if l == "END:VEVENT", let e = ev { if let ds = e["DTSTART"], let (st, all) = date(ds) { let en = e["DTEND"].flatMap { date($0)?.0 } ?? st.addingTimeInterval(all ? 86400 : 3600); res.append((st, en, (e["SUMMARY"] ?? "Untitled event").replacingOccurrences(of: "\\,", with: ","), all)) }; ev = nil; continue }
            guard ev != nil, let c = l.firstIndex(of: ":") else { continue }; ev![String(l[..<c]).components(separatedBy: ";")[0]] = String(l[l.index(after: c)...])
        }
        return res
    }
}
struct CalendarTab: View {
    @ObservedObject var cal = CalendarStore.shared
    @State private var month = Calendar.current.date(from: Calendar.current.dateComponents([.year, .month], from: Date()))!
    @State private var day = Date()
    var body: some View {
        let c = Calendar.current; let days = c.range(of: .day, in: .month, for: month)!; let first = c.component(.weekday, from: month) - c.firstWeekday; let off = (first + 7) % 7
        VStack(spacing: 6) {
            HStack { Button { month = c.date(byAdding: .month, value: -1, to: month)! } label: { Image(systemName: "chevron.left") }.buttonStyle(.plain); Spacer(); Text(month, format: .dateTime.month(.wide).year()).font(.system(size: 13, weight: .semibold)); Spacer(); Button { month = c.date(byAdding: .month, value: 1, to: month)! } label: { Image(systemName: "chevron.right") }.buttonStyle(.plain); Pill("Today") { month = c.date(from: c.dateComponents([.year, .month], from: Date()))!; day = Date() } }
            LazyVGrid(columns: Array(repeating: GridItem(.flexible(), spacing: 2), count: 7), spacing: 2) {
                ForEach(0..<off, id: \.self) { _ in Color.clear.frame(height: 24) }
                ForEach(days, id: \.self) { d in
                    let date = c.date(byAdding: .day, value: d - 1, to: month)!; let sel = c.isDate(date, inSameDayAs: day); let has = cal.events.contains { c.isDate($0.start, inSameDayAs: date) }
                    Button { day = date } label: { VStack(spacing: 1) { Text("\(d)").font(.system(size: 11, weight: c.isDateInToday(date) ? .bold : .regular)); Circle().fill(has ? (sel ? Color.black : Color.blue) : .clear).frame(width: 3.5, height: 3.5) }.frame(maxWidth: .infinity, minHeight: 24).foregroundStyle(sel ? .black : .white).background(RoundedRectangle(cornerRadius: 7).fill(sel ? Color.white : (c.isDateInToday(date) ? Look.cardHi : .clear))) }.buttonStyle(.plain)
                }
            }
            ForEach(Array(cal.events.filter { c.isDate($0.start, inSameDayAs: day) }.prefix(3).enumerated()), id: \.offset) { _, e in HStack { Text(e.allDay ? L("All day") : e.start.formatted(date: .omitted, time: .shortened)).font(.system(size: 11, weight: .semibold, design: .monospaced)).foregroundStyle(.blue).frame(width: 70, alignment: .leading); Text(e.title).font(.system(size: 12)).lineLimit(1); Spacer() } }
        }.padding(.horizontal, 16).task { await cal.refresh() }
    }
}

// MARK: - Lyrics (lrclib.net, only when you allow it)

@MainActor final class LyricsModel: ObservableObject {
    static let shared = LyricsModel()
    @Published var lines: [(Double, String)] = []
    @Published var plain = ""; @Published var message = ""; var key = ""
    func load() async {
        let m = Media.shared; guard m.hasSession else { message = "Play a song to see its lyrics."; return }
        guard Settings.shared.p.lyricsOnline else { message = "Finding lyrics sends the song, artist and length to lrclib.net. Turn it on in Settings."; return }
        let k = m.title + m.artist; guard k != key else { return }; key = k; lines = []; plain = ""; message = "Loading lyrics…"
        var c = URLComponents(string: "https://lrclib.net/api/get")!; c.queryItems = [URLQueryItem(name: "track_name", value: m.title), URLQueryItem(name: "artist_name", value: m.artist), URLQueryItem(name: "duration", value: "\(Int(m.duration))")]
        guard let (d, _) = try? await URLSession.shared.data(from: c.url!), let j = try? JSONSerialization.jsonObject(with: d) as? [String: Any] else { message = "No matching lyrics for this recording."; return }
        if j["instrumental"] as? Bool == true { message = "Instrumental"; return }
        if let s = j["syncedLyrics"] as? String { lines = Self.parse(s) } else { plain = j["plainLyrics"] as? String ?? "" }
        message = lines.isEmpty && plain.isEmpty ? "No matching lyrics for this recording." : m.title + " · " + m.artist
    }
    static func parse(_ s: String) -> [(Double, String)] {
        let rx = try! NSRegularExpression(pattern: "\\[(\\d+):(\\d+(?:\\.\\d+)?)\\]([^\\n\\r]*)"); let ns = s as NSString
        return rx.matches(in: s, range: NSRange(location: 0, length: ns.length)).map { m in (Double(ns.substring(with: m.range(at: 1)))! * 60 + Double(ns.substring(with: m.range(at: 2)))!, ns.substring(with: m.range(at: 3)).trimmingCharacters(in: .whitespaces)) }
    }
}
struct LyricsTab: View {
    @ObservedObject var m = Media.shared
    @ObservedObject var l = LyricsModel.shared
    var body: some View {
        VStack(alignment: .leading) {
            Text(L(l.message)).font(.system(size: 11)).foregroundStyle(Look.dim)
            ScrollViewReader { r in ScrollView { VStack(alignment: .leading, spacing: 6) {
                if l.lines.isEmpty { Text(l.plain).font(.system(size: 13)) }
                let cur = l.lines.lastIndex { $0.0 <= m.position } ?? -1
                ForEach(Array(l.lines.enumerated()), id: \.offset) { i, ln in Text(ln.1.isEmpty ? "♪" : ln.1).font(.system(size: i == cur ? 17 : 14, weight: .semibold)).foregroundStyle(i == cur ? .white : Look.dim).id(i) }
            }.onChange(of: l.lines.lastIndex { $0.0 <= m.position }) { _, i in if let i { withAnimation { r.scrollTo(i, anchor: .center) } } } } }
        }.padding(.horizontal, 20).task(id: m.title) { await l.load() }
    }
}

// MARK: - Watch: read a part of the screen and say when it changes

@MainActor final class WatchModel: ObservableObject {
    static let shared = WatchModel()
    @Published var status = "Choose any part of the screen, like a progress bar or a build status. Perch reads it and tells you when it changes."
    @Published var watching = false
    @Published var cond = "changes"
    @Published var text = ""
    var rect: CGRect?; private var sig: [UInt8]?, timer: Timer?, changedAt = Date.distantPast, changed = false
    func choose() {
        Island.shared.pinned = false; Island.shared.close()
        DispatchQueue.main.asyncAfter(deadline: .now() + 0.3) { RegionPicker.pick { r in
            guard let r else { return }; self.rect = r; self.status = "Area chosen. Press Watch."
            if let cg = CGWindowListCreateImage(r, .optionOnScreenOnly, kCGNullWindowID, []) { self.baseline = NSImage(cgImage: cg, size: .zero) }
        } }
    }
    var baseline: NSImage?
    func toggle() {
        if watching { timer?.invalidate(); watching = false; status = "Stopped."; return }
        guard let b = baseline, rect != nil else { status = "Choose an area first."; return }
        sig = Self.signature(b); changed = false; watching = true; status = "Watching since " + Date().formatted(date: .omitted, time: .shortened)
        timer = Timer.scheduledTimer(withTimeInterval: 3, repeats: true) { _ in Task { @MainActor in self.poll() } }
    }
    func poll() {
        guard let r = rect, let cg = CGWindowListCreateImage(r, .optionOnScreenOnly, kCGNullWindowID, []) else { return }
        evaluate(NSImage(cgImage: cg, size: .zero))
    }
    func evaluate(_ img: NSImage) {
        let s = Self.signature(img); let diff = sig.map { zip(s, $0).reduce(0) { $0 + abs(Int($1.0) - Int($1.1)) } } ?? 0; let was = sig != nil; sig = s
        var hit: String?
        if was && diff > 24 * 24 * 2 { changed = true; changedAt = Date(); if cond == "changes" { hit = "It changed" } }
        if cond == "settles" && changed && Date().timeIntervalSince(changedAt) >= 30 { hit = "It stopped changing" }
        if cond == "contains" || cond == "reaches", let cg = img.cgImage(forProposedRect: nil, context: nil, hints: nil) {
            let req = VNRecognizeTextRequest(); try? VNImageRequestHandler(cgImage: cg).perform([req]); let t = (req.results ?? []).compactMap { $0.topCandidates(1).first?.string }.joined(separator: " ")
            if cond == "contains", !text.isEmpty, t.localizedCaseInsensitiveContains(text) { hit = "It shows " + text }
            if cond == "reaches", let target = Double(text), let m = t.split(whereSeparator: { !$0.isNumber && $0 != "." }).compactMap({ Double($0) }).first(where: { $0 >= target }) { hit = "It reached \(m)" }
        }
        if let h = hit { timer?.invalidate(); watching = false; status = h; Notices.shared.add("Watch: " + h, symbol: "eye"); NSSound(named: "Ping")?.play() }
    }
    static func signature(_ img: NSImage) -> [UInt8] {
        guard let cg = img.cgImage(forProposedRect: nil, context: nil, hints: nil) else { return [] }
        let w = 24, h = 24; var px = [UInt8](repeating: 0, count: w * h); let ctx = CGContext(data: &px, width: w, height: h, bitsPerComponent: 8, bytesPerRow: w, space: CGColorSpaceCreateDeviceGray(), bitmapInfo: CGImageAlphaInfo.none.rawValue)
        ctx?.draw(cg, in: CGRect(x: 0, y: 0, width: w, height: h)); return px
    }
}
struct WatchTab: View {
    @ObservedObject var w = WatchModel.shared
    var body: some View {
        VStack(alignment: .leading, spacing: 8) {
            Text(L(w.status)).font(.system(size: 11.5)).foregroundStyle(Look.dim)
            HStack { ForEach([("changes", "When it changes"), ("settles", "When it stops changing"), ("contains", "When it shows"), ("reaches", "When the number reaches")], id: \.0) { c in Button { w.cond = c.0 } label: { Text(L(c.1)).font(.system(size: 10)).foregroundStyle(w.cond == c.0 ? .black : .white).padding(.horizontal, 7).padding(.vertical, 4).background(Capsule().fill(w.cond == c.0 ? Color.white : Look.card)) }.buttonStyle(.plain) } }
            TextField(L("Text or number"), text: $w.text).textFieldStyle(.roundedBorder).frame(width: 200)
            HStack { Pill("Choose area", primary: true) { w.choose() }; Pill(w.watching ? "Stop watching" : "Watch") { w.toggle() } }
        }.padding(.horizontal, 16)
    }
}

// MARK: - AI agents: reads the session files Claude Code and Codex keep on this Mac (read-only; no prompts or replies are kept)

struct AgentUse { let at: Date; let agent: String; let model: String; let project: String; let inTok: Int; let outTok: Int; let cacheRead: Int; let cacheWrite: Int
    var total: Int { inTok + outTok + cacheRead + cacheWrite }
    var value: Double { let m = model.lowercased(); let p: (Double, Double, Double, Double)? = m.contains("opus") ? (15, 75, 1.5, 18.75) : m.contains("sonnet") ? (3, 15, 0.3, 3.75) : m.contains("haiku") ? (1, 5, 0.1, 1.25) : (m.contains("gpt") || m.contains("codex")) ? (1.25, 10, 0.125, 1.25) : nil; guard let p else { return 0 }; return (Double(inTok) * p.0 + Double(outTok) * p.1 + Double(cacheRead) * p.2 + Double(cacheWrite) * p.3) / 1e6 } }

@MainActor final class AgentModel: ObservableObject {
    static let shared = AgentModel()
    @Published var uses: [AgentUse] = []
    @Published var loaded = false; @Published var working = ""
    private var wasWorking = false, since = Date(), timer: Timer?
    private var home: URL { FileManager.default.homeDirectoryForCurrentUser }
    func start() { timer = Timer.scheduledTimer(withTimeInterval: 12, repeats: true) { _ in Task { @MainActor in self.watch() } } }
    func found(_ a: String) -> Bool { FileManager.default.fileExists(atPath: home.appendingPathComponent(a == "Claude" ? ".claude/projects" : ".codex/sessions").path) }
    func refresh() async {
        let s = Settings.shared.p; let h = home; let since = Date().addingTimeInterval(-31 * 86400)
        let out = await Task.detached { () -> [AgentUse] in
            var all: [AgentUse] = []
            func files(_ sub: String) -> [URL] { guard let en = FileManager.default.enumerator(at: h.appendingPathComponent(sub), includingPropertiesForKeys: [.contentModificationDateKey]) else { return [] }; return en.compactMap { $0 as? URL }.filter { $0.pathExtension == "jsonl" && ((try? $0.resourceValues(forKeys: [.contentModificationDateKey]).contentModificationDate) ?? .distantPast) > since } }
            if s.agentsClaude { for f in files(".claude/projects") { all += Self.readClaude(f) } }
            if s.agentsCodex { for f in files(".codex/sessions") { all += Self.readCodex(f) } }
            return all.filter { $0.at >= since }
        }.value
        uses = out; loaded = true
    }
    nonisolated static func lines(_ f: URL) -> [Data] { (try? Data(contentsOf: f))?.split(separator: UInt8(ascii: "\n")).map { Data($0) } ?? [] }
    nonisolated static func readClaude(_ f: URL) -> [AgentUse] {
        var res: [AgentUse] = [], ids = Set<String>(); let iso = ISO8601DateFormatter(); iso.formatOptions = [.withInternetDateTime, .withFractionalSeconds]
        for l in lines(f) {
            guard let s = String(data: l, encoding: .utf8), s.contains("\"usage\""), s.contains("\"assistant\""), let j = try? JSONSerialization.jsonObject(with: l) as? [String: Any], let msg = j["message"] as? [String: Any], let u = msg["usage"] as? [String: Any] else { continue }
            let id = (msg["id"] as? String ?? "") + ":" + (j["requestId"] as? String ?? ""); if id.count > 1 && !ids.insert(id).inserted { continue }
            let model = msg["model"] as? String ?? ""; if model == "<synthetic>" { continue }
            let at = (j["timestamp"] as? String).flatMap { iso.date(from: $0) ?? ISO8601DateFormatter().date(from: $0) } ?? Date()
            res.append(AgentUse(at: at, agent: "Claude", model: model, project: ((j["cwd"] as? String) ?? "").split(separator: "/").last.map(String.init) ?? "", inTok: u["input_tokens"] as? Int ?? 0, outTok: u["output_tokens"] as? Int ?? 0, cacheRead: u["cache_read_input_tokens"] as? Int ?? 0, cacheWrite: u["cache_creation_input_tokens"] as? Int ?? 0))
        }
        return res
    }
    nonisolated static func readCodex(_ f: URL) -> [AgentUse] {
        var res: [AgentUse] = [], model = "", cwd = ""; let iso = ISO8601DateFormatter(); iso.formatOptions = [.withInternetDateTime, .withFractionalSeconds]
        for l in lines(f) {
            guard let s = String(data: l, encoding: .utf8), s.contains("token_count") || s.contains("turn_context") || s.contains("session_meta"), let j = try? JSONSerialization.jsonObject(with: l) as? [String: Any], let p = j["payload"] as? [String: Any] else { continue }
            if let m = p["model"] as? String { model = m }; if let c = p["cwd"] as? String { cwd = c }
            guard let info = p["info"] as? [String: Any], let u = info["last_token_usage"] as? [String: Any] else { continue }
            let cached = u["cached_input_tokens"] as? Int ?? 0; let at = (j["timestamp"] as? String).flatMap { iso.date(from: $0) } ?? Date()
            res.append(AgentUse(at: at, agent: "Codex", model: model.isEmpty ? "codex" : model, project: cwd.split(separator: "/").last.map(String.init) ?? "", inTok: max(0, (u["input_tokens"] as? Int ?? 0) - cached), outTok: u["output_tokens"] as? Int ?? 0, cacheRead: cached, cacheWrite: 0))
        }
        return res
    }
    func live() -> (Bool, String) {
        let cut = Date().addingTimeInterval(-25)
        for (sub, name) in [(".claude/projects", "Claude Code"), (".codex/sessions", "Codex")] {
            guard let en = FileManager.default.enumerator(at: home.appendingPathComponent(sub), includingPropertiesForKeys: [.contentModificationDateKey]) else { continue }
            for case let u as URL in en where u.pathExtension == "jsonl" { if let d = try? u.resourceValues(forKeys: [.contentModificationDateKey]).contentModificationDate, d > cut { return (true, name) } }
        }
        return (false, "")
    }
    func watch() {
        let p = Settings.shared.p; guard p.agentsFinishAlert else { wasWorking = false; return }
        let (w, name) = live(); working = w ? name : ""
        if w && !wasWorking { since = Date() }
        if !w && wasWorking && Date().timeIntervalSince(since) >= 120 { Notices.shared.add(name.isEmpty ? "An agent finished" : "\(name) finished", symbol: "sparkle") }
        wasWorking = w
    }
}
struct AgentsTab: View {
    @ObservedObject var a = AgentModel.shared
    @State private var period = 1
    var body: some View {
        let from = Calendar.current.date(byAdding: .day, value: period == 0 ? 0 : period == 1 ? -6 : -29, to: Calendar.current.startOfDay(for: Date()))!; let rows = a.uses.filter { $0.at >= from }
        let k: (Int) -> String = { n in n >= 1_000_000 ? String(format: "%.1fM", Double(n) / 1e6) : n >= 1000 ? String(format: "%.1fK", Double(n) / 1e3) : "\(n)" }
        ScrollView { VStack(alignment: .leading, spacing: 8) {
            HStack { ForEach(Array(["Today", "7 days", "30 days"].enumerated()), id: \.offset) { i, t in Button { period = i } label: { Text(L(t)).font(.system(size: 11, weight: .semibold)).foregroundStyle(period == i ? .black : .white).padding(.horizontal, 10).padding(.vertical, 5).background(Capsule().fill(period == i ? Color.white : Look.card)) }.buttonStyle(.plain) }; Pill("Refresh") { Task { await a.refresh() } } }
            if !a.found("Claude") && !a.found("Codex") { Text(L("No usage from Claude Code or Codex yet. It appears here as soon as either works on this Mac.")).font(.system(size: 11.5)).foregroundStyle(Look.dim) }
            else if !a.loaded { Text(L("Reading usage…")).font(.system(size: 11.5)).foregroundStyle(Look.dim) }
            else {
                HStack { stat(k(rows.reduce(0) { $0 + $1.total }), "tokens"); stat(String(format: "$%.2f", rows.reduce(0) { $0 + $1.value }), "API value (estimate)"); let t = rows.reduce(0) { $0 + $1.total }; stat(t == 0 ? "0%" : "\(rows.reduce(0) { $0 + $1.cacheRead } * 100 / t)%", "from cache") }
                if Settings.shared.p.agentsDailyBudget > 0 && period == 0 { Text(String(format: "Budget: $%.2f left", max(0, Settings.shared.p.agentsDailyBudget - rows.reduce(0) { $0 + $1.value }))).font(.system(size: 11)).foregroundStyle(Look.dim) }
                Text(a.working.isEmpty ? L("No agent working right now") : L("Working now: ") + a.working).font(.system(size: 11)).foregroundStyle(a.working.isEmpty ? Look.dim : .green)
                Text("MODELS").font(.system(size: 9.5, design: .monospaced)).foregroundStyle(Look.dim)
                ForEach(Dictionary(grouping: rows, by: \.model).sorted { $0.value.reduce(0) { $0 + $1.total } > $1.value.reduce(0) { $0 + $1.total } }.prefix(4), id: \.key) { g in HStack { Text(g.key.isEmpty ? "unknown" : g.key).font(.system(size: 11.5)); Spacer(); Text(k(g.value.reduce(0) { $0 + $1.total })).font(.system(size: 11, design: .monospaced)); Text(String(format: "$%.2f", g.value.reduce(0) { $0 + $1.value })).font(.system(size: 11, design: .monospaced)).foregroundStyle(Look.dim) } }
                Text("PROJECTS").font(.system(size: 9.5, design: .monospaced)).foregroundStyle(Look.dim)
                ForEach(Dictionary(grouping: rows, by: \.project).sorted { $0.value.reduce(0) { $0 + $1.total } > $1.value.reduce(0) { $0 + $1.total } }.prefix(4), id: \.key) { g in HStack { Text(g.key.isEmpty ? "unknown" : g.key).font(.system(size: 11.5)); Spacer(); Text(k(g.value.reduce(0) { $0 + $1.total })).font(.system(size: 11, design: .monospaced)) } }
            }
        }.padding(.horizontal, 16) }.task { await a.refresh() }
    }
    func stat(_ v: String, _ l: String) -> some View { VStack(alignment: .leading) { Text(v).font(.system(size: 19, weight: .semibold)); Text(L(l)).font(.system(size: 10)).foregroundStyle(Look.dim) }.frame(maxWidth: .infinity, alignment: .leading) }
}

// MARK: - Assistant

struct ChatMsg: Identifiable { let id = UUID(); let role: String; let text: String }
struct AssistantTab: View {
    @ObservedObject var s = Settings.shared
    @State private var msgs: [ChatMsg] = []
    @State private var input = ""; @State private var busy = false; @State private var key = Secret.get("anthropic") ?? ""
    var body: some View {
        VStack(spacing: 6) {
            if key.isEmpty { VStack(spacing: 8) { Text(L("Paste your own Anthropic API key. It is kept in the macOS Keychain and sent only to Anthropic.")).font(.system(size: 11.5)).foregroundStyle(Look.dim); SecureField("sk-ant-…", text: $input).textFieldStyle(.roundedBorder); Pill("Save key", primary: true) { Secret.set("anthropic", input); key = input; input = "" } }.padding(.horizontal, 20) }
            else {
                ScrollView { VStack(alignment: .leading, spacing: 6) { ForEach(msgs) { m in Text(m.text).font(.system(size: 12)).padding(8).frame(maxWidth: .infinity, alignment: m.role == "user" ? .trailing : .leading).background(RoundedRectangle(cornerRadius: 10).fill(m.role == "user" ? Look.cardHi : Look.card)).textSelection(.enabled) }; if busy { ProgressView().controlSize(.small) } }.padding(.horizontal, 14) }
                HStack { TextField(L("Ask anything"), text: $input).textFieldStyle(.roundedBorder).onSubmit(send); Pill("Send", primary: true, action: send); Pill("Forget key") { Secret.set("anthropic", ""); key = ""; msgs = [] } }.padding(.horizontal, 14)
            }
        }
    }
    func send() {
        let t = input.trimmingCharacters(in: .whitespaces); guard !t.isEmpty, !busy else { return }; input = ""; msgs.append(ChatMsg(role: "user", text: t)); busy = true
        let hist = msgs.suffix(16).map { ["role": $0.role, "content": $0.text] }; let model = s.p.aiModel
        Task { let r = await Claude.ask(key: key, model: model, system: "You are Perch's assistant, living in a Mac menu-bar island. Be brief and practical.", messages: hist); msgs.append(ChatMsg(role: "assistant", text: r ?? "The request failed. Check your key and connection.")); busy = false }
    }
}

// MARK: - Manage: uninstall, devices, install (Homebrew), clean, pictures, processes, wallpaper

struct ManageTab: View {
    @State private var page = "Uninstall"
    @State private var apps: [URL] = []; @State private var q = ""; @State private var out = ""; @State private var armed = ""
    @State private var procs: [(String, String, Double)] = []
    @State private var sizes: [String: Int64] = [:]; @State private var picked = Set<String>()
    static let pages = ["Uninstall", "Devices", "Install", "Cleaner", "Pictures", "Processes", "Wallpaper"]
    static let caches: [(String, String, [String])] = [("User caches", "Caches Mac rebuilds on demand.", ["~/Library/Caches"]), ("Logs", "Old log files.", ["~/Library/Logs"]), ("Crash reports", "Reports kept after a crash.", ["~/Library/Logs/DiagnosticReports"]), ("Xcode derived data", "Build products Xcode recreates.", ["~/Library/Developer/Xcode/DerivedData"])]
    var body: some View {
        VStack(spacing: 8) {
            ScrollView(.horizontal, showsIndicators: false) { HStack { ForEach(Self.pages, id: \.self) { p in Button { page = p; q = ""; out = ""; load() } label: { Text(L(p)).font(.system(size: 11, weight: .semibold)).foregroundStyle(page == p ? .black : .white).padding(.horizontal, 10).padding(.vertical, 5).background(Capsule().fill(page == p ? Color.white : Look.card)) }.buttonStyle(.plain) } } }
            if ["Uninstall", "Install", "Processes"].contains(page) { TextField(L("Filter or search"), text: $q).textFieldStyle(.roundedBorder).onSubmit { load() } }
            ScrollView { VStack(spacing: 5) { content } }
        }.padding(.horizontal, 14).onAppear { load() }
    }
    @ViewBuilder var content: some View {
        switch page {
        case "Uninstall": ForEach(apps.filter { q.isEmpty || $0.lastPathComponent.localizedCaseInsensitiveContains(q) }, id: \.self) { u in row(u.deletingPathExtension().lastPathComponent, u.path, armed == u.path ? "Sure?" : "Uninstall") { if armed != u.path { armed = u.path } else { uninstall(u) } } }
        case "Devices": Text(out).font(.system(size: 11, design: .monospaced)).frame(maxWidth: .infinity, alignment: .leading)
        case "Install": Text(L("Search and install with Homebrew. Install and remove open Terminal so you can watch.")).font(.system(size: 11)).foregroundStyle(Look.dim)
            ForEach(out.split(separator: "\n").prefix(15).map(String.init), id: \.self) { n in row(n, "", "Install") { Tools.runTerminal("brew install \(n)") } }
        case "Cleaner": ForEach(Self.caches, id: \.0) { c in Button { if picked.contains(c.0) { picked.remove(c.0) } else { picked.insert(c.0) } } label: { GlassCard(pad: 8) { HStack { Image(systemName: picked.contains(c.0) ? "checkmark.square.fill" : "square"); VStack(alignment: .leading) { Label_(c.0, size: 12, bold: true); Label_(c.1 + "  " + ByteCountFormatter.string(fromByteCount: sizes[c.0] ?? 0, countStyle: .file), size: 10.5, dim: true) }; Spacer() } } }.buttonStyle(.plain) }
            Pill("Move selected to the Trash", primary: true) { clean() }
        case "Pictures": Text(L("Drop pictures here to convert them to JPEG, 1600 px wide, into a “Perch converted” folder. Originals are never touched.")).font(.system(size: 11)).foregroundStyle(Look.dim)
            GlassCard { Text(out.isEmpty ? L("Drop pictures here") : out).frame(maxWidth: .infinity, minHeight: 60) }.onDrop(of: [.fileURL], isTargeted: nil) { ps in for p in ps { _ = p.loadObject(ofClass: URL.self) { u, _ in if let u { Task { @MainActor in out = Self.convert(u) } } } }; return true }
        case "Processes": ForEach(procs.filter { q.isEmpty || $0.0.localizedCaseInsensitiveContains(q) || $0.1 == q }, id: \.1) { p in row(p.0, "pid \(p.1) · " + String(format: "%.0f MB", p.2), armed == p.1 ? "Sure?" : "End") { if armed != p.1 { armed = p.1 } else { Tools.run("/bin/kill", [p.1]); armed = ""; load() } } }
        default: Text(L("Pick a picture with the button, then it becomes the desktop picture on every display.")).font(.system(size: 11)).foregroundStyle(Look.dim); Pill("Choose picture…", primary: true) { let p = NSOpenPanel(); p.allowedContentTypes = [.image]; if p.runModal() == .OK, let u = p.url { for s in NSScreen.screens { try? NSWorkspace.shared.setDesktopImageURL(u, for: s, options: [:]) } } }
        }
    }
    func row(_ t: String, _ sub: String, _ b: String, _ act: @escaping () -> Void) -> some View { GlassCard(pad: 8) { HStack { VStack(alignment: .leading) { Label_(t, size: 12, bold: true); if !sub.isEmpty { Label_(sub, size: 10, dim: true) } }; Spacer(); Pill(b, action: act) } } }
    func load() {
        switch page {
        case "Uninstall": apps = ((try? FileManager.default.contentsOfDirectory(at: URL(fileURLWithPath: "/Applications"), includingPropertiesForKeys: nil)) ?? []).filter { $0.pathExtension == "app" }.sorted { $0.lastPathComponent < $1.lastPathComponent }
        case "Devices": Task.detached { let o = Tools.runOut("/usr/sbin/system_profiler", ["SPBluetoothDataType", "SPUSBDataType", "-detailLevel", "mini"]); await MainActor.run { out = o } }
        case "Install": if !q.isEmpty { let qq = q; Task.detached { let o = Tools.runOut("/usr/bin/env", ["brew", "search", qq]); await MainActor.run { out = o } } }
        case "Cleaner": for c in Self.caches { Task.detached { let n = c.2.reduce(Int64(0)) { $0 + Self.size(($1 as NSString).expandingTildeInPath) }; await MainActor.run { sizes[c.0] = n } } }
        case "Processes": Task.detached { let o = Tools.runOut("/bin/ps", ["-axo", "pid=,rss=,comm="]); let rows = o.split(separator: "\n").compactMap { l -> (String, String, Double)? in let f = l.split(separator: " ", maxSplits: 2, omittingEmptySubsequences: true); guard f.count == 3, let rss = Double(f[1]) else { return nil }; return ((f[2] as Substring).split(separator: "/").last.map(String.init) ?? "", String(f[0]), rss / 1024) }.sorted { $0.2 > $1.2 }.prefix(60); await MainActor.run { procs = Array(rows) } }
        default: break
        }
    }
    nonisolated static func size(_ p: String) -> Int64 { var n: Int64 = 0; if let en = FileManager.default.enumerator(at: URL(fileURLWithPath: p), includingPropertiesForKeys: [.fileSizeKey]) { for case let u as URL in en { n += Int64((try? u.resourceValues(forKeys: [.fileSizeKey]).fileSize) ?? 0) } }; return n }
    func uninstall(_ u: URL) {
        armed = ""; let id = Bundle(url: u)?.bundleIdentifier ?? ""
        let leftovers = ["~/Library/Application Support/", "~/Library/Caches/", "~/Library/Preferences/", "~/Library/Containers/", "~/Library/Saved Application State/"].map { ($0 as NSString).expandingTildeInPath + "/" + id }.filter { !id.isEmpty && FileManager.default.fileExists(atPath: $0) }
        NSWorkspace.shared.recycle([u]) { _, _ in }
        if !leftovers.isEmpty { let a = NSAlert(); a.messageText = L("Also move \(leftovers.count) leftover items to the Trash?"); a.informativeText = leftovers.joined(separator: "\n"); a.addButton(withTitle: L("Move them")); a.addButton(withTitle: L("Keep")); if a.runModal() == .alertFirstButtonReturn { NSWorkspace.shared.recycle(leftovers.map { URL(fileURLWithPath: $0) }) { _, _ in } } }
        Notices.shared.add("Moved to the Trash", u.lastPathComponent, symbol: "trash"); load()
    }
    func clean() { let paths = Self.caches.filter { picked.contains($0.0) }.flatMap { $0.2 }.map { ($0 as NSString).expandingTildeInPath }
        let urls = paths.flatMap { (try? FileManager.default.contentsOfDirectory(at: URL(fileURLWithPath: $0), includingPropertiesForKeys: nil)) ?? [] }
        NSWorkspace.shared.recycle(urls) { _, _ in Task { @MainActor in Notices.shared.add("Cleaned up", "\(urls.count) items moved to the Trash.", symbol: "sparkles") } }; picked = []; sizes = [:]; load() }
    nonisolated static func convert(_ u: URL) -> String {
        guard let img = NSImage(contentsOf: u), let cg = img.cgImage(forProposedRect: nil, context: nil, hints: nil) else { return "Not a picture." }
        let w = min(1600, cg.width), h = cg.height * w / cg.width; let ctx = CGContext(data: nil, width: w, height: h, bitsPerComponent: 8, bytesPerRow: 0, space: CGColorSpaceCreateDeviceRGB(), bitmapInfo: CGImageAlphaInfo.noneSkipLast.rawValue)!; ctx.draw(cg, in: CGRect(x: 0, y: 0, width: w, height: h))
        let dir = u.deletingLastPathComponent().appendingPathComponent("Perch converted"); try? FileManager.default.createDirectory(at: dir, withIntermediateDirectories: true)
        var f = dir.appendingPathComponent(u.deletingPathExtension().lastPathComponent + ".jpg"); var i = 2; while FileManager.default.fileExists(atPath: f.path) { f = dir.appendingPathComponent(u.deletingPathExtension().lastPathComponent + " (\(i)).jpg"); i += 1 }
        guard let out = ctx.makeImage(), let d = NSBitmapImageRep(cgImage: out).representation(using: .jpeg, properties: [.compressionFactor: 0.85]) else { return "Could not convert." }
        try? d.write(to: f); return "Saved " + f.lastPathComponent
    }
}

// MARK: - Command bar and radial menu

@MainActor final class CommandBar {
    static let shared = CommandBar()
    private static let settingsPages: [(String, String)] = [("Wi-Fi", "x-apple.systempreferences:com.apple.wifi-settings-extension"), ("Bluetooth", "x-apple.systempreferences:com.apple.BluetoothSettings"), ("Displays", "x-apple.systempreferences:com.apple.Displays-Settings.extension"), ("Sound", "x-apple.systempreferences:com.apple.Sound-Settings.extension"), ("Battery", "x-apple.systempreferences:com.apple.Battery-Settings.extension"), ("Privacy & Security", "x-apple.systempreferences:com.apple.settings.PrivacySecurity.extension"), ("Keyboard", "x-apple.systempreferences:com.apple.Keyboard-Settings.extension"), ("Trackpad", "x-apple.systempreferences:com.apple.Trackpad-Settings.extension"), ("Mouse", "x-apple.systempreferences:com.apple.Mouse-Settings.extension"), ("Software Update", "x-apple.systempreferences:com.apple.Software-Update-Settings.extension"), ("Login Items", "x-apple.systempreferences:com.apple.LoginItems-Settings.extension")]
    private static let emoji: [(String, String)] = [("😀", "smile happy"), ("😂", "laugh joy"), ("😍", "love eyes"), ("🤔", "think"), ("😢", "cry sad"), ("🎉", "party tada"), ("👍", "thumbs up yes"), ("👎", "thumbs down no"), ("🙏", "thanks please pray"), ("🔥", "fire hot"), ("✅", "check done"), ("❌", "cross no"), ("⚠️", "warning"), ("❤️", "heart love"), ("🚀", "rocket launch"), ("💡", "idea bulb"), ("☕", "coffee"), ("🍕", "pizza"), ("👀", "eyes look"), ("💯", "hundred")]
    func toggle() {
        if Pickers.pal.panel != nil { Pickers.pal.close(); return }
        Pickers.pal.show(placeholder: "Search apps, calculate, convert, find a clip…", footer: "Return runs the highlighted row · Esc closes") { q in self.items(q) }
    }
    func items(_ q: String) -> [PickItem] {
        let t = q.trimmingCharacters(in: .whitespaces); guard !t.isEmpty else { return [] }; var r: [PickItem] = []; let l = t.lowercased()
        if let a = Calc.eval(t) { r.append(PickItem(title: "= " + a, sub: "Return copies the answer", symbol: "equal") { NSPasteboard.general.clearContents(); NSPasteboard.general.setString(a, forType: .string) }) }
        let apps = (try? FileManager.default.contentsOfDirectory(atPath: "/Applications")) ?? []
        for a in apps.filter({ $0.lowercased().contains(l) && $0.hasSuffix(".app") }).prefix(5) { r.append(PickItem(title: String(a.dropLast(4)), sub: "App", symbol: "app") { NSWorkspace.shared.open(URL(fileURLWithPath: "/Applications/" + a)) }) }
        for w in NSWorkspace.shared.runningApplications.filter({ $0.activationPolicy == .regular && ($0.localizedName ?? "").lowercased().contains(l.replacingOccurrences(of: "quit ", with: "")) }).prefix(3) {
            if l.hasPrefix("quit ") { r.append(PickItem(title: "Quit " + (w.localizedName ?? ""), sub: "Asks it to close so it can save", symbol: "xmark") { w.terminate() }) } else { r.append(PickItem(title: w.localizedName ?? "", sub: "Running · Return switches to it", symbol: "macwindow") { w.activate() }) } }
        for c in Clip.shared.items.filter({ ($0.text ?? "").lowercased().contains(l) }).prefix(3) { r.append(PickItem(title: String((c.text ?? "").prefix(60)), sub: "From your clipboard history", symbol: "doc.on.clipboard") { Clip.shared.copyBack(c) }) }
        for p in Self.settingsPages.filter({ $0.0.lowercased().contains(l) }).prefix(3) { r.append(PickItem(title: p.0 + " settings", sub: "Opens System Settings", symbol: "gearshape") { NSWorkspace.shared.open(URL(string: p.1)!) }) }
        for e in Self.emoji.filter({ l.count >= 3 && $0.1.contains(l) }).prefix(4) { r.append(PickItem(title: e.0 + "  " + e.1.split(separator: " ")[0], sub: "Emoji · Return copies it", symbol: "face.smiling") { NSPasteboard.general.clearContents(); NSPasteboard.general.setString(e.0, forType: .string) }) }
        func sw(_ title: String, _ keys: String, _ a: @escaping () -> Void) { if l.count >= 3 && keys.contains(l) { r.append(PickItem(title: title, sub: "", symbol: "switch.2", run: a)) } }
        sw("Switch between light and dark", "dark mode light appearance", Tools.toggleDarkMode); sw("Take a screenshot", "screenshot capture", { Tools.screenshot() }); sw("Record the screen", "record screen video", Tools.toggleRecording); sw("Pick a colour", "colour color picker", Tools.pickColour); sw("Copy text from the screen", "ocr text qr", Tools.copyTextFromScreen)
        sw("Start cleaning mode", "cleaning keyboard wipe", { CleaningMode.shared.start() }); sw("Empty the Trash", "empty trash", Tools.emptyTrash); sw("Show or hide hidden files", "hidden files", Tools.toggleHiddenFiles); sw("Open the camera mirror", "camera mirror", CameraWindow.toggle)
        if l.count >= 3 { for (n, m) in [("Sleep", "pmset sleepnow"), ("Restart", "restart"), ("Shut down", "shutdown")] where n.lowercased().hasPrefix(l) { r.append(PickItem(title: n, sub: "Asks first", symbol: "power") { let a = NSAlert(); a.messageText = n + "?"; a.addButton(withTitle: n); a.addButton(withTitle: L("Cancel")); if a.runModal() == .alertFirstButtonReturn { Tools.osa("tell application \"System Events\" to \(n == "Sleep" ? "sleep" : n == "Restart" ? "restart" : "shut down")") } }) } }
        if l.count >= 3, let en = FileManager.default.enumerator(at: FileManager.default.homeDirectoryForCurrentUser.appendingPathComponent("Documents"), includingPropertiesForKeys: nil, options: [.skipsHiddenFiles]) { var n = 0; let start = Date(); for case let u as URL in en { if u.lastPathComponent.lowercased().contains(l) { r.append(PickItem(title: u.lastPathComponent, sub: u.path, symbol: "doc") { NSWorkspace.shared.open(u) }); n += 1 }; if n >= 4 || Date().timeIntervalSince(start) > 0.15 { break } } }
        r.append(PickItem(title: "Search the web for “\(t)”", sub: "Opens your browser. Nothing is sent until you press Return.", symbol: "magnifyingglass") { NSWorkspace.shared.open(URL(string: "https://duckduckgo.com/?q=" + (t.addingPercentEncoding(withAllowedCharacters: .urlQueryAllowed) ?? ""))!) })
        return r
    }
}

struct RadialView: View {
    let items: [(String, String, () -> Void)]; let close: () -> Void
    var body: some View {
        ZStack {
            Circle().fill(.ultraThinMaterial).frame(width: 90, height: 90).overlay(Text(L("Perch")).font(.system(size: 11, weight: .semibold)))
            ForEach(Array(items.enumerated()), id: \.offset) { i, it in
                let a = Double(i) / Double(items.count) * 2 * .pi - .pi / 2
                Button { close(); it.2() } label: { VStack(spacing: 2) { Image(systemName: it.1).font(.system(size: 17)); Text(L(it.0)).font(.system(size: 9, weight: .semibold)) }.frame(width: 62, height: 62).background(Circle().fill(.ultraThinMaterial)).overlay(Circle().strokeBorder(.white.opacity(0.25))) }.buttonStyle(.plain).offset(x: cos(a) * 110, y: sin(a) * 110)
            }
        }.frame(width: 320, height: 320)
    }
}
@MainActor enum Radial {
    static var panel: NSPanel?
    static var catalog: [(String, String, () -> Void)] { [("Screenshot", "camera.viewfinder", { Tools.screenshot() }), ("Play/Pause", "playpause.fill", { Media.shared.toggle() }), ("Mute", "speaker.slash", { Audio.muted.toggle() }), ("Colour", "eyedropper", Tools.pickColour), ("Lock", "lock", Tools.lockScreen), ("Command", "command", { CommandBar.shared.toggle() }), ("Clipboard", "doc.on.clipboard", Pickers.clipboard), ("Camera", "web.camera", CameraWindow.toggle)] }
    static func toggle() {
        if let p = panel { p.close(); panel = nil; return }
        let loc = NSEvent.mouseLocation; let p = NSPanel(contentRect: NSRect(x: loc.x - 160, y: loc.y - 160, width: 320, height: 320), styleMask: [.borderless, .nonactivatingPanel], backing: .buffered, defer: false)
        p.contentView = NSHostingView(rootView: RadialView(items: catalog) { p.close(); panel = nil }); p.isOpaque = false; p.backgroundColor = .clear; p.level = .popUpMenu; p.orderFrontRegardless(); panel = p
    }
}

// MARK: - Companion

@MainActor final class Mascot: ObservableObject {
    static let shared = Mascot()
    @Published var mood = "smile"
    @Published var hop = false
    var panel: NSPanel?
    func sync() {
        let p = Settings.shared.p; guard p.mascotOn else { panel?.close(); panel = nil; return }
        if panel == nil { let w = NSPanel(contentRect: NSRect(x: 0, y: 0, width: 60, height: 60), styleMask: [.borderless, .nonactivatingPanel], backing: .buffered, defer: false); w.contentView = NSHostingView(rootView: MascotView()); w.isOpaque = false; w.backgroundColor = .clear; w.level = .statusBar; w.ignoresMouseEvents = false; w.orderFrontRegardless(); panel = w }
        if let s = NSScreen.main { panel?.setFrameOrigin(NSPoint(x: p.mascotSide == "right" ? s.frame.midX + 118 : s.frame.midX - 118 - 60, y: s.frame.maxY - 60 - 2)) }
    }
    func react(_ n: Notice) { let t = (n.title + n.body).lowercased(); mood = t.contains("fail") || t.contains("low") || t.contains("full") ? "sad" : "happy"; hop.toggle(); DispatchQueue.main.asyncAfter(deadline: .now() + 2) { self.mood = "smile" } }
}
struct MascotView: View {
    @ObservedObject var m = Mascot.shared
    @ObservedObject var s = Settings.shared
    var body: some View {
        let shape: AnyShape = s.p.mascotShape == "pill" ? AnyShape(Capsule()) : s.p.mascotShape == "square" ? AnyShape(RoundedRectangle(cornerRadius: 12)) : AnyShape(Circle())
        ZStack { shape.fill(LinearGradient(colors: [.white, Color(white: 0.85)], startPoint: .top, endPoint: .bottom)).frame(width: s.p.mascotShape == "pill" ? 46 : 38, height: s.p.mascotShape == "pill" ? 32 : 38).shadow(radius: 3)
            HStack(spacing: 8) { Circle().fill(.black).frame(width: 5, height: 7); Circle().fill(.black).frame(width: 5, height: 7) }.offset(y: -4)
            Path { p in p.move(to: CGPoint(x: -7, y: 0)); p.addQuadCurve(to: CGPoint(x: 7, y: 0), control: CGPoint(x: 0, y: m.mood == "sad" ? -6 : m.mood == "happy" ? 12 : 7)) }.stroke(.black, lineWidth: 1.6).offset(x: 30, y: 36).frame(width: 60, height: 60)
        }.frame(width: 60, height: 60).offset(y: m.hop ? -6 : 0).animation(.interpolatingSpring(stiffness: 220, damping: 8), value: m.hop).onHover { if $0 { m.mood = "happy"; m.hop.toggle() } else { m.mood = "smile" } }
    }
}
