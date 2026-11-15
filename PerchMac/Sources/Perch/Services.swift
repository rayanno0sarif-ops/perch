import SwiftUI
import AppKit
import CoreAudio
import AudioToolbox
import IOKit
import IOKit.ps
import IOKit.pwr_mgt
import Carbon.HIToolbox

// MARK: - Now playing (MediaRemote when macOS allows it, otherwise Spotify / Music over AppleScript)

@MainActor final class Media: ObservableObject {
    static let shared = Media()
    @Published var title = "", artist = "", album = "", source = ""
    @Published var art: NSImage?
    @Published var playing = false
    @Published var position = 0.0, duration = 0.0
    var hasSession: Bool { !title.isEmpty }
    private var timer: Timer?
    private typealias InfoFn = @convention(c) (DispatchQueue, @escaping ([String: Any]) -> Void) -> Void
    private typealias CmdFn = @convention(c) (UInt32, CFDictionary?) -> Bool
    private let bundle = CFBundleCreate(kCFAllocatorDefault, NSURL(fileURLWithPath: "/System/Library/PrivateFrameworks/MediaRemote.framework"))

    func start() { timer = Timer.scheduledTimer(withTimeInterval: 1, repeats: true) { _ in Task { @MainActor in self.refresh() } }; refresh() }
    func refresh() {
        if let b = bundle, let p = CFBundleGetFunctionPointerForName(b, "MRMediaRemoteGetNowPlayingInfo" as CFString) {
            let f = unsafeBitCast(p, to: InfoFn.self)
            f(DispatchQueue.main) { [weak self] info in
                Task { @MainActor in
                    guard let self else { return }
                    let t = info["kMRMediaRemoteNowPlayingInfoTitle"] as? String ?? ""
                    if t.isEmpty { self.scriptRefresh(); return }
                    self.title = t; self.artist = info["kMRMediaRemoteNowPlayingInfoArtist"] as? String ?? ""; self.album = info["kMRMediaRemoteNowPlayingInfoAlbum"] as? String ?? ""
                    self.duration = info["kMRMediaRemoteNowPlayingInfoDuration"] as? Double ?? 0
                    self.position = info["kMRMediaRemoteNowPlayingInfoElapsedTime"] as? Double ?? 0
                    self.playing = (info["kMRMediaRemoteNowPlayingInfoPlaybackRate"] as? Double ?? 0) > 0
                    if let d = info["kMRMediaRemoteNowPlayingInfoArtworkData"] as? Data { self.art = NSImage(data: d) }
                    self.source = "System"
                }
            }
        } else { scriptRefresh() }
    }
    private func scriptRefresh() {
        for app in ["Spotify", "Music"] where NSRunningApplication.runningApplications(withBundleIdentifier: app == "Spotify" ? "com.spotify.client" : "com.apple.Music").first != nil {
            let src = "tell application \"\(app)\" to if player state is not stopped then return (name of current track) & \"|\" & (artist of current track) & \"|\" & (album of current track) & \"|\" & (player state as string) & \"|\" & (player position as string) & \"|\" & (duration of current track as string)"
            if let r = NSAppleScript(source: src)?.executeAndReturnError(nil).stringValue {
                let f = r.components(separatedBy: "|"); if f.count >= 6 {
                    title = f[0]; artist = f[1]; album = f[2]; playing = f[3].contains("playing"); position = Double(f[4].replacingOccurrences(of: ",", with: ".")) ?? 0
                    var d = Double(f[5].replacingOccurrences(of: ",", with: ".")) ?? 0; if app == "Spotify" { d /= 1000 }; duration = d; source = app; return
                }
            }
        }
        title = ""; artist = ""; playing = false
    }
    private func send(_ cmd: UInt32, script: String) {
        if let b = bundle, let p = CFBundleGetFunctionPointerForName(b, "MRMediaRemoteSendCommand" as CFString) { _ = unsafeBitCast(p, to: CmdFn.self)(cmd, nil) }
        if source == "Spotify" || source == "Music" { _ = NSAppleScript(source: "tell application \"\(source)\" to \(script)")?.executeAndReturnError(nil) }
        DispatchQueue.main.asyncAfter(deadline: .now() + 0.4) { self.refresh() }
    }
    func toggle() { send(2, script: "playpause") }
    func next() { send(4, script: "next track") }
    func previous() { send(5, script: "previous track") }
}

