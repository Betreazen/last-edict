"""HD через Topaz (tools/hd/hd_layer.py, Р19): выгрузка в одну плоскую папку и обратный импорт слоем мода — на выдуманных кадрах."""
import json
import sys
from pathlib import Path

import pytest
from PIL import Image

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / "tools" / "hd"))
import hd_layer as hd  # noqa: E402


def make(tmp: Path) -> tuple[Path, Path]:
    data, assets = tmp / "data", tmp / "assets"
    (data / "ui").mkdir(parents=True)
    (data / "gfx").mkdir(parents=True)
    # Кадр интерфейса с прозрачным краем.
    frame = Image.new("RGBA", (8, 6), (200, 10, 10, 255))
    for x in range(8):
        frame.putpixel((x, 0), (0, 0, 0, 0))
    (assets / "ui" / "Interf" / "frames").mkdir(parents=True)
    frame.save(assets / "ui" / "Interf" / "frames" / "BTN_TEST.webp", lossless=True)
    (data / "ui" / "assets.json").write_text(json.dumps([
        {"name": "BTN_TEST", "archive": "Interf.ff", "file": "Interf/frames/BTN_TEST.webp", "w": 8, "h": 6, "sha256": "x"}]),
        encoding="utf-8")
    # Портрет с углом обрезки и анимация из двух кадров.
    (assets / "gfx" / "Faces" / "plain").mkdir(parents=True)
    Image.new("RGBA", (4, 5), (1, 2, 3, 255)).save(assets / "gfx" / "Faces" / "plain" / "P1.webp", lossless=True)
    (assets / "gfx" / "Faces" / "anims" / "A1").mkdir(parents=True)
    for f in ("000_A", "001_B"):
        Image.new("RGBA", (3, 3), (9, 9, 9, 255)).save(assets / "gfx" / "Faces" / "anims" / "A1" / f"{f}.webp", lossless=True)
    (data / "gfx" / "Faces.json").write_text(json.dumps({"archive": "Faces.ff", "images": [
        {"name": "P1", "file": "Faces/plain/P1.webp", "w": 10, "h": 10, "at": [2, 3]}],
        "anims": [{"name": "A1", "dir": "Faces/anims/A1", "w": 3, "h": 3, "frames": ["000_A", "001_B"]}]}), encoding="utf-8")
    return data, assets


def test_export_flat_names_and_manifest(tmp_path: Path) -> None:
    data, assets = make(tmp_path)
    out = tmp_path / "out"
    assert hd.export(out, ["ui", "faces"], data, assets) == 0
    files = sorted(p.name for p in out.iterdir())
    assert files == ["gfx__Faces__A1~000_A.png", "gfx__Faces__A1~001_B.png", "gfx__Faces__P1.png", "manifest.json",
                     "ui__Interf__BTN_TEST.png"]
    manifest = json.loads((out / "manifest.json").read_text(encoding="utf-8"))
    p1 = next(e for e in manifest["entries"] if e["name"] == "P1")
    assert (p1["w"], p1["h"], p1["fileW"], p1["fileH"], p1["at"]) == (10, 10, 4, 5, [2, 3])


@pytest.mark.parametrize("jobs", [1, 2])   # 2 — пачками по процессам: опись та же
def test_import_writes_scaled_layer_restores_alpha_reports(tmp_path: Path, jobs: int) -> None:
    data, assets = make(tmp_path)
    out, up, mod = tmp_path / "out", tmp_path / "up", tmp_path / "mod"
    hd.export(out, ["ui", "faces"], data, assets)
    up.mkdir()
    # Topaz: ×2 без прозрачности; портрет — на точку уже (подгоняется); второй кадр анимации — не той пропорции.
    with Image.open(out / "ui__Interf__BTN_TEST.png") as im:
        im.convert("RGB").resize((16, 12)).save(up / "ui__Interf__BTN_TEST.png")
    with Image.open(out / "gfx__Faces__P1.png") as im:
        im.convert("RGB").resize((8, 9)).save(up / "gfx__Faces__P1.png")
    with Image.open(out / "gfx__Faces__A1~000_A.png") as im:
        im.resize((6, 6)).save(up / "gfx__Faces__A1~000_A.png")
    with Image.open(out / "gfx__Faces__A1~001_B.png") as im:
        im.resize((6, 9)).save(up / "gfx__Faces__A1~001_B.png")
    assert hd.import_layer(out, up, mod, "hd-test", jobs) == 0

    ui = json.loads((mod / "data" / "ui" / "assets.json").read_text(encoding="utf-8"))
    assert [(a["name"], a["w"], a["h"], a["scale"], a["file"]) for a in ui] == [("BTN_TEST", 8, 6, 2, "Interf/frames/BTN_TEST.png")]
    with Image.open(mod / "assets" / "ui" / "Interf" / "frames" / "BTN_TEST.png") as im:
        assert im.size == (16, 12) and im.getpixel((5, 0))[3] == 0 and im.getpixel((5, 8))[3] > 240   # альфа исходника вернулась
    faces = json.loads((mod / "data" / "gfx" / "Faces.json").read_text(encoding="utf-8"))
    assert [(i["name"], i["w"], i["h"], i["at"], i["scale"]) for i in faces["images"]] == [("P1", 10, 10, [2, 3], 2)]
    with Image.open(mod / "assets" / "gfx" / "Faces" / "plain" / "P1.png") as im:
        assert im.size == (8, 10)
    assert faces["anims"] == []   # второй кадр не той пропорции — анимация не берётся частично
    assert not (mod / "assets" / "gfx" / "Faces" / "anims" / "A1" / "000_A.png").exists()   # и её записанный кадр убран
    report = (mod / "import-report.txt").read_text(encoding="utf-8")
    assert "прозрачность восстановлена у 1" in report and "gfx__Faces__A1~001_B.png" in report and "анимация Faces.ff/A1" in report
    assert json.loads((mod / "mod.json").read_text(encoding="utf-8"))["id"] == "hd-test"


