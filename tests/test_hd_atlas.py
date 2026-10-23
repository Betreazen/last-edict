"""Листы для апскейла (tools/hd/hd_atlas.py): кадры — в листы и обратно, «апскейл» ×2 ближайшим соседом — кадр в кадр."""
import json
import sys
from pathlib import Path

from PIL import Image

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / "tools" / "hd"))
import hd_atlas as atlas  # noqa: E402


def test_pack_unpack_round_trip(tmp_path: Path) -> None:
    src = tmp_path / "export"
    src.mkdir()
    frames = {}
    for i, (w, h) in enumerate([(30, 20), (50, 40), (10, 70), (64, 64), (5, 5)]):
        im = Image.new("RGBA", (w, h), (i * 40, 255 - i * 40, 7, 255))
        im.putpixel((0, 0), (0, 0, 0, 0))   # прозрачная точка — альфа доезжает до кадра
        name = f"gfx__IsoUnit__U{i}~00{i}_A.png"
        im.save(src / name)
        frames[name] = im
    sheets = tmp_path / "sheets"
    assert atlas.pack(sheets, [src], [], size=96, pad=4) == 0
    metas = sorted(sheets.glob("sheet_*.json"))
    assert len(metas) >= 2   # 96×96 — на все кадры одного листа мало
    placed = [f for m in metas for f in json.loads(m.read_text(encoding="utf-8"))["frames"]]
    assert sorted(f["flat"] for f in placed) == sorted(frames)
    # Апскейл ×2 ближайшим — и обратно.
    up = tmp_path / "sheets-up"
    up.mkdir()
    for png in sheets.glob("sheet_*.png"):
        with Image.open(png) as im:
            im.resize((im.width * 2, im.height * 2), Image.Resampling.NEAREST).save(up / png.name)
    out = tmp_path / "upscaled"
    assert atlas.unpack(sheets, up, out) == 0
    for name, im in frames.items():
        with Image.open(out / name) as got:
            assert got.size == (im.width * 2, im.height * 2)
            assert list(got.getdata()) == list(im.resize(got.size, Image.Resampling.NEAREST).getdata())


def test_frames_keep_gap_and_bounds() -> None:
    sizes = [(f"f{i}", 20, 10 + i) for i in range(12)]
    sheets = atlas.shelves(sizes, size=64, pad=3)
    for sheet in sheets:
        assert max(p.x + p.w for p in sheet) + 3 <= 64 and max(p.y + p.h for p in sheet) + 3 <= 64   # лист с правым зазором — не больше 64
        for a in sheet:
            assert a.x >= 3 and a.y >= 3 and a.x + a.w + 3 <= 64 and a.y + a.h + 3 <= 64
            for b in sheet:
                if a is b:
                    continue
                apart = a.x + a.w + 3 <= b.x or b.x + b.w + 3 <= a.x or a.y + a.h + 3 <= b.y or b.y + b.h + 3 <= a.y
                assert apart, (a, b)


def test_non_integer_scale_is_reported(tmp_path: Path) -> None:
    src = tmp_path / "export"
    src.mkdir()
    Image.new("RGBA", (10, 10), (1, 2, 3, 255)).save(src / "a.png")
    sheets = tmp_path / "sheets"
    atlas.pack(sheets, [src], [], size=64, pad=2)
    up = tmp_path / "up"
    up.mkdir()
    with Image.open(sheets / "sheet_0001.png") as im:
        im.resize((im.width * 3 + 1, im.height * 3)).save(up / "sheet_0001.png")
    assert atlas.unpack(sheets, up, tmp_path / "out") == 1
