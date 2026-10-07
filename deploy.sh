#!/bin/bash
# Build (Release) and install Buff It 2 The Limit (Groups) on a Steam Deck over SSH.
# Usage: DECK=deck@steamdeck.local ./deploy.sh [--bind-menu] [--kingmaker]
#   DECK         SSH target of the deck (required).
#   WOTR_DIR, KINGMAKER_DIR  game folders on the deck; default: the internal Steam library.
#   --kingmaker  install the Kingmaker build (BuffIt2TheLimit.Kingmaker) instead of the WotR one.
#   --bind-menu  in every bi2tl-*.json: open menu = F7 (Steam Input L5 tap), Long = F6,
#                Important = F9, Quick cleared (groups are applied from the menu).
#                No Shift: Steam Input drops delayed keys of a short tap, so chords never arrive.
set -euo pipefail

DECK="${DECK:?set DECK to the SSH target of the deck, e.g. DECK=deck@steamdeck.local}"
STEAM_COMMON="/home/deck/.local/share/Steam/steamapps/common"
GAME="${WOTR_DIR:-$STEAM_COMMON/Pathfinder Second Adventure}"
MOD_DIR="$GAME/mods/BuffIt2TheLimit"
HERE="$(cd "$(dirname "$0")" && pwd)"
OUT="$HERE/BuffIt2TheLimit/bin/Release"

BIND=0
KM=0
for a in "$@"; do
  case "$a" in
    --bind-menu) BIND=1 ;;
    --kingmaker) KM=1 ;;
    *) echo "unknown option: $a"; exit 1 ;;
  esac
done

DOTNET="${DOTNET:-$(command -v dotnet || echo "$HOME/.dotnet/dotnet")}"
md5_of() { if command -v md5sum >/dev/null; then md5sum "$1" | cut -d' ' -f1; else md5 -q "$1"; fi; }
build() {
  "$DOTNET" build "$1" -c Release -p:SolutionDir="$HERE/" --nologo -v q || { echo "build failed: $1"; exit 1; }
}

if [ "$KM" = 1 ]; then
  build "$HERE/BuffIt2TheLimit.Kingmaker/BuffIt2TheLimit.Kingmaker.csproj"
  KM_MOD_DIR="${KINGMAKER_DIR:-$STEAM_COMMON/Pathfinder Kingmaker}/Mods/PadBuffsKingmaker"
  KM_OUT="$HERE/BuffIt2TheLimit.Kingmaker/bin/Release"
  ssh -o ConnectTimeout=5 "$DECK" true || { echo "deck unreachable"; exit 1; }
  if ssh "$DECK" "pgrep -f '[K]ingmaker.exe' >/dev/null"; then
    echo "Kingmaker is running, close it before installing"; exit 1
  fi
  ssh "$DECK" "mkdir -p '$KM_MOD_DIR/UserSettings' && rm -f '$KM_MOD_DIR'/*.cache"
  scp -q "$KM_OUT/PadBuffsKingmaker.dll" "$KM_OUT/Info.json" "$DECK:$KM_MOD_DIR/"
  [ "$(md5_of "$KM_OUT/PadBuffsKingmaker.dll")" = "$(ssh "$DECK" "md5sum '$KM_MOD_DIR/PadBuffsKingmaker.dll'" | cut -d' ' -f1)" ] \
    || { echo "checksum mismatch after copy"; exit 1; }
  echo "installed to $DECK:$KM_MOD_DIR"
  exit 0
fi

build "$HERE/BuffIt2TheLimit/BuffIt2TheLimit.csproj"
ssh -o ConnectTimeout=5 "$DECK" true || { echo "deck unreachable"; exit 1; }
if ssh "$DECK" "pgrep -f '[W]rath.exe' >/dev/null"; then
  echo "Wrath is running, close it before installing"; exit 1
fi

ssh "$DECK" "rm -f '$MOD_DIR'/*.cache '$MOD_DIR'/*.cache.pdb"
scp -q "$OUT/BuffIt2TheLimit.dll" "$OUT/Info.json" "$DECK:$MOD_DIR/"
[ "$(md5_of "$OUT/BuffIt2TheLimit.dll")" = "$(ssh "$DECK" "md5sum '$MOD_DIR/BuffIt2TheLimit.dll'" | cut -d' ' -f1)" ] \
  || { echo "checksum mismatch after copy"; exit 1; }
echo "installed to $DECK:$MOD_DIR"

if [ "$BIND" = 1 ]; then
  ssh "$DECK" "cd '$MOD_DIR/UserSettings' && python3 - bi2tl-*.json" <<'EOF'
import json, sys
for path in sys.argv[1:]:
    raw = open(path, 'rb').read()
    bom = raw.startswith(b'\xef\xbb\xbf')
    data = json.loads(raw.decode('utf-8-sig'))
    key = lambda k: {'Key': k, 'Ctrl': False, 'Shift': False, 'Alt': False}
    data['OpenBuffMenuKey'] = key('F7')
    data['ShortcutKeys'] = {'Long': key('F6'), 'Quick': key('None'), 'Important': key('F9')}
    open(path, 'wb').write((b'\xef\xbb\xbf' if bom else b'') + json.dumps(data, ensure_ascii=False, indent=2).encode())
    print('rebound', path)
EOF
fi
