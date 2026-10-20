#!/usr/bin/env bash
# Builds a notarized release: build/release/TrackpadTurn-<version>.zip (+ .sha256, notes.md).
# It publishes nothing; it prints the commands that do.
#
# One-time setup (README → Releasing):
#   1. A "Developer ID Application" certificate in your keychain.
#   2. Notary credentials saved under the profile name below:
#        xcrun notarytool store-credentials TrackpadTurn --apple-id <apple-id> --team-id <team-id>
#
# NOTARY_PROFILE overrides the keychain profile name (default: TrackpadTurn).
set -euo pipefail

ROOT="$(cd "$(dirname "$0")/.." && pwd)"
APP="$ROOT/build/TrackpadTurn.app"
OUT="$ROOT/build/release"
NOTARY_PROFILE="${NOTARY_PROFILE:-TrackpadTurn}"
VERSION="$(sed -n 's/^## Version: *//p' "$ROOT/Addon/TrackpadTurn/TrackpadTurn.toc")"
ZIP="$OUT/TrackpadTurn-$VERSION.zip"

fail() { echo "error: $*" >&2; exit 1; }

# Preflight: fail before the slow steps, with the fix in the message.
security find-identity -v -p codesigning | grep -q '"Developer ID Application' \
	|| fail "no 'Developer ID Application' certificate in the keychain (README → Releasing, step 1)."
xcrun notarytool history --keychain-profile "$NOTARY_PROFILE" >/dev/null 2>&1 \
	|| fail "no notary credentials named '$NOTARY_PROFILE' (README → Releasing, step 2)."
grep -q "^## $VERSION\$" "$ROOT/CHANGELOG.md" \
	|| fail "CHANGELOG.md has no '## $VERSION' section."
if [[ -n "$(git -C "$ROOT" status --porcelain)" ]]; then
	echo "warning: uncommitted changes; the release will not match any commit." >&2
fi

swift test --package-path "$ROOT"
"$ROOT/scripts/build-app.sh" --release

codesign --verify --strict --deep --verbose=2 "$APP"
archs="$(lipo -archs "$APP/Contents/MacOS/TrackpadTurn")"
[[ "$archs" == *arm64* && "$archs" == *x86_64* ]] || fail "binary is not universal (got: $archs)."

mkdir -p "$OUT"
rm -f "$ZIP" "$ZIP.sha256"
upload="$OUT/TrackpadTurn-$VERSION-notarization.zip"
# ditto keeps the bundle's signature and metadata intact; plain zip can break them.
ditto -c -k --keepParent "$APP" "$upload"

echo "Submitting to Apple's notary service (usually a few minutes)…"
result="$(xcrun notarytool submit "$upload" --keychain-profile "$NOTARY_PROFILE" --wait --output-format json)"
rm -f "$upload"
status="$(jq -r '.status' <<<"$result")"
if [[ "$status" != "Accepted" ]]; then
	submission="$(jq -r '.id' <<<"$result")"
	xcrun notarytool log "$submission" --keychain-profile "$NOTARY_PROFILE" >&2 || true
	fail "notarization finished with status '$status'."
fi

# Staple the ticket so the app opens without a network check, then zip the stapled app.
xcrun stapler staple "$APP"
xcrun stapler validate "$APP"
spctl --assess --type execute --verbose=2 "$APP"
ditto -c -k --keepParent "$APP" "$ZIP"
(cd "$OUT" && shasum -a 256 "$(basename "$ZIP")" > "$(basename "$ZIP").sha256")

# Release notes = this version's CHANGELOG section.
awk -v heading="## $VERSION" '$0 == heading { found = 1; next } found && /^## / { exit } found' \
	"$ROOT/CHANGELOG.md" > "$OUT/notes.md"

cat <<EOF

Notarized release ready:
  $ZIP
  $ZIP.sha256

To publish:
  git -C "$ROOT" tag v$VERSION && git -C "$ROOT" push origin v$VERSION
  gh release create v$VERSION "$ZIP" "$ZIP.sha256" --title "TrackpadTurn $VERSION" --notes-file "$OUT/notes.md"
EOF
