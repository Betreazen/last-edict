"""content/test-pack — выход tools/test_pack.py: файлы в репозитории совпадают с генератором (правят генератор, а не JSON)."""
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / "tools"))
import test_pack as tp  # noqa: E402


def test_files_match_the_generator():
    stale = [p.relative_to(tp.ROOT).as_posix() for p, text in tp.render().items()
             if not p.exists() or p.read_text(encoding="utf-8") != text]
    assert stale == [], f"content/test-pack разошёлся с tools/test_pack.py: {stale} — py tools/test_pack.py"


def test_entities_have_no_legacy_id():
    """Ключ правил — id содержимого: test-pack — как мод, без id оригинала у сущностей и в картах."""
    kinds = tp.build()
    assert all("legacyId" not in e for entities in kinds.values() for e in entities)
    assert all(e["id"].startswith(tp.MOD + ":") for entities in kinds.values() for e in entities)
