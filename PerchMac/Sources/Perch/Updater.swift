import SwiftUI
import AppKit
import CryptoKit

// Updates from the project's GitHub releases. The only thing sent is the request for the latest release.
// The download is checked against the checksum published with it before anything is replaced, and the old app is kept for rolling back.
@MainActor final class Updater: ObservableObject {
    static let shared = Updater()
    static let repo = "rayanno0sarif-ops/perch"
    struct Release { let version: String; let notes: String; let zip: URL; let sha: URL; let page: URL }
    @Published var available: Release?
    @Published var message = ""
    @Published var busy = false

    var current: String { Bundle.main.infoDictionary?["CFBundleShortVersionString"] as? String ?? "1.0.0" }
    private var support: URL { FileManager.default.urls(for: .applicationSupportDirectory, in: .userDomainMask)[0].appendingPathComponent("Perch") }
    private var previous: URL { support.appendingPathComponent("previous") }

    private static func newer(_ a: String, than b: String) -> Bool {
        let x = a.split(separator: ".").map { Int($0) ?? 0 }, y = b.split(separator: ".").map { Int($0) ?? 0 }
        for i in 0..<max(x.count, y.count) { let p = i < x.count ? x[i] : 0, q = i < y.count ? y[i] : 0; if p != q { return p > q } }
        return false
    }

    func check(manual: Bool) async {
        var req = URLRequest(url: URL(string: "https://api.github.com/repos/\(Self.repo)/releases/latest")!)
        req.setValue("Perch-updater", forHTTPHeaderField: "User-Agent"); req.setValue("application/vnd.github+json", forHTTPHeaderField: "Accept"); req.timeoutInterval = 20
        guard let (d, r) = try? await URLSession.shared.data(for: req), (r as? HTTPURLResponse)?.statusCode == 200,
              let j = try? JSONSerialization.jsonObject(with: d) as? [String: Any], let tag = j["tag_name"] as? String else { if manual { message = "Could not check. Is there a release yet, and are you online?" }; return }
        Settings.shared.p.updateLast = Date().formatted(date: .abbreviated, time: .shortened)
        let v = tag.trimmingCharacters(in: CharacterSet(charactersIn: "vV"))
        guard Self.newer(v, than: current) else { available = nil; if manual { message = "You are on the latest version (\(current))." }; return }
        let assets = j["assets"] as? [[String: Any]] ?? []
        func url(_ name: String) -> URL? { assets.first { ($0["name"] as? String)?.lowercased() == name.lowercased() }.flatMap { ($0["browser_download_url"] as? String).flatMap(URL.init) } }
        guard let zip = url("Perch-mac.zip"), let sha = url("Perch-mac.zip.sha256"), let page = (j["html_url"] as? String).flatMap(URL.init) else { if manual { message = "Version \(v) exists, but it has no Mac download with a checksum yet." }; return }
        available = Release(version: v, notes: j["body"] as? String ?? "", zip: zip, sha: sha, page: page)
        if manual || Settings.shared.p.updateSkipped != v { message = "Version \(v) is ready."; if !manual { Notices.shared.add("Perch \(v) is ready", "Open Settings, then Updates, to install it.", symbol: "arrow.down.circle") } }
    }

    func daily() {
        let p = Settings.shared.p; guard p.updateCheck else { return }
        Task { await check(manual: false) }
    }

