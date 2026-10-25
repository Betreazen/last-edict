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
| Turn order in battle | hidden | ✅ strip of portraits above the battlefield, exact for the current and the next round |
| Settings | 15 toggles, no window modes | ✅ a new settings window with tabs: window/fullscreen, vsync, frame cap, picture scaling, large interface, five audio buses, key and gamepad remapping, language |
| Rules data | binary tables fixed in the engine | ✅ typed JSON content with schemas and mod layers |
| Game actions | direct calls | ✅ commands with a state hash — any game can be replayed from its journal |
| Initiative | rolled each round | ✅ rolled a round ahead, effects applied live |
| Random numbers | one generator for everything | ✅ separate streams for battles, AI, loot and the map — one AI change no longer reshuffles every battle |
| Players and races | up to 5 players, one per race | ✅ the same race twice, teams with a shared victory, more than five players, races as mod content, race choice built from the content (mod races included) · ✅ lobby for friends (E7) |
| Balance | by feel | ✅ headless simulator: thousands of AI games per seed, win rates by race, first move, snowballing |
| Screen | 800×600 | ✅ any aspect ratio (16:9, 16:10, 21:9, 4:3): the map fills the screen, windows stay sharp · ✅ HD art pipeline (export → upscale → import, BC7) and an HD display mode: drawn at window resolution, vector text, sharpening · ✅ wide battle like the DisciplesGL wrapper · 📋 an HD art pack |
| Input | mouse and keyboard | ✅ full gamepad: stick pointer, D-pad between buttons, button hints for Xbox, PlayStation and Steam Deck, remapping · 🚧 tested on real devices |
| Multiplayer | LAN of 2002 | ✅ friends' rooms by code through a small relay: the host computes the rules, each player sees only what their fog shows · ✅ AI on free seats, turn timer, async games (move — close the game — your friend moves later) · ✅ battles between two players, each commanding their own units · 🚧 rejoining in the middle of a battle |
| Building window | a fixed tree on a panel | ✅ the tree comes from data: branch tabs, zoom to the cursor, drag with the middle button, gamepad, a card for the chosen building; mods add branches and buildings without code |
| Modding | external tools | ✅ built-in Studio: building tree, any content by schema with undo, a battle arena and AI stand (base vs mod), map painting at any size, hot reload into a running game, `d2mod` CLI · ✅ new buildings, units (own pictures, attacks, immunities, auras), items and lords by form; map objects, players and events; layer conflicts and a packed mod archive · 📋 mod abilities (chance of a second hit, recovery in battle, marks on the target, "only one in the squad"), own battle animations |

✅ done · 🚧 in progress · 📋 planned

### Turn-order strip

<a href="https://github.com/Betreazen/last-edict/releases/download/media/compare-battle-queue.png"><img src="https://github.com/Betreazen/last-edict/releases/download/media/compare-battle-queue-small.png" alt="Battle in the original and in Last Edict with the turn-order strip"></a>

Who acts next is visible at a glance: portraits in initiative order, the current unit highlighted, waiting and skipping units
marked; hovering a portrait highlights the unit on the field.

### Widescreen

<a href="https://github.com/Betreazen/last-edict/releases/download/media/compare-widescreen.png"><img src="https://github.com/Betreazen/last-edict/releases/download/media/compare-widescreen-small.png" alt="The world map at 16:9 before and in Last Edict"></a>

The map fills any aspect ratio instead of a 4:3 picture with bars; windows and panels keep their pixel-sharp size.

### Settings window

<a href="https://github.com/Betreazen/last-edict/releases/download/media/compare-settings.png"><img src="https://github.com/Betreazen/last-edict/releases/download/media/compare-settings-small.png" alt="The original options screen and the Last Edict settings window"></a>

One window with tabs — screen, sound, game, battle, controls, language — with hints and a 15-second confirmation for a new window mode.

### HD

<a href="https://github.com/Betreazen/last-edict/releases/download/media/compare-hd-detail.png"><img src="https://github.com/Betreazen/last-edict/releases/download/media/compare-hd-detail-small.png" alt="A battle detail with the original art and with the upscaled HD layer"></a>

