# TrackpadTurn

**A Roblox-style trackpad camera for World of Warcraft on macOS.** Swipe two fingers to turn. No holding a click while you drag.

- **Swipe to turn.** A two-finger swipe over the world turns your camera and character, like holding the right mouse button. Press W and you run where you look.
- **Tilt.** Up/down swipes tilt the camera. They are damped (0.77×, as in Roblox), so a sideways swipe stays level.
- **Pinch** zooms in and out.
- **Run.** Hold a two-finger click to run forward, like holding both mouse buttons. Swipe while holding to steer. If you prefer, make each hold toggle autorun instead (Settings → AddOns → TrackpadTurn).
- **Lists still scroll.** Swipes over chat, bags or the quest log scroll them as usual.
- **No glide.** The camera stops the moment your fingers lift.

It works with any Mac trackpad, built-in or Magic Trackpad. A real mouse and its wheel behave exactly as before.

## Install

1. **Get the app.** Download `TrackpadTurn-<version>.zip` from [Releases](../../releases/latest), unzip it, move **TrackpadTurn** to Applications, and open it. A hand-on-trackpad icon appears in the menu bar.
2. **Allow Accessibility.** macOS asks the first time. If you miss the prompt, go to **System Settings → Privacy & Security → Accessibility** and switch on TrackpadTurn. The app notices by itself.
3. **Install the addon.** Click the menu bar icon, then click **Install** next to your game flavor (Classic Era, Retail, and so on).
4. **Restart WoW once,** so it finds the new addon. If WoW lists it as out of date, tick **Load out of date AddOns**.

That's all. Each app update also updates the addon copies it installed; type `/reload` in game to load the new version.

## Use

| Gesture | Over the 3D world | Over a frame (chat, bags, quest log) |
|---|---|---|
| Two-finger swipe left/right | Turn camera and character | Nothing |
| Two-finger swipe up/down | Tilt camera | Scroll the frame |
| Pinch | Zoom | Zoom |
| Two-finger click, held | Run forward, or toggle autorun (swipe to steer) | Same |
| Two-finger click, quick | Right-click (interact, loot) | Right-click |

**Menu bar panel:** turn speed, zoom speed, tilt on/off, pinch on/off, hold-to-run on/off, reverse each axis, open at login, and an on/off switch. WoW's own mouse sensitivity settings also scale turning.

**In game:**
- `/tpt` shows whether signals from the app arrive, and how many swipes turned the camera.
- `/tpt debug` prints why each swipe turned or was left alone, for example "cursor over ChatFrame1".
- `/tpt settings` opens the addon's settings (Settings → AddOns → TrackpadTurn). Its one setting: "Two-finger hold toggles autorun".

## Troubleshooting

- **`/tpt` says "no signal".** Check three things: the app is running, its switch is on, and it is allowed under Accessibility. The panel's status line tells you which one is missing.
- **Swipes do nothing while chat is open.** That is on purpose: while you type, keys go to the chat box.
- **Turning is too fast or too slow.** Use **Turn speed** in the panel.
- **Swipe direction feels backwards.** Use **Reverse left/right** or **Reverse up/down**.

## Privacy and safety

- **What it sees:** only scroll, pinch and right-click events, never your key presses. It makes no network connections and keeps no record of your input.
- **What it sends:** while WoW is in front, it sends F16–F19 key presses and mouse movement to the game, only while you swipe, pinch or hold a two-finger click.
- **Game rules:** it is one-to-one input remapping, in the same family as BetterTouchTool or Steam Input. One finger gesture becomes one continuous camera motion, with no timers, repeats or automation. Blizzard does not approve third-party tools, so use it at your own risk.
- **Open source:** the whole app and addon are in this repository, so you can check all of the above yourself.

## Compatibility

- **macOS:** 14 Sonoma or newer, on Apple Silicon and Intel.
- **Tested:** WoW Classic Era 1.15.9 with a Magic Trackpad.
- **Should work, untested:** WoW Forever 1.60.1, Retail, Anniversary and Mists Classic. The addon uses only APIs that every current client has, and its TOC lists each client's Interface number. Reports are welcome.

## Known limits

- **F16–F19 are taken.** These are the signal keys. If you bind something to them yourself, the addon's binding wins.
- **Custom wheel bindings can fire.** If your plain mouse wheel is bound to something other than camera zoom, swipes may trigger that binding. The same goes for Shift- or Ctrl-wheel bindings when you swipe while holding that modifier.
- **Holding runs anywhere.** Holding a two-finger click runs you (or toggles autorun) even over a frame. Quick clicks are unaffected.
- **Press first, then swipe.** macOS reports no click for a press made in the middle of a two-finger scroll, so to run you must press before you start swiping.
- **No glide in lists.** Chat, the quest log and other lists stop scrolling the moment your fingers lift. The app cannot tell a world swipe from a list swipe, so it drops the glide for both.
- **Magic Mouse turns too.** It scrolls the same way a trackpad does, so its surface also turns the camera.

## Uninstall

1. Quit TrackpadTurn from its panel and delete it from Applications.
2. Delete the `TrackpadTurn` folder from each game flavor's `Interface/AddOns` folder.
3. Optionally, remove it from **System Settings → Privacy & Security → Accessibility**.

## How it works

The job needs two parts, because neither can do it alone:

| Part | Knows | Does |
|---|---|---|
| **TrackpadTurn.app** (menu bar) | What your fingers do | Holds **F19** down for the length of a swipe, taps **F17/F18** while you pinch, and holds **F16** while a two-finger click is held. Sends mouse movement while you swipe, and drops the trackpad's glide events for WoW. |
| **TrackpadTurn addon** | What is under the cursor | On F19 over the 3D world it starts mouselook, so the movement turns the camera. F16 runs Blizzard's own move-forward binding. Over a frame it does nothing, and the frame scrolls. It ignores wheel zoom during a swipe and for the ticks WoW sends just after the release. |

The contract between the two parts is `Sources/TrackpadTurnCore/WowSignalProtocol.swift` ↔ `SIGNAL_KEYS` in `Addon/TrackpadTurn/TrackpadTurn.lua`. Change both together.

## Develop

Requires Xcode 16 or newer.

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

## Releasing

`scripts/release.sh` runs the tests and builds a universal, hardened, Developer ID–signed app. It then notarizes and staples it, and writes `build/release/TrackpadTurn-<version>.zip` with a checksum and release notes from `CHANGELOG.md`. It publishes nothing; at the end it prints the `git tag` and `gh release create` commands.

One-time setup:

1. **Certificate:** Xcode → Settings → Accounts → Manage Certificates → **+** → **Developer ID Application**. This needs a paid Apple Developer membership.
2. **Notary credentials:** create an app-specific password at [account.apple.com](https://account.apple.com), then run:
   ```bash
   xcrun notarytool store-credentials TrackpadTurn --apple-id <your-apple-id> --team-id <your-team-id>
   ```

To cut a release, bump `## Version:` in `Addon/TrackpadTurn/TrackpadTurn.toc` and add a matching section to `CHANGELOG.md`. Then run `scripts/release.sh`.

## License

[MIT](LICENSE)