    // Downloads, verifies and swaps in the new app, then relaunches.
    func install() async {
        guard let rel = available, !busy else { return }; busy = true; defer { busy = false }
        do {
            message = "Downloading \(rel.version)…"
            let (tmp, _) = try await URLSession.shared.download(from: rel.zip)
            message = "Checking the download…"
            let (shaData, _) = try await URLSession.shared.data(from: rel.sha)
            let expected = (String(data: shaData, encoding: .utf8) ?? "").split(whereSeparator: { $0 == " " || $0 == "\n" || $0 == "\t" }).first.map { $0.lowercased() } ?? ""
            let actual = SHA256.hash(data: try Data(contentsOf: tmp)).map { String(format: "%02x", $0) }.joined()
            guard expected == actual else { message = "The download did not match its checksum, so nothing was changed."; return }
            message = "Unpacking…"
            let work = FileManager.default.temporaryDirectory.appendingPathComponent("perch-update-\(UUID().uuidString.prefix(8))")
            try FileManager.default.createDirectory(at: work, withIntermediateDirectories: true)
            let zipPath = work.appendingPathComponent("update.zip"); try FileManager.default.moveItem(at: tmp, to: zipPath)
            let out = work.appendingPathComponent("new"); try FileManager.default.createDirectory(at: out, withIntermediateDirectories: true)
            guard Tools.run("/usr/bin/ditto", ["-xk", zipPath.path, out.path]) == 0, FileManager.default.fileExists(atPath: out.appendingPathComponent("Perch.app/Contents/MacOS/Perch").path) else { message = "The download does not contain Perch.app, so nothing was changed."; return }
            try FileManager.default.createDirectory(at: previous, withIntermediateDirectories: true)
            let backup = previous.appendingPathComponent("Perch-\(current).app"); let target = Bundle.main.bundlePath
            // a helper waits for Perch to quit, keeps the old app, moves the new one in, clears the download quarantine flag and relaunches
            let script = work.appendingPathComponent("update.sh")
            try """
            #!/bin/sh
            while kill -0 \(ProcessInfo.processInfo.processIdentifier) 2>/dev/null; do sleep 0.3; done
            rm -rf "\(backup.path)"
            mv "\(target)" "\(backup.path)" || exit 1
            mv "\(out.path)/Perch.app" "\(target)" || { mv "\(backup.path)" "\(target)"; exit 1; }
            xattr -dr com.apple.quarantine "\(target)" 2>/dev/null
            open "\(target)"
            """.write(to: script, atomically: true, encoding: .utf8)
            let p = Process(); p.executableURL = URL(fileURLWithPath: "/bin/sh"); p.arguments = [script.path]; try p.run()
            message = "Restarting…"; NSApp.terminate(nil)
        } catch { message = "The update failed: \(error.localizedDescription)" }
    }

    // Puts the app that was kept before the last update back.
    func rollback() {
        guard let last = ((try? FileManager.default.contentsOfDirectory(at: previous, includingPropertiesForKeys: [.contentModificationDateKey])) ?? []).filter({ $0.pathExtension == "app" })
            .max(by: { ((try? $0.resourceValues(forKeys: [.contentModificationDateKey]).contentModificationDate) ?? .distantPast) < ((try? $1.resourceValues(forKeys: [.contentModificationDateKey]).contentModificationDate) ?? .distantPast) }) else { message = "There is no earlier version kept."; return }
        let target = Bundle.main.bundlePath; let script = FileManager.default.temporaryDirectory.appendingPathComponent("perch-rollback.sh")
        try? """
        #!/bin/sh
        while kill -0 \(ProcessInfo.processInfo.processIdentifier) 2>/dev/null; do sleep 0.3; done
        rm -rf "\(target)" && cp -R "\(last.path)" "\(target)" && open "\(target)"
        """.write(to: script, atomically: true, encoding: .utf8)
        let p = Process(); p.executableURL = URL(fileURLWithPath: "/bin/sh"); p.arguments = [script.path]; try? p.run(); NSApp.terminate(nil)
    }
}

struct UpdatesPage: View {
    @ObservedObject var u = Updater.shared; @ObservedObject var s = Settings.shared
    var body: some View {
        Section_("Updates")
        Row("This version", "\(u.current). Updates come from the project's GitHub releases.") { EmptyView() }
        Row("Look for updates once a day", "Off by default. The only thing sent is the request for the latest release.") { Toggle("", isOn: $s.p.updateCheck).labelsHidden().toggleStyle(.switch) }
        Row("Check now", s.p.updateLast.isEmpty ? "Not checked yet." : "Last checked \(s.p.updateLast).") { Pill("Check", primary: true) { Task { await u.check(manual: true) } } }
        if !u.message.isEmpty { Row(u.message) { EmptyView() } }
        if let r = u.available {
            Row("Version \(r.version)", r.notes.isEmpty ? "No release notes." : String(r.notes.prefix(600))) { HStack { Pill("Update now", primary: true) { Task { await u.install() } }; Pill("Not this one") { s.p.updateSkipped = r.version }; Pill("Release page") { NSWorkspace.shared.open(r.page) } } }
            Row("How it works", "Perch downloads the release, checks it against its published checksum, quits, swaps the app, keeps your old one, and reopens. Nothing is replaced if the check fails.") { EmptyView() }
        }
        Row("Go back a version", "Puts the app kept before the last update back and reopens.") { Pill("Roll back") { u.rollback() } }
    }
}