With an upscaled art layer (your own upscale of your own copy — the art never ships with the project) the game draws straight at
window resolution: units, portraits and the capital at full detail, text in a vector font, an optional sharpening pass. All shots
here use a ×3 layer at 1920×1080. Full-size
pairs: [world map](https://github.com/Betreazen/last-edict/releases/download/media/compare-hd-map.png), [battle](https://github.com/Betreazen/last-edict/releases/download/media/compare-hd-battle.png), [hero window](https://github.com/Betreazen/last-edict/releases/download/media/compare-hd-hero.png).

### Building window and Studio

<a href="https://github.com/Betreazen/last-edict/releases/download/media/compare-build-tree.png"><img src="https://github.com/Betreazen/last-edict/releases/download/media/compare-build-tree-small.png" alt="The building window before and in Last Edict"></a>

The building tree comes from data: branches are tabs, the tree zooms to the cursor and pans with the middle button or a gamepad, the
chosen building opens as a card. Mods add branches and buildings without code — and the built-in
[Studio](https://github.com/Betreazen/last-edict/releases/download/media/studio-tree.png) edits them with undo while a running game reloads the mod on save.

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
<tr>
<td><a href="https://github.com/Betreazen/last-edict/releases/download/media/studio-units.png"><img src="https://github.com/Betreazen/last-edict/releases/download/media/studio-units-small.png" alt="Studio: a mod unit"></a><br><sub>Studio: a new unit with its own pictures</sub></td>
<td><a href="https://github.com/Betreazen/last-edict/releases/download/media/studio-map.png"><img src="https://github.com/Betreazen/last-edict/releases/download/media/studio-map-small.png" alt="Studio: map objects"></a><br><sub>Studio: map objects, players and events</sub></td>
</tr>
<tr>
<td><a href="https://github.com/Betreazen/last-edict/releases/download/media/lobby.png"><img src="https://github.com/Betreazen/last-edict/releases/download/media/lobby-small.png" alt="Multiplayer lobby"></a><br><sub>Multiplayer lobby: a room for friends</sub></td>
<td></td>
</tr>
<tr>
<td><a href="https://github.com/Betreazen/last-edict/releases/download/media/controls.png"><img src="https://github.com/Betreazen/last-edict/releases/download/media/controls-small.png" alt="Controls"></a><br><sub>Key and gamepad remapping</sub></td>
<td><a href="https://github.com/Betreazen/last-edict/releases/download/media/gamepad.png"><img src="https://github.com/Betreazen/last-edict/releases/download/media/gamepad-small.png" alt="Gamepad hints"></a><br><sub>Playing with a gamepad: button hints</sub></td>
</tr>
</table>

## Roadmap

| Stage | Status |
|---|---|
| Faithful port of the original, proven by harnesses | ✅ done (the reference port) |
| E1 Quick wins — turn-order strip, technical settings, faster checks | ✅ done |
| E2 Foundation — content schema, effect registry, commands and journal, assemblies | ✅ done |
| E3 Separation — this repository, own test content, "port vs port" snapshots | ✅ done |
| E4 Own rules — initiative a round ahead, separate random streams, five races, teams, more players, balance simulator | ✅ done |
| E5 Screen and input — any aspect ratio, new settings window, gamepad and Steam Deck, HD art pipeline | ✅ done |
| E6 Building tree window and the Studio | ✅ done |
| Studio 2 — creating buildings, units, attacks and map objects in the Studio | ✅ done |
| Studio 3 — mod abilities, own battle animations, a custom arena | 📋 next |
| E7 Play with friends — lobby, relay, async games, battles between players | ✅ done |
| E8 Network polish — rejoining mid-battle, chat, lord names | 📋 next |

Decisions and details: [`docs/own-version-roadmap.md`](docs/own-version-roadmap.md).

## Progress

<!-- Новые записи — сверху: дата, что изменилось, снимки «было / стало» (порядок — docs/media.md). -->

- **2026-10-25 — Playing with friends over the network.** "Multiplayer" in the main menu opens a lobby: create a room, give friends
  its six-letter code, choose who sits where (you, a friend or the AI), set a turn timer or make the game asynchronous. The host's game
  computes the rules and sends each player only what their fog of war shows, as small changes; a small relay forwards the traffic, so
  nobody opens ports. Turns go in the usual order, AI players move between people. Your battles play on your own screen with no
  network delay and the host replays them to check; when two players meet, each commands their own units. A dropped connection comes
  back on its own. In an async game the relay keeps the game: make your move, close the game, and your friend moves when they like.
  Two machines through a remote relay end with exactly the same game as a replay of the host's journal, at about 0.3 s per command and
  about 40 KB per day. Rules unchanged: the snapshots are identical ([the lobby](https://github.com/Betreazen/last-edict/releases/download/media/lobby.png)).

- **2026-10-25 — Studio 2: a mod without touching files.** The Studio creates buildings, units, items and lords from a form,
  each with its own pictures (HD too) that never replace the original art. A new unit starts as a copy of an existing one. Units get
  attacks, immunities and auras — a new mod-only rule: a unit strengthens its squad, its neighbours or itself, or weakens the
  enemy, while it stands on the field. Reference pickers search by name and show the icon, and check errors sit next to their field.
  On the map the Studio places cities, ruins, shops, crystals, bags and squads, edits players, races and events, and starts a game on
  that map. A mod gets a layer-conflict window and packs into an archive with a file list, checksums and the ruleset hash that
  network games compare. Every change is a command with undo. Rules unchanged: the snapshots are identical
  ([a new unit](https://github.com/Betreazen/last-edict/releases/download/media/studio-units.png), [map objects](https://github.com/Betreazen/last-edict/releases/download/media/studio-map.png)).

- **2026-10-25 — A new ×3 HD layer.** All the art was upscaled again with different settings at ×3 (the earlier ×4 pass looked
  worse); the gallery and every before / after pair are re-shot at 1920×1080 with it. A new upscale drops in without code changes: the
  same sheets go through the upscaler and the import picks up the new frames. Oversized cursors are now scaled down to fit.

- **2026-10-24 — Building window and Studio.** The building tree is now data: branches are tabs over the tree, it zooms to the cursor
  and pans with the middle button or a gamepad, and the chosen building opens as a card instead of the old side panel. Mods add
  branches and buildings without code. A built-in Studio edits a mod with undo: the building tree, any content by its schema, a battle
  arena and an AI stand that compare the base game with the mod, and maps of any size; a running game reloads the mod on save.
  Maps up to 1024×1024 are measured and playable. Water, map spells and battle event pictures now use the HD art too.
  Rules unchanged: the snapshots are identical ([building window before / after](https://github.com/Betreazen/last-edict/releases/download/media/compare-build-tree.png),
  [the Studio](https://github.com/Betreazen/last-edict/releases/download/media/studio-tree.png)).

- **2026-10-24 — HD and crisp at any resolution.** An HD display mode draws the game at window resolution, so an upscaled art layer
  shows at full detail ([before / after](https://github.com/Betreazen/last-edict/releases/download/media/compare-hd-detail.png)); the map keeps its own pixel size and shows more of the world
  ([before / after](https://github.com/Betreazen/last-edict/releases/download/media/compare-hd-map.png)); text uses a vector font; an optional sharpening pass; scrolls and cards over the map keep
  their original size; the battle goes wide on 16:9 and wider, like the DisciplesGL wrapper ([before / after](https://github.com/Betreazen/last-edict/releases/download/media/compare-hd-battle.png)).
  The HD tools resume an interrupted import, never write game art where git would pick it up, and the capital no longer spends
  a gigabyte of video memory. Rules unchanged: the snapshots are identical.

- **2026-10-22 — Screen and input.** The map fills any aspect ratio ([before / after](https://github.com/Betreazen/last-edict/releases/download/media/compare-widescreen.png)); a new settings
  window with tabs, key and gamepad remapping ([before / after](https://github.com/Betreazen/last-edict/releases/download/media/compare-settings.png)); the whole game is playable with a gamepad —
  the stick drives the pointer, the D-pad jumps between buttons, hints follow the device (Xbox, PlayStation, Steam Deck); the race
  choice is built from the content, so mod races appear on their own; an HD art round trip (export → upscale → import, BC7 textures).
  Hotseat is gone — friends will play over the network (E7). Rules unchanged: the snapshots are identical.

- **2026-10-22 — Own rules.** Initiative is rolled a round ahead and the turn-order strip shows two exact rounds; every system draws
  from its own random stream; rules work on content ids, so a mod race needs no original ids; two players of the same race, teams with
  a shared victory and six-player maps; a headless balance simulator (600 test-content games in under 90 s on 32 threads).
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
