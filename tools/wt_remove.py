#!/usr/bin/env python3
r"""wt_remove.py — безопасный снос рабочей копии git (worktree) и её ветки.

Зачем. 18.09.2026 `git worktree remove --force` прошёл по ссылкам на каталоги (junction/symlink), которые подагенты
заводили внутри рабочей копии ради прогонов, и стёр настоящие данные основного репозитория: сперва распакованные
кадры `assets/` (пересобрались), затем журналы и сохранения корпуса с 09.09 (невосстановимы, копии не было).

Что делает:
  1. ищет внутри рабочей копии все ссылки на каталоги (reparse points) — вне `.git`;
  2. снимает каждую как ССЫЛКУ (`os.unlink` / `rmdir` без содержимого), не заходя внутрь;
  3. проверяет, что ссылок не осталось; если осталась хоть одна — останавливается и ничего не удаляет;
  4. только после этого зовёт `git worktree remove --force` и, если попросили, удаляет ветку.

    python tools/wt_remove.py ../dg-wt-ui2              # снести рабочую копию
    python tools/wt_remove.py ../dg-wt-ui2 --branch     # и её ветку (git branch -d — только слитую)
    python tools/wt_remove.py --selfcheck

Правило: рабочие копии сносить ТОЛЬКО этим скриптом. Пути к данным задавать переменными окружения
(`D2_CORPUS_DIR`, `D2_ASSETS_DIR`), а не ссылками.
"""
from __future__ import annotations

import argparse
import os
import stat
import subprocess
import sys
import tempfile
from pathlib import Path

REPO = Path(__file__).resolve().parents[1]


def is_link(p: Path) -> bool:
    """Ссылка на каталог в любом виде: symlink или junction (reparse point на Windows)."""
    if p.is_symlink():
        return True
    try:
        attrs = os.lstat(p).st_file_attributes  # type: ignore[attr-defined]
    except (AttributeError, OSError):
        return False
    return bool(attrs & stat.FILE_ATTRIBUTE_REPARSE_POINT)


def find_links(root: Path) -> list[Path]:
    """Все ссылки внутри root, не заходя ни в них, ни в .git."""
    found: list[Path] = []

    def fail(e: OSError) -> None:
        raise e                          # непрочитанный каталог мог бы скрыть ссылку от проверки

    for dirpath, dirnames, filenames in os.walk(root, onerror=fail, followlinks=False):
        here = Path(dirpath)
        keep = []
        for d in dirnames:
            p = here / d
            if d == ".git":
                continue
            if is_link(p):
                found.append(p)          # внутрь не идём
            else:
                keep.append(d)
        dirnames[:] = keep
        found.extend(here / f for f in filenames if is_link(here / f))
    return found


def unlink(p: Path) -> None:
    """Снять ссылку, не трогая то, на что она указывает."""
    try:
        os.unlink(p)                     # symlink на файл/каталог
    except OSError:
        os.rmdir(p)                      # junction: rmdir снимает саму ссылку, содержимое цели не трогает


def registered_worktrees() -> list[Path]:
    """Рабочие копии этого репозитория по git (основной каталог в списке тоже есть)."""
    r = subprocess.run(["git", "-C", str(REPO), "worktree", "list", "--porcelain"],
                       capture_output=True, text=True, encoding="utf-8")
    if r.returncode != 0:
        raise OSError(f"git worktree list: {r.stderr.strip()}")
    return [Path(line[len("worktree "):]).resolve() for line in r.stdout.splitlines() if line.startswith("worktree ")]


def remove(worktree: Path, branch: bool) -> int:
    worktree = worktree.resolve()
    if worktree == REPO:
        print("ОТКАЗ: это основной репозиторий, а не рабочая копия")
        return 2
    if not worktree.exists():
        print(f"нет каталога {worktree}")
        return 2
    # До снятия ссылок: иначе путь вроде «..» снял бы все ссылки пользователя, а git потом всё равно отказал бы.
    if not any(os.path.samefile(worktree, w) for w in registered_worktrees() if w.exists()):
        print(f"ОТКАЗ: {worktree} — не рабочая копия этого репозитория (git worktree list)")
        return 2
    name = None
    if branch:
        head = subprocess.run(["git", "-C", str(worktree), "rev-parse", "--abbrev-ref", "HEAD"],
                              capture_output=True, text=True, encoding="utf-8")
        if head.returncode != 0:
            print(f"ОТКАЗ: ветку рабочей копии не определить: {head.stderr.strip()}")
            return 1
        name = head.stdout.strip() or None
    try:
        links = find_links(worktree)
        for p in links:
            print(f"снимаю ссылку: {p}")
            unlink(p)
        left = find_links(worktree)
    except OSError as e:
        print(f"ОТКАЗ: обход или снятие ссылки не удались, удалять опасно: {e}")
        return 1
    if left:
        print("ОТКАЗ: ссылки не снялись, удалять опасно:")
        for p in left:
            print(f"  {p}")
        return 1
    r = subprocess.run(["git", "-C", str(REPO), "worktree", "remove", "--force", str(worktree)])
    if r.returncode != 0:
        return r.returncode
    print(f"рабочая копия снесена: {worktree} (ссылок снято {len(links)})")
    if name and name != "HEAD":
        r = subprocess.run(["git", "-C", str(REPO), "branch", "-d", name])
        return r.returncode
    return 0


def selfcheck() -> int:
    """Ссылка на каталог с файлом внутри: после снятия ссылки файл цели на месте."""
    with tempfile.TemporaryDirectory() as tmp:
        t = Path(tmp)
        target = t / "real"
        target.mkdir()
        (target / "keep.txt").write_text("данные")
        wt = t / "wt"
        wt.mkdir()
        link = wt / "assets"
        try:
            os.symlink(target, link, target_is_directory=True)
        except OSError:
            subprocess.run(["cmd", "/c", "mklink", "/J", str(link), str(target)],
                           capture_output=True, check=True)
        found = find_links(wt)
        assert found == [link], found
        unlink(link)
        assert not link.exists() and not find_links(wt), "ссылка не снята"
        assert (target / "keep.txt").read_text() == "данные", "цель пострадала"
    print("самопроверка: ссылка найдена и снята, цель цела")
    return 0


def main() -> int:
    p = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    p.add_argument("worktree", nargs="?", type=Path)
    p.add_argument("--branch", action="store_true", help="удалить и ветку (только слитую)")
    p.add_argument("--selfcheck", action="store_true")
    a = p.parse_args()
    if a.selfcheck:
        return selfcheck()
    if not a.worktree:
        p.error("нужен путь к рабочей копии или --selfcheck")
    return remove(a.worktree, a.branch)


if __name__ == "__main__":
    sys.stdout.reconfigure(encoding="utf-8")
    sys.exit(main())
