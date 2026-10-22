#!/usr/bin/env python3
"""build_videos.py — ролики игры (Bink .BIK) в Ogg Theora (.ogv) + опись для порта.

Порт не должен в рантайме знать Bink. Ролики переводятся один раз этим инструментом:

  assets/video/MenuAnim/<ИМЯ>.ogv   — 52 записи Interf/MenuAnim.ff (переходы меню, смена расы, лица лордов)
  assets/video/Briefing/<ИМЯ>.ogv   — Briefing/*.bik (брифинги кампаний, заставки побед и поражений)
  assets/video/Briefing/<ИМЯ>.ogg   — Briefing/briefel*.wav (озвучка брифингов кампании эльфов, звук без картинки)
  assets/video/Video/<ИМЯ>.ogv      — Video/*.bik (вступление, титры, концовки)
  data/video/videos.json            — опись, она и коммитится

Опись — список записей: имя (ВЕРХНИЙ регистр, без расширения), источник, путь от assets/video, размер, частота
кадров, число кадров, длительность, есть ли звук, короткий sha256 ИСХОДНОГО файла. Все числа берутся из исходника
(ffprobe по .BIK), а не из результата: опись воспроизводится байт-в-байт при любой версии ffmpeg.
У звуковых записей (.ogg) картинки нет: w = h = fps = frames = 0.

Игра берётся из D2_GAME_DIR (по умолчанию — game-pristine рядом с репозиторием моддинга). ffmpeg и ffprobe —
из переменных FFMPEG и FFPROBE (по умолчанию из PATH). Перед работой кодировщик Theora проверяется пробным
роликом: сборка ffmpeg 8.1 (gyan.dev) пишет межкадровые блоки Theora так, что их не читает ни декодер ffmpeg,
ни Godot, — такой ffmpeg инструмент не возьмёт.

Запуск:
  python tools/video-import/build_videos.py
"""
from __future__ import annotations

import hashlib
import json
import os
import subprocess
import sys
import tempfile
import time
from concurrent.futures import ThreadPoolExecutor
from dataclasses import dataclass
from fractions import Fraction
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / "ui-assets"))
from ff_extract import FFArchive  # noqa: E402  (распаковщик MQDB из ui-assets, без изменений)

DEFAULT_GAME_DIR = Path(__file__).resolve().parents[3] / "disciples-modding" / "game-pristine"  # рядом с репозиторием
SHA_LEN = 16
# Качество Theora 0..10: 8 — без заметных на глаз потерь (сверка с Bink — PSNR ≥ 35 дБ на кадр в Godot).
VIDEO_Q = "8"
AUDIO_Q = "6"  # Vorbis 0..10, ≈192 кбит/с для 44.1 кГц стерео
SELF_TEST_FRAMES = 30


@dataclass(frozen=True)
class Job:
    name: str        # ВЕРХНИЙ регистр, без расширения
    source: str      # MenuAnim | Briefing | Video
    src: Path        # исходный файл (.BIK или .wav)
    is_audio: bool   # звук без картинки (.wav → .ogg)

    @property
    def rel(self) -> str:
        return f"{self.source}/{self.name}.{'ogg' if self.is_audio else 'ogv'}"


def tool(var: str, default: str) -> str:
    return os.environ.get(var) or default


def run(args: list[str]) -> subprocess.CompletedProcess:
    return subprocess.run(args, capture_output=True, text=True, encoding="utf-8", errors="replace")


def check_encoder(ffmpeg: str, tmp: Path) -> str | None:
    """Пробный ролик с движением: кодируем в Theora и читаем обратно. Любая ошибка декодера — кодировщик негоден."""
    probe = tmp / "selftest.ogv"
    enc = run([ffmpeg, "-v", "error", "-y", "-f", "lavfi",
               "-i", f"testsrc=size=320x240:rate=15:duration={SELF_TEST_FRAMES / 15}",
               "-c:v", "libtheora", "-q:v", VIDEO_Q, str(probe)])
    if enc.returncode != 0:
        return f"ffmpeg не кодирует Theora: {enc.stderr.strip()[:300]}"
    dec = run([ffmpeg, "-v", "error", "-i", str(probe), "-f", "null", "-"])
    if dec.returncode != 0 or dec.stderr.strip():
        return ("кодировщик libtheora этого ffmpeg пишет нечитаемые межкадровые блоки "
                f"({dec.stderr.strip().splitlines()[0][:120]}); укажите другой ffmpeg в FFMPEG")
    return None


def probe(ffprobe: str, path: Path, is_audio: bool) -> dict:
    """Размер, частота кадров, число кадров, длительность, звук — из исходника."""
    if is_audio:
        # Внутри .wav — MP3, и длительность из заголовка (по средней скорости) короче на ≈0.5 с.
        # Точная — сумма длительностей пакетов в единицах времени потока.
        r = run([ffprobe, "-v", "error", "-select_streams", "a:0", "-show_entries",
                 "stream=time_base:packet=duration", "-of", "json", str(path)])
        info = json.loads(r.stdout)
        ticks = sum(int(p["duration"]) for p in info["packets"])
        dur = float(ticks * Fraction(info["streams"][0]["time_base"]))
        return {"w": 0, "h": 0, "fps": 0, "frames": 0, "duration": round(dur, 3), "audio": True}
    r = run([ffprobe, "-v", "error", "-count_packets", "-show_entries",
             "stream=codec_type,width,height,r_frame_rate,nb_read_packets", "-of", "json", str(path)])
    if r.returncode != 0:
        raise RuntimeError(f"{path.name}: ffprobe: {r.stderr.strip()[:200]}")
    streams = json.loads(r.stdout)["streams"]
    v = next(s for s in streams if s["codec_type"] == "video")
    rate = Fraction(v["r_frame_rate"])
    # Кадр Bink — пакет. В .ogv неизменный кадр пишется пустым пакетом (повтор предыдущего), поэтому сверять
    # результат надо тоже по пакетам: декодер ffmpeg пустые пакеты кадрами не считает.
    frames = int(v["nb_read_packets"])
    fps = round(float(rate), 3)
    return {
        "w": int(v["width"]), "h": int(v["height"]),
        "fps": int(fps) if fps == int(fps) else fps,
        "frames": frames,
        "duration": round(float(frames / rate), 3),
        "audio": any(s["codec_type"] == "audio" for s in streams),
    }


