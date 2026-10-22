# Godot 4.7.2: чего нет в документации

Заметки про поведение движка, проверенные прогоном в этом репозитории 18.09.2026
(Godot 4.7.2 mono, Windows 11, GeForce RTX 4070 SUPER). У каждой сказано, чем проверена.
Справочник по API — `tools/godot-ref/` (`godot_ref.py Sprite2D.texture`).

Проверка велась временной сценой `godot/RefProbe.tscn` + `RefProbe.cs` (в git не
осталась, код заметок — её куски).

## Запуск сцены

```bash
dotnet build godot/LastEdict.Godot.csproj                                  # сначала C#
cmd //c "C:\Tools\bin\godot.cmd --headless --path godot"            # главная сцена, без окна
cmd //c "C:\Tools\bin\godot.cmd --path godot res://RefProbe.tscn -- --arg"   # окно + своя сцена
```

- Сцена задаётся позиционным аргументом (`res://…tscn`) и перебивает `run/main_scene`
  из `project.godot`.
- Всё после `--` уходит в `OS.GetCmdlineUserArgs()`; до `--` аргументы съедает движок.
- `godot.cmd` — шим над `Godot_v4.7.2-stable_mono_win64_console.exe`. Из Git Bash
  работают обе формы — и `cmd //c "…"` (так написано в README репозитория), и прямой
  вызов `"C:/Tools/bin/godot.cmd" --headless …`; код возврата проходит наружу в обоих
  (проверено: прямой вызов дал `EXIT=3` и полный лог в 2350 байт). Вывод лучше сразу
  слать в файл: он длинный и с трассами C#.
- Сборка C#: без `dotnet build` движок берёт старую сборку из `godot/.godot/mono/`,
  свежий класс сцены он не увидит. Проверено: правка `RefProbe.cs` без пересборки
  давала прежний вывод.

## Снимок экрана: headless не годится

`--headless` подставляет пустой (dummy) рендерер, кадрового буфера в нём нет:

```text
viewport tex: ok            # текстура вьюпорта есть
ERROR: Parameter "t" is null.  at: texture_2d_get (servers/rendering/dummy/storage/texture_storage.h:110)
shot: null                  # GetImage() вернул null
```

Проверено: `GetViewport().GetTexture().GetImage()` под `--headless` возвращает `null`,
и `--rendering-driver vulkan` вместе с `--display-driver headless` ничего не меняет
(`DisplayServer.GetName() == "headless"`, `RenderingServer.GetVideoAdapterName()` пуст).
Снимок снимается **только с окном**:

```csharp
private int _frames;

public override void _Process(double delta)
{
    if (++_frames < 3) return;                     // первый кадр ещё не нарисован
    Image shot = GetViewport().GetTexture().GetImage();
    shot.SavePng(ProjectSettings.GlobalizePath("user://") + "shot.png");
    GetTree().Quit(0);
}
```

Проверено с окном: `shot: 1152x648`, `savepng shot: Ok`, файл лёг в
`%APPDATA%\Godot\app_userdata\Disciples\shot.png`.

**Не ждать `RenderingServer.FramePostDraw`**: под `--headless` сигнал не приходит
никогда, `await ToSignal(...)` вешает процесс насмерть (проверено — прогон убит
по таймауту 120 с, дошёл только до строк перед ожиданием). Счётчик кадров в
`_Process` работает в обоих режимах.

## PNG с диска в рантайме

```csharp
var img = Image.LoadFromFile(path);      // статический, сразу Image
var img2 = new Image();
Error err = img2.Load(path);             // тот же результат, код ошибки отдельно
var tex = ImageTexture.CreateFromImage(img);
```

Проверено: оба пути дали `4x4 fmt=Rgba8`, `Load()` вернул `Ok`. Путь — обычный
файловый (`ProjectSettings.GlobalizePath("user://")`, `res://` тоже принимается).
`Image.Load` не проходит импорт ресурсов Godot — это и нужно для картинок,
которые появляются рядом с игрой, а не в проекте.

## Ключ прозрачности (пурпур 255,0,255)

Картинки игры используют пурпур как прозрачность. Ключ выбивается по пикселям
до создания текстуры:

```csharp
for (int y = 0; y < img.GetHeight(); y++)
for (int x = 0; x < img.GetWidth(); x++)
{
    Color c = img.GetPixel(x, y);
    if (c.R8 == 255 && c.G8 == 0 && c.B8 == 255) img.SetPixel(x, y, new Color(0, 0, 0, 0));
}
var sp = new Sprite2D { Texture = ImageTexture.CreateFromImage(img) };
sp.TextureFilter = CanvasItem.TextureFilterEnum.Nearest;   // обязательно
```

Проверено: после выбивания `a(0,0)=0`, `a(1,1)=1` (пурпур прозрачен, зелёный пиксель цел).

**`TextureFilter.Nearest` — не косметика, а условие численной сверки.** С фильтром
по умолчанию (линейным) увеличенный спрайт смешивает цвет с соседними выбитыми
пикселями, и чистый зелёный `(0,1,0)` читается со снимка как
`(0.035, 0.808, 0.035)`. С `Nearest` тот же пиксель читается ровно `(0, 1, 0)`
(оба числа сняты с настоящего снимка вьюпорта). Фон по умолчанию — `(0.298, 0.298, 0.298)`.

**Настройка проекта `default_texture_filter` действует только на корневой вьюпорт.** У `SubViewport`,
созданного в коде, `CanvasItemDefaultTextureFilter` = 1 (Linear), и спрайт без своего фильтра (`ParentNode`)
внутри него сглаживается. Поэтому корни видов во вьюпортах (`MapView`, `BattleView`) ставят себе
`TextureFilter = Nearest`, а дети наследуют. Проверено 11.10.2026: строка проверки экрана карты печатала
`фильтр=0 фильтр_вьюпорта=1`, после правки — `фильтр=1` (`tools/ui-render-check.py` держит это число).

## Масштаб окна и подписи (проверено 11.10.2026, прогон 3 пользователя)

- **Растяжение `viewport` корневого окна выводит кадр на экран ближайшим соседом.** Стенд: вьюпорт 1600×1200 с полосами в 2 точки
  через 6, окно 1280×720 (сжатие 0,6); снимок экрана (`DisplayServer.ScreenGetImageRect`) в строке — только 0 и 255, полосы то в одну,
  то в две точки. Фильтра или шейдера на этот шаг нет, поэтому сглаженное сжатие — свой `SubViewport` и `Sprite2D` с его текстурой и
  шейдером (`godot/scenes/ui/ScreenStage.cs`).
- **Снимок окна с окном** — `GetTree().Root.GetTexture().GetImage()`, когда у корня растяжение выключено
  (`ContentScaleMode = Disabled`): это ровно точки окна (так снимает `--stage-shot`). `DisplayServer.ScreenGetImageRect(rect)` снимает
  экран целиком — вместе с чужими окнами поверх.
- **У headless окно 64×64**, `--resolution` его не меняет: раскладку под настоящее окно сцена берёт из своего ключа (`--window-size`).
- **`Label` без оформления растёт по шрифту темы и обратно не уменьшается.** Подпись, созданная с `Size = (138, 18)` до `LabelSettings`,
  получает высоту 23 (наименьший размер по шрифту темы 16); после растрового шрифта 13 её наименьший размер — 1, а высота остаётся 23.
  Ставить оформление раньше текста и размер — после (`UiArt.Place`).
- **Вертикальное выравнивание `Label`**: блок = высоты строк + межстрочные между ними (без последнего), начало — `(высота − блок) / 2`
  с отбрасыванием дроби; текст выше места выравнивание не сбрасывает. Замер: шрифт 13, межстрочный 4, место 85 — одна строка с 36,
  две — с 27; место 18 — с 2. У движка игры правило другое (`LastEdict.Data.Ui.UiTextBlock`, `docs/ui-text.md`).
- **Ввод в `SubViewport` без контейнера** — `PushInput(событие, true)` в его координатах; `IsInputHandled()` после вызова говорит, взял
  ли его кто-то внутри. `GetMousePosition()` такого вьюпорта — точка последнего поданного события мыши.
- **До первого события мыши она (0, 0)** (проверено 21.10.2026, `tools/publish/screenshots.py`): окно в фокусе, мышь в стороне от
  окна — вид карты краем ехал вверх-влево, пока грузилась земля (`wheel up` в первом же кадре сдвинул вид на −32,−32 вместо 0,−16). Где
  нужна «мышь над окном» — сверять `DisplayServer.MouseGetPosition()` с прямоугольником окна (`MapView.MouseOverWindow`).

