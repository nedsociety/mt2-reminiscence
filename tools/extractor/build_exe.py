"""Freeze reminiscence_extract.py into ReminiscenceExtract.exe.

Native Windows CPython and the container's wine-python both run this.
"""

from __future__ import annotations

import argparse
import os
import subprocess
import sys
from pathlib import Path


ROOT = Path(__file__).resolve().parent
COLLECT = (
    "UnityPy",
    "fmod_toolkit",
    "pyfmodex",
    "texture2ddecoder",
    "etcpak",
    "astc_encoder",
    "archspec",
)


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--dist", type=Path, default=ROOT / "dist")
    args = parser.parse_args()

    import archspec

    archspec_json = Path(archspec.__file__).resolve().parent / "json"
    if not archspec_json.is_dir():
        print(f"archspec data is missing at {archspec_json}", file=sys.stderr)
        return 1

    dist = args.dist.resolve()
    dist.mkdir(parents=True, exist_ok=True)
    command = [
        sys.executable,
        "-m",
        "PyInstaller",
        "--noconfirm",
        "--onefile",
        "--name",
        "ReminiscenceExtract",
        "--distpath",
        str(dist),
        "--workpath",
        str(ROOT / "build"),
        "--specpath",
        str(ROOT),
    ]
    for package in COLLECT:
        command.extend(["--collect-all", package])
    command.extend(
        [
            "--add-data",
            f"{archspec_json}{os.pathsep}archspec/json",
            str(ROOT / "reminiscence_extract.py"),
        ]
    )
    subprocess.check_call(command, cwd=ROOT)
    print(f"Built {dist / 'ReminiscenceExtract.exe'}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
