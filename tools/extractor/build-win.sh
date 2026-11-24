#!/usr/bin/env bash
# Freeze reminiscence_extract.py into a win_x64 exe with the Wine CPython.
set -euo pipefail

root="$(cd "$(dirname "$0")" && pwd)"
cd "$root"

if ! command -v wine-python >/dev/null; then
  echo "wine-python is not on PATH. Rebuild the devcontainer." >&2
  exit 1
fi

xvfb-run -a wine-python build_exe.py
