# Разработка: инструменты, корпус, сцены Godot

> Для разработчика. Описание проекта для всех — [README.md](../README.md); публичное дерево и импорт данных игры — [publishing.md](publishing.md). Команды ниже — из корня репозитория. Вместо соседнего `../disciples-modding/game-pristine` подойдёт любая копия игры GOG (`D2_GAME_DIR`), а данные порта из неё собирает `py tools/import_game.py`.

**Главное правило проекта — [docs/principles.md](principles.md):** поведение как в оригинале, устройство по
современным игровым стандартам (моддинг, развитие, графика); старые форматы игры только импортируются.

Перенос Disciples 2 в Godot 4: ядро игровых правил на C# (.NET 10), рендер и UI — на Godot.

Раскладка godot-части, настройки проекта и порядок проверок — [docs/godot-architecture.md](godot-architecture.md);
формат модов — [docs/modding.md](modding.md).

## Лицензия

GPL-3.0 (см. `LICENSE`).

## Игровые данные

Проект не содержит ресурсов игры. Пути к оригинальной копии берутся из переменной
окружения `D2_GAME_DIR`; если она не задана, используется путь по умолчанию —
`../disciples-modding/game-pristine` (репозиторий `disciples-godot` и
`disciples-modding` должны лежать рядом). См. `src/Engine.Play/GamePaths.cs`.

## Тесты

```bash
dotnet test
```

## Инструменты

Команды — из корня репозитория, в Git Bash; пути с пробелами — в кавычках.
Предпосылка: рядом с этим репозиторием лежит `../disciples-modding` с игрой в
`game-pristine/` (или переменная окружения `D2_GAME_DIR` указывает на свою копию).

Экспорт кадров юнита в `assets/` — обязателен перед запуском Godot,
`assets/` в git не хранится:

```bash
python tools/sprites_export.py
```

Регенерация `src/Import.Legacy.Formats/Sg/Sg.g.cs` из `tools/ksy/sg.ksy` — работает только
при `cwd = tools/ksy`; в git уже закоммичен готовый `Sg.g.cs`, регенерация
нужна лишь после правки `.ksy`:

```bash
cd tools/ksy && npm ci && node compile.js
```

Золотые файлы для тестов (уже закоммичены, перегенерировать — только если
источник данных изменился):

```bash
python tools/dbf_dump.py <file.dbf> [--head N]
python tools/sg_golden.py
```

## Справочник по Godot

Локальная база API движка (`tools/godot-ref/godot47.jsonl`, собрана из самого Godot) —
чтобы не гадать имена и сигнатуры и не лезть в интернет:

```bash
python tools/godot-ref/godot_ref.py Sprite2D
python tools/godot-ref/godot_ref.py --search "viewport texture"
python tools/godot-ref/godot_ref.py --methods Node --grep child
```

Пересборка после обновления движка и правило имён C# против GDScript —
`tools/godot-ref/README.md`. Проверенные заметки о поведении движка (headless,
снимки экрана, ключ прозрачности, коды возврата) — `docs/godot-notes.md`.

## Корпус

Снятие боевых журналов зонда (`tools/d2probe/`) в `corpus/` — сырые данные для тестов C#
на реальном бое, не только на синтетике. Предпосылка: `../disciples-modding/game-dev`
собран и зонд оттуда установлен (см. `tools/d2probe/README.md`, «Установка и снятие
журнала»); прогон использует `../disciples-modding/tools/gamedrv/` (`goto_battle.py` +
`autoplay.py`) — сам этот прогон не запускает игру, если её не собираются занимать.

Один раз перед первым прогоном — поставить зонд с эталонными таблицами, **без** маркера
автобоя. Бой доигрывает сама игра: `corpus_run.py` нажимает «Быстрый поединок» и Enter на
свитке (`gamedrv.goto_battle.press_fast_battle`). Маркер `probe/autobattle` (`--autobattle`)
искажает очередь ходов — сторона человека не ходит (06.09, `docs/corpus-schema.md`), такие
журналы годны только для формул. Без `--vanilla-globals` игра с модовыми таблицами `game-dev`
закрывается на переходе «ролик → главное меню» (бисекция 05.09), да и формулы должны
считаться по ванильным данным:

```bash
python tools/d2probe/install.py --vanilla-globals
```

Там же, один раз: в `Disciple.ini` игры нужен `Intro=1`. При `Intro=0` движок после
пропуска логотипов закрывает игру вместо главного меню; `goto_battle.py` гасит ролики
сам. Файл правится **побайтово** — перезапись целиком ломает игру:

```bash
python ../disciples-modding/tools/gamedrv/ini.py set Disciple Intro 1
```

Снять корпус (игру во время прогона не занимать — иначе цикл отрисовки не идёт и журнал
не появится):

```bash
python tools/corpus_run.py --count 5           # первые 5 боёв, corpus/<сегодня>/
python tools/corpus_run.py --count 50 --resume # добрать до 50+, не теряя уже снятое
```

