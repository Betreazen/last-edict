# Основания, решения и открытые вопросы

Дата: 22.09.2026. Этот файл отличает факты текущего проекта от будущих решений.

## 1. Объём исследования

Прочитаны проектные документы, настройки сборок, ключевые загрузчики и модели.
Посчитаны файлы/байты ресурсов; проверены все ссылки world gfx из действующих описей.
Из media прочитаны manifests и выборочно ffprobe для одного ролика и одной озвучки.
Не выполнялись массовый pixel decode, поиск дубликатов всех кадров, сжатие, замеры
реальной RAM/VRAM игры, GPU profiling или тесты пропускной способности сервера.

Поэтому размеры файлов ниже измерены; ожидаемые gains trim/dedup/кодеков — гипотезы,
требующие P2. Указанные в HANDOFF проценты и число тестов — состояние документа,
а не результат нового тестового прогона.

## 2. Связь с текущим проектом

| Свидетельство | Что установлено | Следствие для будущей версии |
|---|---|---|
| [LastEdict.Godot.csproj](../../godot/LastEdict.Godot.csproj) | Godot.NET.Sdk 4.7.2, net10.0, ссылки на Data/Rules | C# binaries не становятся Rust/Wasm автоматически |
| [project.godot](../../godot/project.godot) | UiFlow — main scene, 1280×720 viewport | UI-поток и размеры — источник эталона, не готовая web-архитектура |
| [HANDOFF](../HANDOFF.md), [step6](../step6-game-loop.md) | Полный цикл ещё описан как работа впереди | Нельзя начинать миграцию до принятого G0 |
| [LastEdict.Rules.csproj](../../src/Engine.Rules/LastEdict.Rules.csproj) | Правила не зависят от Godot SDK | Подходящая граница чистого Rust core |
| [GameData](../../godot/autoload/GameData.cs) | Корни data/assets вычисляются вне res:// | Web требует явного manifest/asset provider |
| [MapSource](../../godot/scenes/map/MapSource.cs) | Карта читается из локального .sg | Будущий сценарий проходит offline-import |
| [GameCatalog](../../src/Engine.Play/Game/GameCatalog.cs) | Есть метаданные сценария/раса и merge | Переиспользуются смысл и ID, добавляются версии и зависимости |
| [MapObjects](../../godot/scenes/map/MapObjects.cs) | FrameMs=66, массивы отдельных текстур в словаре | Сохранять темп; исследовать atlas residency и cache budget |
| [BattleView](../../godot/scenes/battle/BattleView.cs) | FrameMs=66, загрузка кадров из отдельных PNG | Аналогично; это не измерение FPS оригинала |
| [BattleScene](../../src/Game.UiModel/Gfx/BattleScene.cs) | Холст 800×600, anchor (400,490), стороны не зеркалятся | Trim требует offsets и сохранения сторон/слоёв |
| [MapView](../../godot/scenes/map/MapView.cs), [GroundChunks](../../src/Game.UiModel/Gfx/GroundChunks.cs) | Чанки 256, лимит 96 в view, CPU background thread | Переиспользовать принцип, web worker требует нового адаптера |
| [EngineRng](../../src/Engine.Rules/Rng/EngineRng.cs) | R250, snapshot таблицы/индекса; Fit — подбор по корпусу | Полный RNG в saves; Fit вне runtime |
| [gfx builder](../../tools/gfx-assets/build_gfx_assets.py) | RGBA, magenta key, optimize=False; хеши сокращены | Есть пространство для lossless build-оптимизации |
| [gfx README](../../tools/gfx-assets/README.md) | На диске остаются исходные sheets, не включённые в runtime-opись | Не паковать каталог целиком |
| [video builder](../../tools/video-import/build_videos.py) | Bink → Theora, Vorbis; manifest описывает исходник | Новые кодеки строить из masters, проверять готовый результат |
| [modding](../modding.md) | Моды — слои данных по ID | Сохранить управляемые overlays без произвольного runtime-кода |

## 3. Инвентаризация на диске

Единицы: МБ/ГБ — десятичные, МиБ/ГиБ — двоичные. Полный assets = 2 688 373 811 байт
(2,688 ГБ, около 2,504 ГиБ). Это исходный рабочий набор, не web-download budget.