// MARK: - Sound, microphone

enum Audio {
    private static func defaultDevice(input: Bool) -> AudioDeviceID {
        var id = AudioDeviceID(0); var size = UInt32(MemoryLayout<AudioDeviceID>.size)
        var a = AudioObjectPropertyAddress(mSelector: input ? kAudioHardwarePropertyDefaultInputDevice : kAudioHardwarePropertyDefaultOutputDevice, mScope: kAudioObjectPropertyScopeGlobal, mElement: kAudioObjectPropertyElementMain)
        AudioObjectGetPropertyData(AudioObjectID(kAudioObjectSystemObject), &a, 0, nil, &size, &id); return id
    }
    private static func volAddr(input: Bool) -> AudioObjectPropertyAddress {
        AudioObjectPropertyAddress(mSelector: kAudioHardwareServiceDeviceProperty_VirtualMainVolume, mScope: input ? kAudioObjectPropertyScopeInput : kAudioObjectPropertyScopeOutput, mElement: kAudioObjectPropertyElementMain)
    }
    static func volume(input: Bool = false) -> Float {
        var v: Float32 = 0; var size = UInt32(MemoryLayout<Float32>.size); var a = volAddr(input: input)
        AudioObjectGetPropertyData(defaultDevice(input: input), &a, 0, nil, &size, &v); return v
    }
    static func setVolume(_ v: Float, input: Bool = false) {
        var x = max(0, min(1, v)); var a = volAddr(input: input)
        AudioObjectSetPropertyData(defaultDevice(input: input), &a, 0, nil, UInt32(MemoryLayout<Float32>.size), &x)
    }
    static var muted: Bool {
        get { var m: UInt32 = 0; var size = UInt32(4); var a = AudioObjectPropertyAddress(mSelector: kAudioDevicePropertyMute, mScope: kAudioObjectPropertyScopeOutput, mElement: kAudioObjectPropertyElementMain); AudioObjectGetPropertyData(defaultDevice(input: false), &a, 0, nil, &size, &m); return m != 0 }
        set { var m: UInt32 = newValue ? 1 : 0; var a = AudioObjectPropertyAddress(mSelector: kAudioDevicePropertyMute, mScope: kAudioObjectPropertyScopeOutput, mElement: kAudioObjectPropertyElementMain); AudioObjectSetPropertyData(defaultDevice(input: false), &a, 0, nil, 4, &m) }
    }
    // The microphone is muted by taking its input level to zero and remembering the old level.
    private static var micSaved: Float = 0.7
    static var micMuted: Bool { volume(input: true) < 0.01 }
    static func toggleMic() {
        if micMuted { setVolume(micSaved, input: true); Task { @MainActor in Notices.shared.add("Microphone on", symbol: "mic") } }
        else { micSaved = max(0.1, volume(input: true)); setVolume(0, input: true); Task { @MainActor in Notices.shared.add("Microphone muted", symbol: "mic.slash") } }
    }
    static func outputs() -> [(id: AudioDeviceID, name: String)] {
        var size = UInt32(0); var a = AudioObjectPropertyAddress(mSelector: kAudioHardwarePropertyDevices, mScope: kAudioObjectPropertyScopeGlobal, mElement: kAudioObjectPropertyElementMain)
        AudioObjectGetPropertyDataSize(AudioObjectID(kAudioObjectSystemObject), &a, 0, nil, &size)
        var ids = [AudioDeviceID](repeating: 0, count: Int(size) / MemoryLayout<AudioDeviceID>.size)
        AudioObjectGetPropertyData(AudioObjectID(kAudioObjectSystemObject), &a, 0, nil, &size, &ids)
        return ids.compactMap { id in
            var sz = UInt32(0); var oa = AudioObjectPropertyAddress(mSelector: kAudioDevicePropertyStreams, mScope: kAudioObjectPropertyScopeOutput, mElement: kAudioObjectPropertyElementMain)
            AudioObjectGetPropertyDataSize(id, &oa, 0, nil, &sz); if sz == 0 { return nil }
            var name: CFString = "" as CFString; var ns = UInt32(MemoryLayout<CFString>.size)
            var na = AudioObjectPropertyAddress(mSelector: kAudioObjectPropertyName, mScope: kAudioObjectPropertyScopeGlobal, mElement: kAudioObjectPropertyElementMain)
            AudioObjectGetPropertyData(id, &na, 0, nil, &ns, &name); return (id, name as String)
        }
    }
    static func setOutput(_ id: AudioDeviceID) { var d = id; var a = AudioObjectPropertyAddress(mSelector: kAudioHardwarePropertyDefaultOutputDevice, mScope: kAudioObjectPropertyScopeGlobal, mElement: kAudioObjectPropertyElementMain); AudioObjectSetPropertyData(AudioObjectID(kAudioObjectSystemObject), &a, 0, nil, UInt32(MemoryLayout<AudioDeviceID>.size), &d) }
    static func nextOutput() {
        let o = outputs(); guard o.count > 1 else { return }
        let cur = defaultDevice(input: false); let at = o.firstIndex { $0.id == cur } ?? 0; let n = o[(at + 1) % o.count]; setOutput(n.id)
        Task { @MainActor in Notices.shared.add("Sound output changed", n.name, symbol: "speaker.wave.2") }
    }
}