def encode(ffmpeg: str, job: Job, meta: dict, out: Path) -> None:
    out.parent.mkdir(parents=True, exist_ok=True)
    if job.is_audio:
        codec = ["-map", "0:a:0", "-c:a", "libvorbis", "-q:a", AUDIO_Q]
    else:
        codec = ["-map", "0:v:0", "-c:v", "libtheora", "-q:v", VIDEO_Q]
        if meta["audio"]:
            codec += ["-map", "0:a:0", "-c:a", "libvorbis", "-q:a", AUDIO_Q]
    r = run([ffmpeg, "-v", "error", "-y", "-i", str(job.src), *codec, "-map_metadata", "-1", str(out)])
    if r.returncode != 0 or not out.is_file():
        raise RuntimeError(f"{job.rel}: ffmpeg: {r.stderr.strip()[:300]}")


def collect(game: Path, tmp: Path) -> list[Job]:
    """Все ролики трёх источников. Записи MenuAnim.ff выкладываются во временный каталог: ffmpeg читает файл."""
    jobs: list[Job] = []
    ff = FFArchive(game / "Interf" / "MenuAnim.ff")
    for e in ff.entries:
        stem, ext = os.path.splitext(e.name)
        if ext.upper() != ".BIK":
            continue
        raw = tmp / "MenuAnim" / e.name
        raw.parent.mkdir(parents=True, exist_ok=True)
        raw.write_bytes(ff.raw(e))
        jobs.append(Job(stem.upper(), "MenuAnim", raw, False))
    for source in ("Briefing", "Video"):
        for p in sorted((game / source).iterdir()):
            ext = p.suffix.lower()
            if ext in (".bik", ".wav"):
                jobs.append(Job(p.stem.upper(), source, p, ext == ".wav"))
    return jobs


def build_one(ffmpeg: str, ffprobe: str, job: Job, assets: Path) -> dict:
    meta = probe(ffprobe, job.src, job.is_audio)
    encode(ffmpeg, job, meta, assets / job.rel)
    return {"name": job.name, "source": job.source, "file": job.rel, **meta,
            "sha256": hashlib.sha256(job.src.read_bytes()).hexdigest()[:SHA_LEN]}


def main() -> int:
    game = Path(os.environ.get("D2_GAME_DIR") or DEFAULT_GAME_DIR)
    repo = Path(__file__).resolve().parents[2]
    assets = repo / "assets" / "video"
    manifest_path = repo / "data" / "video" / "videos.json"
    ffmpeg, ffprobe = tool("FFMPEG", "ffmpeg"), tool("FFPROBE", "ffprobe")

    missing = [p for p in ("Interf/MenuAnim.ff", "Briefing", "Video") if not (game / p).exists()]
    if missing:
        print(f"нет в {game}: {', '.join(missing)}", file=sys.stderr)
        return 1

    t0 = time.perf_counter()
    with tempfile.TemporaryDirectory(prefix="d2-video-") as td:
        tmp = Path(td)
        err = check_encoder(ffmpeg, tmp)
        if err:
            print(f"ОШИБКА: {err}", file=sys.stderr)
            return 1
        jobs = collect(game, tmp)
        dup = len(jobs) - len({(j.source, j.name) for j in jobs})
        if dup:
            print(f"ОШИБКА: {dup} повторов пары источник+имя", file=sys.stderr)
            return 1
        # libtheora однопоточный — ролики кодируются параллельно, по одному на ядро.
        with ThreadPoolExecutor(max_workers=os.cpu_count() or 4) as pool:
            rows = list(pool.map(lambda j: build_one(ffmpeg, ffprobe, j, assets), jobs))

    rows.sort(key=lambda r: (r["source"], r["name"]))
    for source in ("MenuAnim", "Briefing", "Video"):
        part = [r for r in rows if r["source"] == source]
        size = sum((assets / r["file"]).stat().st_size for r in part)
        print(f"{source}: роликов {len(part)}, {size / 2**20:.1f} МиБ")
    manifest_path.parent.mkdir(parents=True, exist_ok=True)
    # По записи на строку: обычный JSON, но читаемый diff в git.
    body = ",\n".join(json.dumps(r, ensure_ascii=False, separators=(",", ":")) for r in rows)
    manifest_path.write_text(f"[\n{body}\n]\n", encoding="utf-8", newline="\n")
    print(f"{manifest_path.relative_to(repo).as_posix()}: {len(rows)} записей, "
          f"{time.perf_counter() - t0:.0f} с")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