Журналы — `corpus/<yyyymmdd>/battle-NNN.jsonl`, манифест на прогон — рядом
`corpus/<yyyymmdd>/manifest.json`; сами эти каталоги вне git (`corpus/.gitignore`), в git —
только `corpus/manifest.json` (сводка по всем каталогам, пересобирается в конце каждого
`corpus_run.py`) и `corpus/sample/one_battle.jsonl` (образец из настоящего боя, для
тестов C# без установленной игры). Проверка одного журнала — две ступени, подробности в
`docs/corpus-schema.md`:

```bash
python tools/d2probe/tests/probe_json_check.py corpus/<yyyymmdd>/battle-001.jsonl
python tools/d2probe/tests/probe_invariants.py corpus/<yyyymmdd>/battle-001.jsonl
```

`tools/corpus_run.py --dry-run --from-file <журнал> --out <каталог>` (в каталог с `manifest.json` — только с `--resume`) проверяет перенос +
манифест + счётчики без игры вообще — так же испытан на `corpus/sample/one_battle.jsonl`.

Откат зонда (вернуть тулсет 0.14.1):

```bash
python tools/d2probe/install.py --restore
```

## Godot

```bash
dotnet build godot/LastEdict.Godot.csproj
```

Сцены лежат в `godot/scenes/<раздел>/` и запускаются явным путём; главная сцена
проекта — `res://scenes/boot/Boot.tscn` (проверка импорта данных игры, затем `res://scenes/ui/UiFlow.tscn`). Стенд `AnimTest` без окна печатает
`frames: N` и завершается кодом 0/1:

```bash
cmd //c "C:\Tools\bin\godot.cmd --headless --path godot res://scenes/dev/AnimTest.tscn"
```

С окном — без `--headless`, с аргументом `-- --show`:

```bash
cmd //c "C:\Tools\bin\godot.cmd --path godot res://scenes/dev/AnimTest.tscn -- --show"
```

Проигрывание записанного боя (`turns: N`):

```bash
cmd //c "C:\Tools\bin\godot.cmd --headless --path godot res://scenes/battle/BattleReplay.tscn"
```

### Экраны интерфейса

Сцена `UiScreen` рисует экран кадрами из `assets/ui` (PNG в git не лежат, их
собирает `python tools/ui-assets/build_ui_assets.py`). Сами экраны и опись кадров
даёт автозагрузка `GameData`: базовый набор из `data/ui` плюс наложенные моды из
`mods/` и `user://mods` (формат — [docs/modding.md](modding.md)). `--no-mods`
оставляет только базовый набор, `--mods <каталог>` берёт один свой корень;
`tools/ui-render-check.py` гоняется с `--no-mods`.
Кадры игры хранят прозрачность маджентой (255,0,255) — сцена вырезает её при
загрузке; испечь альфу прямо в PNG было бы дешевле, но это перегенерация всех
1940 кадров, и пока не сделано.

Без окна экран складывается на стороне процессора в PNG (в headless у Godot
пустой драйвер отрисовки, снимок вьюпорта там не взять) и печатает одну строку:

```bash
cmd //c "C:\Tools\bin\godot.cmd --headless --path godot res://scenes/ui/UiScreen.tscn -- --screen DLG_MAIN_MENU --png ../.cache/ui-check/x.png"
# screen=DLG_MAIN_MENU drawn=10 skipped=0 magenta=0.000000 opaque=1.0000 lang=ru
#   textids=8 strings=8 nostring=0 captions=8 missing=- unknown=-
```

`skipped` — ссылки на кадры, которых нет в описи или на диске; `magenta` —
доля непрорезанной мадженты (обязана быть 0); `opaque` — доля непрозрачных
точек, по ней видно пустую картинку. Код выхода 1, если есть пропуски или
маджента.

Дальше в строке — про текст: `textids` — сколько элементов ссылается на строку
локализации, `strings` — скольким она нашлась, `nostring` и `missing` — каким
не нашлась, `captions` — сколько надписей нарисовано, `unknown` — какие
директивы оформления остались непонятыми. Строки лежат в
`data/ui/strings.<язык>.json` (выбор — `--lang`, по умолчанию `ru`), шрифты —
в `data/ui/fonts.json`; откуда всё это взялось и почему выбран EB Garamond —
[docs/ui-text.md](ui-text.md).

Семь экранов разом плюс проверка кириллицы (и есть проверка, которую можно
гонять):

```bash
python tools/ui-render-check.py
```

С окном — те же аргументы плюс `--show` (кадры узлами `Sprite2D`, надписи —
`Label` с разобранным оформлением: выравнивание, шрифт, цвет):

```bash
cmd //c "C:\Tools\bin\godot.cmd --path godot res://scenes/ui/UiScreen.tscn -- --show --screen DLG_OPTIONS"
```

Экран тянется к окну целиком: равномерный масштаб по меньшей стороне плюс
центрирование. Якоря (`UiAnchor`) пока не задействованы — как их включить,
описано у `UiScreenView.Rescale`.

`godot` в командах выше — шим на
`Godot_v4.7.2-stable_mono_win64_console.exe` (см. `C:\Tools\bin\godot.cmd`).
