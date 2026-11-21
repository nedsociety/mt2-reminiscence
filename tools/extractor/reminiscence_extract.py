"""Extract Monster Train 1 field and boss music from a local install.

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
MANIFEST_VERSION = 3
TLD_DIRECTORY = (
    Path("MonsterTrain_Data") / "StreamingAssets" / "aa" / "StandaloneWindows64" / "DLC" / "DLC_1"
)
TRACK_PREFIX = "Reminiscence/"

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
    role: str
    high_transition_seconds: float
    layers: tuple[Layer, ...]

    @property
    def track_id(self) -> str:
        return f"{TRACK_PREFIX}{self.music_id}"


def field_piece(piece_id: str, music_id: str, base_asset: str, high_asset: str, high_transition_seconds: float = 0.0) -> Piece:
    return Piece(
        piece_id,
        music_id,
        "field",
        high_transition_seconds,
        (
            Layer("base", base_asset, piece_id),
            Layer("high", high_asset, f"{piece_id}-high"),
        ),
    )


def boss_piece(piece_id: str, music_id: str, asset_name: str) -> Piece:
    return Piece(piece_id, music_id, "boss", 0.0, (Layer("base", asset_name, piece_id),))


# Field cues join MT2's ring 1/2/4/5/7 pool. Boss cues are the MT1 side of a coin flip.
# Layer changes are instant. Warmongers' original 0.25s blend mutes because the snapshots are inverses.
PIECES = (
    field_piece("warmongers", "BattleMusic_Original", "1_Base Layer", "2_High Itensity"),
    field_piece("wildwood", "BattleMusic_Wildwood", "Battle_Wildwood", "Battle_Wildwood_High"),
    field_piece("remnant-waltz", "BattleMusic_WaxenArches", "Battle_WaxArches", "Battle_WaxArches_High"),
    field_piece("lost-to-the-bogs", "BattleMusic_Bogwurm", "Battle_Bogwurm", "Battle_Bogwurm_High"),
    field_piece("crossing-the-styx", "BattleMusic_StyxSea", "Battle_StyxSea", "Battle_Styx_High"),
    field_piece("fallen-lords", "BattleMusic_FallenLords", "Battle_FallenLords", "Battle_FallenLords_High"),
    field_piece("initial-descent", "BattleMusic_Limbo", "Battle_Limbo", "Battle_Limbo_High"),
    field_piece("simple-machines", "BattleMusic_Crucible", "Battle_Crucible", "Battle_Crucible_High"),
    boss_piece("daedalus", "BattleMusic_Daedalus", "BossBattle_Daedalus"),
    boss_piece("talos", "BattleMusic_Talos", "BossBattle_Talos"),
    boss_piece("fel", "BattleMusic_Fel", "BossBattle_Fel"),
    boss_piece("archus", "BattleMusic_Archus", "BossBattle_Archus"),
    boss_piece("seraph", "BattleMusic_Seraph", "BossBattle_Seraph"),
    boss_piece("the-last-divinity", "BattleMusic_TLD", "BossBattle_TLD"),
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
    tld = tld_installed(mt1)
    pieces = active_pieces(tld)
    if not tld:
        print("The Last Divinity bundle is absent. Skipping BattleMusic_TLD.")
    if cache_is_current(out, pieces, tld):
        print(f"Music cache is current at {out}")
        return 0

    wanted = {layer.asset_name for piece in pieces for layer in piece.layers}
    found = find_clips(assets, wanted)
    missing = sorted(wanted - set(found))
    if missing:
        print("MT1 audio clips not found: " + ", ".join(missing), file=sys.stderr)
        return 3

    names = localization_blob(localization)
    written = []
    for piece in pieces:
        name = localized_name(names, piece.music_id)
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
        share_length = all(
            layer["samples"] == layers[0]["samples"] and layer["sampleRate"] == layers[0]["sampleRate"] for layer in layers[1:]
        )
        entry = {
            "id": piece.piece_id,
            "musicId": piece.music_id,
            "trackId": piece.track_id,
            "role": piece.role,
            "name": name,
            "highTransitionSeconds": piece.high_transition_seconds,
            "layersShareLength": share_length,
            "lengthDeltaSamples": max(layer["samples"] for layer in layers) - min(layer["samples"] for layer in layers),
            "layers": layers,
        }
        if share_length:
            entry["sampleRate"] = layers[0]["sampleRate"]
            entry["channels"] = layers[0]["channels"]
            entry["samples"] = layers[0]["samples"]
            entry["durationSeconds"] = layers[0]["samples"] / layers[0]["sampleRate"]
            entry["loop"] = layers[0]["loop"]
        written.append(entry)
        loop = layers[0]["loop"]
        print(
            f"{name}: loop {loop['startSeconds']:.3f}s to {loop['endSeconds']:.3f}s "
            f"({loop['startSample']} to {loop['endSample']} samples)"
        )

    manifest = {
        "version": MANIFEST_VERSION,
        "tld": tld,
        "mt1": str(mt1),
        "source": {
            "assets": file_record(assets),
            "resource": file_record(resource),
            "localization": file_record(localization),
        },
        "pieces": written,
    }
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


def tld_installed(mt1: Path) -> bool:
    """True when the TLD Addressables directory contains a bundle.

    Steam and non-Steam installs both keep that depot at this path. The check
    does not consult Steam.
    """
    folder = mt1 / TLD_DIRECTORY
    if not folder.is_dir():
        return False
    return any(path.is_file() and path.suffix.lower() == ".bundle" for path in folder.iterdir())


def active_pieces(tld: bool) -> tuple[Piece, ...]:
    if tld:
        return PIECES
    return tuple(piece for piece in PIECES if piece.music_id != "BattleMusic_TLD")


def localization_blob(resources_assets: Path) -> bytes:
    env = Environment(str(resources_assets))
    for obj in env.objects:
        if obj.type.name != "MonoBehaviour":
            continue
        raw = obj.get_raw_data()
        if b"BattleMusic_Limbo" in raw:
            return raw
    raise RuntimeError("MT1 localization source has no music names")


def localized_name(raw: bytes, music_id: str) -> str:
    """English name for a music id. MT1 stores English at language index 0."""
    for term in (f"{music_id}_Name", music_id):
        name = read_term(raw, term)
        if name:
            return name
    raise RuntimeError(f"No localized name for {music_id}")


def read_term(raw: bytes, term: str) -> str | None:
    key = term.encode("utf-8")
    pattern = struct.pack("<i", len(key)) + key
    start = 0
    while True:
        index = raw.find(pattern, start)
        if index < 0:
            return None
        start = index + 1
        try:
            found, offset = read_string(raw, index)
            if found != term:
                continue
            _term_type, offset = read_i32(raw, offset)
            _description, offset = read_string(raw, offset)
            count, offset = read_i32(raw, offset)
            if count < 1 or count > 16:
                continue
            name, _offset = read_string(raw, offset)
        except (RuntimeError, UnicodeDecodeError, struct.error):
            continue
        if name:
            return name


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


def cache_is_current(out: Path, pieces: tuple[Piece, ...], tld: bool) -> bool:
    """True when the expected wav files are already present.

    Monster Train 1 is not receiving new builds, so source hashes are not
    compared. A newly installed TLD bundle changes the expected set and is
    extracted on the next run.
    """
    manifest_path = out / MANIFEST_NAME
    if not manifest_path.is_file():
        return False
    try:
        manifest = json.loads(manifest_path.read_text(encoding="utf-8"))
    except json.JSONDecodeError:
        return False
    if manifest.get("version") != MANIFEST_VERSION or manifest.get("tld") is not tld:
        return False
    recorded = {item.get("id"): item for item in manifest.get("pieces", [])}
    if set(recorded) != {piece.piece_id for piece in pieces}:
        return False
    for piece in pieces:
        item = recorded[piece.piece_id]
        if item.get("trackId") != piece.track_id or item.get("role") != piece.role or "name" not in item:
            return False
        layers = {layer.get("id"): layer for layer in item.get("layers", [])}
        if set(layers) != {layer.layer_id for layer in piece.layers}:
            return False
        for layer in piece.layers:
            file = out / layers[layer.layer_id].get("file", "")
            if not file.is_file() or file.stat().st_size == 0:
                return False
    return True


if __name__ == "__main__":
    try:
        raise SystemExit(main())
    except RuntimeError as exc:
        print(str(exc), file=sys.stderr)
        raise SystemExit(1)
