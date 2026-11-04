# Changelog

One version number covers the app and the addon. It comes from `## Version:` in `Addon/TrackpadTurn/TrackpadTurn.toc`.

## 1.2.0

- **Added: a Windows helper** for laptops with a Precision Touchpad (Windows 10/11, x64 and Arm64): swipe to turn and tilt, pinch to zoom, hold a two-finger click to run, lists still scroll, no glide. It's a tray app with a settings window and an addon installer, shipped as one self-contained exe. It has not been tested in game yet. How it works:
  - It reads the touchpad's raw finger contacts, because Windows gives games only wheel events, with no begin or end and with glide added after the lift.
  - It sends mouse movement only while WoW's mouselook hides the cursor; on Windows the movement would otherwise drag the pointer away from a list you scroll.
  - It does not start with Windows unless you tick **Start at login**.
- **Changed: the signal protocol is now version 3,** with a key set per platform. WoW on Windows ignores F13–F24, so Windows uses Pause (swipe), Home/End (zoom) and Insert (run): keys that type no text and toggle nothing. The Mac keeps F16–F19.
- **Changed:** the addon's "no signal" help (`/tpt`) now names the right steps for each platform.

## 1.1.0

- **Added: hold a two-finger click to run forward,** like holding both mouse buttons; swiping steers while you hold. It works like this:
  - A quick two-finger click stays a plain right-click (interact, loot), because running starts only after 0.2 s.
  - The forward key runs Blizzard's own `MOVEFORWARD` binding; addon code can't start movement itself. (`SetMouselookOverrideBinding` only remaps mouse buttons, so it couldn't be used to restrict running to mouselook.)
  - Only trackpad clicks count; a mouse's right button keeps its normal right-drag.
  - Holding over a frame also runs; quick clicks never do.
  - The press must start the gesture: macOS reports no click for a press made in the middle of a two-finger scroll.
- **Added:** an in-game setting, Settings → AddOns → TrackpadTurn → "Two-finger hold toggles autorun". When it's on, each held two-finger click turns autorun on or off (Blizzard's `TOGGLEAUTORUN`) instead of running while held. `/tpt settings` opens it. If you change it during combat, it applies when combat ends.
- **Added:** a menu bar toggle that switches the two-finger hold on or off ("Hold a two-finger click to run").
- **Changed:** the signal protocol is now version 2, with the new F16 forward key.

## 1.0.3

- **Fixed:** in WoW Forever, a "blocked from an action only available to the Blizzard UI" popup appeared now and then. The cause was 1.0.2's `RunBinding` call. Forever's zoom binding calls `MoveViewInStart`, which addon code may not reach, so the game blocked it. The wheel now zooms with `CameraZoomIn`/`CameraZoomOut` on every client.
- **Changed:** back to a single TOC, which lists every client's Interface number (11509, 20505, 50504, 16001, 120105).
- **Added:** if the game ever blocks one of the addon's calls, the addon names the call in chat.
- **Upgrading from 1.0.2:** quit and restart the game; `/reload` is not enough. The client remembers which TOC it loaded at launch, and `/reload` does not re-scan addon files, so it can't pick up the removed TOC.

## 1.0.2

- **Added:** WoW Forever support. The addon now ships a `TrackpadTurn_Mainline.toc` (Interface 16001 for Forever, 120105 for Retail) next to the Classic-family TOC. The app's addon list also names the `_forever_` folder.
- **Changed:** the mouse wheel now runs each client's own zoom binding, the way Blizzard's own UI code passes on a binding. Retail and Forever keep their smooth zoom, and Classic keeps its instant zoom. Before, the addon used Classic's instant zoom everywhere.

## 1.0.1

- **Fixed:** releasing a swipe could zoom the camera hard in or out. WoW sends a few wheel ticks just after the swipe key is released, and the addon now ignores those as part of the swipe.
- **Added:** the helper logs its input handling. To watch it live:
  `/usr/bin/log stream --level debug --predicate 'subsystem == "io.kartin.TrackpadTurn"'`

## 1.0.0

First version. It is tested on WoW Classic Era 1.15.9 with a Magic Trackpad.

- **Swipe to turn.** A two-finger swipe over the world turns the camera and the character, like holding the right mouse button.
- **Tilt.** Up/down swipes tilt the camera, damped to 0.77×.
- **Pinch** zooms.
- **Lists still scroll.** Swipes over frames scroll them as usual.
- **Menu bar panel** with turn speed, zoom speed, tilt, pinch, axis reversal and open at login.
- **Addon kept in sync.** The app bundles the addon, installs it, and updates the copies it installed.