// MARK: - Brightness (built-in display through DisplayServices) and a dimming layer for any display

@MainActor final class Brightness: ObservableObject {
    static let shared = Brightness()
    @Published var level: Float = 1
    private typealias GetFn = @convention(c) (CGDirectDisplayID, UnsafeMutablePointer<Float>) -> Int32
    private typealias SetFn = @convention(c) (CGDirectDisplayID, Float) -> Int32
    private let handle = dlopen("/System/Library/PrivateFrameworks/DisplayServices.framework/DisplayServices", RTLD_NOW)
    private var layers: [CGDirectDisplayID: NSWindow] = [:]
    var builtIn: Bool { handle != nil }
    func read() { guard let h = handle, let p = dlsym(h, "DisplayServicesGetBrightness") else { return }; var v: Float = 1; if unsafeBitCast(p, to: GetFn.self)(CGMainDisplayID(), &v) == 0 { level = v } }
    func set(_ v: Float) {
        level = max(0.02, min(1, v))
        if let h = handle, let p = dlsym(h, "DisplayServicesSetBrightness") { _ = unsafeBitCast(p, to: SetFn.self)(CGMainDisplayID(), level) }
        else { dim(display: CGMainDisplayID(), left: Double(level)) }
    }
    func nudge(_ d: Float) { read(); set(level + d); Island.shared.flash("sun.max", "Brightness \(Int(level * 100))%") }
    // A dark, click-through window over one display, for monitors that cannot be adjusted.
    func dim(display: CGDirectDisplayID, left: Double) {
        guard let scr = NSScreen.screens.first(where: { ($0.deviceDescription[NSDeviceDescriptionKey("NSScreenNumber")] as? CGDirectDisplayID) == display }) else { return }
        if left >= 0.99 { layers[display]?.close(); layers[display] = nil; return }
        let w = layers[display] ?? { let w = NSWindow(contentRect: scr.frame, styleMask: .borderless, backing: .buffered, defer: false); w.isOpaque = false; w.backgroundColor = .black; w.ignoresMouseEvents = true; w.level = .screenSaver; w.collectionBehavior = [.canJoinAllSpaces, .stationary]; w.hasShadow = false; w.orderFrontRegardless(); layers[display] = w; return w }()
        w.setFrame(scr.frame, display: true); w.alphaValue = CGFloat(1 - max(0.1, left))
    }
}

// MARK: - System stats

