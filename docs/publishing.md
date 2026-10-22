# Публикация: импорт данных игры, публичное дерево, сборка Windows

21.10.2026, ветка `r21/pub`. Решения пользователя — «Публикуем новым публичным репозиторием, чистым снимком одним коммитом»; данные
оригинальной игры не кладём ни в репозиторий, ни в сборку — порт берёт их из купленной копии (GOG), как OpenMW и fheroes2.
Приватный `Betreazen/disciples-godot` остаётся рабочим (в нём `data/**` по-прежнему в git — им пользуются харнессы и проверки).

## 1. Что в `data/**` наше, а что выведено из игры

| Путь | Чьё | Откуда |
|---|---|---|
| `data/rules/*.json` | игра | `Globals/*.dbf` (шаг `rules`, бывший `tools/RulesImport`) |
| `data/game/*.json`, `data/map/*.json` | игра | `Globals`, `Exports/*.sg`, `Campaign`, `Scens`, `ImgData` (шаг `game`, бывший `tools/GameImport`) |
| `data/scenarios/*.json` | игра | `Exports/*.sg`, главы саг `Scens/*.dbf` (шаг `scenarios`) |
| `data/scenarios/lang/ru/*.json` | русификация + наш перевод | слой Руссобит-М (`Exports`, `Scens/Sinfo`, `Tscen`) — шаг `texts`; поверх — дописанные переводчиком порта пропуски слоя и машинный перевод шести сценариев, которых в слое нет (`'Greenskins'` и др., `Source: машинный перевод`). В публичное дерево не идёт целиком: это перевод текстов игры |
| `data/ui/screens.json` | игра | `Interf/Interf.dlg` + `CustomLobby.dlg` (лобби D2ModdingToolset, GPL-3; его в GOG нет — берётся из `tools/d2probe/Examples`, в сборке — `data/import`) |
| `data/ui/strings.en.json`, `strings.ru.json` | игра / русификация | `Globals/Tglobal.dbf`, `Interf/TApp.DBF`; русские — поверх слой русификации |
| `data/ui/assets.json`, `data/gfx/*.json` | игра | описи кадров `Interf/*.ff`, `Imgs/*.ff` (шаги `ui`, `gfx`) |
| `data/ui/capital.json`, `leader-names.{en,ru}.json` | игра | `Imgs/capital.dat`, `Globals/Tleader.dbf` |
| `data/sound/*.json`, `data/video/videos.json` | игра | `Sounds/*.wdb`, `Battle.wdt`, `Music/`; ролики `Interf/MenuAnim.ff`, `Briefing`, `Video` |
| `data/ui/{flow,flow.manual,defaults,transitions,fonts}.json` | **наше** | граф экранов из журналов зонда, ручные дополнения, начальные значения, ролики смены экранов, набор шрифтов |
| `data/music/playlists.json` | **наше** | музыка по ситуациям |
| `data/fonts/EBGaramond-*.ttf` | **наше** (OFL) | шрифт по умолчанию |
| `mods/_examples/**` | **наше** | примеры модов |

Русификация Руссобит-М — чужие проприетарные файлы (`disciples-vanilla/l10n/russobit/README.md`): в публичном дереве ни её, ни
выведенного из неё (`strings.ru.json`, `leader-names.ru.json`, `scenarios/lang/ru`, кириллица шрифтов) нет. Её даёт игрок, указав
папку русской копии игры (`--l10n`): у неё та же раскладка (`Globals`, `Interf`, `Scens`, `Exports`).

## 2. Импорт (`src/Import.Legacy`, `GameImporter`)

Один импорт на C# для игры и разработчика — перенос сборщиков, которые раньше жили на Python (`tools/ui-assets`, `tools/gfx-assets`,
`tools/sound-assets`, `tools/video-import`, `import_fonts.py`, `import_capital.py`, `import_leader_names.py`) и консольных импортёров
(`tools/RulesImport`, `GameImport`, `ScenarioImport`, `UiImport`, `UiStringsImport`; они остаются для выборочных прогонов). Шаги:
`rules game scenarios screens strings texts capital leaders fonts sound ui gfx video`. Выход — как раскладка репозитория:
`<корень>/data/**` и `<корень>/assets/**` плюс отметка `<корень>/import.json` (формат, «целиком», откуда, шаги).

- Без движка: разбор MQDB (`FfArchive`), PNG (`PngCodec`: в архивах только RGB 8 бит и палитра 8 бит), сборка кадров из кусков листов,
  ключ прозрачности (RGB565 0xF81F и индекс палитры `transparent_index`), обрезка по занятой части, хеши описей, JSON байт в байт как у
  Python (`PyJson`, `PyCompat`). Кодек WebP — у движка (`IImageEncoder`, `godot/scenes/boot/GodotImageEncoder.cs`: lossless и exact).
- Ролики — внешний ffmpeg (`ffmpeg/` рядом с exe или `PATH`); нет его или кодировщик Theora негоден — шаг `video` пропускается, игра
  идёт без роликов (импорт всё равно «целиком»).
- Сверка с прежними сборщиками (21.10, `game-pristine` + `russobit`, полный импорт 265 с (16 потоков сборки на 32-ядерной машине; графика мира 167 с против ~9 мин у Python, ролики 85 с), 1,3 ГБ): все описи
  `data/**` — те же байты, что в git, кроме двух мест: (1) `data/ui/assets.json` — 11 кадров (`DLG_CHOOSE_LORD_*H`,
  `DLG_MAIN_MENU_BG0TRANS`, `DLG_R_C_SPELL_BLACKM_P`, `DRAGDROP_BIGFACE_*`) с другим хешем: опись в git собрана до правила
  `transparent_index` (11.10, пункт 2 прогона 2) и не пересобиралась — нынешний `build_ui_assets.py` даёт то же, что импорт (у этих
  кадров индекс 0 палитры чёрный, теперь он прозрачен, как блитит движок); (2) `scenarios/lang/ru` без нашего перевода (см. §1).
  Проверки: `ImportParityTests` (звук, шрифты, столица, имена, опись роликов — байт в байт; атласы шрифтов — по точкам).

