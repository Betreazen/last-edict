#!/usr/bin/env python3
"""make_public.py — дерево публичного репозитория из текущего HEAD (решения — tools/publish/public.json, разбор — docs/publishing.md).

Что делает:
  1. берёт файлы коммита HEAD (git archive — незакоммиченное в снимок не попадает);
  2. выкидывает исключённое по решениям (данные, выведенные из игры; синтетические карты; сохранения оригинала);
  3. вычищает личное в текстовых файлах (почта, домашний каталог в путях) — по правилам `replace`;
  4. дописывает в .gitignore импортированные данные игры;
  5. кладёт дерево в --out, делает там `git init` и ОДИН коммит с автором из решений (ничего не пушит);
  6. сканирует снимок на секреты: gitleaks, если есть (`--gitleaks <путь>` или в PATH), иначе встроенные правила (ключи облаков, токены
     GitHub/Slack/OpenAI/Anthropic, приватные ключи, почта вне разрешённых); находки — код выхода 2.

Печатает исключённое (по правилу: файлов, байт, первые пути), сколько замен и итоговый размер.

Запуск (из корня репозитория):
  py tools/publish/make_public.py --out "C:/…/disciples-godot-public"
  py tools/publish/make_public.py --out <каталог> --dry-run        # только отчёт, без записи
--out должен быть пустым или прежним снимком этого же скрипта (в нём файл .public-snapshot) — его содержимое заменяется.
"""
from __future__ import annotations

import argparse
import fnmatch
import io
import json
import os
import re
import shutil
import subprocess
import sys
import tarfile
from dataclasses import dataclass, field
from pathlib import Path

REPO = Path(__file__).resolve().parents[2]
DECISIONS = Path(__file__).with_name("public.json")
MARKER = ".public-snapshot"
TEXT_LIMIT = 8 * 2**20   # больше — не правим и сканируем только как двоичные
SECRET_RULES = [
    ("AWS access key", r"\b(AKIA|ASIA)[0-9A-Z]{16}\b"),
    ("GitHub token", r"\b(ghp|gho|ghu|ghs|ghr)_[A-Za-z0-9]{36,}\b"),
    ("GitHub fine-grained token", r"\bgithub_pat_[A-Za-z0-9_]{60,}\b"),
    ("Slack token", r"\bxox[abprs]-[A-Za-z0-9-]{10,}\b"),
    ("Anthropic key", r"\bsk-ant-[A-Za-z0-9_-]{20,}\b"),
    ("OpenAI/OpenRouter key", r"\bsk-(proj-|or-v1-)?[A-Za-z0-9_-]{32,}\b"),
    ("Google API key", r"\bAIza[0-9A-Za-z_-]{35}\b"),
    ("private key", r"-----BEGIN (RSA |EC |OPENSSH |DSA |PGP )?PRIVATE KEY( BLOCK)?-----"),
    ("JWT", r"\beyJ[A-Za-z0-9_-]{10,}\.eyJ[A-Za-z0-9_-]{10,}\.[A-Za-z0-9_-]{10,}\b"),
    ("password assignment", r"(?i)\b(password|passwd|secret|api[_-]?key|access[_-]?token)\s*[:=]\s*['\"][^'\"\s]{8,}['\"]"),
]
EMAIL = re.compile(r"[A-Za-z0-9._%+-]+@[A-Za-z0-9-]+(\.[A-Za-z0-9-]+)*\.[A-Za-z]{2,}")


@dataclass
class Rule:
    why: str
    paths: list[str]
    keep: list[str] = field(default_factory=list)
    files: list[tuple[str, int]] = field(default_factory=list)


def match(path: str, pattern: str) -> bool:
    """Шаблон пути: `**` — любой хвост каталогов, иначе fnmatch по всему пути (с учётом регистра)."""
    if pattern.endswith("/**"):
        return path.startswith(pattern[:-2])
    if pattern.startswith("**/"):   # имя файла в любом каталоге — без учёта регистра (*.sg и *.SG, *.LDR)
        return fnmatch.fnmatchcase(path.rsplit("/", 1)[-1].lower(), pattern[3:].lower())
    return fnmatch.fnmatchcase(path, pattern)


def head_files() -> tuple[str, list[tuple[str, bytes, int]]]:
    """Файлы коммита HEAD: (путь, содержимое, режим)."""
    sha = subprocess.run(["git", "rev-parse", "HEAD"], cwd=REPO, check=True, capture_output=True, text=True).stdout.strip()
    raw = subprocess.run(["git", "archive", "--format=tar", "HEAD"], cwd=REPO, check=True, capture_output=True).stdout
    files = []
    with tarfile.open(fileobj=io.BytesIO(raw)) as tar:
        for m in tar.getmembers():
            if m.isfile():
                files.append((m.name, tar.extractfile(m).read(), m.mode))
            elif m.issym():
                raise SystemExit(f"ОШИБКА: в дереве ссылка {m.name} -> {m.linkname} (ссылки в снимок не берём)")
    return sha, files


