# Формат данных порта и правила для инструментов

Общий договор для редактора карт и инструментов модов. Кто пишет данные порта — обязан ему следовать. Основа —
[`docs/principles.md`](../principles.md); здесь — практические следствия для инструментов.

## 1. Семь правил

| № | Правило | Как проверяется |
|---|---|---|
| П1 | **Данные отдельно от кода.** Инструмент пишет только файлы данных (`data/…` мода), никогда не код и не `assets/` базы | тест: после операции инструмента `git status` базы чист, изменены только файлы внутри каталога мода |
| П2 | **Старые форматы только на входе.** `.sg`, `.dbf`, `.dlg`, `.ff` читаются импортёрами (`tools/ScenarioImport`, `tools/RulesImport`, `tools/UiImport`, `tools/gfx-assets`); рантайм и инструменты работают с JSON/PNG/WebP порта | ядро `LastEdict.Authoring` не ссылается на `LastEdict.Data.Sg`/`LastEdict.Data.Dbf` для записи (кроме экспорта §6) |
| П3 | **Редактор не меняет правил игры.** Он ставит объекты и задаёт их поля в пределах, которые понимают `LastEdict.Rules` и `GameSession`; новой механики через редактор нет. Новая механика — код в `LastEdict.Rules` + данные, отдельной задачей с харнессом | проверка карты (§5) отказывает в значении, которого правила не знают (`UnsupportedCondition`/`UnsupportedEffect`, неизвестный `TypeId`) |
| П4 | **База только читается.** Правка базового содержимого = запись в слой мода (как плагин Creation Kit); `data/` базы инструмент не трогает | тест П1 |
| П5 | **Опознание по устойчивому id.** Наложение слоёв — по id (экран `Id`, кадр `archive`+`name`, сценарий `Id`, строка таблицы — ключ); id не переиспользуются и не меняются при сохранении | тест «загрузил → сохранил без правок → файл байт в байт» (§4) |
| П6 | **Всё проверяемо кодом.** Каждая операция инструмента есть в CLI и печатает числа; окно — обёртка | у каждой задачи вех — проверка без окна |
| П7 | **Поведение как в оригинале.** Карта, сделанная редактором, играется движком порта так же, как та же карта из `.sg` в оригинале; если карта экспортируется в `.sg`, она проходит проверку совместимости (§6) | харнесс: импорт `.sg` → сохранение редактором → загрузка → `GameSession` даёт то же начальное состояние, что прямой импорт |

## 2. Где что лежит

