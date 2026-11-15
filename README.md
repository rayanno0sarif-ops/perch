# Perch

**A notch at the top of your screen.** Music, notices, clipboard history, a file shelf, quick controls, screenshots, a window switcher and a few dozen small utilities, behind one glass pill. Nothing runs that you have not switched on, and nothing leaves your computer unless you ask it to.

Perch is an original app. It is inspired by the look of NotchPixel and by the feature set of [vorssaint-utils](https://github.com/vorssaint/vorssaint-utils), but contains no code from either.

| | Status |
|---|---|
| **Windows** (`Perch/`) | Built and used day to day. WPF on .NET 10. |
| **macOS** (`PerchMac/`) | Written on a Windows PC, **compiles in CI on a Mac runner, but has never been run**. Expect runtime bugs. |

Landing page: [`docs/index.html`](docs/index.html) (open it in a browser, or publish `docs/` with GitHub Pages).

---

## Contents

- [What it does](#what-it-does)
- [Install and build](#install-and-build)
- [Updates](#updates)
- [Everything is off until you switch it on](#everything-is-off-until-you-switch-it-on)
- [Privacy](#privacy)
- [Shortcuts](#shortcuts)
- [How the code is organised](#how-the-code-is-organised)
- [How it was tested](#how-it-was-tested)
- [What is not built](#what-is-not-built)
- [Licence and credits](#licence-and-credits)

---

## What it does

### The island
A floating glass pill at the top of the screen. Rest the pointer on it and it opens into a row of tabs. Drag it anywhere, or lock it in place. It thins out over maximised windows, steps aside in full-screen games (with an optional CPU/GPU/RAM strip), and shows a short message in its closed state for music, timers, volume, brightness, microphone mute, power and downloads. Glass clarity, colour, corner roundness, motion and size follow your Windows accessibility settings.

### Tabs
Music, Sites, Notices, Shelf, Clipboard, Widgets, Controls, Mixer, Tools, Apps, Utilities, Notes, Reminders, Call, System, Manage and Assistant are on by default. **Timer, Calendar, Lyrics, Watch and AI agents** start hidden so an update never grows the island by itself; turn them on under *Settings, Panels*.

| Tab | What is in it |
|---|---|
| Music | Cover, play/pause/skip, seek, volume for any player (Windows media sessions) |
| Sites | Your bookmarks for daily pages |
| Notices | Timers, screenshots and messages from your own scripts; Windows toast mirroring |
| Shelf | A temporary holding spot for files, dragged in and out |
| Clipboard | History with search and pins, optionally kept between sessions (encrypted for your account) |
| Widgets | Calendar strip, focus timer, forecast, CPU/memory/network |
| Controls / Mixer | Volume, mute, power plan, per-app volume, output switching |
| Tools | Screenshot, snip, record, camera, six launcher slots |
| Utilities | Keep awake, colour picker, copy text from the screen (OCR and QR), plain text, clean link, dark mode, desktop icons, hidden files, extensions, lock, empty bin, quit others, a calculator and unit converter |
| Manage | Uninstall (with leftovers to the Recycle Bin), devices and Bluetooth battery, winget install, cleaner (with a schedule), picture converter, processes (end, tree, restart), wallpaper, disk images, WhatsApp downloads |
| System | Ports, speed test, app updates, temp files, drives |
| Timer | Countdown, stopwatch, Pomodoro |
| Calendar | Month view and agenda from Windows' calendar and `.ics` files or links |
| Lyrics | Synced lyrics from lrclib.net (opt-in) or an imported `.lrc` |
| Watch | Pick part of the screen; get told when it changes, settles, shows some text or a number |
| AI agents | Token and API-value usage of Claude Code and Codex, read from their local session files |
| Assistant | Chat with Claude using your own API key; it can write small extensions you review before adding |

### Input and windows
All off by default, and the mouse hook steps aside while a fullscreen game is in front.

- **Mouse:** reverse the wheel or tilt wheel, scroll sideways while holding a key, scroll speed, same distance per notch, smooth scrolling, pointer acceleration off, side and middle button actions, extra-click filter.
- **Keyboard:** key debounce, Caps Lock as a super key (Ctrl+Alt+Shift) with a tap action, Alt+F4 / Ctrl+W protection (hold or press twice), typed snippets with `{{date:format}}`, `{{time}}`, `{{clipboard}}`, a searchable snippet menu.
- **Windows:** an Alt+Tab replacement with live previews and search; layouts (halves, thirds, two-thirds, quarters, sixths, corners, margin, next/previous display, put it back) with gaps; hold Alt and drag anywhere to move, add Shift to resize, let go at an edge to snap; mouse-button drag to switch virtual desktops; focus follows mouse.
- **Cleaning mode:** switches the keyboard and mouse off while you wipe them; hold Esc for three seconds to finish.
- **Capture:** frozen-screen area / window / display screenshots with an editor (arrow, line, rectangle, ellipse, pen, highlighter, text, numbers, pixelate, solid block, spotlight, crop, backgrounds, watermark), pin to screen, scrolling capture; a screen recorder with computer sound and microphone, trim, speed, MP4 and GIF; a floating camera mirror.
- **Display:** DDC/CI brightness and monitor-speaker volume per display, a dimming layer, brightness shortcuts.
- **Energy:** keep awake on a timer, while chosen apps run, while on the charger or with a second display, with an optional pointer nudge; Bluetooth off during sleep.
- **Alerts:** CPU, memory and disk warnings with your own thresholds.
- **Language:** the assistant translates the interface into 13 languages using your own key.
- **Features hub:** one-click bundles (Essentials, Windows, Battery and quiet, Everything) and a readout of what is listening.

Per-feature status, including what is only partly built, is in [`Perch/PORT-LEDGER.md`](Perch/PORT-LEDGER.md).

---

## Install and build

### Windows
Requires Windows 10 1903+ or Windows 11 and the [.NET 10 SDK](https://dotnet.microsoft.com/download).

```powershell
cd Perch
dotnet build -c Release
.\bin\Release\net10.0-windows10.0.19041.0\Perch.exe
```

Perch shows a tray icon (double-click for settings). Stop it before rebuilding: the running exe locks its own output file.

### macOS (compiles, never run)
Requires macOS 14+ and Xcode or the Command Line Tools.

```sh
cd PerchMac
./build.sh
open Perch.app
```

Grant Accessibility, Screen Recording, Camera, Calendars and Notifications as features ask. See [`PerchMac/README.md`](PerchMac/README.md).

---

## Updates

Both apps can update themselves from this repo's GitHub releases. It is **off by default** (*Settings, Updates*): switch on "Look for updates once a day", or press *Check now*.

- Only the request for the latest release is sent. Nothing about you or your computer.
- *Update now* downloads the release, checks it against the checksum published beside it, closes Perch, swaps the files, keeps your old version, and starts again. If the checksum does not match, nothing is replaced.
- *Roll back* puts the kept version back.
- If Perch is installed in a protected folder (Program Files), run it as administrator once or update by hand.

Publishing a release: push a tag, for example `git tag v1.1.0 && git push origin v1.1.0`. The `release` workflow builds `Perch-win-x64.zip` and `Perch-mac.zip` with `.sha256` files and attaches them. The updater looks for exactly those names. The `build` workflow compiles both apps on every push, which is also the first real compile of the Mac version.

---

## Everything is off until you switch it on

Input hooks are the part of Perch that touches the most, so they are strict:

- Nothing is hooked on a fresh install. The keyboard and mouse hooks start only when a tweak that needs them is on, and stop when the last one is turned off.
- The hooks run on their own thread. Perch never sends input from inside a hook (that stalls Windows); a worker thread sends it, marked so Perch ignores its own events.
- Settings, Features shows whether the mouse or keyboard is currently being watched.

---

## Privacy

- Settings live in `%AppData%\Perch` (Windows) or user defaults (Mac). Clipboard history is in memory unless you turn on persistence, then it is encrypted for your account.
- No analytics, no account, no telemetry.
- The network is touched only by things you can see: weather (the city and its coordinates go to Open-Meteo), the speed test, winget, optional lyrics (lrclib.net), the assistant and translation (Anthropic, with your own key, interface words only for translation), and `.ics` links you add.
- AI-agent usage reads `~/.claude` and `~/.codex` session files **read-only** and keeps only dates, model names, project folders and token counts, never prompts or replies.
- Anything destructive (uninstall leftovers, cleaner, WhatsApp cleanup) goes to the Recycle Bin or asks first.

---

## Shortcuts

Set them under *Settings, Keys*. Defaults on Windows: open/close the island `Ctrl+Alt+I`, command bar `Ctrl+Alt+K`, radial menu `Ctrl+Alt+R`. Layouts, screenshot, record, switcher, snippets, clipboard window, camera, cleaning mode, brightness and more are unset until you bind them. Windows registers them with `RegisterHotKey`, so the system consumes the press and it never reaches the app in front.

---

## How the code is organised

### Windows (`Perch/`)

| File | Contents |
|---|---|
| `Core.cs` | `Settings` (a `partial` class; each module adds its own fields), the `UI` helpers, `Toggle`, `Segmented`, `MiniSlider` |
| `Island.cs`, `Glass.cs` | The window, hotkeys, acrylic backdrop |
| `Tabs.cs`, `Extras.cs`, `Apps.cs`, `System.cs`, `Chat.cs` | The tabs, command bar, assistant and extensions |
| `Input.cs`, `Windows.cs` | Low-level hooks, cleaning mode, the switcher, layouts, drag gestures |
| `Manage.cs`, `Manage2.cs` | Manage tab and scheduled jobs |
| `Capture.cs`, `Media.cs` | Screenshot and editor, scrolling capture, recorder, GIF writer, camera |
| `Island2.cs`, `Agents.cs`, `Fun.cs`, `Display.cs`, `CommandBarExtras.cs`, `Extras2.cs` | Timer, calendar, lyrics, watch, agents, language, companion, display control, extra command-bar sources, snippets menu |
| `SettingsWindow.cs`, `SettingsExt.cs` | Settings pages |

### macOS (`PerchMac/Sources/Perch`)
`Store`, `Services`, `Island`, `Tabs`, `Tools`, `Input`, `Extras`, `Region`, `SettingsWindow`, `main`. See its README.

---

## How it was tested

On Windows, features were exercised on windows the tests created themselves, with a Python harness that injects input and captures only the test window's rectangle. Verified that way: input tweaks, cleaning mode, window layouts, the switcher, Alt-drag, screenshot editor, recorder (MP4 and GIF written), and the new settings pages rendered to images. Items that would touch your real apps or devices (uninstalling, winget installs, camera, Bluetooth, mounting images) are built but were not run. [`Perch/PORT-LEDGER.md`](Perch/PORT-LEDGER.md) marks which is which.

The macOS version compiles in the `build` workflow on a macOS runner, but nobody has launched it yet.

---

## What is not built

Per-app volume boost past 100% and fan control (need drivers), XDR brightness, fixed Space order and the upcoming-songs queue (Windows exposes none), recorder auto-zoom and blur, radial-menu profiles, a multi-pad scratchpad, video compression, and most of the Mac extras listed in its README.

---

## Licence and credits

No licence file is included yet; choose one before publishing. vorssaint-utils is GPL-3.0-or-later. Perch's code was written from the behaviour described in its README, strings and settings, not from its source. If you distribute Perch, take advice on whether that is enough or whether Perch should be GPL too. NotchPixel was observed only through its public UI. All names and trademarks belong to their owners.
