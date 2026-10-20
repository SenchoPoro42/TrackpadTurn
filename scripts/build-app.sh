#!/usr/bin/env bash
# Builds build/TrackpadTurn.app: the menu bar helper with the WoW addon bundled inside.
#
#   scripts/build-app.sh            build only
#   scripts/build-app.sh --install  build, copy to ~/Applications, and (re)launch it
set -euo pipefail

ROOT="$(cd "$(dirname "$0")/.." && pwd)"
APP="$ROOT/build/TrackpadTurn.app"
INSTALLED_APP="$HOME/Applications/TrackpadTurn.app"
# The addon's TOC version is the single version number for both halves.
VERSION="$(sed -n 's/^## Version: *//p' "$ROOT/Addon/TrackpadTurn/TrackpadTurn.toc")"

swift build -c release --package-path "$ROOT"
BIN_DIR="$(swift build -c release --package-path "$ROOT" --show-bin-path)"

rm -rf "$APP"
mkdir -p "$APP/Contents/MacOS" "$APP/Contents/Resources/Addon"
cp "$BIN_DIR/TrackpadTurn" "$APP/Contents/MacOS/TrackpadTurn"
cp -R "$ROOT/Addon/TrackpadTurn" "$APP/Contents/Resources/Addon/"
cp "$ROOT/Resources/AppIcon.icns" "$APP/Contents/Resources/" # regenerate: swift scripts/make-icon.swift
sed "s/__VERSION__/$VERSION/g" "$ROOT/Resources/Info.plist" > "$APP/Contents/Info.plist"

# Sign with a real identity when there is one: macOS ties the Accessibility grant to it, so the
# grant survives rebuilds. Ad-hoc signing ("-") works but macOS asks again after every build.
IDENTITY="${CODESIGN_IDENTITY:-$(security find-identity -v -p codesigning \
	| awk -F'"' '/Developer ID Application|Apple Development/ { print $2; exit }')}"
codesign --force --sign "${IDENTITY:--}" "$APP"
echo "Built $APP (v$VERSION, signed: ${IDENTITY:-ad-hoc})"

if [[ "${1:-}" == "--install" ]]; then
	osascript -e 'tell application id "io.kartin.TrackpadTurn" to quit' >/dev/null 2>&1 || true
	mkdir -p "$HOME/Applications"
	rm -rf "$INSTALLED_APP"
	cp -R "$APP" "$INSTALLED_APP"
	open "$INSTALLED_APP"
	echo "Installed and launched $INSTALLED_APP"
fi
