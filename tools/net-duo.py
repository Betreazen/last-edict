"""Партия двух машин через посредника (Э7, docs/network.md §9): хост — этот ПК, клиент — тот же ПК или EliteBook.

Драйвер сам собирает игру и посредника, при --remote копирует дерево и базу содержимого на EliteBook и собирает игру там, поднимает
посредника (локально; EliteBook ходит к нему SSH-туннелем — порты на ПК не открываются; или --relay <ws-адрес> готового посредника),
запускает игру на хосте и клиенте с кодом комнаты и ходами по сценарию, ждёт конца (день > --days), затем повторяет журнал хоста
(tools/JournalReplay) и сверяет: хеш хоста = хеш повтора, вид клиента = проекция повтора для его игрока.

Сценарии (--case):
  play     — доиграть до дня N (бои человека и ИИ, ходы по очереди);
  drop     — то же, но клиент рвёт связь посреди партии (переподключение и resync);
  async    — асинхронная комната: ходы по очереди, после каждого хода игра закрывается (tools/net-duo.py сам открывает её снова).

  py tools/net-duo.py [--case play] [--days 3] [--remote] [--relay ws://…/ws] [--scenario "Arrival of the Pilgrims"]
                      [--seats S143PL0001,S143PL0002] [--keep]

Итог — строка «net-duo: ok …» (код 0) или «net-duo: FAIL …» (код 1); числа — задержка хода, трафик, память — в .cache/net/<прогон>/.
Графика — D2_ASSETS_DIR (у EliteBook — ~/le/assets); данные импорта — data/ рабочей копии (у EliteBook — его data/ с базой с ПК).
"""
from __future__ import annotations

import argparse
import json
import os
import re
import socket
import subprocess
import sys
import tarfile
import time
from pathlib import Path

REPO = Path(__file__).resolve().parent.parent
GODOT = os.environ.get("GODOT", r"C:\Tools\bin\godot.cmd")
REMOTE = "elitebook"
REMOTE_ROOT = "~/le/net"          # дерево игры на EliteBook (свой каталог, стенд бенча ~/le/last-edict не трогается)
REMOTE_ENV = "source ~/le/env.sh"


def log(text: str) -> None:
    print(text, flush=True)


def run(cmd: list[str] | str, **kw) -> subprocess.CompletedProcess:
    return subprocess.run(cmd, check=True, text=True, encoding="utf-8", errors="replace", **kw)


def ssh(script: str, check: bool = True, timeout: int | None = None) -> subprocess.CompletedProcess:
    """Скрипт bash на EliteBook (через stdin, как велит ~/.claude/docs/elitebook-ssh.md)."""
    return subprocess.run(["ssh", REMOTE, "bash -s"], input=script, text=True, encoding="utf-8", errors="replace",
                          capture_output=True, check=check, timeout=timeout)


def free_port() -> int:
    with socket.socket() as s:
        s.bind(("127.0.0.1", 0))
        return s.getsockname()[1]


def build_local() -> None:
    log("net-duo: сборка игры и посредника")
    run(["dotnet", "build", str(REPO / "godot" / "LastEdict.Godot.csproj"), "-v", "q", "-nologo"], stdout=subprocess.DEVNULL)
    run(["dotnet", "build", str(REPO / "tools" / "Relay" / "Relay.csproj"), "-v", "q", "-nologo"], stdout=subprocess.DEVNULL)
    run(["dotnet", "build", str(REPO / "tools" / "JournalReplay" / "JournalReplay.csproj"), "-v", "q", "-nologo"], stdout=subprocess.DEVNULL)