def head_submodules() -> list[tuple[str, str]]:
    """Подмодули HEAD (gitlink, режим 160000): (путь, коммит). git archive их не выгружает — в снимок они идут ссылками, как в
    исходном дереве (сторонние библиотеки зонда tools/d2probe, URL — в .gitmodules)."""
    tree = subprocess.run(["git", "ls-tree", "-r", "-z", "HEAD"], cwd=REPO, check=True, capture_output=True, text=True).stdout
    links = []
    for entry in filter(None, tree.split("\0")):
        meta, path = entry.split("\t", 1)
        mode, kind, obj = meta.split()
        if mode == "160000" and kind == "commit":
            links.append((path, obj))
    return links


def is_text(data: bytes) -> bool:
    return len(data) <= TEXT_LIMIT and b"\0" not in data[:8192]


def sanitize(path: str, data: bytes, replaces: list[dict], counts: dict[str, int]) -> bytes:
    if not is_text(data):
        return data
    try:
        text = data.decode("utf-8")
    except UnicodeDecodeError:
        return data
    for r in replaces:
        text, n = re.subn(r["regex"], r["with"], text)
        if n:
            counts[r["why"]] = counts.get(r["why"], 0) + n
    return text.encode("utf-8")


def scan_builtin(root: Path, allowed_emails: set[str]) -> list[str]:
    found = []
    compiled = [(name, re.compile(rx)) for name, rx in SECRET_RULES]
    for path in sorted(root.rglob("*")):
        if not path.is_file() or ".git" in path.relative_to(root).parts:
            continue
        data = path.read_bytes()
        if not is_text(data):
            continue
        text = data.decode("utf-8", "replace")
        rel = path.relative_to(root).as_posix()
        for name, rx in compiled:
            for m in rx.finditer(text):
                line = text.count("\n", 0, m.start()) + 1
                found.append(f"{rel}:{line}: {name}: {m.group(0)[:12]}…")
        for m in EMAIL.finditer(text):
            email = m.group(0)
            # Не почта: имена пакетов и версии (foo@1.2.3), адреса в примерах (example.com), сноски вида x@2x.png.
            if email in allowed_emails or email.lower().endswith(("example.com", "example.org", ".png", ".jpg", ".svg", ".webp")):
                continue
            line = text.count("\n", 0, m.start()) + 1
            found.append(f"{rel}:{line}: почта: {email}")
    return found


def scan_gitleaks(exe: str, root: Path) -> tuple[int, str]:
    report = root.parent / (root.name + ".gitleaks.json")
    if report.exists():
        report.unlink()
    # Разобранные ложные срабатывания — .gitleaks.toml в корне снимка (правила по умолчанию плюс исключения).
    config = ["--config", str(root / ".gitleaks.toml")] if (root / ".gitleaks.toml").is_file() else []
    r = subprocess.run([exe, "dir", str(root), *config, "--no-banner", "--redact", "--report-format", "json", "--report-path", str(report)],
                       capture_output=True, text=True, encoding="utf-8", errors="replace")
    if r.returncode not in (0, 1):   # старые версии: подкоманды dir нет
        r = subprocess.run([exe, "detect", "--no-git", "--source", str(root), *config, "--no-banner", "--redact", "--report-format", "json",
                            "--report-path", str(report)], capture_output=True, text=True, encoding="utf-8", errors="replace")
    if r.returncode not in (0, 1) or not report.exists():
        raise SystemExit(f"ОШИБКА: gitleaks не отработал (код {r.returncode}): {r.stderr.strip()[-300:]}")
    findings = json.loads(report.read_text(encoding="utf-8") or "[]")
    lines = [f"{f.get('File')}:{f.get('StartLine')}: {f.get('RuleID')}" for f in findings]
    return len(findings), "\n".join(lines) or (r.stderr.strip().splitlines() or ["0 находок"])[-1]


def clear_out(out: Path) -> None:
    if not out.exists():
        out.mkdir(parents=True)
        return
    entries = list(out.iterdir())
    if not entries:
        return
    if not (out / MARKER).is_file():
        raise SystemExit(f"ОШИБКА: {out} не пуст и это не прежний снимок (нет {MARKER}) — укажите пустой каталог")
    for e in entries:
        if e.is_dir() and not e.is_symlink():
            shutil.rmtree(e, onerror=_writable)
        else:
            e.unlink()


def _writable(func, path, _exc) -> None:
    os.chmod(path, 0o666)   # файлы .git только для чтения
    func(path)


def git(out: Path, *args: str, env: dict | None = None) -> str:
    return subprocess.run(["git", "-C", str(out), *args], check=True, capture_output=True, text=True, env=env).stdout.strip()


