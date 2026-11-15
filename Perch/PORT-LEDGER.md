# Port ledger: vorssaint-utils → Perch

One row per feature in vorssaint's `FeatureCatalog.swift` (the `AppFeature` enum). Status words:

- **built**: written in Perch. "Run" says whether it was exercised on a real window or device; otherwise it is built and compiled only.
- **partial**: built, with what is missing named.
- **native**: Windows already does it; Perch links to the right Settings page.
- **impossible**: one-line technical reason.

Read for this: README, the landing page (vorssaint.com), `FeatureCatalog.swift`, `Defaults.swift` (every settings key), the English text of every
`*Strings.swift`, the CHANGELOG, and the Swift of the agent-usage readers. Clean-room C#: behaviour taken from the text, no code copied.
vorssaint is GPL-3.0-or-later; see the note at the end.

| # | vorssaint feature | Status | Where in Perch / run status |
|---|---|---|---|
| 1 | switcher | built | `Windows.cs` Switcher. Alt+Tab or a shortcut, live DWM previews, search, arrows, Delete closes, per-app hide, Alt+` same-program. **Run**: opened on two test windows, Tab moved the selection, Esc closed. Alt+Tab itself (the hook path) not injected, because it would have hit the foreground app |
| 2 | dockPreview | native | Taskbar hover previews; the switcher also previews |
| 3 | dockClick | native | Taskbar click minimises/cycles; Settings → Windows links to it |
| 4 | windowMaximizer | native | Maximise never creates another desktop |
| 5 | windowLayout | built | Halves, thirds, two-thirds, vertical thirds, quarter rows/columns, sixths, corners, centre, margin-maximise, whole display, next/previous display, restore, gaps, screen gap, side-repeat cycling, ignored programs, pointer-to-next-display, Alt-drag move/resize, edge snap on release. **Run**: layouts on a test window; Alt-drag moved a test window; edge-snap call executed. Drag-resize and 2-display moves not run (one monitor) |
| 6 | autoQuit | built | `Manage.cs` Housekeeping |
| 7 | spacesOrder | impossible | Windows virtual desktops are never reordered by recent use, so there is no order to pin |
| 8 | scrollInverter | built | `Input.cs`. Run |
| 9 | scrollHorizontal | built | `Input.cs`. Run |
| 10 | scrollZoom | native | Ctrl+wheel zooms in nearly every Windows app |
| 11 | focusFollowsMouse | built | Earlier pass; delay options not added |
| 12 | smoothScroll | built | `Input.cs`. Not run |
| 13 | linearScroll | built | `Input.cs`. Run |
| 14 | scrollSpeed | built | `Input.cs`. Not run |
| 15 | mouseAcceleration | built | `Input.cs`. Not run (already off on this PC) |
| 16 | mouseNavigation | native | Back/Forward buttons work in Explorer and browsers |
| 17 | mouseButtonShortcuts | built | Side and middle buttons → 26 actions incl. radial, snippets, switcher, screenshot. Run. Side-wheel directions not built (tilt wheel only reverses/scrolls) |
| 18 | middleClick | native | Precision touchpad taps are set in Settings → Touchpad |
| 19 | mouseClickDebounce | built | Run |
| 20 | keyboardDebounce | built | Run |
| 21 | textSnippets | built | Typed triggers, `{{date:fmt}}`/time zone/clipboard variables, folders, a searchable menu (`QuickPick`). Menu run in the build only; expansion run |
| 22 | superKey | partial | Caps Lock only (vorssaint also offers right-side modifiers); per-app exclusions use the shared ignore list |
| 23 | quitWindowProtection | partial | Alt+F4 / Ctrl+W hold or double press, per-program. The "extra modifier" mode is not built. Run |
| 24 | clipboardHistory | built | Search, pin, retention, encrypted persistence, files; history window with Ctrl+1–9; skip sensitive text; clear on lock/sleep; ignored programs. Not run |
| 25 | pastePlain | built | Earlier pass |
| 26 | finderCutPaste | native | Explorer has Ctrl+X/Ctrl+V |
| 27 | finderRename | native | F2 |
| 28 | shelf | built | Earlier pass |
| 29 | urlCleaner | built | Earlier pass |
| 30 | diskImageInstaller | built | `Manage2.cs` Disks page: mount ISO/IMG/VHD, run its installer, eject, optionally recycle the image; notice when one is mounted. Not run (would mount a real image) |
| 31 | mixer | partial | Per-app volume built earlier. Pin/reorder, hide inactive, per-app output routing not built; boost past 100% impossible without an audio driver |
| 32 | soundOutputSwitcher | built | |
| 33 | audioPriority | partial | Output priority built; input priority and a preferred microphone not built |
| 34 | micMute | built | |
| 35 | musicBlock | built | `Extras2.cs`: a listed music app that starts within seconds of a play key is closed. Not run |
| 36 | keepAwake | built | Timer, apps, charger, external display, pause while locked, pointer nudge. Not run. End-at-a-time picker not built |
| 37 | brightness | built | `Display.cs`: DDC/CI brightness and monitor-speaker volume/mute per display, dimming layer, brightness shortcuts. **Run**: the page read this PC's monitor (DDC and speakers). Setting values and the shortcuts not run |
| 38 | extraBrightness | impossible | Needs an XDR panel's HDR headroom; Windows has no equivalent |
| 39 | bluetoothSleep | built | |
| 40 | quickLauncher | built | Six launcher slots (Tools tab) + radial menu + snippet/clipboard pickers |
| 41 | quickToggles | built | Dark mode, desktop icons, hidden files, extensions, display off, screen saver, lock, empty bin, quit others |
| 42 | quitApps | built | Apps tab; "Quit others" tile/shortcut with a kept list |
| 43 | colorPicker | built | HEX, rgb(), hsl(), lower-case css, C# |
| 44 | screenOCR | built | Text and QR/bar codes (ZXing). QR not run |
| 45 | cleaningMode | built | Run |
| 46 | mediaTools | partial | Image convert/resize/watermark built; video compression, GIF-from-video and text-from-file not built (the recorder writes GIF and MP4) |
| 47 | cleaner | built | Categories, schedule (daily/weekly), old-screenshot rule. Schedule not run |
| 48 | uninstaller | built | |
| 49 | homebrew | built | winget search/install/remove/updates |
| 50 | appUpdates | built | winget updates list (System tab) |
| 51 | screenshot | built | `Capture.cs`: frozen-screen area/window/whole display, delay, pointer, file-name and subfolder patterns with `%#` numbering, editor (arrow, line, rectangle, ellipse, pen, highlighter, text, numbers, pixelate, solid block, spotlight, crop, backgrounds, watermark), copy/save/save as/pin/copy text/shelf, scrolling capture. **Run**: area capture → editor, arrow drawn. Scrolling capture not run. Expiring links not built (they need the vorssaint server) |
| 52 | cameraPreview | built | `Media.cs` floating mirror, camera switch, flip. Not run (opens the camera) |
| 53 | radialMenu | partial | Ring built (eight items, shortcut or mouse button). Profiles, submenus, icon/colour editor not built |
| 54 | scratchpad | partial | Notes tab (one pad, autosaves). Multiple tabs, Markdown preview not built |
| 55 | commandBar | partial | Apps, calculator, units, clipboard, open windows, quit, Windows settings pages, power, switches, emoji, colours, dates, files in Desktop/Documents/Pictures, usage ranking. Pins, aliases, per-row shortcuts, scripts, menu commands not built |
| 56 | screenRecorder | partial | `Media.cs`: area/window, countdown, pause, fps, quality, computer sound + microphone tracks, trim, speed, MP4 and GIF, copy. **Run**: recorded a test window, MP4 and GIF files written. Automatic click zooms, blur areas, text/image overlays, cursor styling, share links not built |
| 57 | wallpaper | built | Manage → Wallpaper. Run (rendered). Per-display wallpaper not built |
| 58 | killProcess | built | Manage → Processes: end, process tree, restart, copy path, grouping, filter. Run (rendered/listed) |
| 59 | portManager | built | System tab |
| 60 | notch | built | The island itself |
| 61 | notchCalendar | built | Calendar tab: month view, day list, Windows appointments, .ics files/links. Windows appointments may be refused to unpackaged apps; the .ics path is the reliable one. Countdown-in-the-pill not built. Rendered only |
| 62 | notchNotifications | built | Earlier pass |
| 63 | notchGestures | partial | Scroll-to-open exists (`ReverseScroll`); swipe-to-change-track not built |
| 64 | notchTimer | built | Timer tab: timer, stopwatch, Pomodoro (focus/short/long, sessions). Rendered only |
| 65 | notchAccessories | built | Warns once at 20% for paired Bluetooth devices. Not run (no Bluetooth device reported battery here) |
| 66 | notchLyrics | built | Lyrics tab (lrclib.net, opt-in; or an imported .lrc), synced highlight, offset. Not run |
| 67 | notchQueue | impossible | Windows media sessions do not expose an upcoming-songs list |
| 68 | notchLiveEqualizer | partial | Island bars move with playback state; they are not driven by the player's audio level |
| 69 | notchDownloads | built | Earlier pass (download notices) |
| 70 | notchAgents | partial | `Agents.cs` reads Claude Code and Codex session files (read-only): tokens, API value estimate, models, projects, 14-day bars, "working now", finished alert, daily budget. **Run**: read this PC's real Claude Code sessions. Plan-limit cards, resets, OpenCode and Copilot not built; prices are a small family table, so values are estimates |
| 71 | notchWatch | built | Watch tab: choose an area; changes / settles / shows text / number reaches. Rendered only |
| 72 | notchMascot | built | `Fun.cs` companion beside the island: shapes, colours, side, visits, hides when idle, reacts to notices, pettable. Not run |
| 73 | monitorCPU | partial | Load alerts with your own threshold; temperature not built (Windows exposes none without a driver) |
| 74 | monitorGPU | partial | GPU load on the Gaming strip; no history graph |
| 75 | monitorMemory | built | Memory alert with your threshold; process list by memory |
| 76 | monitorNetwork | built | Speed test, totals, IP |
| 77 | monitorDisk | built | Free-space alert with your threshold; disk list |
| 78 | monitorPower | partial | Battery alerts and time left; battery health/wear not built |
| 79 | connectedDevices | built | Manage → Devices |
| 80 | fanControl | impossible | Needs a vendor or kernel fan driver; Windows has no safe fan-write API |

Also ported from vorssaint's other pages: Features hub with bundles and a "what is listening" readout (Settings → Features), settings
export/import (earlier), launch at login, remove-Perch-data, languages by assistant translation (Settings → Language), window-layout ignored programs,
messaging-downloads cleanup and organiser for WhatsApp (Manage → Downloads), Command-bar usage ranking.

Not applicable to Windows and not built: menu-bar readouts and icon gallery (Perch has no menu bar), macOS permission pages (Windows has no equivalent
prompts), Sparkle-style self-update, Homebrew, disk-image `.dmg`, Stage Manager, Mission Control, App Exposé, AirPlay routing, XDR, fans.

## GPL note
vorssaint is GPL-3.0-or-later. This C# was written from the behaviour described in its README, strings and settings, not from its code. If Perch is ever
distributed, take licence advice; for private use nothing changes.
