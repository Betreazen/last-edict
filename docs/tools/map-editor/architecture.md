# Редактор карт: архитектура

Общее устройство «Мастерской» — [`../studio-architecture.md`](../studio-architecture.md). Здесь — что переиспользуем и что
добавляем для «Карты».

## 1. Что уже есть и берётся как есть

| Нужно редактору | Есть в порте | Где |
|---|---|---|
| чтение/запись сценария | `ScenarioFile.Read/Write/FromSg`, `ScenarioData` | `src/Engine.Play/Play/ScenarioFile.cs` |
| слои и поиск сценария | `GameContent.Load/Discover/ScenarioPath` | `src/Game.UiModel/Content/GameContent.cs` |
| модель карты и клетки | `MapState`, `Tile(byte Raw)`, `Ground` | `src/Engine.Rules/Map/Model.cs` |
| вид для отрисовки | `MapLooks` и `*Look` | `src/Engine.Play/Map/MapLooks.cs` |
| земля без движка | `GroundComposer(GroundType[,], mapSeed, IGroundArt)`, `GroundChunks.Refresh`, `GroundGeometry.CellOrigin/CellAt` | `src/Game.UiModel/Gfx/GroundComposer.cs`, `Ground.cs`; `MapSource.GroundCells(map)` |
| объекты карты | `MapScene.Build(looks, MapArt, Gfx, animated, viewer, objectives)`, `MapObjects` | `src/Game.UiModel/Gfx`, `godot/scenes/map/MapObjects.cs` |
| вид карты в Godot | `MapView` (`Viewer == null` — уже «вид редактора»: рисуются `LOCATION*`, туман не режет) | `godot/scenes/map/MapView*.cs`; пример без партии — `godot/scenes/dev/GroundTest.cs` |
| мини-карта, туман | `MiniMap`, `MapFog` | `src/Engine.Play/Map`, `godot/scenes/map/MiniMapView.cs` |
| проходимость, вход, путь | `Movement.Passable/BlockedByObject/IsDeepWater`, `Sites.Entrance`, `PathFinder.Find` | `src/Engine.Rules/Map` |
| инварианты карты | `MapInvariants.Check(MapState, GameBook?) → Violation(Code, Object, Detail)` (unit_missing, slots, hp, leader, owner, off_map, plan, inside, capacity, item, relation…) | `src/D2.Verification/Diagnostics` |
| события | `ScenarioEventModel` (виды через `JsonDerivedType`), `EventConditions`, `ScenarioEvents` | `src/Engine.Rules/Map` |
| карточки по правой кнопке | `MapCards` | `src/Game.UiModel/Map/MapCards.cs` |
| бой двух отрядов («шансы») | `BattleSim` | `src/Engine.Rules/Battle` |
| старт партии | `NewGame.Start(ScenarioData, NewGameSetup, GameCatalog, GameBook?)` | `src/Game.UiModel/Play/NewGame.cs` |
| запуск карты | `MapScreen.tscn -- --scenario --player --humans --mods --check` | `godot/scenes/map/MapScreen.cs`, `MapSource.cs` |
| графика кистей | `BRUSHI*`/`BRUSHV*` в архивах (кадры редактора оригинала) | `data/gfx/*.json` |

## 2. Что добавить

### Ядро `src/D2.Authoring` (без Godot)

