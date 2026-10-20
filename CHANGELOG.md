# Changelog

One version number covers the app and the addon. It comes from `## Version:` in `Addon/TrackpadTurn/TrackpadTurn.toc`.

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