def sync_remote(work: Path) -> None:
    """Дерево рабочей копии (файлы git, с незакоммиченными правками) и база содержимого — на EliteBook; там — сборка игры."""
    log("net-duo: дерево и база содержимого — на EliteBook")
    files = run(["git", "-C", str(REPO), "ls-files", "-co", "--exclude-standard"], capture_output=True).stdout.splitlines()
    pack = work / "tree.tar.gz"
    with tarfile.open(pack, "w:gz") as tar:
        for f in files:
            p = REPO / f
            if p.is_file():
                tar.add(p, arcname=f)
        # Данные импорта (вне git): база содержимого и то, что читает игра. Только между своими машинами — в репозиторий не попадают (Р3).
        data = REPO / "data"
        for sub in ("content", "rules", "scenarios", "ui", "game", "map", "gfx", "fonts", "sound", "music", "video"):
            if (data / sub).exists():
                tar.add(data / sub, arcname=f"data/{sub}")
        for f in data.glob("*.json"):
            tar.add(f, arcname=f"data/{f.name}")
    run(["scp", "-q", str(pack), f"{REMOTE}:/tmp/le-net-tree.tar.gz"])
    out = ssh(f"""set -e
{REMOTE_ENV}
mkdir -p {REMOTE_ROOT}
cd {REMOTE_ROOT}
find . -mindepth 1 -maxdepth 1 ! -name '.godot' -exec rm -rf {{}} + 2>/dev/null || true
tar -xzf /tmp/le-net-tree.tar.gz
dotnet build godot/LastEdict.Godot.csproj -v q -nologo 2>&1 | tail -3
cd godot && timeout 300 "$GODOT" --headless --import --path . >/dev/null 2>&1 || true
""", timeout=1800)
    log("net-duo: EliteBook собран: " + out.stdout.strip().splitlines()[-1] if out.stdout.strip() else "net-duo: EliteBook собран")


def start_relay(work: Path, port: int) -> subprocess.Popen:
    env = dict(os.environ, ASPNETCORE_URLS=f"http://127.0.0.1:{port}", RELAY_DATA=str(work / "relay"))
    exe = REPO / "tools" / "Relay" / "bin" / "Debug" / "net10.0" / "Relay.dll"
    proc = subprocess.Popen(["dotnet", str(exe)], env=env, stdout=open(work / "relay.log", "w", encoding="utf-8"), stderr=subprocess.STDOUT)
    for _ in range(100):
        try:
            with socket.create_connection(("127.0.0.1", port), timeout=0.2):
                return proc
        except OSError:
            time.sleep(0.1)
    raise RuntimeError("посредник не поднялся")


def game_args(role: list[str], days: int, script: int) -> list[str]:
    return ["--headless", "--path", "godot", "res://scenes/map/MapScreen.tscn", "--", *role, "--net-script", str(script),
            "--net-days", str(days)]


def assets_dir() -> str:
    return os.environ.get("D2_ASSETS_DIR") or str((REPO.parent / "disciples-godot" / "assets").resolve())


def main() -> int:
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--case", default="play", choices=["play", "drop", "async"])
    ap.add_argument("--days", type=int, default=3)
    ap.add_argument("--remote", action="store_true", help="клиент — на EliteBook")
    ap.add_argument("--relay", help="готовый посредник (ws://…/ws); без него — свой локальный")
    ap.add_argument("--scenario", default="Arrival of the Pilgrims")
    ap.add_argument("--seats", default="S143PL0001,S143PL0002")
    ap.add_argument("--seed", type=int, default=5)
    ap.add_argument("--no-build", action="store_true")
    ap.add_argument("--timeout", type=int, default=900)
    args = ap.parse_args()

    stamp = time.strftime("%Y%m%d-%H%M%S")
    work = REPO / ".cache" / "net" / f"{args.case}-{stamp}"
    work.mkdir(parents=True, exist_ok=True)
    if not args.no_build:
        build_local()
        if args.remote:
            sync_remote(work)
    port = free_port()
    relay = None if args.relay else start_relay(work, port)
    relay_url = args.relay or f"ws://127.0.0.1:{port}/ws"
    tunnel = None
    procs: list[subprocess.Popen] = []
    try:
        env = dict(os.environ, D2_ASSETS_DIR=assets_dir(), D2_COMMAND_JOURNAL=str(work / "host.journal"))
        room_file = work / "room.txt"
        host_role = ["--net-host", args.scenario, "--net-seats", args.seats, "--net-seed", str(args.seed), "--net-relay", relay_url,
                     "--net-room-out", str(room_file), "--net-nick", "host"]
        started = time.time()
        host = subprocess.Popen([GODOT, *game_args(host_role, args.days, 1)], cwd=REPO, env=env,
                                stdout=open(work / "host.log", "w", encoding="utf-8"), stderr=subprocess.STDOUT)
        procs.append(host)
        for _ in range(600):
            if room_file.exists() and room_file.read_text().strip():
                break
            if host.poll() is not None:
                raise RuntimeError(f"хост вышел до комнаты (код {host.returncode}), {work / 'host.log'}")
            time.sleep(0.2)
        room = room_file.read_text().strip()
        log(f"net-duo: комната {room}, посредник {relay_url}")
        client_role = ["--net-join", room, "--net-nick", "client"]
        if args.case == "drop":
            client_role += ["--net-drop-at", "2"]
        if args.remote:
            remote_relay = relay_url
            if not args.relay:
                # Туннель: на EliteBook 127.0.0.1:<порт> ведёт к посреднику этого ПК (порты ПК наружу не открываются).
                tunnel = subprocess.Popen(["ssh", "-N", "-R", f"{port}:127.0.0.1:{port}", REMOTE], stdout=subprocess.DEVNULL,
                                          stderr=subprocess.DEVNULL)
                time.sleep(2)
            quoted = " ".join("'" + a.replace("'", "'\\''") + "'" for a in game_args(client_role + ["--net-relay", remote_relay], args.days, 2))
            script = f"""{REMOTE_ENV}
cd {REMOTE_ROOT}
/usr/bin/time -v timeout {args.timeout} "$GODOT" {quoted} > /tmp/le-net-client.log 2>&1
echo "exit=$?" >> /tmp/le-net-client.log
"""
            client = subprocess.Popen(["ssh", REMOTE, "bash -s"], stdin=subprocess.PIPE, stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL,
                                      text=True)
            assert client.stdin is not None
            client.stdin.write(script)
            client.stdin.close()
        else:
            client_env = {k: v for k, v in env.items() if k != "D2_COMMAND_JOURNAL"}   # журнал пишет только хост
            client = subprocess.Popen([GODOT, *game_args(client_role + ["--net-relay", relay_url], args.days, 2)], cwd=REPO, env=client_env,
                                      stdout=open(work / "client.log", "w", encoding="utf-8"), stderr=subprocess.STDOUT)
        procs.append(client)
        deadline = time.time() + args.timeout
        while time.time() < deadline and (host.poll() is None or client.poll() is None):
            time.sleep(0.5)
        elapsed = time.time() - started
        if args.remote:
            run(["scp", "-q", f"{REMOTE}:/tmp/le-net-client.log", str(work / "client.log")])
        return verify(work, args, elapsed)
    finally:
        for p in procs:
            if p.poll() is None:
                p.kill()
        if tunnel is not None:
            tunnel.kill()
        if relay is not None:
            relay.kill()