def test_fit_rejects_non_integer_scale() -> None:
    src = Image.new("RGBA", (10, 10))
    assert hd.fit(src, Image.new("RGB", (15, 15)))[0] is None
    assert hd.fit(src, Image.new("RGB", (40, 41)))[0] == 4
    assert hd.fit(src, Image.new("RGB", (20, 30)))[0] is None


def upscale_all(out: Path, up: Path, scale: int = 2) -> None:
    """«Апскейл» ×scale ближайшим соседом всех выгруженных кадров."""
    up.mkdir(exist_ok=True)
    for f in out.glob("*.png"):
        with Image.open(f) as im:
            im.resize((im.width * scale, im.height * scale), Image.Resampling.NEAREST).save(up / f.name)


def test_export_skips_frames_not_kept(tmp_path: Path) -> None:
    """Земля, туман и дороги в слой не идут (kept) — и в выгрузку под апскейл тоже."""
    data, assets = make(tmp_path)
    (assets / "gfx" / "IsoTerrn" / "plain").mkdir(parents=True)
    for name in ("FOG01", "ROAD02", "WATER03"):
        Image.new("RGBA", (4, 4), (5, 5, 5, 255)).save(assets / "gfx" / "IsoTerrn" / "plain" / f"{name}.webp", lossless=True)
    (data / "gfx" / "IsoTerrn.json").write_text(json.dumps({"archive": "IsoTerrn.ff", "images": [
        {"name": n, "file": f"IsoTerrn/plain/{n}.webp", "w": 4, "h": 4} for n in ("FOG01", "ROAD02", "WATER03")], "anims": []}),
        encoding="utf-8")
    out = tmp_path / "out"
    assert hd.export(out, ["map"], data, assets) == 0
    assert sorted(p.name for p in out.glob("*.png")) == ["gfx__IsoTerrn__WATER03.png"]


def test_reimport_prunes_entries_no_longer_kept(tmp_path: Path) -> None:
    """Слой, импортированный до исключения земли и тумана: повторный импорт вычищает их записи, PNG и DDS."""
    data, assets = make(tmp_path)
    out, up, mod = tmp_path / "out", tmp_path / "up", tmp_path / "mod"
    hd.export(out, ["faces"], data, assets)
    upscale_all(out, up)
    gfx = mod / "assets" / "gfx"
    (gfx / "Ground").mkdir(parents=True)
    (gfx / "IsoTerrn").mkdir(parents=True)
    for f in ("Ground/G1.png", "Ground/G1.dds", "IsoTerrn/FOG1.png", "IsoTerrn/FOG1.dds", "IsoTerrn/LAVA.png"):
        (gfx / f).write_bytes(b"x")
    (mod / "data" / "gfx").mkdir(parents=True)
    (mod / "data" / "gfx" / "Ground.json").write_text(json.dumps({"archive": "Ground.ff", "images": [
        {"name": "G1", "file": "Ground/G1.png", "w": 1, "h": 1, "scale": 2}], "anims": []}), encoding="utf-8")
    (mod / "data" / "gfx" / "IsoTerrn.json").write_text(json.dumps({"archive": "IsoTerrn.ff", "images": [
        {"name": "FOG1", "file": "IsoTerrn/FOG1.png", "w": 1, "h": 1, "scale": 2},
        {"name": "LAVA", "file": "IsoTerrn/LAVA.png", "w": 1, "h": 1, "scale": 2}], "anims": []}), encoding="utf-8")
    assert hd.import_layer(out, up, mod, "hd-test") == 0
    assert not (mod / "data" / "gfx" / "Ground.json").exists()
    terrain = json.loads((mod / "data" / "gfx" / "IsoTerrn.json").read_text(encoding="utf-8"))
    assert [i["name"] for i in terrain["images"]] == ["LAVA"]
    assert sorted(str(p.relative_to(gfx)).replace("\\", "/") for p in gfx.rglob("*") if p.is_file() and "Faces" not in p.parts) \
        == ["IsoTerrn/LAVA.png"]
    assert "вычищено не идущих в слой 2" in (mod / "import-report.txt").read_text(encoding="utf-8")


