# Моддинг: архитектура

Общее устройство — [`../studio-architecture.md`](../studio-architecture.md). Здесь — что меняется в загрузке данных и что
добавляет ядро для модов.

## 1. Сейчас

```text
GameData (godot/autoload) ─┬─ GameContent.Load(dataDir, mods)  экраны, кадры, граф, каталог, gfx, MapArt, ролики, строки, шрифты — слоятся
                           ├─ GameBook.Load(RulesDir)           data/rules — ТОЛЬКО база
                           └─ SoundAssets.Load(...)             data/sound — ТОЛЬКО база (godot/scenes/map/GameSound.cs)
```

`GameTables.Read(dir, name)` кэширует таблицу по «путь + время записи + размер» — готовая опора для перечитывания.

## 2. Слой правил

| Шаг | Изменение | Где |
|---|---|---|
| 1 | `RulesSource` — список каталогов `data/rules` (база + `<мод>/data/rules` по порядку слоёв) | `src/Import.Legacy.Formats/Dbf/RulesSource.cs` |
| 2 | `GameTables.Read(RulesSource, name)`: база + наложение строк по ключу из схемы (§3 спецификации); кэш по ключу «набор файлов + времена» | `GameTables.cs` |
| 3 | все загрузчики (`GameBook.Load`, `RulesLoader`, `EconomyLoader`, `SpellLoader`, `ModifierLoader`, `MapCards.LandmarkNames`…) принимают `RulesSource` вместо строки каталога; перегрузка `string dir` остаётся для импорта и харнессов (читают `.dbf` игры) | `src/Engine.Play/**` |
| 4 | `GameData.Book` строится из `RulesSource` активных модов | `godot/autoload/GameData.cs` |
| 5 | харнессы С1/С2/Ф7 продолжают читать базу (или `.dbf`) — моды не влияют на доказательства | тесты |

Проверка шага: `dotnet test` до/после — все прежние тесты зелёные (16.10 — 2033); новый тест «мод меняет `HIT_POINT` → `GameBook` видит
новое, без мода — старое».

## 3. Схема таблиц

`data/rules/schema/*.schema.json` + класс `TableSchema` (`src/Import.Legacy.Formats/Dbf/TableSchema.cs`): ключ, типы, ссылки, пределы,
смысл. Первичная генерация — скрипт `tools/RulesImport --schema` (типы и длины из `.dbf`, ключ — первая колонка `*_ID`),
`ref` и `meaning` — ручная разметка с источником (документ/адрес реверса). Тест: схема покрывает все колонки всех таблиц,
все `ref` разрешаются на базе (0 висячих).

## 4. Ядро `D2.Authoring` для модов

| Класс | Назначение |
|---|---|
| `ModProject` | каталог мода: `mod.json` (v1/v2), слои ниже него, запись только внутри |
| `ModGraph` | порядок наложения по `dependencies`/`loadAfter`, циклы, конфликты; заменяет порядок «по имени» в `GameContent.Discover` (порядок без зависимостей не меняется — тест на текущих модах) |
| `LayerView` | для любой сущности: «база / каждый слой / итог» и кто перекрыл (окно конфликтов как в Creation Kit) |
| `Validation/Mod/*` | правила `MODnnn` |
| `Packaging/ModPacker` | zip + `manifest.json`, `install` |
| `TableDocument` | правка строк таблицы мода командами (`SetCell`, `AddRow`, `RemoveRow`), отмена — общая `EditHistory` |

## 5. CLI `d2mod`

```text
d2mod new       <каталог> --id <id>                 # mod.json v2 и раскладка
d2mod validate  <каталог> [--all-layers]            # mod=<id> files=N errors=E warnings=W; код 0/1
d2mod diff      <каталог> [--table Gunits]           # что мод меняет относительно базы, по строкам и колонкам
d2mod texts     <каталог> --missing ru
d2mod pack      <каталог> [--out <zip>]
d2mod install   <zip> [--force]
d2mod run       [--mod <каталог>] [--only] [--scenario <Id> --player <PlayerId>]
d2mod migrate   <каталог>                            # при смене формата
d2mod map …                                          # см. ../map-editor/architecture.md §4
```

`tools/d2mod` — консольный проект .NET поверх `D2.Authoring`; Python-обёрток не нужно (проверки — `tests/test_d2mod.py`).

## 6. Окно «Мод» (позже, K4)

`godot/scenes/studio/mod/`: дерево файлов слоя, таблица строк с колонками по схеме (`meaning` в заголовке, `ref` — выпадающий
список с именами из строк), три колонки «база / мод / итог», панель конфликтов (`LayerView`), панель проблем (`MODnnn`),
предпросмотр: юнит — карточка и анимация боя (`BattleScene`), экран — `UiScreenView`, кадр — картинка.

## 7. Горячая перезагрузка

`ContentWatcher` (ядро): `FileSystemWatcher` по каталогам активных слоёв → событие «изменились слои X» → игра пересобирает
`GameContent` (экраны, строки, графика, звук) и перерисовывает открытое; таблицы правил — только сообщение «перезапустите
партию». Включается `--watch` (по умолчанию выкл., чтобы не мешать проверкам).
