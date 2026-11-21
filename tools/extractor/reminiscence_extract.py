"""Extract Monster Train 1 field music from a local install.

The Windows build of this tool is frozen with PyInstaller. Monster Train 1
stores these cues as FSB5 AudioClips, so playback bytes come from the FMOD
Engine runtime shipped beside UnityPy. See LICENSE.md.
"""

from __future__ import annotations

import argparse
import hashlib
import json
import os
import re
import struct
import sys
from dataclasses import dataclass
from pathlib import Path

from UnityPy import Environment
from UnityPy.helpers.ResourceReader import get_resource_data


ASSETS_RELATIVE = Path("MonsterTrain_Data") / "sharedassets0.assets"
RESOURCE_NAME = "sharedassets0.resource"
LOCALIZATION_RELATIVE = Path("MonsterTrain_Data") / "resources.assets"
MANIFEST_NAME = "manifest.json"
MANIFEST_VERSION = 2

FSB_HEADER_SIZE = 60
FSB_FREQUENCIES = {1: 8000, 2: 11000, 3: 11025, 4: 16000, 5: 22050, 6: 24000, 7: 32000, 8: 44100, 9: 48000}
FSB_CHUNK_LOOP = 3
FSB_CHUNK_FREQUENCY = 2


@dataclass(frozen=True)
class Layer:
    layer_id: str
    asset_name: str
    file_stem: str


@dataclass(frozen=True)
class Piece:
    piece_id: str
    music_id: str
    layers: tuple[Layer, ...]


# Initial Descent is the Limbo ring theme. The ignited layer is Battle_Limbo_High.
PIECES = (
    Piece(
        "initial-descent",
        "BattleMusic_Limbo",
        (
            Layer("base", "Battle_Limbo", "initial-descent"),
            Layer("ignited", "Battle_Limbo_High", "initial-descent-ignited"),
        ),
    ),
)


def main() -> int:
    parser = argparse.ArgumentParser(description="Extract Reminiscence field music from a local MT1 install.")
    parser.add_argument("--mt1", help="Monster Train 1 install directory. Defaults to MT1_ROOT, then Steam library search.")
    parser.add_argument(
        "--out",
        default="reminiscence-music",
        help="Cache directory for the extracted wav files and manifest (default: ./reminiscence-music).",
    )
    args = parser.parse_args()

    mt1 = resolve_mt1(args.mt1)
    if mt1 is None:
        if args.mt1:
            print(f"MT1 not found at {args.mt1}.", file=sys.stderr)
        else:
            print("MT1 not found. Pass --mt1 or set MT1_ROOT to the Monster Train install directory.", file=sys.stderr)
        return 2

    assets = mt1 / ASSETS_RELATIVE
    resource = assets.with_name(RESOURCE_NAME)
    localization = mt1 / LOCALIZATION_RELATIVE
    if not assets.is_file() or not resource.is_file() or not localization.is_file():
        print(f"MT1 audio source is missing under {mt1}.", file=sys.stderr)
        print(f"Expected {assets}, {resource}, and {localization}.", file=sys.stderr)
        return 2

    out = Path(args.out)
    out.mkdir(parents=True, exist_ok=True)
    source = {
        "assets": file_record(assets),
        "resource": file_record(resource),
        "localization": file_record(localization),
    }
    if cache_is_current(out, source):
        print(f"Music cache is current at {out}")
        return 0

    wanted = {layer.asset_name for piece in PIECES for layer in piece.layers}
    found = find_clips(assets, wanted)
    missing = sorted(wanted - set(found))
    if missing:
        print("MT1 audio clips not found: " + ", ".join(missing), file=sys.stderr)
        return 3

    pieces = []
    for piece in PIECES:
        name = localized_name(localization, piece.music_id)
        layers = []
        for layer in piece.layers:
            info = fsb_info(found[layer.asset_name])
            data = clip_bytes(found[layer.asset_name])
            dest = out / f"{layer.file_stem}.wav"
            dest.write_bytes(data)
            layers.append(
                {
                    "id": layer.layer_id,
                    "asset": layer.asset_name,
                    "file": dest.name,
                    "bytes": len(data),
                    "sha256": hashlib.sha256(data).hexdigest(),
                    "sampleRate": info["sampleRate"],
                    "channels": info["channels"],
                    "samples": info["samples"],
                    "loop": info["loop"],
                }
            )
            print(f"Wrote {dest} ({len(data)} bytes) from {layer.asset_name}")
        loop = shared_loop(layers)
        if any(layer["samples"] != layers[0]["samples"] or layer["sampleRate"] != layers[0]["sampleRate"] for layer in layers[1:]):
            raise RuntimeError(f"{piece.music_id} layers do not share a length")
        pieces.append(
            {
                "id": piece.piece_id,
                "musicId": piece.music_id,
                "name": name,
                "sampleRate": layers[0]["sampleRate"],
                "channels": layers[0]["channels"],
                "samples": layers[0]["samples"],
                "durationSeconds": layers[0]["samples"] / layers[0]["sampleRate"],
                "loop": loop,
                "layers": layers,
            }
        )
        print(
            f"{name}: loop {loop['startSeconds']:.3f}s to {loop['endSeconds']:.3f}s "
            f"({loop['startSample']} to {loop['endSample']} samples)"
        )

    manifest = {"version": MANIFEST_VERSION, "mt1": str(mt1), "source": source, "pieces": pieces}
    (out / MANIFEST_NAME).write_text(json.dumps(manifest, indent=2) + "\n", encoding="utf-8")
    print(f"Wrote {out / MANIFEST_NAME}")
    return 0


