#!/usr/bin/env bash
set -euo pipefail

BASE="${1:-/home/amp/.ampdata/instances/dmix01/counter-strike2/730/game/csgo}"
HERE="$(cd "$(dirname "$0")" && pwd)"

cd "$HERE"
dotnet publish -c Release -o out

DEST="$BASE/addons/counterstrikesharp/plugins/GameManager"
CFG="$BASE/cfg/gamemanager"

sudo mkdir -p "$DEST" "$CFG"
sudo cp out/GameManager.dll "$DEST/"
sudo cp out/GameManager.deps.json "$DEST/" 2>/dev/null || true
sudo cp out/GameManager.pdb "$DEST/" 2>/dev/null || true

for f in 1v1 retake 5v5 mix prac; do
  if [ ! -e "$CFG/$f.cfg" ]; then
    echo "// GameManager $f mode" | sudo tee "$CFG/$f.cfg" >/dev/null
  fi
done

echo
echo "Installed to: $DEST"
echo "Restart the CS2 instance, then run: css_plugins list"
