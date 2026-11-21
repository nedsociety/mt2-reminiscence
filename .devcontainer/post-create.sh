#!/usr/bin/env bash
set -euo pipefail

echo "== Reminiscence devcontainer =="

mkdir -p "${EXTRACT_ROOT:-/workspace/.extract}" \
  /workspace/.extract/mt1 \
  /workspace/.extract/mt2

steam_ok=0
mt1_ok=0
mt2_ok=0

if [[ -d "${STEAM_ROOT:-/mnt/steam}/steamapps" ]]; then
  steam_ok=1
  echo "Steam mount:  ${STEAM_ROOT} (ok)"
else
  echo "Steam mount:  ${STEAM_ROOT:-/mnt/steam} (MISSING — check host ~/.local/share/Steam)"
fi

if [[ -d "${MT1_ROOT:-}" && -f "${MT1_ROOT}/MonsterTrain.exe" ]]; then
  mt1_ok=1
  echo "MT1:          ${MT1_ROOT} (ok)"
else
  echo "MT1:          ${MT1_ROOT:-unset} (MISSING)"
fi

if [[ -d "${MT2_ROOT:-}" && -f "${MT2_ROOT}/MonsterTrain2.exe" ]]; then
  mt2_ok=1
  echo "MT2:          ${MT2_ROOT} (ok)"
else
  echo "MT2:          ${MT2_ROOT:-unset} (MISSING)"
fi

echo "Tools:"
if command -v AssetRipper >/dev/null; then echo "  AssetRipper: $(command -v AssetRipper)"; else echo "  AssetRipper missing"; fi
command -v ilspycmd >/dev/null && ilspycmd --version | sed 's/^/  ilspycmd /' || echo "  ilspycmd missing"
python3 -c "import UnityPy; print(f'  UnityPy {UnityPy.__version__}')" 2>/dev/null || echo "  UnityPy missing"
command -v ffmpeg >/dev/null && ffmpeg -version 2>&1 | head -n 1 | sed 's/^/  /' || echo "  ffmpeg missing"
if command -v agent >/dev/null; then
  echo "  cursor-agent: $(command -v agent)"
else
  echo "  cursor-agent missing"
fi
if [[ -x "${WINPYTHON:-/home/vscode/.local/winpython}/python.exe" && -x /usr/local/bin/wine-python ]]; then
  echo "  wine-python:  ${WINPYTHON:-/home/vscode/.local/winpython}/python.exe"
else
  echo "  wine-python missing"
fi
if command -v docker >/dev/null; then
  if docker info >/dev/null 2>&1; then
    echo "  docker:       $(docker version --format '{{.Server.Version}}')"
  else
    echo "  docker:       cli present, daemon not running"
  fi
else
  echo "  docker missing"
fi

plugins="${MT2_ROOT:-}/BepInEx/plugins"
if [[ $mt2_ok -eq 1 ]]; then
  if [[ -d "$plugins" ]]; then
    echo "MT2 plugins:  $plugins (writable mount for debug installs)"
  else
    echo "MT2 plugins:  BepInEx not installed yet — run scripts/install-bepinex-mt2.sh"
  fi
fi

echo
echo "Quick extracts (examples):"
echo "  scripts/decompile-assemblies.sh          # MT1+MT2 Assembly-CSharp -> .extract/*/code"
echo "  AssetRipper --headless --port 5000       # web UI (forwarded); load game folder manually"
echo "  # scripted assets/audio: UnityPy + ffmpeg (see scripts/)"
echo "  agent   # Cursor CLI agent (needs CURSOR_API_KEY or agent login)"

if [[ $steam_ok -ne 1 || $mt1_ok -ne 1 || $mt2_ok -ne 1 ]]; then
  echo
  echo "WARNING: game mounts incomplete; autodiscovery/debug installs will fail until Steam is mounted."
  exit 0
fi
