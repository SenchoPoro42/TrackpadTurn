# TrackpadTurn

Roblox-style trackpad camera for World of Warcraft on macOS.

- **Two-finger swipe over the world** turns your camera and character, like holding the right mouse button. W then runs where you look.
- **Up/down swipes** tilt the camera. They are damped (0.77×, like Roblox) so a sideways swipe stays level.
- **Pinch** zooms in and out.
- **Swipes over chat, bags or the quest log** scroll them as usual.
- **No glide:** the camera stops when your fingers lift.

It works with any Mac trackpad. A real mouse and its wheel behave exactly as before.

## How it works

Two parts are needed, because neither can do the job alone:

| Part | Knows | Does |
|---|---|---|
| **TrackpadTurn.app** (menu bar) | What your fingers do | Holds **F19** down for the length of a swipe, taps **F17/F18** while you pinch, and sends mouse movement while you swipe. Drops the trackpad's glide events for WoW. |
| **TrackpadTurn addon** | What is under the cursor | On F19 over the 3D world it starts mouselook, so the movement turns the camera. Over a frame it does nothing, and the frame scrolls. While a swipe is held, the wheel does not zoom. |

The contract between them is `Sources/TrackpadTurnCore/WowSignalProtocol.swift` ↔ `SIGNAL_KEYS` in `Addon/TrackpadTurn/TrackpadTurn.lua`.

It is one-to-one input remapping: one finger gesture becomes one continuous camera motion. There are no timers, no repeats and no automation.

## Install

```bash
scripts/build-app.sh --install
```

This builds the app, copies it to `~/Applications/TrackpadTurn.app` and launches it. Then:

1. **Allow Accessibility.** macOS asks the first time. If you miss the prompt, go to System Settings → Privacy & Security → Accessibility and switch on TrackpadTurn. The app picks up the change by itself.
2. **Install the addon.** Click the menu bar icon (hand on a trackpad), then click **Install** next to your game flavor.
3. **Restart WoW** once, so it sees the new addon. If WoW lists the addon as out of date, tick *Load out of date AddOns*.

After that, each app update also updates the installed addon. Use `/reload` in game to load it.

## In game

- `/tpt` shows status: whether signals arrive, and how many swipes turned the camera.
- `/tpt debug` prints why each swipe turned or was left alone, for example "cursor over ChatFrame1".

## Settings (menu bar panel)

Turn speed, zoom speed, tilt on/off, pinch on/off, reverse each axis, and open at login.
WoW's own mouse sensitivity settings also scale turning.

## Develop

```bash
swift test                    # gesture logic (TrackpadTurnCore)
swiftlint                     # lint
scripts/build-app.sh          # build/TrackpadTurn.app
```

- `TrackpadTurnCore` is pure logic: scroll and pinch samples in, actions out. It is fully unit-tested.
- The app target holds only the macOS glue: event tap, input synthesis, addon installer and UI.
- The build signs with your Apple Development identity when you have one. The Accessibility grant then survives rebuilds; with ad-hoc signing macOS asks again after every build.

## Known limits

- **F17–F19 are taken.** These are the signal keys. If you bind something to them yourself, the addon's binding wins.
- **Custom wheel bindings can fire.** If your plain mouse wheel is bound to something other than camera zoom, swipes may trigger that binding. The same goes for Shift- or Ctrl-wheel bindings when you swipe while holding that modifier.
- **No glide in lists.** Chat, the quest log and other lists stop scrolling the moment your fingers lift. The app cannot tell a world swipe from a list swipe, so it drops the glide for both.
- **Magic Mouse turns too.** It scrolls the same way a trackpad does, so its surface also turns the camera.