@MainActor final class Stats: ObservableObject {
    static let shared = Stats()
    @Published var cpu = 0.0, mem = 0.0, down = 0.0, up = 0.0, battery = -1, charging = false, diskFree = 0.0
    private var prevTicks: [UInt32] = [0, 0, 0, 0], prevRx: UInt64 = 0, prevTx: UInt64 = 0, timer: Timer?
    func start() { timer = Timer.scheduledTimer(withTimeInterval: 2, repeats: true) { _ in Task { @MainActor in self.sample() } }; sample() }
    func sample() {
        var info = host_cpu_load_info(); var count = mach_msg_type_number_t(MemoryLayout<host_cpu_load_info>.size / MemoryLayout<integer_t>.size)
        let r = withUnsafeMutablePointer(to: &info) { $0.withMemoryRebound(to: integer_t.self, capacity: Int(count)) { host_statistics(mach_host_self(), HOST_CPU_LOAD_INFO, $0, &count) } }
        if r == KERN_SUCCESS {
            let t = [info.cpu_ticks.0, info.cpu_ticks.1, info.cpu_ticks.2, info.cpu_ticks.3]; let d = zip(t, prevTicks).map { Double($0 &- $1) }; let tot = d.reduce(0, +)
            if tot > 0 { cpu = 1 - d[2] / tot }; prevTicks = t
        }
        var vm = vm_statistics64(); var vc = mach_msg_type_number_t(MemoryLayout<vm_statistics64>.size / MemoryLayout<integer_t>.size)
        let vr = withUnsafeMutablePointer(to: &vm) { $0.withMemoryRebound(to: integer_t.self, capacity: Int(vc)) { host_statistics64(mach_host_self(), HOST_VM_INFO64, $0, &vc) } }
        if vr == KERN_SUCCESS { let page = Double(vm_kernel_page_size); let used = Double(vm.active_count + vm.wire_count + vm.compressor_page_count) * page; mem = used / Double(ProcessInfo.processInfo.physicalMemory) }
        var rx: UInt64 = 0, tx: UInt64 = 0; var ifa: UnsafeMutablePointer<ifaddrs>?
        if getifaddrs(&ifa) == 0 { var p = ifa; while let c = p { if let a = c.pointee.ifa_addr, a.pointee.sa_family == UInt8(AF_LINK), let data = c.pointee.ifa_data?.assumingMemoryBound(to: if_data.self), !String(cString: c.pointee.ifa_name).hasPrefix("lo") { rx += UInt64(data.pointee.ifi_ibytes); tx += UInt64(data.pointee.ifi_obytes) }; p = c.pointee.ifa_next }; freeifaddrs(ifa) }
        if prevRx > 0 { down = Double(rx &- prevRx) / 2; up = Double(tx &- prevTx) / 2 }; prevRx = rx; prevTx = tx
        if let snap = IOPSCopyPowerSourcesInfo()?.takeRetainedValue(), let list = IOPSCopyPowerSourcesList(snap)?.takeRetainedValue() as? [CFTypeRef], let first = list.first,
           let d = IOPSGetPowerSourceDescription(snap, first)?.takeUnretainedValue() as? [String: Any] {
            battery = d[kIOPSCurrentCapacityKey] as? Int ?? -1; charging = (d[kIOPSPowerSourceStateKey] as? String) == kIOPSACPowerValue
        }
        if let v = try? URL(fileURLWithPath: "/").resourceValues(forKeys: [.volumeAvailableCapacityForImportantUsageKey, .volumeTotalCapacityKey]), let a = v.volumeAvailableCapacityForImportantUsage, let t = v.volumeTotalCapacity, t > 0 { diskFree = Double(a) / Double(t) }
        Alerts.check(self)
    }
    static func rate(_ b: Double) -> String { b > 1_048_576 ? String(format: "%.1f MB/s", b / 1_048_576) : String(format: "%.0f KB/s", b / 1024) }
}

// MARK: - Alerts with your own thresholds