def resolve_mt1(explicit: str | None) -> Path | None:
    if explicit:
        path = host_path(explicit)
        if (path / "MonsterTrain.exe").is_file():
            return path
        return None
    candidates: list[str] = []
    if os.environ.get("MT1_ROOT"):
        candidates.append(os.environ["MT1_ROOT"])
    for steam in steam_roots():
        libraries = [steam, *steam_library_roots(steam)]
        for library in libraries:
            candidates.append(str(library / "steamapps" / "common" / "Monster Train"))
    seen: set[str] = set()
    for candidate in candidates:
        path = host_path(candidate)
        key = str(path).lower()
        if key in seen:
            continue
        seen.add(key)
        if (path / "MonsterTrain.exe").is_file():
            return path
    return None


def steam_roots() -> list[Path]:
    raw = []
    if os.environ.get("STEAM_ROOT"):
        raw.append(os.environ["STEAM_ROOT"])
    home = Path.home()
    raw.extend(
        [
            str(home / ".steam" / "steam"),
            str(home / ".steam" / "root"),
            str(home / ".local" / "share" / "Steam"),
            "/mnt/steam",
            r"C:\Program Files (x86)\Steam",
            r"C:\Program Files\Steam",
        ]
    )
    roots = []
    seen: set[str] = set()
    for item in raw:
        path = host_path(item)
        key = str(path).lower()
        if key in seen or not path.is_dir():
            continue
        seen.add(key)
        roots.append(path)
    return roots


def steam_library_roots(steam: Path) -> list[Path]:
    libraries = []
    for relative in (Path("steamapps") / "libraryfolders.vdf", Path("config") / "libraryfolders.vdf"):
        vdf = steam / relative
        if not vdf.is_file():
            continue
        text = vdf.read_text(encoding="utf-8", errors="ignore")
        for match in re.finditer(r'"path"\s+"([^"]+)"', text):
            libraries.append(host_path(match.group(1).replace("\\\\", "\\")))
    return libraries


def host_path(value: str) -> Path:
    """Map a POSIX path into Wine's Z: drive when this process is Windows."""
    path = Path(value)
    if path.exists():
        return path
    if sys.platform == "win32" and value.startswith("/"):
        translated = Path("Z:\\") / value.lstrip("/").replace("/", "\\")
        if translated.exists():
            return translated
    return path


def find_clips(assets: Path, names: set[str]) -> dict:
    env = Environment(str(assets))
    found = {}
    for obj in env.objects:
        if obj.type.name != "AudioClip":
            continue
        tree = obj.read_typetree()
        name = tree.get("m_Name")
        if name in names:
            found[name] = obj
            if len(found) == len(names):
                break
    return found


def fsb_info(obj) -> dict:
    """Read sample rate, length, and the loop window from an FSB5 AudioClip.

    CoreAudioSystem plays these cues with AudioSource.loop set, and these
    FSB5 headers have no partial LOOP chunk, so the window is the whole clip.
    """
    tree = obj.read_typetree()
    resource = tree["m_Resource"]
    raw = get_resource_data(resource["m_Source"], obj.assets_file, resource["m_Offset"], resource["m_Size"])
    if raw[:4] != b"FSB5":
        raise RuntimeError(f"{tree.get('m_Name')} is not an FSB5 clip")
    _magic, _version, count, _header_bytes, _name_bytes, _data_bytes, _mode = struct.unpack_from("<4sIIIIII", raw, 0)
    if count != 1:
        raise RuntimeError(f"{tree.get('m_Name')} contains {count} FSB samples")
    offset = FSB_HEADER_SIZE
    info = struct.unpack_from("<Q", raw, offset)[0]
    offset += 8
    next_chunk = bits(info, 0, 1)
    frequency = FSB_FREQUENCIES.get(bits(info, 1, 4))
    channels = bits(info, 5, 1) + 1
    samples = bits(info, 34, 30)
    loop_start = 0
    loop_end = samples
    while next_chunk:
        chunk = struct.unpack_from("<I", raw, offset)[0]
        offset += 4
        next_chunk = bits(chunk, 0, 1)
        chunk_size = bits(chunk, 1, 24)
        chunk_type = bits(chunk, 25, 7)
        payload = raw[offset : offset + chunk_size]
        offset += chunk_size
        if chunk_type == FSB_CHUNK_FREQUENCY and len(payload) >= 4:
            frequency = struct.unpack_from("<I", payload)[0]
        elif chunk_type == FSB_CHUNK_LOOP and len(payload) >= 8:
            loop_start, loop_end = struct.unpack_from("<II", payload)
    if not frequency:
        raise RuntimeError(f"{tree.get('m_Name')} has no sample rate")
    return {
        "sampleRate": frequency,
        "channels": channels,
        "samples": samples,
        "loop": loop_window(loop_start, loop_end, frequency),
    }


