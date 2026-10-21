# Changelog

One version number covers the app and the addon. It comes from `## Version:` in `Addon/TrackpadTurn/TrackpadTurn.toc`.

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
