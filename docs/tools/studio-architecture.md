# «Мастерская»: общее устройство

Общее для обоих рабочих пространств. Частности — `map-editor/architecture.md`, `modding-kit/architecture.md`.

## 1. Слои

```text
src/Engine.Rules            правила (есть) — редактор только читает: проходимость, Land, EventConditions, Sites…
src/Engine.Play             данные и загрузчики (есть): GameContent, ScenarioFile, GameTables, GfxAssets, MapLooks…
src/LastEdict.Authoring        НОВОЕ ядро инструментов, без Godot:
   Project/             проект = каталог мода (ModProject): mod.json, слои ниже него, пути
   Documents/           документы: ScenarioDocument, TableDocument, ModManifestDocument
   Commands/            команды правки (IEditCommand) и история (EditHistory)
   Validation/          правила проверки (IValidationRule), отчёт (ValidationReport)
   Ids/                 выдача id по схеме .sg (IdAllocator)
   Looks/               пересчёт вида MapLooks из MapState (LooksBuilder)
   Packaging/           сборка архива мода, манифест файлов, хэши
src/LastEdict.Authoring.Tests  тесты ядра (xunit, как LastEdict.Data.Tests)
tools/d2mod             НОВОЕ CLI (dotnet, тонкая обёртка над LastEdict.Authoring)
godot/scenes/studio     НОВОЕ окно «Мастерской» (Control-интерфейс Godot), рабочие пространства Map/ и Mod/
```

Правило проекта «если можно проверить без окна — место в `src/`» ([`docs/godot-architecture.md`](../godot-architecture.md))
действует жёстко: в `godot/scenes/studio` нет решений, только узлы, ввод и вызовы команд ядра.

## 2. Почему тот же проект Godot, а не отдельный

| Вариант | За | Против |
|---|---|---|
| **Отдельная главная сцена в `godot/` (рекомендация)** | `MapView`, `GroundArt`, `MapObjects`, `GfxTextures`, `GameData` переиспользуются без копий; одна сборка `LastEdict.Godot` | главная сцена игры — `UiFlow.tscn`; «Мастерская» запускается явным путём `res://scenes/studio/Studio.tscn` или своим пресетом экспорта |
| Отдельный проект `studio/` с `ProjectReference` на `LastEdict.Godot` | чистое разделение | сцены Godot (`.tscn`) между проектами не делятся — `MapView.tscn` пришлось бы копировать |
| Встроить в игру (как OpenRA) | одно окно | игра и редактор смешивают ввод и состояние; противоречит правилу «сцена только рисует» |

Запуск игры с картой — **отдельный процесс** `godot --path godot res://scenes/map/MapScreen.tscn -- --scenario <Id>
--player <PlayerId> --mods <корень>` (аргументы уже есть: `MapSource`, `MapScreen`). Падение партии не роняет редактор.

## 3. Документ, команды, отмена

- Состояние документа — неизменяемые записи порта (`ScenarioData`, `MapState` — `record`). Команда получает документ и
  возвращает новый (`IEditCommand.Apply(doc) → doc'`), история хранит пары «до/после» (общие части записей разделяются,
  копируется только изменённое: сетка клеток 144×144 = 20 КБ — копия на мазок дешёвая).
- Мазок кистью — одна команда (собирается за время зажатой кнопки), отмена — целиком.
- `UndoRedo` Godot не используется: история в ядре проверяется тестами и доступна CLI (`d2mod apply script.json`).
- Каждая команда имеет имя для журнала и сериализуемые параметры → журнал правок = сценарий воспроизведения для тестов
  («применить 200 команд, отменить все → файл байт в байт как был»).

## 4. Проверка и отчёт

`ValidationReport { Errors, Warnings, Items[ {RuleId, Level, Target, Location, Message} ] }`. Источник правил — общий для
окна и CLI. Проверка запускается: при сохранении (только быстрые правила, ≤100 мс на 144×144), по кнопке и `d2mod validate`
(все правила, в том числе «пробная загрузка в `GameSession`»).

## 5. Горячая перезагрузка

- Ядро следит за файлами проекта (`FileSystemWatcher`) и перечитывает изменённый слой; кэш таблиц уже учитывает время
  записи (`GameTables.Read`: ключ — путь + время + размер).
- Игра, запущенная из «Мастерской», получает перезагрузку по файлу-сигналу `<мод>/.studio/reload` (вопрос пользователю:
  нужна ли перезагрузка в идущей партии — рекомендация: только экраны, строки и графика; правила и карта — перезапуском
  партии, иначе состояние партии расходится с данными).

## 6. Проверки «числами» (стиль проекта)

| Что | Как |
|---|---|
| ядро | `dotnet test src/LastEdict.Authoring.Tests` — круговые проверки, команды, проверка карт, сборка модов |
| CLI | `tests/test_d2mod.py` (pytest, как `tests/test_tools_guards.py`): код возврата и строка итога |
| окно без глаз | `Studio.tscn -- --check --open <мод> --scenario <Id> --script <команды.json>` печатает `studio: doc=<Id> tiles=… objects=… errors=… undo=…` и выходит `Quit(код)`; строка — в `tools/ui-render-check.py` |
| запуск игры | `MapScreen.tscn -- --check --scenario <Id> --mods <корень>` (уже есть `--check`) |

## 7. Безопасность

- Инструмент пишет только внутри открытого каталога мода и `.studio/` в нём; путь вне — отказ (проверка в `ModProject`).
- Никаких ссылок на каталоги (symlink/junction) — данные другого места передаются переменными `D2_ASSETS_DIR`, `D2_GAME_DIR`.
- Рабочие копии `../dg-wt-*` для разработки — только `python tools/wt_remove.py <путь> --branch` при сносе.
- После серии правок кода — `PYTHONHASHSEED=0 graphify update .`.