@MainActor enum Alerts {
    private static var last: [String: Date] = [:]; private static var hot = 0
    private static func cool(_ k: String) -> Bool { if let t = last[k], Date().timeIntervalSince(t) < Double(max(1, Settings.shared.p.alertCooldown)) * 60 { return false }; last[k] = Date(); return true }
    static func check(_ s: Stats) {
        let p = Settings.shared.p
        if p.alertCpu { hot = s.cpu * 100 >= Double(p.alertCpuPct) ? hot + 1 : 0; if hot >= 30 && cool("cpu") { Notices.shared.add("The processor has been busy", String(format: "%.0f%% for a minute.", s.cpu * 100), symbol: "cpu") } }
        if p.alertMem && s.mem * 100 >= Double(p.alertMemPct) && cool("mem") { Notices.shared.add("Memory is nearly full", String(format: "%.0f%% in use.", s.mem * 100), symbol: "memorychip") }
        if p.alertDisk && s.diskFree * 100 <= Double(p.alertDiskPct) && cool("disk") { Notices.shared.add("The disk is nearly full", String(format: "%.0f%% free.", s.diskFree * 100), symbol: "internaldrive") }
        if s.battery >= 0 && s.battery <= 20 && !s.charging && cool("bat") { Notices.shared.add("Battery is low", "\(s.battery)% left.", symbol: "battery.25") }
    }
}

// MARK: - Weather (Open-Meteo: no key, only the city name and coordinates are sent)

@MainActor final class Weather: ObservableObject {
    static let shared = Weather()
    @Published var temp = "", summary = "", place = ""
    func refresh() async {
        var p = Settings.shared.p
        if p.lat == 0 && p.lon == 0, !p.city.isEmpty, let u = URL(string: "https://geocoding-api.open-meteo.com/v1/search?count=1&name=" + (p.city.addingPercentEncoding(withAllowedCharacters: .urlQueryAllowed) ?? "")),
           let (d, _) = try? await URLSession.shared.data(from: u), let j = try? JSONSerialization.jsonObject(with: d) as? [String: Any], let r = (j["results"] as? [[String: Any]])?.first { p.lat = r["latitude"] as? Double ?? 0; p.lon = r["longitude"] as? Double ?? 0; place = r["name"] as? String ?? ""; Settings.shared.p = p }
        guard p.lat != 0 || p.lon != 0, let u = URL(string: "https://api.open-meteo.com/v1/forecast?latitude=\(p.lat)&longitude=\(p.lon)&current=temperature_2m,weather_code&temperature_unit=\(p.fahrenheit ? "fahrenheit" : "celsius")"),
              let (d, _) = try? await URLSession.shared.data(from: u), let j = try? JSONSerialization.jsonObject(with: d) as? [String: Any], let c = j["current"] as? [String: Any] else { return }
        let t = c["temperature_2m"] as? Double ?? 0; temp = String(format: "%.0f°", t)
        let code = c["weather_code"] as? Int ?? 0; summary = code == 0 ? "Clear" : code < 4 ? "Cloudy" : code < 50 ? "Fog" : code < 70 ? "Rain" : code < 80 ? "Snow" : "Storms"
    }
}

// MARK: - Keep awake