def main() -> int:
    ap = argparse.ArgumentParser(description=__doc__.split("\n\n")[0])
    ap.add_argument("--out", type=Path, required=True, help="каталог снимка (пустой или прежний снимок)")
    ap.add_argument("--dry-run", action="store_true", help="только отчёт: что исключается и сколько весит")
    ap.add_argument("--gitleaks", help="путь к gitleaks (иначе из PATH, нет — встроенные правила)")
    args = ap.parse_args()
    sys.stdout.reconfigure(encoding="utf-8")
    decisions = json.loads(DECISIONS.read_text(encoding="utf-8"))
    rules = [Rule(r["why"], r["paths"], r.get("keep", [])) for r in decisions["exclude"]]

    sha, files = head_files()
    kept: list[tuple[str, bytes, int]] = []
    for path, data, mode in files:
        rule = next((r for r in rules if any(match(path, p) for p in r.paths) and not any(match(path, k) for k in r.keep)), None)
        if rule:
            rule.files.append((path, len(data)))
        else:
            kept.append((path, data, mode))

    print(f"HEAD {sha[:10]}: файлов {len(files)}, {sum(len(d) for _, d, _ in files) / 2**20:.1f} МиБ")
    print("Исключено:")
    for r in rules:
        size = sum(n for _, n in r.files)
        print(f"  {r.why}: файлов {len(r.files)}, {size / 2**20:.2f} МиБ")
        tops: dict[str, int] = {}
        for p, _ in r.files:
            top = "/".join(p.split("/")[:2]) if p.count("/") >= 2 else p
            tops[top] = tops.get(top, 0) + 1
        for top, n in sorted(tops.items()):
            print(f"    {top}: {n}")
    counts: dict[str, int] = {}
    kept = [(p, sanitize(p, d, decisions["replace"], counts), m) for p, d, m in kept]
    gitignore = next((i for i, (p, _, _) in enumerate(kept) if p == ".gitignore"), None)
    extra = "\n".join(decisions["gitignore"]) + "\n"
    if gitignore is None:
        kept.append((".gitignore", extra.encode(), 0o644))
    else:
        p, d, m = kept[gitignore]
        kept[gitignore] = (p, d.rstrip(b"\n") + b"\n" + extra.encode(), m)
    print("Вычищено: " + (", ".join(f"{k} — {v}" for k, v in counts.items()) or "ничего"))
    total = sum(len(d) for _, d, _ in kept)
    print(f"В снимке: файлов {len(kept)}, {total / 2**20:.1f} МиБ")
    if args.dry_run:
        return 0

    out = args.out.resolve()
    if REPO in out.parents or out == REPO or out in REPO.parents:
        raise SystemExit("ОШИБКА: --out внутри репозитория или выше него")
    clear_out(out)
    for path, data, mode in kept:
        target = out / path
        target.parent.mkdir(parents=True, exist_ok=True)
        target.write_bytes(data)
        if mode & 0o111:
            target.chmod(target.stat().st_mode | 0o111)
    (out / MARKER).write_text(f"{sha}\n", encoding="utf-8")

    author = decisions["author"]
    env = {**os.environ, "GIT_AUTHOR_NAME": author["name"], "GIT_AUTHOR_EMAIL": author["email"],
           "GIT_COMMITTER_NAME": author["name"], "GIT_COMMITTER_EMAIL": author["email"]}
    git(out, "init", "-q", "-b", "main")
    git(out, "config", "core.autocrlf", "false")
    git(out, "config", "user.name", author["name"])
    git(out, "config", "user.email", author["email"])
    # Отметка снимка — служебная, в коммит не идёт.
    (out / ".git" / "info").mkdir(parents=True, exist_ok=True)
    with open(out / ".git" / "info" / "exclude", "a", encoding="utf-8") as f:
        f.write(f"/{MARKER}\n")
    git(out, "-c", "core.excludesFile=", "add", "-A")   # глобальные исключения пользователя на снимок не влияют
    submodules = head_submodules()
    for path, obj in submodules:
        git(out, "update-index", "--add", "--cacheinfo", f"160000,{obj},{path}")
    print(f"Подмодули ссылками: {len(submodules)} ({', '.join(p.rsplit('/', 1)[-1] for p, _ in submodules)})")
    git(out, "commit", "-q", "-m", decisions["message"], "-m", f"Snapshot of private tree {sha[:10]}.", env=env)
    commit = git(out, "log", "-1", "--format=%H %an <%ae>")
    print(f"Снимок: {out}, коммит {commit}")

    exe = args.gitleaks or shutil.which("gitleaks")
    allowed = set(decisions.get("allowed_emails", []))
    builtin = scan_builtin(out, allowed)
    print(f"Скан встроенными правилами: находок {len(builtin)}")
    for f in builtin[:50]:
        print(f"  {f}")
    leaks = 0
    if exe:
        leaks, text = scan_gitleaks(exe, out)
        print(f"gitleaks: находок {leaks}\n{text}")
    else:
        print("gitleaks нет — только встроенные правила")
    return 2 if builtin or leaks else 0


if __name__ == "__main__":
    sys.exit(main())
