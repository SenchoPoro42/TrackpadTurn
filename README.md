# TrackpadTurn

**A Roblox-style trackpad camera for World of Warcraft on macOS and Windows.** Swipe two fingers to turn. No holding a click while you drag.

- **Swipe to turn.** A two-finger swipe over the world turns your camera and character, like holding the right mouse button. Press W and you run where you look.
- **Tilt.** Up/down swipes tilt the camera. They are damped (0.77×, as in Roblox), so a sideways swipe stays level.
- **Pinch** zooms in and out.
- **Run.** Hold a two-finger click to run forward, like holding both mouse buttons. Swipe while holding to steer. If you prefer, make each hold toggle autorun instead (Settings → AddOns → TrackpadTurn).
- **Lists still scroll.** Swipes over chat, bags or the quest log scroll them as usual.
- **No glide.** The camera stops the moment your fingers lift.

It works with any Mac trackpad (built-in or Magic Trackpad) and with any Windows laptop touchpad that is a Precision Touchpad. A real mouse and its wheel behave exactly as before.

## Install on a Mac

1. **Get the app.** Download `TrackpadTurn-<version>.zip` from [Releases](../../releases/latest), unzip it, move **TrackpadTurn** to Applications, and open it. A hand-on-trackpad icon appears in the menu bar.
2. **Allow Accessibility.** macOS asks the first time. If you miss the prompt, go to **System Settings → Privacy & Security → Accessibility** and switch on TrackpadTurn. The app notices by itself.
3. **Install the addon.** Click the menu bar icon, then click **Install** next to your game flavor (Classic Era, Retail, and so on).
4. **Restart WoW once,** so it finds the new addon. If WoW lists it as out of date, tick **Load out of date AddOns**.

That's all. Each app update also updates the addon copies it installed; type `/reload` in game to load the new version.

## Install on Windows

Needs Windows 10 or 11 and a **Precision Touchpad** (most laptops since 2016; Settings → Bluetooth & devices → Touchpad says "Your PC has a precision touchpad").

1. **Get the app.** From [Releases](../../releases), download `TrackpadTurn-<version>-windows-x64.exe` (or the `-windows-arm64.exe` on a Snapdragon laptop) and put it somewhere permanent, such as `Documents`. It is one file; nothing else to install. The Windows helper is in beta, so its releases are marked pre-release.
2. **Run it.** The app isn't code-signed, so Windows SmartScreen shows "Windows protected your PC" the first time. Click **More info → Run anyway**. A hand icon appears in the system tray (click **^** next to the clock if it is hidden), and the settings window opens.
3. **Install the addon.** Click **Install** next to your game flavor. If WoW isn't in `Program Files`, click **Choose WoW folder…** first. If Windows asks for administrator approval to write to WoW's folder, say yes.
4. **Restart WoW once,** so it finds the new addon. If WoW lists it as out of date, tick **Load out of date AddOns**.
5. **Optional:** for swipes while you hold a movement key, set **Settings → Bluetooth & devices → Touchpad → Taps → Touchpad sensitivity** to **Most sensitive**. Otherwise Windows ignores the touchpad for a moment after each key press. The settings window shows this tip when it applies.

TrackpadTurn does not start with Windows unless you tick **Start at login** in its settings window. Starting the exe again opens the settings window of the copy that already runs.

## Use

| Gesture | Over the 3D world | Over a frame (chat, bags, quest log) |
|---|---|---|
| Two-finger swipe left/right | Turn camera and character | Nothing |
| Two-finger swipe up/down | Tilt camera | Scroll the frame |
| Pinch | Zoom | Zoom |
| Two-finger click, held | Run forward, or toggle autorun (swipe to steer) | Same |
| Two-finger click, quick | Right-click (interact, loot) | Right-click |

**Menu bar panel (Mac) / settings window (Windows, click the tray icon):** turn speed, zoom speed, tilt on/off, pinch on/off, hold-to-run on/off, reverse each axis, open at login, and an on/off switch. WoW's own mouse sensitivity settings also scale turning.

**In game:**
- `/tpt` shows whether signals from the app arrive, and how many swipes turned the camera.
- `/tpt debug` prints why each swipe turned or was left alone, for example "cursor over ChatFrame1".
- `/tpt settings` opens the addon's settings (Settings → AddOns → TrackpadTurn). Its one setting: "Two-finger hold toggles autorun".

## Troubleshooting