| Класс | Назначение |
|---|---|
| `ScenarioDocument` | неизменяемый документ: `ScenarioData` + карточка `ScenarioInfo` + тексты по языкам + путь в моде |
| `LooksBuilder.From(MapState, …) → MapLooks` | пересчёт списков вида из модели (источник истины — `Map`); проверка: на 85 сценариях `LooksBuilder` = импорт |
| `IdAllocator` | следующий свободный id вида (`KC`, `UN`, `FT`, `EV`…) по схеме `S<NNN><КК><XXXX>`; у `MapState` есть `IdNext` (max+1) — опереться на него |
| `PlanIndex`-обновление | пересчёт `Plan` по следам объектов (как `rebuild_plan`/`footprint` в `tools/synth-maps/synth_build.py`: столица 5×5, город 4×4, лавки и руины 3×3) |
| `Commands/Map/*` | `PaintTerrain`, `PaintGround`, `PlaceTrees`, `PlaceMountain`, `PlaceRoad`, `PlaceObject`, `MoveObject`, `DeleteObject`, `EditStack`, `EditSite`, `EditPlayer`, `SetRelation`, `AddEvent`, `EditEvent`, `AddZone`, `SetVariable`, `SetText`, `PasteRegion`, `RandomizeMountains`, `RandomizeTerrain` |
| `Validation/Map/*` | правила `MAPnnn` ([`spec.md`](spec.md) §11); обёртка над `MapInvariants.Check` + свои |
| `Palette` | что можно ставить: юниты (`Gunits` всех слоёв), предметы, заклинания, ориентиры (`GLmark`), картинки лавок/руин/мешков — из таблиц и описей **всех активных слоёв** |
| `Playtest` | временный слой `.studio/playtest`, строка запуска процесса |

Зависимость ядра от таблиц правил — через `GameBook` и `GameTables`; пока у таблиц нет слоя модов (K0 в
[`../modding-kit/plan.md`](../modding-kit/plan.md)), палитра видит только базу.

### Godot `godot/scenes/studio`

```text
studio/
  Studio.tscn  StudioView.cs            окно: меню, вкладки рабочих пространств, строка состояния, --check
  map/
    MapWorkspace.tscn  MapWorkspace.cs  карта (MapView в режиме Viewer=null) + панели
    MapTools.cs                         ввод → команды ядра (кисть, выбор, перенос)
    Palette.tscn  Inspector.tscn         палитра и свойства (Control: Tree, ItemList, SpinBox…)
    StackEditor.tscn  SiteEditor.tscn  PlayerEditor.tscn  EventEditor.tscn  TextsEditor.tscn
    ProblemsPanel.tscn                  отчёт проверки, двойной щелчок — к объекту
```

`MapView` сейчас создаётся вокруг `GameSession` (`Show(session, name, viewer)`). Для редактора: построить сессию из
документа (как стоячий режим: `new GameSession(new GameState(GameInfo.FromMap(name, looks), looks), MapArt, Book)`) и после
команды обновлять **только изменённое**: земля — `GroundChunks.Refresh` затронутых кусков, объекты — пересборка `MapScene`.
Если перестройка всей сессии на каждую команду окажется ≤ 50 мс на 144×144 — оставить простой путь (замер в E1.2).

Интерфейс редактора — стандартные `Control` Godot со своей темой, а не экраны игры из `screens.json` (экраны игры —
800×600 и под логику игры; у редактора свои задачи). Шрифты — из `data/fonts`.

## 3. Поток правки

```text
ввод (MapTools) → IEditCommand → EditHistory.Apply → новый ScenarioDocument
   → LooksBuilder (только при изменении объектов) → MapView: Refresh(кусок/объекты)
   → быстрые правила проверки (фон) → ProblemsPanel
```

## 4. CLI (тот же код)

```text
d2mod map new     --mod <каталог> --id <Id> --size 96 --races HU,UN,DW,HE
d2mod map apply   --mod <каталог> --id <Id> --script commands.json     # команды ядра по журналу
d2mod map check   --mod <каталог> --id <Id>                             # errors=N warnings=M, код 0/1
d2mod map import  --mod <каталог> --sg <файл.sg> [--id <Id>]
d2mod map play    --mod <каталог> --id <Id> --player <PlayerId> [--check]
```

## 5. Решения, принятые здесь

| Решение | Почему |
|---|---|
| источник истины — `Looks.Map`, списки вида производные | сейчас `MapLooks` и `MapState` строятся из `.sg` параллельно; при ручной правке две копии разойдутся |
| формат файла — тот же `d2-port-scenario` (без отдельного «исходника редактора») | один формат = игра читает то же, что пишет редактор; свои данные редактора — в `.studio/` |
| id по схеме `.sg` | экспорт в `.sg` остаётся возможным, ключи переводов устойчивы |
| история в ядре, не `UndoRedo` Godot | проверяется `dotnet test`, доступна CLI |
| запуск игры отдельным процессом | падение партии не теряет правки, у игры свой `GameData` с playtest-слоем |