| Данные | Файл базы | Формат | Слой мода сейчас | Загрузчик |
|---|---|---|---|---|
| сценарий (карта + события) | `data/scenarios/<Id>.json` | `d2-port-scenario` v5 (`ScenarioFile.Format`/`Version`) | да: `<мод>/data/scenarios/<Id>.json`, поиск по слоям `GameContent.ScenarioPath` | `ScenarioFile.Read` → `ScenarioData(Id, Looks, Events)` |
| карточка сценария в меню | `data/game/scenarios.json` | список карточек | да, по `Id` (`GameCatalog.Merge`) | `GameContent.Catalog` |
| кампании | `data/game/campaigns.json` | главы со сценариями | да, по `Id` (`GameCatalog.Merge`) | `Campaign`, `CampaignStore` |
| расы и лорды (меню) | `data/game/races.json` | список | да, по типу расы | `GameCatalog` |
| перевод сценария | `data/scenarios/lang/<яз>/<Id>.json` | `d2-port-scenario-texts` v1, ключи `name`, `event:<id>:<n>`, `site:<id>:name`… | да, по ключу | `ScenarioTexts`, `tools/scenario-ru/README.md` |
| таблицы правил (юниты, предметы, заклинания, постройки, ИИ…) | `data/rules/<Имя>.json`, 31 таблица | `{table, columns[{name,type,length,decimals}], rows[]}` — колонки как в `.dbf` | **нет** (`GameBook.Load(RulesDir)`, `GameTables.Read(dir, name)` — один каталог) | `GameTables`, `RulesLoader`, `EconomyLoader`, `SpellLoader` |
| данные карты для отрисовки (слои, ориентиры, горы, бонусы хода, юниты карты) | `data/map/*.json` | свой | да (`MapArtData.Merge`) | `GameContent.MapArt` |
| графика мира и боя | `data/gfx/<Архив>.json` + `assets/gfx` | опись: `images`, `anims{name,dir,ext,w,h,frames}`; WebP/PNG | да, `archive`+`name` (`GfxAssets.Merge`) | `GfxAssets` |
| экраны интерфейса | `data/ui/screens.json` (132 экрана) | свой, якоря, кадры по состояниям | да, по `Id` целиком (`UiScreens.Merge`) | `GameContent.Screens` |
| кадры интерфейса | `data/ui/assets.json` + `assets/ui` | `{name, archive, file, w, h, sha256}` | да (`UiAssets.Merge`) | `UiAssets` |
| граф экранов, умолчания, ролики переходов, шрифты | `data/ui/flow.json`, `defaults.json`, `transitions.json`, `fonts.json` | свои | да | `UiFlow`, `UiDefaults`, `UiTransitions`, `UiFonts` |
| строки интерфейса и имена из таблиц (`X000TG2001`) | `data/ui/strings.{ru,en}.json` | словарь id → текст | да, по id; запасной язык `en` | `UiStrings.Merge` |
| звук | `data/sound/*.json` + `assets` | описи | **нет** (`SoundAssets`) | `SoundAssets`, `UnitSounds` |
| ролики | `data/video/videos.json` + `assets/video` | Ogg Theora | да, по имени | `UiVideos` |

Слои: `mods/` репозитория → `user://mods`; внутри корня — по имени каталога; побеждает последний
([`docs/modding.md`](../modding.md)). Пробелы «нет» в столбце «Слой мода» закрывает [`modding-kit/plan.md`](modding-kit/plan.md) K0–K1.

## 3. Сценарий `d2-port-scenario` v5 — что важно инструменту

Верх файла: `{Format, Version, Id, Looks, Events}` (`ScenarioFile.Envelope`).

- `Looks.Map` — **модель правил** (`MapState`, `src/Engine.Rules/Map/Model.cs`): `Size` (48…144), `Tiles` (сетка байтов
  base64: биты 0–2 — раса-владелец земли `Tile.Terrain`, биты 3–5 — грунт `Ground`: `Plain`/`Forest`/`Water`/`Mountain`),
  `Stacks`, `Units`, `Players`, `Objects`, `Plan` (клетка → id объектов), `Fog` (по игроку), `Sites`, `Relations`,
  `Limits` (`MaxUnit`, `MaxSpell`, `MaxLeader`, `MaxCity`, сложность, `SuggestedLevel`), `ItemTypes`, `Subraces`, `MapSeed`, …
- `Looks.TreeImage`/`TreeGroup` — картинка деревьев клетки (биты 19–31 слова клетки `.sg`).
- `Looks.Forts`/`Ruins`/`Sites`/`Crystals`/`Bags`/`Landmarks`/`Mountains`/`Roads`/`Stacks`/`Banners` — **вид для отрисовки**
  (`MapLooks`, `src/Engine.Play/Map/MapLooks.cs`). Они дублируют часть `Map`. **Для инструмента источник истины — `Map`;
  списки вида пересчитываются из него** одной функцией ядра (её ещё нет — задача E0.3 в [`map-editor/plan.md`](map-editor/plan.md)).
- `Events` — `ScenarioEventSet`: `Events[]` (условия `EventCondition` — 17 видов, действия `EventEffect` — 24 вида,
  `src/Engine.Rules/Map/ScenarioEventModel.cs`; неизвестные — `Unknown` с `Category`), `Zones` (точка + радиус), `Variables`,
  `Templates` (шаблоны отрядов для `CreateStack`), `SubraceOwners`.