- **`/tpt` says "no signal" (Mac).** Check three things: the app is running, its switch is on, and it is allowed under Accessibility. The panel's status line tells you which one is missing.
- **`/tpt` says "no signal" (Windows).** Check that the tray icon is there and the settings window says "Ready". If it says WoW runs as administrator, run TrackpadTurn as administrator too (right-click → Run as administrator): Windows blocks input from a normal app to an administrator one.
- **Something odd on Windows.** Tick **Write a diagnostic log** in the settings window, try again, and send `%LOCALAPPDATA%\TrackpadTurn\Logs\TrackpadTurn.log` (**Open log folder**).
- **Swipes do nothing while chat is open.** That is on purpose: while you type, keys go to the chat box.
- **Turning is too fast or too slow.** Use **Turn speed** in the panel.
- **Swipe direction feels backwards.** Use **Reverse left/right** or **Reverse up/down**.

## Privacy and safety

- **What it sees:** only scroll, pinch and right-click events (on Windows also the touchpad's finger positions), never your key presses. It makes no network connections and keeps no record of your input. The Windows diagnostic log, when you switch it on, records gesture events only, never keys.
- **What it sends:** while WoW is in front, it sends signal key presses (Mac: F16–F19; Windows: Pause, Insert, Home, End) and mouse movement to the game, only while you swipe, pinch or hold a two-finger click.
- **Game rules:** it is one-to-one input remapping, in the same family as BetterTouchTool or Steam Input. One finger gesture becomes one continuous camera motion, with no timers, repeats or automation. Blizzard does not approve third-party tools, so use it at your own risk.
- **Open source:** the whole app and addon are in this repository, so you can check all of the above yourself.

## Compatibility

- **macOS:** 14 Sonoma or newer, on Apple Silicon and Intel.
- **Windows:** 10 or 11 (x64 or Arm64) with a Precision Touchpad. Built and checked in CI; not yet tested in game.
- **Tested:** WoW Classic Era 1.15.9 with a Magic Trackpad.
- **Should work, untested:** WoW Forever 1.60.1, Retail, Anniversary and Mists Classic. The addon uses only APIs that every current client has, and its TOC lists each client's Interface number. Reports are welcome.

## Known limits

- **Signal keys are taken.** Mac: F16–F19. Windows: Pause, Insert, Home and End (WoW on Windows ignores F13–F24). If you bind something to them yourself, the addon's binding wins. Home/End still zoom, as in WoW's default bindings; Insert no longer pitches the camera.
- **Custom wheel bindings can fire.** If your plain mouse wheel is bound to something other than camera zoom, swipes may trigger that binding. The same goes for Shift- or Ctrl-wheel bindings when you swipe while holding that modifier.
- **Holding runs anywhere.** Holding a two-finger click runs you (or toggles autorun) even over a frame. Quick clicks are unaffected.
- **Press first, then swipe (Mac).** macOS reports no click for a press made in the middle of a two-finger scroll, so to run you must press before you start swiping.
- **No glide in lists.** Chat, the quest log and other lists stop scrolling the moment your fingers lift. The app cannot tell a world swipe from a list swipe, so it drops the glide for both.
- **Magic Mouse turns too (Mac).** It scrolls the same way a trackpad does, so its surface also turns the camera.
- **Mouse wheel right after a swipe (Windows).** Windows can't say whether a wheel event came from the touchpad or a mouse, so for a moment after your fingers lift (until the touchpad's glide stops, at most 2 s) wheel events are dropped.
- **Turn speed on Windows** follows Windows' pointer speed and "Enhance pointer precision" unless WoW reads raw mouse input (`/console rawMouseEnable 1`).
- **Some laptops click left with two fingers (Windows).** Then holding a two-finger click runs forward but steers only the camera, like holding the left mouse button.

## Uninstall

1. Quit TrackpadTurn (Mac: from its panel; Windows: tray icon → Quit) and delete the app. On Windows, untick **Start at login** first if you ticked it.
2. Delete the `TrackpadTurn` folder from each game flavor's `Interface/AddOns` folder.
3. Optionally: on a Mac, remove it from **System Settings → Privacy & Security → Accessibility**; on Windows, delete `%APPDATA%\TrackpadTurn` and `%LOCALAPPDATA%\TrackpadTurn`.

## How it works

The job needs two parts, because neither can do it alone:

| Part | Knows | Does |
|---|---|---|
| **TrackpadTurn helper** (Mac menu bar / Windows tray) | What your fingers do | Holds the **swipe** key down for the length of a swipe, taps the **zoom** keys while you pinch, and holds the **run** key while a two-finger click is held. Sends mouse movement while you swipe, and drops the trackpad's glide events for WoW. |
| **TrackpadTurn addon** | What is under the cursor | On F19 over the 3D world it starts mouselook, so the movement turns the camera. F16 runs Blizzard's own move-forward binding. Over a frame it does nothing, and the frame scrolls. It ignores wheel zoom during a swipe and for the ticks WoW sends just after the release. |

| Signal | Mac | Windows |
|---|---|---|
| Swipe (held) | F19 | Pause |
| Zoom in / out (tapped) | F17 / F18 | Home / End |
| Run (held) | F16 | Insert |

The contract between the parts is `Sources/TrackpadTurnCore/WowSignalProtocol.swift` (Mac) and `Windows/src/TrackpadTurn.Core/WowSignalProtocol.cs` (Windows) ↔ `SIGNAL_KEY_SETS` in `Addon/TrackpadTurn/TrackpadTurn.lua`. Change them together; a Windows test checks the addon still matches.

**On Windows** there are no gesture phases to read, so the helper reads the touchpad's raw finger contacts (Raw Input, Precision Touchpad HID reports) to know exactly when two fingers land, move, pinch and lift. A low-level mouse hook drops the wheel events WoW must not see (before a swipe starts, the glide after lift, a pinch's Ctrl+wheel). Mouse movement is sent only while WoW's mouselook hides the cursor, because on Windows it would otherwise move the pointer.

## Develop

**Mac helper:** requires Xcode 16 or newer.

```bash
swift test                          # gesture logic (TrackpadTurnCore)
swiftlint                           # lint
scripts/build-app.sh --install      # build, copy to ~/Applications, relaunch
```

- **`TrackpadTurnCore`** is pure logic: scroll and pinch samples in, actions out. It is fully unit-tested.
- **The app target** holds only the macOS glue: event tap, input synthesis, addon installer and UI.
- **Signing:** dev builds are signed with your Apple Development identity when you have one. The Accessibility grant then survives rebuilds; with ad-hoc signing macOS asks again after every build.
- **Watch the helper's input handling live:**

  ```bash
  /usr/bin/log stream --level debug --predicate 'subsystem == "io.kartin.TrackpadTurn"'
  ```

**Windows helper** (`Windows/`): requires the .NET 10 SDK. It builds and its tests run on macOS too; the app itself only runs on Windows.

```bash
dotnet test Windows/TrackpadTurn.Windows.slnx
dotnet publish Windows/src/TrackpadTurn.Windows -c Release -r win-x64 -o Windows/artifacts/win-x64
```

- **`TrackpadTurn.Core`** is the pure logic: the Mac trackers ported one-to-one (their tests too), plus what Windows needs on top: the contact-frame gesture recognizer, the wheel filter and the hold-to-run timing (`GestureRouter`).
- **`TrackpadTurn.Windows`** is the Win32 glue: Raw Input, the mouse hook, `SendInput`, the tray, the settings window and the addon installer. Win32 bindings come from CsWin32 (`NativeMethods.txt`).
- **CI** (`.github/workflows/windows.yml`) runs the tests, publishes the x64 and Arm64 exes as artifacts, and runs `TrackpadTurn.exe --self-test`, which checks on real Windows that the hooks see the app's own marked input.
- **The icon** comes from `swift scripts/make-icon.swift --windows-only`.

## Releasing

`scripts/release.sh` runs the tests and builds a universal, hardened, Developer ID–signed app. It then notarizes and staples it, and writes `build/release/TrackpadTurn-<version>.zip` with a checksum and release notes from `CHANGELOG.md`. It publishes nothing; at the end it prints the `git tag` and `gh release create` commands.

One-time setup:

1. **Certificate:** Xcode → Settings → Accounts → Manage Certificates → **+** → **Developer ID Application**. This needs a paid Apple Developer membership.
2. **Notary credentials:** create an app-specific password at [account.apple.com](https://account.apple.com), then run:
   ```bash
   xcrun notarytool store-credentials TrackpadTurn --apple-id <your-apple-id> --team-id <your-team-id>
   ```

To cut a release, bump `## Version:` in `Addon/TrackpadTurn/TrackpadTurn.toc` and add a matching section to `CHANGELOG.md`. Then run `scripts/release.sh`.

The Windows exes come from the Windows CI run's artifacts. They are not code-signed, so first runs show SmartScreen's "More info → Run anyway".

## License

[MIT](LICENSE)
