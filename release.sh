#!/bin/bash
# Package Buff It 2 The Limit (Pad) as a release zip and optionally publish it on GitHub.
#
# Usage: ./release.sh wotr|kingmaker [--publish] [--notes FILE]
#   wotr       BuffIt2TheLimit/ for <WotR>/mods/, tag v<Info.json Version>-pad.<N>, N = next unreleased number
#   kingmaker  PadBuffsKingmaker/ for <Kingmaker>/Mods/, tag kingmaker-v<Info.json Version>
#   --publish  create the tag and the GitHub release (needs gh, a clean tree and branch gamepad pushed)
#   --notes    release notes in Markdown; without it a short default text is used
#
# Zips go to dist/. The game's assemblies come from GamePath.props as for any build.
set -euo pipefail

HERE="$(cd "$(dirname "$0")" && pwd)"
REPO="sergusaev/wrath-epic-buffing"
DOCS="https://github.com/$REPO/blob/gamepad/pad-docs"
GUIDE="https://github.com/sergusaev/pathfinder-mods/blob/main/docs"
DOTNET="${DOTNET:-$(command -v dotnet || echo "$HOME/.dotnet/dotnet")}"

TARGET="${1:-}"
case "$TARGET" in
  wotr|kingmaker) shift ;;
  *) echo "usage: ./release.sh wotr|kingmaker [--publish] [--notes FILE]"; exit 1 ;;
esac
PUBLISH=0; NOTES=""
while [ $# -gt 0 ]; do
  case "$1" in
    --publish) PUBLISH=1 ;;
    --notes) NOTES="$2"; shift ;;
    *) echo "unknown option: $1"; exit 1 ;;
  esac
  shift
done

version_of() { sed -n 's/^[[:space:]]*"Version"[[:space:]]*:[[:space:]]*"\([^"]*\)".*/\1/p' "$1" | head -1; }
released() { git -C "$HERE" ls-remote --tags origin "refs/tags/$1" | grep -q .; }

if [ "$TARGET" = wotr ]; then
  PROJECT="BuffIt2TheLimit/BuffIt2TheLimit.csproj"
  OUT="$HERE/BuffIt2TheLimit/bin/Release"
  FOLDER="BuffIt2TheLimit"
  BASE="$(version_of "$HERE/BuffIt2TheLimit/Info.json")"
  N=1
  while released "v$BASE-pad.$N"; do N=$((N + 1)); done
  VERSION="$BASE-pad.$N"
  TAG="v$VERSION"
  ZIPNAME="BuffIt2TheLimit-Pad-$VERSION.zip"
  TITLE="Buff It 2 The Limit (Pad) $VERSION (Wrath of the Righteous)"
  INSTALL="Unpack into \`<WotR>/mods/\` so the result is \`mods/BuffIt2TheLimit/Info.json\`; when updating, delete \`*.cache\` there. It replaces the original Buff It 2 The Limit and BubbleBuffs and keeps their settings files."
  GUIDE_LINK="[pad-docs/README.md]($DOCS/README.md) ([русский]($DOCS/README.ru.md))"
else
  PROJECT="BuffIt2TheLimit.Kingmaker/BuffIt2TheLimit.Kingmaker.csproj"
  OUT="$HERE/BuffIt2TheLimit.Kingmaker/bin/Release"
  FOLDER="PadBuffsKingmaker"
  VERSION="$(version_of "$HERE/BuffIt2TheLimit.Kingmaker/Info.json")"
  TAG="kingmaker-v$VERSION"
  ZIPNAME="PadBuffsKingmaker-$VERSION.zip"
  TITLE="Buff It 2 The Limit (Pad) for Kingmaker $VERSION"
  INSTALL="Unpack into \`<Kingmaker>/Mods/\` so the result is \`Mods/PadBuffsKingmaker/Info.json\`; when updating, delete \`*.cache\` there. The menu key is F7 by default."
  GUIDE_LINK="[pad-docs/README.md, section Kingmaker]($DOCS/README.md#kingmaker) ([русский]($DOCS/README.ru.md#kingmaker))"
fi

if [ "$PUBLISH" = 1 ]; then
  [ "$(git -C "$HERE" rev-parse --abbrev-ref HEAD)" = gamepad ] || { echo "switch to branch gamepad first"; exit 1; }
  [ -z "$(git -C "$HERE" status --porcelain --untracked-files=no)" ] || { echo "commit your changes first"; exit 1; }
  git -C "$HERE" fetch -q origin gamepad
  [ "$(git -C "$HERE" rev-parse HEAD)" = "$(git -C "$HERE" rev-parse origin/gamepad)" ] || { echo "push gamepad first"; exit 1; }
  ! released "$TAG" || { echo "$TAG is already released, bump Version in Info.json"; exit 1; }
fi

"$DOTNET" build "$HERE/$PROJECT" -c Release -p:SolutionDir="$HERE/" --nologo -v q || { echo "build failed"; exit 1; }

STAGE="$(mktemp -d)"
trap 'rm -rf "$STAGE"' EXIT
mkdir -p "$STAGE/$FOLDER" "$HERE/dist"
if [ "$TARGET" = wotr ]; then
  cp -R "$OUT/." "$STAGE/$FOLDER/"
  rm -f "$STAGE/$FOLDER"/*.pdb
else
  cp "$OUT/PadBuffsKingmaker.dll" "$HERE/BuffIt2TheLimit.Kingmaker/Info.json" "$STAGE/$FOLDER/"
fi
ZIP="$HERE/dist/$ZIPNAME"
rm -f "$ZIP"
# zip is missing in Git Bash on Windows; bsdtar (macOS tar, Windows tar.exe) writes zip archives too.
if command -v zip >/dev/null; then
  (cd "$STAGE" && zip -q -r -X "$ZIP" "$FOLDER")
elif tar --version 2>/dev/null | grep -q bsdtar; then
  (cd "$STAGE" && tar -a -cf "$ZIP" "$FOLDER")
elif [ -x /c/Windows/System32/tar.exe ]; then
  (cd "$STAGE" && /c/Windows/System32/tar.exe -a -cf "$ZIP" "$FOLDER")
else
  echo "need zip or bsdtar to pack"; exit 1
fi
echo "packed: $ZIP ($TAG)"

[ "$PUBLISH" = 1 ] || exit 0

if [ -z "$NOTES" ]; then
  NOTES="$STAGE/notes.md"
  cat > "$NOTES" <<EOF
$TITLE.

- $INSTALL

Based on [Gh05d/wrath-epic-buffing](https://github.com/Gh05d/wrath-epic-buffing) (MIT; Vek17, factubsio, Gh05d).

Guide: $GUIDE_LINK. Steam Deck setup: [guide]($GUIDE/steam-deck.md) ([русский]($GUIDE/steam-deck.ru.md)).
EOF
fi
gh release create "$TAG" "$ZIP" -R "$REPO" --target gamepad --title "$TITLE" --notes-file "$NOTES"