- **id объектов** — схема `.sg`: `S<NNN><КК><XXXX>` (`S143KC0000`: `KC` — отряд, `UN` — юнит, `PL` — игрок, `FT` — крепость,
  `SR` — подраса, `LO` — зона, `TM` — шаблон, `EV` — событие, `BG` — мешок, `IM` — предмет, `FG` — туман, `PB` — постройки,
  …). Новые объекты получают id той же схемы (следующий свободный номер своего вида) — это держит экспорт в `.sg` возможным и
  не ломает ключи переводов (`event:<id>:<n>`). Ссылки на таблицы — id `G000…` (`G000UU3001` — тип юнита).
- Версия: `FromJson` отказывает файлу новее сборки. Любое изменение формата — `Version + 1`, чтение старых версий
  остаётся (как у сохранений `SaveJson` v1…v8).

## 4. Требования к записи

1. **Круговая проверка**: `Read → Write` без правок даёт тот же файл байт в байт (сериализатор `SaveJson.Options`,
   порядок ключей стабилен). Харнесс на всех 33 сценариях базы и 4 `port-synth`.
2. **Атомарная запись**: во временный файл рядом → `File.Replace`; резервная копия прошлой версии в
   `<мод>/.studio/backup/<Id>.<время>.json` (вне git мода, последних N=10).
3. Тексты сценария пишутся в сам сценарий **на языке оригинала карты** (поле `Name`, `Text`…), переводы — отдельными
   файлами `lang/<яз>` (как сейчас). Редактор, где автор пишет по-русски, задаёт язык карты в свойствах (вопрос — в
   [`map-editor/spec.md`](map-editor/spec.md) §9).
4. Никаких абсолютных путей в данных; пути — от каталога мода.

## 5. Проверка (общая для карт и модов)

Одно ядро проверок `LastEdict.Authoring.Validation`: список правил, каждое — id (`MAP012`, `MOD004`), уровень
(`error`/`warning`/`info`), объект, клетка или файл+путь JSON, текст по-русски. CLI печатает итог `errors=N warnings=M` и
возвращает код (0 — без ошибок). Окно показывает тот же список с переходом к объекту. Каталог правил — в
[`map-editor/spec.md`](map-editor/spec.md) §11 и [`modding-kit/spec.md`](modding-kit/spec.md) §9.

## 6. Экспорт в `.sg` (для оригинала) — когда и с какой проверкой

**Вопрос пользователю.** Варианты:

| Вариант | Плюсы | Минусы |
|---|---|---|
| А. Не делать: для оригинала — ScenEdit | ноль работы, нет риска битых `.sg` | карту из «Мастерской» нельзя сыграть в оригинале → нельзя снять эталонный журнал зонда |
| Б. **Экспорт с проверкой совместимости, поздняя веха (рекомендация)** | эталонные партии в оригинале для любой карты редактора (как сейчас у `port-synth` через `tools/synth-maps`) | писатель `.sg` на C#; нужно доказывать байт в байт |
| В. Экспорт с первой версии | — | задерживает редактор, писатель `.sg` сложен |

Рекомендация — Б (веха E6). Опора уже есть: `tools/synth-maps/sglayout.py` пишет `.sg`, раскладки тел совпадают с
`src/Import.Legacy.Formats/Sg/SagaLayouts.cs` «байт в байт на всех штатных картах». Проверка совместимости при экспорте отказывает, если в
карте есть то, чего нет в оригинале: юниты/предметы/графика мода, размер вне 48…144, больше игроков, чем умеет игра, события
`Unknown`, тексты длиннее полей `.sg`, отступления порта из `docs/HANDOFF.md`. Доказательство экспорта: `.sg → импорт →
экспорт → .sg` байт в байт на штатных картах + первый запуск в оригинале делает пользователь.
