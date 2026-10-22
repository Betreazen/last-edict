"""HD через Topaz (tools/hd/hd_layer.py, Р19): выгрузка в одну плоскую папку и обратный импорт слоем мода — на выдуманных кадрах."""
import json
import sys
from pathlib import Path

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
    assert hd.export(out, ["ui", "faces"], None, data, assets) == 0
    files = sorted(p.name for p in out.iterdir())
    assert files == ["gfx__Faces__A1~000_A.png", "gfx__Faces__A1~001_B.png", "gfx__Faces__P1.png", "manifest.json",
                     "ui__Interf__BTN_TEST.png"]
    manifest = json.loads((out / "manifest.json").read_text(encoding="utf-8"))
    p1 = next(e for e in manifest["entries"] if e["name"] == "P1")
    assert (p1["w"], p1["h"], p1["fileW"], p1["fileH"], p1["at"]) == (10, 10, 4, 5, [2, 3])


def test_import_writes_scaled_layer_restores_alpha_reports(tmp_path: Path) -> None:
    data, assets = make(tmp_path)
    out, up, mod = tmp_path / "out", tmp_path / "up", tmp_path / "mod"
    hd.export(out, ["ui", "faces"], None, data, assets)
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
    assert hd.import_layer(out, up, mod, "hd-test") == 0

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
