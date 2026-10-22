"""content/test-pack — выход tools/test_pack.py: файлы в репозитории совпадают с генератором (правят генератор, а не JSON)."""
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / "tools"))
import test_pack as tp  # noqa: E402


def test_files_match_the_generator():
    stale = [p.relative_to(tp.ROOT).as_posix() for p, text in tp.render().items()
             if not p.exists() or p.read_text(encoding="utf-8") != text]
    assert stale == [], f"content/test-pack разошёлся с tools/test_pack.py: {stale} — py tools/test_pack.py"


def test_no_legacy_id_collides_with_the_original_range():
    kinds = tp.with_legacy_ids(tp.build())
    ids = [e["legacyId"] for entities in kinds.values() for e in entities if "legacyId" in e]
    assert ids and all(i.startswith("t000") and int(i[-4:], 16) >= 0xA001 for i in ids)
    assert len(ids) == len(set(i.lower() for i in ids))
