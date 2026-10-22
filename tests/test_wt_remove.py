"""wt_remove.py: отказ до снятия ссылок, если путь — не рабочая копия этого репозитория; ошибки обхода не глотаются."""
import os
import subprocess
import sys
from pathlib import Path

import pytest

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / "tools"))
import wt_remove as wr  # noqa: E402


def make_link(link: Path, target: Path) -> None:
    try:
        os.symlink(target, link, target_is_directory=True)
    except OSError:
        subprocess.run(["cmd", "/c", "mklink", "/J", str(link), str(target)], capture_output=True, check=True)


def test_refuses_directory_that_is_not_a_worktree(tmp_path: Path) -> None:
    target = tmp_path / "data"
    target.mkdir()
    folder = tmp_path / "not-a-worktree"
    folder.mkdir()
    link = folder / "assets"
    make_link(link, target)

    assert wr.remove(folder, branch=False) != 0
    assert wr.is_link(link), "ссылка в чужом каталоге снята"


def test_find_links_raises_on_walk_error(tmp_path: Path) -> None:
    with pytest.raises(OSError):
        wr.find_links(tmp_path / "missing")