## Коды возврата для автоматических проверок

| Что сделано | Код возврата | Проверено |
| --- | --- | --- |
| `GetTree().Quit(7)` | `7` | прогон `--headless` с `--code=7` |
| `GetTree().Quit()` | `0` | прогон с `--code=0` |
| `OS.Crash("…")` | `0xC000001D` (`-1073741795`, illegal instruction) | прогон `--crash`, `$LASTEXITCODE` |
| необработанное исключение C# | **кода нет**: пишется `ERROR: System.InvalidOperationException…`, движок продолжает крутить цикл | прогон `--throw` завис и был убит по таймауту |

Отсюда правило для проверок без человека: **успех обязан быть явным** — печатать
строку-результат и звать `GetTree().Quit(код)`; запускать всегда под таймаутом
(`timeout 120 cmd //c "…"`), потому что упавший `_Ready`/`_Process` игру не останавливает.
`OS.Crash` годится только чтобы испытать обработчик падений: штатного кода он не даёт.

## `delta` в `_Process` урезана (проверено 15.10.2026, Godot 4.7.2)

`delta` не больше `max_physics_steps_per_frame / physics_ticks_per_second` (по умолчанию 8/60 ≈ 133 мс): прогон headless-скрипта,
`OS.DelayMsec(600)` и `OS.DelayMsec(1500)` в кадре — следующая `delta` 141 и 143 мс; с `max_physics_steps_per_frame=16` — 267 и 280 мс.
Отсюда: длину кадра мерить часами (`Time.GetTicksUsec()` между кадрами), не `delta` (зонд `slow_frame` с порогом 250 мс по `delta`
не срабатывал никогда, Г20 в `docs/perf-log.md`); анимации на `delta` после остановки главного потока не «догоняют» — время остановки
целиком ложится в длительность показа.

## Падения на выходе: финализатор C# против остановки языка (разобрано 14.10.2026)

Симптом: строка проверки напечатана, `Quit(0)` позван, а процесс кончается кодом `0xC000001D` (иногда `0xC0000005`, `0xC000041D`,
`0xC00000FD`). В выводе перед этим — сотни `ERROR: Leaked unsafe reference to object: <Image#…>` (`csharp_script.cpp:179`), затем
`FATAL: Condition "!rc_owner"` (`_instance_binding_reference_callback`) или `"csharp_lang && !csharp_lang->script_bindings.is_empty()"`
(`_instance_binding_free_callback`), стек `GodotObject.Finalize → Dispose(false) → godotsharp_internal_refcounted_disposed` /
`…object_get_associated_gchandle` на потоке `GC.RunFinalizers`. `CRASH_COND` Godot — ловушка `ud2`, отсюда `0xC000001D`; в журнале Windows
это «.NET Runtime 1023 internal error … 0xc000001d» или «1026 unhandled exception» с тем же стеком.

Причина: при выходе `DisposablesTracker.OnGodotShuttingDown` снимает только **живые** обёртки C#. Обёртка, которая уже мусор (слабая ссылка
сброшена сборкой), но ещё стоит в очереди финализатора, снимается потоком финализаторов — параллельно с `CSharpLanguage::finalize` или
после него, и движок падает. Чем больше брошенных обёрток `Image`/`InputEvent…` в последний миг, тем чаще (Г16 ускорила сборку земли —
сотни кусков `Image.CreateFromData` к выходу `ShotCompare` — и частота стала ~20 % при 16 процессах; до неё 0 из 240). Фоновый поток,
создающий `Image` во время остановки (очереди `GfxTextures.Warm`), — та же гонка.

Правило: временные `Image` и другие `RefCounted` из C# — `using`/`Dispose` сразу; при выходе (`GameData._ExitTree`, автозагрузка покидает
дерево последней) — дождаться фоновых разборов (`GfxTextures.Stop`) и прогнать `GC.Collect(); GC.WaitForPendingFinalizers()`, пока язык жив.
Замер: 72 из 300 → 0 из 600 (`ShotCompare`, 16 параллельно).
