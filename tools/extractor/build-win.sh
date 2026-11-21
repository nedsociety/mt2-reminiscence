#!/usr/bin/env bash
# Freeze reminiscence_extract.py into a win_x64 exe with the Wine CPython.
set -euo pipefail

root="$(cd "$(dirname "$0")" && pwd)"
cd "$root"

if ! command -v wine-python >/dev/null; then
  echo "wine-python is not on PATH. Rebuild the devcontainer." >&2
  exit 1
fi

# archspec (pulled in by astc_encoder) loads JSON via a relative path.
# PyInstaller does not pick those files up from the import graph.
archspec_json="${WINPYTHON}/Lib/site-packages/archspec/json"
if command -v winepath >/dev/null; then
  archspec_json_win="$(winepath -w "$archspec_json")"
else
  archspec_json_win="Z:${archspec_json//\//\\}"
fi

xvfb-run -a wine-python -m PyInstaller \
  --noconfirm \
  --onefile \
  --name ReminiscenceExtract \
  --distpath dist \
  --workpath build \
  --collect-all UnityPy \
  --collect-all fmod_toolkit \
  --collect-all pyfmodex \
  --collect-all texture2ddecoder \
  --collect-all etcpak \
  --collect-all astc_encoder \
  --collect-all archspec \
  --add-data "${archspec_json_win};archspec/json" \
  reminiscence_extract.py

echo "Built ${root}/dist/ReminiscenceExtract.exe"
