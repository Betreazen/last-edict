#!/usr/bin/env python3
"""build_sound_assets.py — звуки игры (Sounds/*.wdb) в обычные файлы + опись для порта.

Разбор формата — docs/ai-screen.md §9 (ранг 1 по файлам): .wdb — контейнер MQDB, как .ff; запись 1 — оглавление
«WDB\\0» + u32 (2) + N × [u32 id записи][char name[16]][u32 ?]; прочие записи — целые файлы RIFF WAVE: почти все с MP3
внутри (формат 0x55), остальные — PCM 16 бит. Godot читает MP3 из буфера (AudioStreamMP3), а WAV — только PCM/ADPCM/QOA,
поэтому:

  assets/sound/<Архив>/<ИМЯ>.mp3   — байты чанка data записи MP3, без перекодирования
  assets/sound/<Архив>/<ИМЯ>.wav   — PCM: заново собранный WAV из fmt и data (без cue/LIST)
  data/sound/<Архив>.json          — опись: имя, файл, кодек, частота, каналы, число сэмплов, длительность, хеш

Звуки юнитов — Sounds/Battle.wdt (это .ff, не звук): SNDINFO.DAT (345 × 192 байта: id юнита, замах A и его кадр, кадр удара,
эффект B и его кадр, удар по цели C, «больно» D, шаг E) и списки вариантов *.LST («имя\0, u32 n, n × имя\0») — в
data/sound/UnitSounds.json (разбор 0x5c0be8/0x5c0fcc, docs/ai-screen.md §12).

Музыка — каталог Music/ игры (75 отдельных WAV с MP3 внутри) — тем же путём как «архив» Music (assets/sound/Music, опись
data/sound/Music.json; имя — имя файла без расширения, в верхнем регистре).

Каталог файлов — D2_ASSETS_DIR/sound (иначе assets/sound рядом с репозиторием), в git он не входит; опись коммитится.
Игра — из D2_GAME_DIR (по умолчанию game-pristine репозитория моддинга).

Запуск:
  python tools/sound-assets/build_sound_assets.py            # все .wdb
  python tools/sound-assets/build_sound_assets.py Midgard    # выборочно (Music — музыка)
"""
from __future__ import annotations

import hashlib
import json
import os
import struct
import sys
from pathlib import Path

DEFAULT_GAME_DIR = Path(__file__).resolve().parents[3] / "disciples-modding" / "game-pristine"  # рядом с репозиторием
SHA_LEN = 16
MP3, PCM = 0x55, 1


def records(data: bytes) -> dict[int, tuple[int, int]]:
    """Записи MQDB: id → (смещение данных, размер). Цепочка MQRC после 28 байт заголовка."""
    if data[:4] != b"MQDB":
        raise ValueError("не архив MQDB")
    pos, out = 28, {}
    while pos + 28 <= len(data) and data[pos:pos + 4] == b"MQRC":
        rid, size, real = struct.unpack_from("<3i", data, pos + 8)
        out[rid] = (pos + 28, size)
        pos += 28 + real
    return out


