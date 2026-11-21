#!/usr/bin/env bash
# Decompile MT1/MT2 game assemblies with ilspycmd into .extract/*/code
set -euo pipefail

MT1_ROOT="${MT1_ROOT:-/mnt/steam/steamapps/common/Monster Train}"
MT2_ROOT="${MT2_ROOT:-/mnt/steam/steamapps/common/Monster Train 2}"
EXTRACT_ROOT="${EXTRACT_ROOT:-/workspace/.extract}"

mt1_dll="${MT1_ROOT}/MonsterTrain_Data/Managed/Assembly-CSharp.dll"
mt2_dll="${MT2_ROOT}/MonsterTrain2_Data/Managed/Assembly-CSharp.dll"

mkdir -p "${EXTRACT_ROOT}/mt1/code" "${EXTRACT_ROOT}/mt2/code"

if [[ -f "$mt1_dll" ]]; then
  echo "Decompiling MT1 -> ${EXTRACT_ROOT}/mt1/code"
  ilspycmd -p -o "${EXTRACT_ROOT}/mt1/code" "$mt1_dll"
else
  echo "Skip MT1: missing $mt1_dll" >&2
fi

if [[ -f "$mt2_dll" ]]; then
  echo "Decompiling MT2 -> ${EXTRACT_ROOT}/mt2/code"
  ilspycmd -p -o "${EXTRACT_ROOT}/mt2/code" "$mt2_dll"
else
  echo "Skip MT2: missing $mt2_dll" >&2
fi

echo "Done."