| Группа | Файлов | Байтов |
|---|---:|---:|
| gfx/BatUnits | 80 458 | 1 565 709 917 |
| gfx/IsoSpell | 11 759 | 277 289 557 |
| gfx/IsoUnit | 48 368 | 273 028 686 |
| весь gfx | 148 209 | 2 367 989 450 |
| ui | 2 341 | 109 361 294 |
| video | 110 | 211 023 067 |

World gfx составляет около **88,1%** всего assets, а не 93% из предварительной реплики
обсуждения. В gfx находятся 148 186 PNG (2 259 838 976 байт) и 23 JSON распаковки
(108 150 474 байта). Эти JSON отличаются от небольших runtime-описей в data/gfx.

У `BatUnits/anims`: 75 476 файлов, 1 167 637 470 байт;
у `BatUnits/plain`: 4 969 файлов, 326 310 124 байта;
у `BatUnits/frames`: 12 файлов, 701 738 байт; остальные байты — manifest распаковки.
Это подтверждает необходимость учитывать ссылки, а не отгружать весь каталог.

По `data/gfx/*.json`: 2 446 images + 134 210 frame paths = 136 656 уникальных файлов,
1 756 912 297 байт, missing=0. Разница с gfx на диске 611 077 153 байта не является
измеренной экономией полного web-выпуска: будущие атласы и codec variants дадут другой размер.
Все 136 656 файлов также не нужны одновременно и не обязаны входить в один сценарий.

Грубая сумма `w × h × 4 × frames` по анимационным описям — 188 545 800 992 байта.
Это **теоретический объём всех заявленных RGBA-кадров** без дедупликации, не память
запущенной игры. Размер в текущей анимационной описи берётся по первому кадру, поэтому
точный decoded inventory требует чтения заголовка каждого изображения.

## 4. Видео и аудио

`data/video/videos.json` содержит 110 записей: 52 MenuAnim, 50 Briefing и 8 Video.
На диске 102 .ogv и 8 .ogg. У записей с видео заявлены ~15 FPS (одна дробная 15,002);
размеры 640×360, 800×600, 197×347 и 32×32. Аудиозаписи имеют нулевые image-поля.

Проверка реальных outputs через `C:/ffmpeg/bin/ffprobe.exe`, только чтение:

| Файл | Подтверждено |
|---|---|
| assets/video/Video/CONCDW.ogv | Theora 640×360, 15/1 FPS; Vorbis 44,1 кГц stereo; 52,027 с; 3 719 797 байт |
| assets/video/Briefing/BRIEFEL1.ogg | Vorbis 44,1 кГц stereo; 107,572 с; 2 195 709 байт |

Выборка не доказывает корректность всех файлов. Импортная опись берёт длительности
исходников, а не выходного кодека; новый pipeline обязан проверять также output.
Полная коллекция музыки/SFX пока этой инвентаризацией не установлена.

## 5. Как повторить read-only аудит

PowerShell из корня: `Get-ChildItem assets -Recurse -File` и `Measure-Object Length -Sum`
по интересующим каталогам/расширениям. Для списка runtime gfx:

1. Прочитать `data/gfx/*.json` через `ConvertFrom-Json`.
2. Добавить images[].file и для каждой anim — dir + '/' + frame + '.PNG'.
3. Удалить повторные пути и проверить каждый под assets/gfx.
4. Суммировать реальные Length; отдельно вывести missing и orphan candidates.

Текущая проверка путей выполнялась на Windows с case-insensitive set; перед Linux/CDN
релизом нужен отдельный case-sensitive/path-separator/Unicode-normalization аудит.
Для media использовать ffprobe с `-show_entries stream=codec_name,codec_type,width,height,r_frame_rate,sample_rate,channels:format=duration,size`.
Инвентаризация не запускает импортёры: существующий gfx builder умеет очищать свой output,
поэтому он не является безопасной командой для простого аудита.

## 6. Реестр исходных решений