def last(pattern: str, text: str) -> re.Match | None:
    found = list(re.finditer(pattern, text, re.M))
    return found[-1] if found else None


def verify(work: Path, args, elapsed: float) -> int:
    host_log = (work / "host.log").read_text(encoding="utf-8", errors="replace")
    client_log = (work / "client.log").read_text(encoding="utf-8", errors="replace") if (work / "client.log").exists() else ""
    host_done = last(r"^net-done host day=(\d+) hash=(\w+) commands=(\d+)", host_log)
    client_done = last(r"^net-done client day=(\d+) player=(\w+) view=(\w+) bytes=(\d+)", client_log)
    if not host_done or not client_done:
        log(f"net-duo: FAIL — партия не доиграна (хост {'да' if host_done else 'нет'}, клиент {'да' if client_done else 'нет'}); логи {work}")
        return 1
    replay = REPO / "tools" / "JournalReplay" / "bin" / "Debug" / "net10.0" / "JournalReplay.dll"
    out = subprocess.run(["dotnet", str(replay), str(work / "host.journal"), "--view", client_done.group(2)], capture_output=True,
                         text=True, encoding="utf-8", errors="replace")
    (work / "replay.txt").write_text(out.stdout + out.stderr, encoding="utf-8")
    hash_m = last(r"хеш (\w+)", out.stdout)
    view_m = last(r"вид (\w+) (\w+)", out.stdout)
    battles = len(re.findall(r"^mapbattle start|^aibattle start", host_log + client_log, re.M))
    resyncs = len(re.findall(r"resync", host_log + client_log))
    ok = (out.returncode == 0 and hash_m is not None and hash_m.group(1) == host_done.group(2)
          and view_m is not None and view_m.group(2) == client_done.group(3))
    summary = (f"день {host_done.group(1)}, команд {host_done.group(3)}, хеш {host_done.group(2)[:12]}, "
               f"вид клиента {client_done.group(3)[:12]}, боёв на экране {battles}, resync {resyncs}, "
               f"трафик клиента {int(client_done.group(4)) // 1024} КиБ, {elapsed:.0f} с")
    log(("net-duo: ok — " if ok else "net-duo: FAIL — ") + summary + ("" if ok else f"; повтор: {out.stdout.strip()[-300:]}"))
    (work / "result.json").write_text(json.dumps({"ok": ok, "summary": summary}, ensure_ascii=False), encoding="utf-8")
    return 0 if ok else 1


if __name__ == "__main__":
    sys.exit(main())
