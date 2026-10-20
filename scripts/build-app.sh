#!/usr/bin/env bash
# Builds build/TrackpadTurn.app: the menu bar helper with the WoW addon bundled inside.
#
#   scripts/build-app.sh             build for this Mac, signed with your development identity
#   scripts/build-app.sh --install   ...then copy to ~/Applications and (re)launch it
#   scripts/build-app.sh --release   universal (Apple Silicon + Intel), hardened runtime,
#                                    Developer ID signature; used by scripts/release.sh
#
# CODESIGN_IDENTITY overrides the signing identity in every mode.
set -euo pipefail

ROOT="$(cd "$(dirname "$0")/.." && pwd)"
APP="$ROOT/build/TrackpadTurn.app"
INSTALLED_APP="$HOME/Applications/TrackpadTurn.app"
# The addon's TOC version is the single version number for both halves.
VERSION="$(sed -n 's/^## Version: *//p' "$ROOT/Addon/TrackpadTurn/TrackpadTurn.toc")"

install=0
release=0
for argument in "$@"; do
	case "$argument" in
		--install) install=1 ;;
		--release) release=1 ;;
		*) echo "Unknown option: $argument" >&2; exit 2 ;;
	esac
done

first_identity() {
	security find-identity -v -p codesigning | awk -F'"' -v kind="$1" 'index($2, kind) == 1 { print $2; exit }'
}

build_flags=(-c release --package-path "$ROOT")
if (( release )); then
	build_flags+=(--arch arm64 --arch x86_64)
fi
swift build "${build_flags[@]}"
BIN_DIR="$(swift build "${build_flags[@]}" --show-bin-path)"

rm -rf "$APP"
mkdir -p "$APP/Contents/MacOS" "$APP/Contents/Resources/Addon"
cp "$BIN_DIR/TrackpadTurn" "$APP/Contents/MacOS/TrackpadTurn"
cp -R "$ROOT/Addon/TrackpadTurn" "$APP/Contents/Resources/Addon/"
cp "$ROOT/Resources/AppIcon.icns" "$APP/Contents/Resources/" # regenerate: swift scripts/make-icon.swift
sed "s/__VERSION__/$VERSION/g" "$ROOT/Resources/Info.plist" > "$APP/Contents/Info.plist"

if (( release )); then
	# Notarization requires a Developer ID signature, the hardened runtime and a secure timestamp.
	identity="${CODESIGN_IDENTITY:-$(first_identity "Developer ID Application")}"
	if [[ -z "$identity" ]]; then
		echo "No 'Developer ID Application' certificate found. See README → Releasing." >&2
		exit 1
	fi
	codesign --force --options runtime --timestamp --sign "$identity" "$APP"
else
	# Prefer the development identity: macOS ties the Accessibility grant to the signature, so a
	# stable identity keeps the grant across rebuilds. Ad-hoc ("-") works but asks again each build.
	identity="${CODESIGN_IDENTITY:-$(first_identity "Apple Development")}"
	codesign --force --sign "${identity:--}" "$APP"
fi
echo "Built $APP (v$VERSION, signed: ${identity:-ad-hoc})"

if (( install )); then
	osascript -e 'tell application id "io.kartin.TrackpadTurn" to quit' >/dev/null 2>&1 || true
	mkdir -p "$HOME/Applications"
	rm -rf "$INSTALLED_APP"
	cp -R "$APP" "$INSTALLED_APP"
	open "$INSTALLED_APP"
	echo "Installed and launched $INSTALLED_APP"
fi