def bits(value: int, start: int, length: int) -> int:
    return (value >> start) & ((1 << length) - 1)


def loop_window(start_sample: int, end_sample: int, sample_rate: int) -> dict:
    return {
        "startSample": start_sample,
        "endSample": end_sample,
        "startSeconds": start_sample / sample_rate,
        "endSeconds": end_sample / sample_rate,
    }


def shared_loop(layers: list[dict]) -> dict:
    loops = [layer["loop"] for layer in layers]
    if any(loop != loops[0] for loop in loops[1:]):
        raise RuntimeError("Music layers do not share a loop window")
    return loops[0]


def localized_name(resources_assets: Path, term: str) -> str:
    """English name for an I2 localization term. MT1 stores English at index 0."""
    env = Environment(str(resources_assets))
    key = term.encode("utf-8")
    pattern = struct.pack("<i", len(key)) + key
    for obj in env.objects:
        if obj.type.name != "MonoBehaviour":
            continue
        raw = obj.get_raw_data()
        index = raw.find(pattern)
        if index < 0:
            continue
        _term, offset = read_string(raw, index)
        _term_type, offset = read_i32(raw, offset)
        _description, offset = read_string(raw, offset)
        count, offset = read_i32(raw, offset)
        if count < 1:
            break
        name, _offset = read_string(raw, offset)
        if name:
            return name
    raise RuntimeError(f"No localized name for {term}")


def read_i32(data: bytes, offset: int) -> tuple[int, int]:
    return struct.unpack_from("<i", data, offset)[0], offset + 4


def read_string(data: bytes, offset: int) -> tuple[str, int]:
    length, offset = read_i32(data, offset)
    if length < 0 or offset + length > len(data):
        raise RuntimeError("Invalid string in MT1 localization data")
    text = data[offset : offset + length].decode("utf-8")
    offset = (offset + length + 3) & ~3
    return text, offset


def clip_bytes(obj) -> bytes:
    samples = obj.parse_as_object().samples
    if len(samples) != 1:
        raise RuntimeError(f"Expected one audio sample, found {len(samples)}")
    data = next(iter(samples.values()))
    if data[:4] != b"RIFF":
        raise RuntimeError("Expected a WAV sample from the FSB5 clip")
    return data


def file_record(path: Path) -> dict:
    return {"path": str(path), "bytes": path.stat().st_size, "sha256": sha256(path)}


def sha256(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as handle:
        for chunk in iter(lambda: handle.read(1024 * 1024), b""):
            digest.update(chunk)
    return digest.hexdigest()


def cache_is_current(out: Path, source: dict) -> bool:
    manifest_path = out / MANIFEST_NAME
    if not manifest_path.is_file():
        return False
    try:
        manifest = json.loads(manifest_path.read_text(encoding="utf-8"))
    except json.JSONDecodeError:
        return False
    if manifest.get("version") != MANIFEST_VERSION or manifest.get("source") != source:
        return False
    pieces = {item.get("id"): item for item in manifest.get("pieces", [])}
    for piece in PIECES:
        item = pieces.get(piece.piece_id)
        if not item or "name" not in item or "loop" not in item:
            return False
        layers = {layer.get("id"): layer for layer in item.get("layers", [])}
        for layer in piece.layers:
            recorded = layers.get(layer.layer_id)
            if not recorded:
                return False
            file = out / recorded.get("file", "")
            if not file.is_file() or file.stat().st_size == 0:
                return False
    return True


if __name__ == "__main__":
    try:
        raise SystemExit(main())
    except RuntimeError as exc:
        print(str(exc), file=sys.stderr)
        raise SystemExit(1)