def table(data: bytes, recs: dict[int, tuple[int, int]]) -> list[tuple[str, int]]:
    """Оглавление WDB (запись 1): имя (до первого нуля — хвост после него мусорный) и id записи."""
    off, size = recs[1]
    if data[off:off + 4] != b"WDB\0":
        raise ValueError("нет оглавления WDB")
    out = []
    for i in range((size - 8) // 24):
        p = off + 8 + (i * 24)
        rid = struct.unpack_from("<I", data, p)[0]
        name = data[p + 4:p + 20].split(b"\0")[0].decode("ascii")
        out.append((name, rid))
    return out


def chunks(wav: bytes) -> dict[str, tuple[int, int]]:
    if wav[:4] != b"RIFF" or wav[8:12] != b"WAVE":
        raise ValueError("не RIFF WAVE")
    pos, out = 12, {}
    while pos + 8 <= len(wav):
        cid = wav[pos:pos + 4].decode("latin1")
        size = struct.unpack_from("<I", wav, pos + 4)[0]
        out.setdefault(cid, (pos + 8, size))
        pos += 8 + size + (size & 1)
    return out


def convert(wav: bytes) -> tuple[str, bytes, dict]:
    """Запись → (расширение, байты файла, описание)."""
    c = chunks(wav)
    fo, _ = c["fmt "]
    tag, channels, rate, avg, align, bits = struct.unpack_from("<HHIIHH", wav, fo)
    do, dsize = c["data"]
    body = wav[do:do + dsize]
    if tag == MP3:
        samples = struct.unpack_from("<I", wav, c["fact"][0])[0] if "fact" in c else 0
        return "mp3", body, {"codec": "mp3", "rate": rate, "channels": channels, "samples": samples}
    if tag == PCM:
        fmt = struct.pack("<HHIIHH", PCM, channels, rate, avg, align, bits)
        riff = b"WAVE" + b"fmt " + struct.pack("<I", len(fmt)) + fmt + b"data" + struct.pack("<I", len(body)) + body
        samples = len(body) // max(1, align)
        return "wav", b"RIFF" + struct.pack("<I", len(riff)) + riff, {"codec": "pcm16", "rate": rate, "channels": channels,
                                                                        "samples": samples}
    raise ValueError(f"формат {tag:#x} не поддержан")


def build(archive: Path, out_root: Path) -> dict:
    data = archive.read_bytes()
    recs = records(data)
    out_dir = out_root / archive.stem
    sounds = []
    if recs.get(1, (0, 0))[1] > 8:
        out_dir.mkdir(parents=True, exist_ok=True)
        for name, rid in table(data, recs):
            try:
                off, size = recs[rid]
                wav = data[off:off + size]
                ext, body, info = convert(wav)
            except (KeyError, ValueError, struct.error) as e:
                print(f"{archive.name}: {name} пропущен — {e}", file=sys.stderr)
                continue
            (out_dir / f"{name}.{ext}").write_bytes(body)
            seconds = round(info["samples"] / info["rate"], 3) if info["rate"] else 0
            sounds.append({"name": name, "file": f"{archive.stem}/{name}.{ext}", **info, "seconds": seconds,
                           "sha256": hashlib.sha256(wav).hexdigest()[:SHA_LEN]})
    return {"archive": archive.name, "sounds": sounds}


def build_music(folder: Path, out_root: Path) -> dict:
    """Каталог Music/: каждый файл — целый RIFF WAVE."""
    out_dir = out_root / "Music"
    out_dir.mkdir(parents=True, exist_ok=True)
    sounds = []
    for path in sorted(folder.glob("*.wav"), key=lambda p: p.name.upper()):
        name = path.stem.upper()
        wav = path.read_bytes()
        try:
            ext, body, info = convert(wav)
        except (KeyError, ValueError, struct.error) as e:
            print(f"Music: {path.name} пропущен — {e}", file=sys.stderr)
            continue
        (out_dir / f"{name}.{ext}").write_bytes(body)
        seconds = round(info["samples"] / info["rate"], 3) if info["rate"] else 0
        sounds.append({"name": name, "file": f"Music/{name}.{ext}", **info, "seconds": seconds,
                       "sha256": hashlib.sha256(wav).hexdigest()[:SHA_LEN]})
    return {"archive": "Music", "sounds": sounds}


def cstr(data: bytes, at: int) -> str:
    return data[at:].split(b"\0", 1)[0].decode("latin1")


def build_unit_sounds(wdt: Path) -> dict:
    """Battle.wdt: звуки юнитов (SNDINFO.DAT) и списки вариантов (*.LST)."""
    sys.path.insert(0, str(Path(__file__).resolve().parents[1] / "ui-assets"))
    from ff_extract import FFArchive  # noqa: E402  (копия в tools/ui-assets)
    archive = FFArchive(wdt)
    lists = {}
    units = {}
    for entry in archive.entries:
        raw = archive.raw(entry)
        name = entry.name.upper()
        if name.endswith(".LST"):
            pos = raw.index(b"\0") + 1
            count = struct.unpack_from("<I", raw, pos)[0]
            pos += 4
            items = []
            for _ in range(count):
                end = raw.index(b"\0", pos)
                items.append(raw[pos:end].decode("latin1").upper())
                pos = end + 1
            lists[name[:-4]] = items
        elif name == "SNDINFO.DAT":
            count = struct.unpack_from("<I", raw, 0)[0]
            for i in range(count):
                r = raw[4 + i * 192: 4 + (i + 1) * 192]
                uid = struct.unpack_from("<I", r, 0)[0]
                unit = f"G000UU{uid & 0xFFFF:04X}"
                units[unit] = {
                    "attack": cstr(r, 0x04).upper(), "attackFrame": struct.unpack_from("<i", r, 0x28)[0],
                    "hitFrame": struct.unpack_from("<i", r, 0x2C)[0],
                    "effect": cstr(r, 0x30).upper(), "effectFrame": struct.unpack_from("<i", r, 0x54)[0],
                    "hit": cstr(r, 0x5C).upper(), "hurt": cstr(r, 0x7D).upper(), "walk": cstr(r, 0x9E).upper(),
                }
    return {"units": dict(sorted(units.items())), "lists": dict(sorted(lists.items()))}


def write_manifest(path: Path, doc: dict) -> None:
    """По записи на строку — читаемый diff в git."""
    lines = [f'{{"archive":{json.dumps(doc["archive"])},', '"sounds":[']
    lines.append(",\n".join(json.dumps(s, ensure_ascii=False, separators=(",", ":")) for s in doc["sounds"]))
    lines.append("]}\n")
    path.write_text("\n".join(lines), encoding="utf-8")


def main(argv: list[str]) -> int:
    game = Path(os.environ.get("D2_GAME_DIR") or DEFAULT_GAME_DIR)
    repo = Path(__file__).resolve().parents[2]
    out_root = Path(os.environ["D2_ASSETS_DIR"]) / "sound" if os.environ.get("D2_ASSETS_DIR") else repo / "assets" / "sound"
    data_dir = repo / "data" / "sound"
    wanted = {a.lower() for a in argv[1:]}
    archives = sorted(p for p in (game / "Sounds").glob("*.wdb") if not wanted or p.stem.lower() in wanted)
    music = not wanted or "music" in wanted
    if not archives and not music:
        print(f"нет архивов в {game / 'Sounds'}", file=sys.stderr)
        return 1
    data_dir.mkdir(parents=True, exist_ok=True)
    for archive in archives:
        doc = build(archive, out_root)
        if not doc["sounds"]:
            print(f"{archive.name}: оглавление пустое — пропуск")
            continue
        write_manifest(data_dir / f"{archive.stem}.json", doc)
        kinds = {k: sum(1 for s in doc["sounds"] if s["codec"] == k) for k in ("mp3", "pcm16")}
        minutes = sum(s["seconds"] for s in doc["sounds"]) / 60
        print(f"{archive.name}: звуков {len(doc['sounds'])} (mp3 {kinds['mp3']}, pcm {kinds['pcm16']}), {minutes:.1f} мин -> {out_root / archive.stem}")
    if (not wanted or "battle" in wanted) and (game / "Sounds" / "Battle.wdt").is_file():
        doc = build_unit_sounds(game / "Sounds" / "Battle.wdt")
        lines = ['{"units":{']
        lines.append(",\n".join(f"{json.dumps(k)}:{json.dumps(v, separators=(',', ':'))}" for k, v in doc["units"].items()))
        lines.append('},\n"lists":{')
        lines.append(",\n".join(f"{json.dumps(k)}:{json.dumps(v)}" for k, v in doc["lists"].items()))
        lines.append("}}\n")
        (data_dir / "UnitSounds.json").write_text("\n".join(lines), encoding="utf-8")
        print(f"Battle.wdt: юнитов {len(doc['units'])}, списков {len(doc['lists'])} -> data/sound/UnitSounds.json")
    if music and (game / "Music").is_dir():
        doc = build_music(game / "Music", out_root)
        write_manifest(data_dir / "Music.json", doc)
        minutes = sum(s["seconds"] for s in doc["sounds"]) / 60
        print(f"Music: дорожек {len(doc['sounds'])}, {minutes:.1f} мин -> {out_root / 'Music'}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main(sys.argv))