**Игра у игрока.** Главная сцена — `res://scenes/boot/Boot.tscn`. Данные есть (`GameData.HasGameData`) — сразу меню. Нет — окно
«Укажите папку Disciples II (GOG)»: папка из `--game-dir`, сохранённой настройки `user://import.cfg`, реестра GOG
(`GOG.com\Games\1207663713` или любая «Disciples II», `GameDirFinder`) или обычных папок GOG; по желанию — папка русской копии;
импорт в фоне с ходом на экране, затем меню. Корень данных игры — `GameData.GameRoot`: `--data-dir`, `D2_DATA_DIR`, корень проекта,
если в нём уже есть импорт (репозиторий разработчика, переносная сборка), иначе `user://game` (`%APPDATA%\Godot\app_userdata\Disciples\game`).
Наш контент (`data/ui` описания, `data/fonts`, `data/music`, моды) читается от корня проекта (`GameData.Root`), выведенное из игры — от
`GameRoot` (`GameContent.Load(dataDir, mods, gameDataDir)`). Язык по умолчанию — `ru`, если импортированы русские строки, иначе `en`.

**Разработчик.** `py tools/import_game.py [--game …] [--l10n …] [--out …] [--steps …]` (Godot без окна:
`godot --headless --path godot -- --import …`); по умолчанию выход — сам репозиторий, копия — `D2_GAME_DIR`, слой — соседний
`disciples-vanilla/l10n/russobit`. В приватном репозитории импорт поверх закоммиченных данных даёт тот же `git diff` (пусто, кроме
11 кадров выше), уже лежащий перевод `scenarios/lang/ru` сохраняется.

## 3. Публичное дерево (`tools/publish/make_public.py`, решения — `tools/publish/public.json`)

`py tools/publish/make_public.py --out <каталог> [--dry-run] [--gitleaks <путь>]` — из `HEAD` (`git archive`): исключения по
решениям, вычистка (почта → `noreply`, домашний каталог в путях → `%USERPROFILE%`/`~`), `.gitignore` для импортированных данных,
`git init` и один коммит с автором `Betreazen <128125070+Betreazen@users.noreply.github.com>`; ничего не пушит. Затем скан секретов:
gitleaks, если есть, иначе встроенные правила (ключи AWS/Google, токены GitHub/Slack/OpenAI/Anthropic, приватные ключи, JWT,
присвоения паролей, почта вне разрешённой). Находки — код 2.

Исключается: данные игры (§1), синтетические карты и их инструменты (`tools/synth-maps`, `mods/port-synth`,
`docs/synthetic-maps-port`), сохранения и сценарии оригинала (`*.sg`, `*.csg`, `*.ldr` вне `tools/d2probe`); по ревью безопасности
21.10 — эталоны-выписки таблиц игры (`tests/unit/Engine.Play.Tests/golden/Gunits.json` — вся `Gunits.dbf`, `Tglobal.ru.head.json` — строки
Руссобит-М; их тесты пропускаются без файла), словарь перевода `tools/scenario-ru/glossary.ru.json` (собран из таблиц игры и
Руссобит-М), сам файл решений `tools/publish/public.json` (в правилах вычистки — почта и логин) и `.mcp.json` (пути машины).
Подмодули зонда (`tools/d2probe/{Detours,fmt,GSL,sol2,spdlog,SLikeNet,D2RSG}`) идут ссылками, как в исходном дереве. Остаётся всё остальное,
в том числе `docs/`, зонд `tools/d2probe`, манифесты корпуса, эталоны тестов и снимки `docs/screenshots` (вид порта с графикой игры).
Тесты на чистом снимке: smoke (`--filter "Category!=Corpus&Category!=Synth"`) проходят или пропускаются с причиной «нет данных игры
data/…: импортируйте их из копии Disciples II» (`tests/unit/Engine.Play.Tests/ImportedData.cs`); `Corpus` и `Synth` без корпуса и синтетических
карт не имеют смысла (корпус журналов в git не входит). Драйвер снимков `tools/publish/screenshots.py` — для разработчика с импортом.

## 4. Сборка Windows (`tools/publish/build_windows.ps1`)

Пресет `godot/export_presets.cfg` «Windows Desktop» (x86_64, .NET, без вшитого pck). Для экспорта нужны **mono-шаблоны** 4.7.2
(`%APPDATA%\Godot\export_templates\4.7.2.stable.mono`; обычные `4.7.2.stable` C# не запускают) или свой каталог `-Templates` с
`windows_{release,debug}_x86_64*.exe` из `Godot_v4.7.2-stable_mono_export_templates.tpz`. Экспорту .NET нужен `godot/LastEdict.Godot.sln`
(Godot ищет решение по имени сборки). Рядом с exe — наш контент (§1), лицензии; скрипт проверяет, что файлов игры в сборке нет, и
кладёт `dist/disciples-godot-win64.zip`. `-Check -DataDir <импорт>` — запуск exe без окна до главного меню (`--input "wait 1"`);
с `-ImportFrom <папка игры>` и пустым `-DataDir` — первый запуск с импортом.
