<div align="center">

# Last Edict

**An unofficial, fan-made reimagining of *Disciples II: Rise of the Elves* — the same dark, turn-based world,<br>
rebuilt on a modern engine to be more comfortable, more responsive and open to mods.**

[![CI](https://github.com/Betreazen/last-edict/actions/workflows/ci.yml/badge.svg)](https://github.com/Betreazen/last-edict/actions/workflows/ci.yml)
[![Licence: GPL v3](https://img.shields.io/badge/code-GPL%20v3-blue.svg)](LICENSE)
![Godot 4.7](https://img.shields.io/badge/Godot-4.7-478cbf.svg)
![.NET 10](https://img.shields.io/badge/.NET-10-512bd4.svg)

<a href="https://github.com/Betreazen/last-edict/releases/download/media/main-menu.png"><img src="https://github.com/Betreazen/last-edict/releases/download/media/main-menu-small.png" width="640" alt="Main menu"></a>

</div>

> [!IMPORTANT]
> **Fan project, no game data included.** This repository contains no files, art, sound, texts or tables of the original game.
> To play you need your own purchased copy of *Disciples II* (GOG); Last Edict imports everything from it on first start.
> *Disciples* is a trademark of its respective owners. Last Edict is not affiliated with, endorsed or sponsored by them.
> Screenshots on this page are hosted as release files, outside the git history.

## What we are trying to achieve

*Disciples II* (2002) is still loved for its art, atmosphere and tight tactical battles — and held back by a 2002 engine:
an 800×600 screen, rules baked into code, no gamepad, small maps, five players at most, no way to play with friends today.

Last Edict starts from a **faithful port**: battle formulas, AI decisions, turn order and economy were rebuilt in C# and proven
equal to the original by test harnesses that replay thousands of recorded battles and AI turns. On top of that proven core we
change what the original could not:

| Pillar | What it means |
|---|---|
| ⚔️ **The same game at heart** | Rules start exactly as in the original. Every rule change is deliberate and visible: "port vs port" snapshots replay AI-only games and battles with a fixed seed and fail on any difference until it is accepted on purpose. |
| 🧱 **A modern foundation** | Data separate from code: every unit, spell, building and race is a typed JSON entity, mods layer on top; attack effects are a registry, not a `switch`; every game action is a command, so replays, bug reports and network play come from the same journal. |
| 🎛️ **Comfort and responsiveness** | Things the original never had: a turn-order strip in battle, modern settings (window, fullscreen, vsync, frame cap, audio buses), any aspect ratio, full gamepad control for the Steam Deck. |
| 🌍 **Bigger worlds** | All five races on one map, the same race for several players, teams with a shared victory, more than five players, square maps of any size. |
| 🤝 **Play with friends** | Rooms by code or invitation through a small relay, AI on free seats, asynchronous games, optional turn timer. |
| 🛠️ **Made for modders** | A "Studio" for the building tree, content and maps; new mechanics as data or trusted C# plug-ins; high-resolution art through a simple export/import round trip. |

## How it differs from the original

| Feature | Disciples II | Last Edict |
|---|---|---|
| Turn order in battle | hidden | ✅ strip of portraits above the battlefield · 📋 exact for two rounds ahead |
| Settings | 15 toggles, no window modes | ✅ window/fullscreen, vsync, frame cap, Alt+Enter, five audio buses · 📋 new settings window: tabs, key and gamepad remapping, language |
| Rules data | binary tables fixed in the engine | ✅ typed JSON content with schemas and mod layers |
| Game actions | direct calls | ✅ commands with a state hash — any game can be replayed from its journal |
| Initiative | rolled each round | 📋 rolled a round ahead, effects applied live |
| Players and races | up to 5 players, one per race | 📋 all races on one map, the same race twice, teams, more than five players |
| Screen | 800×600 | 📋 any aspect ratio (16:9, 16:10, 21:9, 4:3), HD art |
| Input | mouse and keyboard | 📋 full gamepad, Steam Deck |
| Multiplayer | LAN of 2002 | 📋 friends' rooms via a relay, async games |
| Modding | external tools | 📋 built-in Studio: building tree, content, maps without a size limit |

✅ done · 🚧 in progress · 📋 planned

### Turn-order strip

<a href="https://github.com/Betreazen/last-edict/releases/download/media/compare-battle-queue.png"><img src="https://github.com/Betreazen/last-edict/releases/download/media/compare-battle-queue-small.png" alt="Battle in the original and in Last Edict with the turn-order strip"></a>

Who acts next is visible at a glance: portraits in initiative order, the current unit highlighted, waiting and skipping units
marked; hovering a portrait highlights the unit on the field.

## Gallery

<table>
<tr>
<td><a href="https://github.com/Betreazen/last-edict/releases/download/media/world-map.png"><img src="https://github.com/Betreazen/last-edict/releases/download/media/world-map-small.png" alt="World map"></a><br><sub>World map</sub></td>
<td><a href="https://github.com/Betreazen/last-edict/releases/download/media/battle.png"><img src="https://github.com/Betreazen/last-edict/releases/download/media/battle-small.png" alt="Battle"></a><br><sub>Battle</sub></td>
</tr>
<tr>
<td><a href="https://github.com/Betreazen/last-edict/releases/download/media/city.png"><img src="https://github.com/Betreazen/last-edict/releases/download/media/city-small.png" alt="Capital"></a><br><sub>Capital</sub></td>
<td><a href="https://github.com/Betreazen/last-edict/releases/download/media/spellbook.png"><img src="https://github.com/Betreazen/last-edict/releases/download/media/spellbook-small.png" alt="Spellbook"></a><br><sub>Spellbook</sub></td>
</tr>
<tr>
<td><a href="https://github.com/Betreazen/last-edict/releases/download/media/hero.png"><img src="https://github.com/Betreazen/last-edict/releases/download/media/hero-small.png" alt="Hero"></a><br><sub>Hero and inventory</sub></td>
<td><a href="https://github.com/Betreazen/last-edict/releases/download/media/diplomacy.png"><img src="https://github.com/Betreazen/last-edict/releases/download/media/diplomacy-small.png" alt="Diplomacy"></a><br><sub>Diplomacy</sub></td>
</tr>
</table>

## Roadmap

| Stage | Status |
|---|---|
| Faithful port of the original, proven by harnesses | ✅ done (the reference port) |
| E1 Quick wins — turn-order strip, technical settings, faster checks | ✅ done |
| E2 Foundation — content schema, effect registry, commands and journal, assemblies | ✅ done |
| E3 Separation — this repository, own test content, "port vs port" snapshots | ✅ done |
| E4 Own rules — initiative a round ahead, separate random streams, five races, teams, more players, balance simulator | 📋 next |
| E5 Screen and input — any aspect ratio, new settings window, gamepad and Steam Deck, HD art pipeline | 📋 |
| E6 Building tree window and the Studio | 📋 |
| E7 Play with friends | 📋 |

Decisions and details: [`docs/own-version-roadmap.md`](docs/own-version-roadmap.md).

## Progress

<!-- Новые записи — сверху: дата, что изменилось, снимки «было / стало» (порядок — docs/media.md). -->

- **2026-10-22 — Last Edict is born.** Separated from the reference port: no game data in the repository, own CC0 test content
  (`content/test-pack`), "port vs port" snapshots that stay identical to the reference, CI on the test content alone.
- **2026-10-21 — Turn-order strip and modern settings.** Battle shows who acts next; window, fullscreen, vsync, frame cap and audio
  buses in the settings; the settings file got a version and migration.

## Getting started

You need **Disciples II: Rise of the Elves, GOG 3.01** (your own copy), the **.NET 10 SDK** (pinned in `global.json`) and
**Godot 4.7.2 .NET**. Optional: [ffmpeg](https://ffmpeg.org) to import the videos.

```bash
git clone https://github.com/Betreazen/last-edict && cd last-edict
dotnet build LastEdict.sln
dotnet test LastEdict.sln                                   # runs on the bundled test content; tests that need game data skip
py tools/import_game.py --game "C:/GOG Games/Disciples II"  # import game data into data/ and assets/ (ignored by git)
godot --path godot                                          # play
```

Started without imported data, the game asks for your Disciples II folder and imports it into `user://game`.

## How the rules are guarded

- `content/test-pack` — a small original rule set (two races and neutrals) and two maps under CC0, generated by
  `tools/test_pack.py`. CI builds and tests the engine on it alone.
- `tests/snapshots` — AI-only games with a fixed seed play 15–20 days plus battles; the state hash after every command is compared
  with the stored file. Snapshots of imported scenarios run once game data has been imported and match the reference port exactly.

## Коротко по-русски

**Last Edict** — неофициальная фанатская переработка *Disciples II: Rise of the Elves*: та же игра по сути, на современном движке
(Godot 4.7, C#/.NET 10), удобнее, отзывчивее и открыта модам. Данных игры в репозитории нет — нужна своя копия (GOG), игра
импортирует всё из неё при первом запуске. Начало — точный порт оригинала, совпадение доказано харнессами; дальше — полоса очереди
ходов, современные настройки, любое соотношение сторон, геймпад и Steam Deck, все пять рас на одной карте, команды и больше пяти
игроков, игра с друзьями по коду комнаты, Студия для модов. План — [`docs/own-version-roadmap.md`](docs/own-version-roadmap.md).

## Licence

Code: GNU GPL v3 (`LICENSE`). `content/test-pack`: CC0. Fonts in `data/fonts`: SIL Open Font License. Game data and screenshots of
the game belong to their respective owners and are not part of this repository.