def test_unreadable_frame_is_noted_not_fatal(tmp_path: Path) -> None:
    """Оборванный файл апскейлера — замечание; прочие кадры принимаются, прогон не встаёт."""
    data, assets = make(tmp_path)
    out, up, mod = tmp_path / "out", tmp_path / "up", tmp_path / "mod"
    hd.export(out, ["ui", "faces"], data, assets)
    upscale_all(out, up)
    (up / "gfx__Faces__P1.png").write_bytes((up / "gfx__Faces__P1.png").read_bytes()[:40])   # обрыв посреди файла
    assert hd.import_layer(out, up, mod, "hd-test") == 0
    report = (mod / "import-report.txt").read_text(encoding="utf-8")
    assert "gfx__Faces__P1.png: не принят" in report
    ui = json.loads((mod / "data" / "ui" / "assets.json").read_text(encoding="utf-8"))
    assert [a["name"] for a in ui] == ["BTN_TEST"]


def test_reimport_resumes_without_rewriting(tmp_path: Path) -> None:
    """Повторный импорт (прерванный прогон): кадр в слое не старше апскейла не пересчитывается, опись — та же."""
    data, assets = make(tmp_path)
    out, up, mod = tmp_path / "out", tmp_path / "up", tmp_path / "mod"
    hd.export(out, ["ui", "faces"], data, assets)
    upscale_all(out, up)
    assert hd.import_layer(out, up, mod, "hd-test") == 0
    target = mod / "assets" / "ui" / "Interf" / "frames" / "BTN_TEST.png"
    stamp = target.stat().st_mtime_ns
    before = (mod / "data" / "ui" / "assets.json").read_text(encoding="utf-8")
    assert hd.import_layer(out, up, mod, "hd-test") == 0
    assert target.stat().st_mtime_ns == stamp
    assert (mod / "data" / "ui" / "assets.json").read_text(encoding="utf-8") == before


def test_import_with_no_upscale_fails(tmp_path: Path) -> None:
    data, assets = make(tmp_path)
    out, up = tmp_path / "out", tmp_path / "up"
    hd.export(out, ["ui"], data, assets)
    up.mkdir()
    assert hd.import_layer(out, up, tmp_path / "mod", "hd-test") == 1


def test_manifest_path_outside_layer_is_refused(tmp_path: Path) -> None:
    """Опись — данные пользователя: «..» в пути кадра не выводит запись за пределы слоя."""
    data, assets = make(tmp_path)
    out, up, mod = tmp_path / "out", tmp_path / "up", tmp_path / "mod"
    hd.export(out, ["ui"], data, assets)
    upscale_all(out, up)
    manifest = json.loads((out / "manifest.json").read_text(encoding="utf-8"))
    manifest["entries"][0]["file"] = "../../../escaped.webp"
    (out / "manifest.json").write_text(json.dumps(manifest), encoding="utf-8")
    hd.import_layer(out, up, mod, "hd-test")
    assert not any(tmp_path.rglob("escaped.png"))
    assert "не принят" in (mod / "import-report.txt").read_text(encoding="utf-8")


def test_guard_output_refuses_tracked_repo_paths() -> None:
    """Р3: графика игры внутри репозитория — только там, где её не берёт git."""
    with pytest.raises(SystemExit):
        hd.guard_output(hd.REPO / "docs" / "hd-export")
    hd.guard_output(hd.REPO / "mods" / "hd-test")   # mods/hd*/ — в .gitignore
    hd.guard_output(hd.REPO.parent / "le-hd")        # вне репозитория — можно


def test_same_name_in_two_folders_gets_unique_flat_names(tmp_path: Path) -> None:
    """Две разные картинки архива с одним именем (IsoTerrn: MOMNE0507 в frames/ и plain/): вторая — с папкой в плоском имени, первая —
    прежняя (её апскейл годится); обе выгружаются."""
    data, assets = make(tmp_path)
    for folder, colour in (("frames", 10), ("plain", 20)):
        (assets / "gfx" / "IsoTerrn" / folder).mkdir(parents=True)
        Image.new("RGBA", (4, 4), (colour, 0, 0, 255)).save(assets / "gfx" / "IsoTerrn" / folder / "M1.webp", lossless=True)
    (data / "gfx" / "IsoTerrn.json").write_text(json.dumps({"archive": "IsoTerrn.ff", "images": [
        {"name": "M1", "file": f"IsoTerrn/{f}/M1.webp", "w": 4, "h": 4} for f in ("frames", "plain")], "anims": []}), encoding="utf-8")
    out = tmp_path / "out"
    assert hd.export(out, ["map"], data, assets) == 0
    assert sorted(p.name for p in out.glob("*.png")) == ["gfx__IsoTerrn__M1.png", "gfx__IsoTerrn__M1~plain.png"]
