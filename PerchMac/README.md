# Perch for Mac

A native Swift/SwiftUI/AppKit version of Perch (macOS 14+). **It was written on a Windows PC with no Swift toolchain, so it has never been compiled or run.**
Expect a round of compile fixes the first time you build it. Nothing here has been tested.

## Build

```sh
cd PerchMac
./build.sh          # swift build -c release, wraps Perch.app with Info.plist, ad-hoc signs it
open Perch.app
```

Grant, when asked: **Accessibility** (input tweaks, window layout, switcher), **Screen Recording** (screenshots, Watch, switcher previews),
**Camera**, **Calendars**, **Notifications**, **Automation** (Music/Spotify, Finder).

## What is in it (all in `Sources/Perch`)

| File | Contents |
|---|---|
| `Store.swift` | Settings (one Codable value), notices, Keychain + AES-GCM helpers, language translation through the assistant, Anthropic client, clipboard history (search, pin, retention, encrypted persistence, sensitive-text filter, concealed-item skip, clear on lock), shelf |
| `Services.swift` | Now playing (MediaRemote, falling back to Spotify/Music over AppleScript), volume/mute/mic mute/output switching (CoreAudio), brightness (DisplayServices + dimming layer), CPU/memory/network/battery, alerts with your thresholds, weather (Open-Meteo), keep awake (power assertions, rules, pointer nudge), global shortcuts (Carbon) |
| `Island.swift` | The floating island/notch panel, hover open, drag, lock, full-screen hide, collapsed activity view, tab bar, the 19 tabs' registry |
| `Tabs.swift` | Music, Sites, Notices, Shelf, Clipboard, Widgets, Controls, Tools, Utilities (+calculator/unit converter), Notes, Reminders, System (ports, speed test, Homebrew updates, drives) |
| `Tools.swift` | Screenshot (system selector) + editor (arrow, rectangle, pen, text, numbers, solid block, background, copy/save/pin/copy text), recording, text and QR from the screen (Vision), colour picker, link cleaner, plain-text paste, dark mode, desktop icons, hidden files, quit others, camera mirror |
| `Input.swift` | Event tap: wheel reverse/sideways/speed/linear, click filter, key debounce, Caps Lock super key, ⌘Q/⌘W protection, snippet expansion, ⌥-drag move/resize with edge snap; cleaning mode; focus follows mouse; window layouts through Accessibility (halves, thirds, two-thirds, quarters, sixths, corners, margin, displays, restore); window switcher; snippet and clipboard palettes |
| `Extras.swift` | Timer/stopwatch/Pomodoro, Calendar (EventKit + .ics), Lyrics (lrclib, opt-in), Watch, AI agents (reads `~/.claude` and `~/.codex`, read-only), Assistant, Manage (uninstall with leftovers, devices, Homebrew install, cleaner, picture converter, processes, wallpaper), command bar, radial menu, companion |
| `Region.swift` | Drag-a-rectangle picker used by Watch |
| `SettingsWindow.swift` | Settings: General, Appearance, Panels (bundles), Input, Windows, Keys, Clipboard, Capture, Energy, Alerts, More tabs, Assistant, Language, About |

## Not built, with the reason

- **Per-app volume mixer**: macOS gives no per-app volume without an audio driver.
- **Extra brightness (XDR)**, **fan control**: need private hardware access.
- **DDC brightness/volume for external monitors**: dimming layer only; DDC on Apple Silicon needs private IOAVService calls.
- **Screen recorder editor** (trim, zoom, blur, GIF): recording uses the system recorder; no editor.
- **Video compression, GIF from video**, **disk-image (.dmg) installer**, **messaging-downloads organiser**, **cleaner schedule**, **music-app blocker**,
  **audio/input device priority**, **radial profiles**, **multi-pad scratchpad**: not written yet.
- **Bluetooth off during sleep**: wired in code (needs `brew install blueutil`) but has no settings switch yet.
- **Windows-only things that do not exist on a Mac**: Win32 taskbar/virtual-desktop gestures, Snap Layouts.

## Licence note

Perch is original code. vorssaint-utils (GPL-3.0) was read for behaviour only.
