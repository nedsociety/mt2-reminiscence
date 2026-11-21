#!/usr/bin/env bash
# Install BepInEx 5 (Windows x64) into the mounted MT2 directory for Proton/Steam.
# MT2 on Linux Steam is the Windows build; use the win pack, not linux.
set -euo pipefail

MT2_ROOT="${MT2_ROOT:-/mnt/steam/steamapps/common/Monster Train 2}"
BEPINEX_VERSION="${BEPINEX_VERSION:-5.4.23.3}"
ZIP_NAME="BepInEx_win_x64_${BEPINEX_VERSION}.zip"
URL="https://github.com/BepInEx/BepInEx/releases/download/v${BEPINEX_VERSION}/${ZIP_NAME}"

if [[ ! -f "${MT2_ROOT}/MonsterTrain2.exe" ]]; then
  echo "MT2 not found at: ${MT2_ROOT}" >&2
  exit 1
fi

if [[ -d "${MT2_ROOT}/BepInEx/core" ]]; then
  echo "BepInEx already present at ${MT2_ROOT}/BepInEx"
  mkdir -p "${MT2_ROOT}/BepInEx/plugins"
  exit 0
fi

tmp="$(mktemp -d)"
trap 'rm -rf "$tmp"' EXIT

echo "Downloading ${ZIP_NAME}..."
curl -fsSL -o "${tmp}/${ZIP_NAME}" "$URL"
unzip -q "${tmp}/${ZIP_NAME}" -d "$tmp/out"

# Archive roots vary slightly; copy contents into game root.
if [[ -d "$tmp/out/BepInEx" ]]; then
  cp -a "$tmp/out"/. "$MT2_ROOT"/
else
  # nested single folder
  inner="$(find "$tmp/out" -maxdepth 1 -mindepth 1 -type d | head -n1)"
  cp -a "$inner"/. "$MT2_ROOT"/
fi

mkdir -p "${MT2_ROOT}/BepInEx/plugins" \
  "${MT2_ROOT}/BepInEx/config" \
  "${MT2_ROOT}/BepInEx/patchers"

echo "Installed BepInEx ${BEPINEX_VERSION} into:"
echo "  ${MT2_ROOT}"
echo "Launch MT2 once (via Steam/Proton) to finish BepInEx setup, then drop mods into:"
echo "  ${MT2_ROOT}/BepInEx/plugins"