@MainActor final class KeepAwake: ObservableObject {
    static let shared = KeepAwake()
    @Published var until: Date?; @Published var on = false
    private var assertion = IOPMAssertionID(0), ruleOn = false, timer: Timer?
    private var jiggleAt = Date.distantPast
    func start() { timer = Timer.scheduledTimer(withTimeInterval: 5, repeats: true) { _ in Task { @MainActor in self.tick() } } }
    private func hold(_ yes: Bool) {
        if yes && assertion == 0 { IOPMAssertionCreateWithName(kIOPMAssertionTypePreventUserIdleDisplaySleep as CFString, IOPMAssertionLevel(kIOPMAssertionLevelOn), "Perch keep awake" as CFString, &assertion) }
        else if !yes && assertion != 0 { IOPMAssertionRelease(assertion); assertion = 0 }
    }
    func cycle() {
        let steps: [TimeInterval?] = [1800, 3600, 7200, nil]; let cur = steps.firstIndex { a in on && ((a == nil && until == nil) || (a != nil && until != nil && abs(until!.timeIntervalSinceNow - a!) < 3000)) }
        let next = on ? (cur ?? -1) + 1 : 0
        if next >= steps.count { on = false; until = nil; hold(ruleOn) } else { on = true; until = steps[next].map { Date().addingTimeInterval($0) }; hold(true) }
        Notices.shared.add(on ? "Keeping this Mac awake" : "Keep awake is off", until.map { "Until " + $0.formatted(date: .omitted, time: .shortened) } ?? (on ? "Until you turn it off." : ""), symbol: "moon.zzz")
    }
    private func tick() {
        if on, let u = until, Date() > u { on = false; until = nil }
        let p = Settings.shared.p; var want = false
        if p.awakeCharger { want = Stats.shared.charging }
        if !want && !p.awakeApps.isEmpty { want = NSWorkspace.shared.runningApplications.contains { a in p.awakeApps.contains(a.bundleIdentifier ?? "") } }
        if !want && p.awakeExternal { want = NSScreen.screens.count > 1 }
        ruleOn = want; hold(on || want)
        if p.awakeJiggle && (on || want) && Date().timeIntervalSince(jiggleAt) > 60 { jiggleAt = Date(); if let e = CGEvent(mouseEventSource: nil, mouseType: .mouseMoved, mouseCursorPosition: CGEvent(source: nil)?.location ?? .zero, mouseButton: .left) { e.setIntegerValueField(.eventSourceUserData, value: Input.mark); e.post(tap: .cghidEventTap) } }
        // quit programs whose last window closed
        for id in p.autoQuitApps { Input.autoQuit(bundleID: id) }
    }
}

// MARK: - Global shortcuts (Carbon hot keys: the system consumes the press, so it never reaches the app in front)

@MainActor final class HotKeys {
    static let shared = HotKeys()
    private var refs: [EventHotKeyRef?] = [], installed = false
    var actions: [UInt32: () -> Void] = [:]
    private static let codes: [String: Int] = ["a": 0, "s": 1, "d": 2, "f": 3, "h": 4, "g": 5, "z": 6, "x": 7, "c": 8, "v": 9, "b": 11, "q": 12, "w": 13, "e": 14, "r": 15, "y": 16, "t": 17, "1": 18, "2": 19, "3": 20, "4": 21, "6": 22, "5": 23, "9": 25, "7": 26, "8": 28, "0": 29, "o": 31, "u": 32, "i": 34, "p": 35, "l": 37, "j": 38, "k": 40, "n": 45, "m": 46, "return": 36, "tab": 48, "space": 49, "left": 123, "right": 124, "down": 125, "up": 126, "c2": 8]
    func register(_ map: [String: () -> Void]) {
        for r in refs { if let r { UnregisterEventHotKey(r) } }; refs.removeAll(); actions.removeAll()
        if !installed {
            var spec = EventTypeSpec(eventClass: OSType(kEventClassKeyboard), eventKind: UInt32(kEventHotKeyPressed))
            InstallEventHandler(GetApplicationEventTarget(), { _, ev, _ -> OSStatus in
                var hk = EventHotKeyID(); GetEventParameter(ev, EventParamName(kEventParamDirectObject), EventParamType(typeEventHotKeyID), nil, MemoryLayout<EventHotKeyID>.size, nil, &hk)
                DispatchQueue.main.async { MainActor.assumeIsolated { HotKeys.shared.actions[hk.id]?() } }; return noErr
            }, 1, &spec, nil, nil); installed = true
        }
        var n: UInt32 = 1
        for (name, act) in map {
            guard let combo = Settings.shared.p.hotkeys[name], !combo.isEmpty else { continue }
            let parts = combo.lowercased().split(separator: "+").map(String.init); var mods: UInt32 = 0; var code: Int?
            for p in parts { switch p { case "ctrl": mods |= UInt32(controlKey); case "opt", "alt": mods |= UInt32(optionKey); case "shift": mods |= UInt32(shiftKey); case "cmd": mods |= UInt32(cmdKey); default: code = Self.codes[p] } }
            guard let c = code else { continue }
            var ref: EventHotKeyRef?; RegisterEventHotKey(UInt32(c), mods, EventHotKeyID(signature: OSType(0x50455243), id: n), GetApplicationEventTarget(), 0, &ref)
            refs.append(ref); actions[n] = act; n += 1
        }
    }
}