| ID | Решение | Почему / условие пересмотра |
|---|---|---|
| D01 | Rust только после G0, отдельный проект | Сохраняет законченный оригинальный порт и проверяемый эталон |
| D02 | Macroquad — кандидат до G1 | Подходит масштабу 2D; не принят без bridge/render tests |
| D03 | Один core для SP Wasm и MP server | Проверяемые одинаковые правила, не два независимых движка |
| D04 | SP локально; MP authoritative | Низкий SP server CPU и серверная проверка сетевых ходов |
| D05 | Main WebGL + worker simulation | Отзывчивый UI; worker не подразумевает shared-memory threads |
| D06 | Public assets отдельно от private MP state | Fog нельзя обеспечить только визуальным затемнением |
| D07 | PNG baseline, trim/dedup/atlas до сложных codecs | Измеримый путь без преждевременного собственного контейнера |
| D08 | Browser media через JS adapter | Проверенный browser decoding; не тащить video decoder в Wasm по умолчанию |
| D09 | Один app service + PostgreSQL + CDN | Понятная транзакция и эксплуатация; масштабирование по измерениям |
| D10 | JSON protocol/save v1 с версиями | Простая диагностика; binary только при показанной необходимости |
| D11 | До 60 FPS, native tempo clips, 30-mode | Плавность определяется камерой/вводом/паузами, не числом кадров PNG |
| D12 | Совместимый ruleset и последующие версии | Отличает дефект миграции от намеренного изменения игры |

Это решения для планирования, не свидетельство наличия указанных модулей в коде.

## 7. Вопросы, которые закрываются позже

| Вопрос | Предварительное допущение | Когда решить |
|---|---|---|
| Что означает завершённый Godot? | Полный цикл и принятый оригинальный вид | G0, по фактическому выпуску |
| Framework/bridge версии | Macroquad + matching JS bundle | P1; перепроверить документацию на ту дату |
| Browser minimum/mobile | Desktop browsers; mobile эксперимент | P1/G1 и отдельный продуктовый этап |
| Целевая одновременная аудитория | Число не задано | P7 до выбора hosting capacity |
| Политика disconnect/таймеров | Пауза/возврат, без скрытой замены на AI | P5/P6 перед пользовательским playtest |
| Скрыта ли сама география? | Для private generated MP возможно; фиксированный публичный сценарий известен | Формат сценария до P2 |
| Качество видео/lossy/HD | Original-size lossless sprites, исходный темп | P2 после сравнительных тестов |
| Список/лицензии публикуемых ресурсов | Source inventory включает происхождение и разрешённый release scope | Перед публикацией контента, не при прототипе |
| Авторские новые механики | Не определены этим планом | После совместимого Rust-выпуска |
| Сроки/состав команды | Не оценены | После P1/P2 и первого rule migration slice |

## 8. Внешние первичные источники

Проверены при составлении документа. Их возможности и версии нужно перепроверить
при начале будущей реализации; никаких сроков появления новых функций здесь не обещано.

- [Macroquad API](https://docs.rs/macroquad/latest/macroquad/) — HTML5 и batching.
- [Miniquad API](https://docs.rs/miniquad/latest/miniquad/) — нижний платформенный слой.
- [Macroquad Wasm/JS](https://macroquad.rs/articles/wasm/) — механизм JS bridge.
- [Axum](https://docs.rs/axum/latest/axum/) — HTTP/service интеграция.
- [Tokio spawn_blocking](https://docs.rs/tokio/latest/tokio/task/fn.spawn_blocking.html) — CPU-bound ограничения.
- [MDN Web Workers](https://developer.mozilla.org/en-US/docs/Web/API/Web_Workers_API/Using_web_workers).
- [MDN requestAnimationFrame](https://developer.mozilla.org/en-US/docs/Web/API/Window/requestAnimationFrame).
- [MDN WebGL best practices](https://developer.mozilla.org/en-US/docs/Web/API/WebGL_API/WebGL_best_practices).
- [MDN Storage quotas](https://developer.mozilla.org/en-US/docs/Web/API/Storage_API/Storage_quotas_and_eviction_criteria).
- [MDN HTTP caching](https://developer.mozilla.org/en-US/docs/Web/HTTP/Guides/Caching).
- [MDN video codecs](https://developer.mozilla.org/en-US/docs/Web/Media/Guides/Formats/Video_codecs).
- [MDN canPlayType](https://developer.mozilla.org/en-US/docs/Web/API/HTMLMediaElement/canPlayType).
- [MDN autoplay](https://developer.mozilla.org/en-US/docs/Web/Media/Guides/Autoplay).
- [FFmpeg](https://ffmpeg.org/ffmpeg.html) и [cwebp](https://developers.google.com/speed/webp/docs/cwebp).

Архитектура, budgets и этапы — собственное проектное предложение на основе этих
возможностей и изученного кода, а не готовая рекомендация авторов framework.
