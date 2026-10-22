using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using LastEdict.Data.Gfx;
using Godot;

namespace LastEdict.Godot;

/// <summary>Общий кэш текстур картинок графики (файл <c>.webp</c> сборки или <c>.png</c>/<c>.webp</c> мода по уже разрешённому пути — слои модов и HD-замена решены до него) для
/// объектов карты (<see cref="MapObjects"/>) и поля боя (<see cref="BattleView"/>): повторное открытие карты и каждый следующий бой
/// партии не разбирают те же PNG заново. Разбор PNG (<see cref="Image.LoadFromFile"/>) — в рабочих потоках, у каждого потока своя
/// <see cref="Image"/>; <see cref="ImageTexture"/> создаётся только в главном потоке.
///
/// <para>Источники: скилл gdscript-knowledge, performance.md «Память, ресурсы и загрузка» (кэш по пути, держать ресурс, а не грузить
/// заново) и «Сборка большого поддерева… в потоке»; signals-async-threads.md «Потокобезопасные API» (уникальный ресурс на поток, в
/// главный поток — готовые данные). Разбор PNG в потоках уже идёт у земли (<see cref="GroundArt"/>).</para>
///
/// <para>В видеопамять уходит только занятая часть кадра (Г7, <see cref="Trim"/>). С Г19 её вырезает сборка: файл — уже занятая часть, угол
/// в холсте — в описи (<see cref="GfxAssets.PlaceOf"/>), и здесь не разбирается весь холст и не ищется занятая часть. Кто смотрит точки
/// файла в координатах холста, берёт <see cref="Canvas"/>.</para>
///
/// <para>HD (Р19, <c>scale</c> в описи, <see cref="GfxAssets.ScaleOf"/>): файл крупнее холста в <c>scale</c> раз, холст и угол в описи —
/// логические. Текстура получает логический размер (<c>SetSizeOverride</c>), поля обрезки — в логических точках: спрайты карты и боя
/// стоят там же, что и без HD, а точки берутся из полной картинки. Кто смотрит точки картинки (маски, попадания, мини-карта, курсоры),
/// получает холст логического размера (<see cref="Canvas"/>); HD-холст для окон — <see cref="CanvasHd"/>.</para>
///
/// <para>Освобождение: <see cref="Release"/> при выходе из партии в меню (текстуры отпускаются сразу, а не сборкой мусора); сверх
/// <see cref="LimitBytes"/> кэш забывает давно не нужные текстуры (LRU, Э5 — для HD: прежде он сбрасывался целиком, и следующий кадр
/// боя разбирал заново всё) без <c>Dispose</c> — кто ещё держит текстуру (кадры живого спрайта), держит её дальше. Все вызовы — из
/// главного потока.</para></summary>
public static class GfxTextures
{
    /// <summary>Предел кэша по байтам разобранных картинок; дальше кэш начинается заново.</summary>
    public const long LimitBytes = 1L << 30;

    private static readonly Dictionary<string, LinkedListNode<Cached>> _cache = new(StringComparer.Ordinal);

    /// <summary>Порядок давности: в начале — последняя нужная текстура, с конца забываются.</summary>
    private static readonly LinkedList<Cached> _order = new();
    private static long _bytes;

    private sealed record Cached(string Path, Texture2D Texture, long Bytes);

    /// <summary>Текстур, забытых по давности (LRU) с начала партии, — для проверок и замеров.</summary>
    public static int Evicted { get; private set; }

    /// <summary>Текстур в кэше — для проверок и замеров.</summary>
    public static int Count => _cache.Count;

    /// <summary>Байт разобранных картинок в кэше — для проверок и замеров.</summary>
    public static long Bytes => _bytes;

    /// <summary>Текстуры файлов по порядку; файла нет — пропуск (как прежде: <c>File.Exists</c>, затем
    /// <c>ImageTexture.CreateFromImage(Image.LoadFromFile(path))</c>).</summary>
    public static Texture2D[] Of(IEnumerable<string> paths)
    {
        var result = new List<Texture2D>();
        foreach (string path in paths)
            if (Of(path) is { } texture) result.Add(texture);
        return [.. result];
    }

    /// <summary>Текстура одного файла: из кэша, из разбора в фоне (<see cref="Warm(IEnumerable{string})"/>; ещё идёт — подождать) или разбором здесь же;
    /// файла нет — null.</summary>
    public static Texture2D? Of(string path)
    {
        if (_cache.TryGetValue(path, out var cached))
        {
            _order.Remove(cached);
            _order.AddFirst(cached);
            return cached.Value.Texture;
        }
        if (Warmed(path) is { } image) return Add(path, image);
        return File.Exists(path) ? Add(path, Load(path)) : null;
    }

    /// <summary>Есть ли файлы (уже в кэше, уже разобраны в фоне или на диске) — та же проверка, что у <see cref="Of(string)"/>; обращения к
    /// диску — в рабочих потоках (при открытии карты их тысячи).</summary>
    public static bool[] Exist(IReadOnlyList<string> paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        var result = new bool[paths.Count];
        var disk = new List<int>();
        for (int i = 0; i < paths.Count; i++)
            if (!(result[i] = _cache.ContainsKey(paths[i]) || Ready(paths[i]))) disk.Add(i);
        Parallel.For(0, disk.Count, k => result[disk[k]] = File.Exists(paths[disk[k]]));
        return result;
    }

    /// <summary>Разборы PNG, начатые заранее в фоне (<see cref="Warm(IEnumerable{string})"/>), по пути. Словарь трогает только главный поток.</summary>
    private static readonly Dictionary<string, Task<Loaded?>> _pending = new(StringComparer.Ordinal);

    /// <summary>Начать в фоне разбор PNG, которые скоро понадобятся (замах следующего ходящего, удар по целям игрока — поле боя,
    /// замечание 35): к мигу показа их текстура создаётся из готовой картинки без разбора в главном потоке. Уже в кэше или уже в разборе —
    /// пропуск. Сбой разбора в потоке — файл потом грузится обычным путём.</summary>
    public static void Warm(IEnumerable<string> paths)
    {
        foreach (string path in paths)
        {
            if (_cache.ContainsKey(path) || _pending.ContainsKey(path)) continue;
            _pending[path] = Track(Task.Run(() => LoadOrNull(path)));
        }
    }

    /// <summary>Начать в фоне разбор файлов строго по порядку списка, не больше чем в <paramref name="threads"/> потоках: длинная очередь
    /// (кадры анимаций карты, Г17) не забирает все ядра у главного потока, а нужные раньше разбираются раньше. Уже в кэше или уже в разборе —
    /// пропуск; <see cref="Release"/> останавливает очередь (неразобранные — null, их потом грузит <see cref="Of(string)"/>).</summary>
    public static void Warm(IEnumerable<string> paths, int threads)
    {
        var queue = new System.Collections.Concurrent.ConcurrentQueue<Queued>();
        foreach (string path in paths)
        {
            if (_cache.ContainsKey(path) || _pending.ContainsKey(path)) continue;
            var item = new Queued(path);
            _pending[path] = item.Done.Task;
            _queued[path] = item;
            queue.Enqueue(item);
        }
        int generation = _generation;
        for (int t = Math.Min(Math.Max(threads, 1), queue.Count); t > 0; t--)
            Track(Task.Run(() =>
            {
                while (queue.TryDequeue(out var item))
                    if (item.Take())   // главный поток не забрал файл себе (Warmed)
                        item.Done.SetResult(generation == Volatile.Read(ref _generation) ? LoadOrNull(item.Path) : null);
            }));
    }

    /// <summary>Рабочие потоки разборов <see cref="Warm(IEnumerable{string})"/> и <see cref="Warm(IEnumerable{string}, int)"/>, ещё
    /// не кончившиеся; трогает только главный поток. Их ждёт <see cref="Stop"/>.</summary>
    private static readonly List<Task> _workers = [];

    private static T Track<T>(T task) where T : Task
    {
        _workers.RemoveAll(w => w.IsCompleted);
        _workers.Add(task);
        return task;
    }

    /// <summary>Выход из процесса (<see cref="GameData"/>, <c>_ExitTree</c>): остановить очереди разбора, дождаться начатых разборов и
    /// отпустить картинки, которые никто не забрал. Рабочий поток, создающий <see cref="Image"/> во время остановки движка, и обёртка C#,
    /// которую сборщик мусора доберёт после остановки языка C#, роняют процесс на выходе (FATAL в <c>csharp_script.cpp</c>, 0xC000001D,
    /// docs/godot-notes.md). Не дождались за <paramref name="timeout"/> — false.</summary>
    public static bool Stop(TimeSpan timeout)
    {
        Interlocked.Increment(ref _generation);   // очереди дальше не разбирают: остаток — null без разбора
        bool done;
        try
        {
            done = Task.WaitAll([.. _workers], timeout);
        }
        catch (AggregateException)
        {
            done = true;   // LoadOrNull не бросает; сбой самой задачи — поток всё равно кончился
        }
        _workers.Clear();
        foreach (var task in _pending.Values)
            if (task.IsCompletedSuccessfully) task.Result?.Image.Dispose();
        _pending.Clear();
        _queued.Clear();
        return done;
    }

    /// <summary>Файл в очереди <see cref="Warm(IEnumerable{string}, int)"/>. Разбирает тот, кто первым его взял (<see cref="Take"/>): фоновый
    /// поток или главный, которому файл понадобился раньше очереди, — разбор не повторяется и не теряется.</summary>
    private sealed class Queued(string path)
    {
        private int _taken;

        public string Path { get; } = path;

        public TaskCompletionSource<Loaded?> Done { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        /// <summary>Взять файл себе; уже взят — false.</summary>
        public bool Take() => Interlocked.CompareExchange(ref _taken, 1, 0) == 0;
    }

    /// <summary>Файлы очередей <see cref="Warm(IEnumerable{string}, int)"/>, ещё не взятые в кэш, по пути; трогает только главный поток.</summary>
    private static readonly Dictionary<string, Queued> _queued = new(StringComparer.Ordinal);

    /// <summary>Номер партии кэша: <see cref="Release"/> его меняет, и очереди <see cref="Warm(IEnumerable{string}, int)"/> прежней
    /// партии дальше не разбирают.</summary>
    private static int _generation;

    /// <summary>Разбор файла в рабочем потоке; файла нет или сбой — null (разбор повторится в главном потоке, <see cref="Of(string)"/>,
    /// и бросит там — поведение как без потоков).</summary>
    private static Loaded? LoadOrNull(string path)
    {
        try
        {
            return File.Exists(path) ? Load(path) : null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>Файл уже разобран в фоне (значит, он есть).</summary>
    private static bool Ready(string path) =>
        _pending.TryGetValue(path, out var task) && task.IsCompletedSuccessfully && task.Result is not null;

    /// <summary>Разобранная заранее картинка пути: готова — она; разбор идёт — подождать его (один файл); не начинали или сбой — null.
    /// Файл очереди <see cref="Warm(IEnumerable{string}, int)"/>, до которого фон ещё не дошёл, главный поток забирает себе и отвечает null —
    /// файл разбирается обычным путём (<see cref="Of(string)"/> здесь же, <see cref="Prefetch"/> — вместе с остальными недостающими), а не
    /// ждёт сотни файлов очереди перед ним (замер Г17: до ~120 мс).</summary>
    private static Loaded? Warmed(string path)
    {
        if (!_pending.Remove(path, out var task)) return null;
        if (_queued.Remove(path, out var item) && !task.IsCompleted && item.Take())
        {
            item.Done.SetResult(null);   // ждущих нет: из _pending задача уже снята
            return null;
        }
        return task.Result;
    }

    /// <summary>Разборов в фоне, ещё не взятых в кэш, — для проверок.</summary>
    public static int Pending => _pending.Count;

    /// <summary>Разобрать в рабочих потоках файлы, которых ещё нет в кэше, и положить их текстуры в кэш (главный поток). Сбой разбора в
    /// потоке не теряется: такой файл потом грузится обычным путём в <see cref="Of(string)"/> и падает там, как прежде.</summary>
    public static void Prefetch(IEnumerable<string> paths)
    {
        var wanted = paths.Where(p => !_cache.ContainsKey(p)).Distinct(StringComparer.Ordinal).ToArray();
        foreach (string path in wanted)
            if (Warmed(path) is { } image) Add(path, image);
        var missing = wanted.Where(p => !_cache.ContainsKey(p)).ToArray();
        if (missing.Length == 0) return;
        var images = new Loaded?[missing.Length];
        var ok = new bool[missing.Length];
        Parallel.For(0, missing.Length, i =>
        {
            try
            {
                if (!File.Exists(missing[i])) return;
                images[i] = Load(missing[i]);
                ok[i] = true;
            }
            catch (Exception)
            {
                // Разбор повторится в главном потоке (Of) и бросит там — поведение как без потоков.
            }
        });
        for (int i = 0; i < missing.Length; i++)
            if (ok[i] && !_cache.ContainsKey(missing[i])) Add(missing[i], images[i]);
    }

    /// <summary>Выход из партии: отпустить все текстуры сразу. Вызывать, когда узлы, державшие кадры кэша (<see cref="MapObjects"/>,
    /// <see cref="BattleView"/>), уже сняты: снятая обёртка C# у живого потребителя — <c>ObjectDisposedException</c>.</summary>
    public static void Release()
    {
        Interlocked.Increment(ref _generation);   // очереди Warm(…, threads) дальше не разбирают
        _queued.Clear();
        _pending.Clear();   // начатые разборы доделает пул потоков, картинки заберёт сборщик мусора
        foreach (var cached in _order) cached.Texture.Dispose();
        _cache.Clear();
        _order.Clear();
        _bytes = 0;
        Evicted = 0;
    }

    /// <summary>Разобранная картинка, обрезанная по непрозрачным точкам: сама обрезка, полный размер холста и угол обрезки в нём — в
    /// логических точках; <see cref="Scale"/> — множитель файла (HD).</summary>
    private sealed record Loaded(Image Image, Vector2I Full, Vector2I At, int Scale = 1)
    {
        /// <summary>Логический размер обрезки.</summary>
        public Vector2I Part => Scale <= 1 ? Image.GetSize() : Image.GetSize() / Scale;
    }

    /// <summary>Разобрать файл и разложить по холсту: сборка уже записала только занятую часть и угол её в холсте (<see cref="GfxAssets.PlaceOf"/>,
    /// Г19) — без поиска занятой части (<see cref="Placed"/>); угла нет (мод, прежняя сборка) — обрезка здесь (<see cref="Trim"/>).</summary>
    private static Loaded? Load(string path)
    {
        int scale = GfxAssets.ScaleOf(path);
        // HD: сжатая копия (DDS BC7 с mip-уровнями рядом, godot/bench/hd_compress.gd) — в текстуру прямо, без разбора PNG.
        if (scale > 1)
            return Scaled((ImageScales.Dds(path) is { } dds ? ImageScales.LoadDds(dds) : null) ?? Image.LoadFromFile(path), GfxAssets.PlaceOf(path), scale);
        var image = Image.LoadFromFile(path);
        return GfxAssets.PlaceOf(path) is { } place ? Placed(image, place) : Trim(image);
    }

    /// <summary>HD-файл (<c>scale</c> &gt; 1): с углом в описи — обрезка в логическом месте холста, без угла — весь холст (занятую часть у
    /// HD не ищем: угол в точках файла не делится на множитель).</summary>
    private static Loaded? Scaled(Image? image, GfxPlace? place, int scale)
    {
        if (image is null) return null;
        if (!image.IsCompressed() && image.GetFormat() != Image.Format.Rgba8) image.Convert(Image.Format.Rgba8);
        var part = image.GetSize() / scale;
        if (place is { } p && Fits(part, p)) return new Loaded(image, new Vector2I(p.W, p.H), new Vector2I(p.X, p.Y), scale);
        return new Loaded(image, part, Vector2I.Zero, scale);
    }

    /// <summary>Файл сборки с углом в холсте из описи (<see cref="GfxPlace"/>): размером с холст — кадр как есть (занята больше половины —
    /// и <see cref="Trim"/> оставил бы его целым), меньше — уже обрезка <see cref="Trim"/>, только без разбора всего холста. Обрезка без
    /// прозрачных точек разбирается Godot в RGB8 — приводится к RGBA8, как вырезка холста (<see cref="Image.GetRegion"/>). Файл не влезает в
    /// холст (подменён в обход описи) — обычная обрезка. null (битый файл) — null.</summary>
    private static Loaded? Placed(Image? image, GfxPlace place)
    {
        if (image is null) return null;
        var size = image.GetSize();
        var full = new Vector2I(place.W, place.H);
        var at = new Vector2I(place.X, place.Y);
        if (size == full && at == Vector2I.Zero) return new Loaded(image, full, Vector2I.Zero);
        if (!Fits(size, place)) return Trim(image);
        if (image.GetFormat() != Image.Format.Rgba8) image.Convert(Image.Format.Rgba8);
        return new Loaded(image, full, at);
    }

    /// <summary>Часть размера <paramref name="size"/> в углу места лежит внутри холста.</summary>
    private static bool Fits(Vector2I size, GfxPlace place) =>
        place.X >= 0 && place.Y >= 0 && size.X > 0 && size.Y > 0 && place.X + size.X <= place.W && place.Y + size.Y <= place.H;

    /// <summary>Картинка файла графики размером с холст, как прежний целый файл (<see cref="Image.LoadFromFile"/>), — для тех, кто смотрит
    /// точки в координатах холста или строит текстуру сам (портреты, маски, курсоры, проверки видимости и снимков). Файл сборки с обрезкой
    /// (<see cref="GfxAssets.PlaceOf"/>, Г19) кладётся в угол прозрачного холста RGBA8; без угла или размером с холст — файл как есть.</summary>
    public static Image Canvas(string path)
    {
        if (GfxAssets.ScaleOf(path) > 1)   // HD — холст в логических точках; полный отпускается сразу (сотни МБ разом)
        {
            var hd = CanvasHd(path);
            var logical = ImageScales.ToLogical(hd);
            if (logical != hd) hd.Dispose();
            return logical;
        }
        var image = Image.LoadFromFile(path);
        if (image is null || GfxAssets.PlaceOf(path) is not { } place) return image!;
        var size = image.GetSize();
        if (size == new Vector2I(place.W, place.H) || !Fits(size, place)) return image;
        if (image.GetFormat() != Image.Format.Rgba8) image.Convert(Image.Format.Rgba8);
        var canvas = Image.CreateEmpty(place.W, place.H, false, Image.Format.Rgba8);
        canvas.BlitRect(image, new Rect2I(Vector2I.Zero, size), new Vector2I(place.X, place.Y));
        image.Dispose();
        return canvas;
    }

    /// <summary>Холст файла графики в точках файла: у HD (<c>scale</c> &gt; 1) — крупнее логического, множитель записан за картинкой
    /// (<see cref="ImageScales"/>): окна рисуют портреты и картины в полной точности. Без множителя — как <see cref="Canvas"/>.</summary>
    public static Image CanvasHd(string path)
    {
        int scale = GfxAssets.ScaleOf(path);
        if (scale <= 1) return Canvas(path);
        var image = Image.LoadFromFile(path);
        if (image is null) return image!;
        if (image.GetFormat() != Image.Format.Rgba8) image.Convert(Image.Format.Rgba8);
        if (GfxAssets.PlaceOf(path) is not { } place || !Fits(image.GetSize() / scale, place)
            || (image.GetSize() == new Vector2I(place.W, place.H) * scale && place.X == 0 && place.Y == 0))
            return ImageScales.With(image, scale, path);   // файл как есть — его сжатая копия годится текстуре
        var canvas = Image.CreateEmpty(place.W * scale, place.H * scale, false, Image.Format.Rgba8);
        canvas.BlitRect(image, new Rect2I(Vector2I.Zero, image.GetSize()), new Vector2I(place.X, place.Y) * scale);
        image.Dispose();
        return ImageScales.With(canvas, scale);
    }

    /// <summary>Обрезать картинку по занятому прямоугольнику (<see cref="Image.GetUsedRect"/>), если он не больше половины площади: кадры
    /// боя и карты — холсты 800×600 вокруг якоря, где юнит занимает несколько процентов (гоблин — 87×57). Прежде в видеопамять уходил весь
    /// холст: ~1,9 МБ на кадр, загрузка ~0,9 мс на кадр в главном потоке при каждом новом действии боя. Вызывается и в рабочих потоках
    /// (своя <see cref="Image"/> у потока). Пустая картинка — одна прозрачная точка. null (битый файл) — null.</summary>
    private static Loaded? Trim(Image? image)
    {
        if (image is null) return null;
        var full = image.GetSize();
        var used = image.GetUsedRect();
        if (used.Size.X <= 0 || used.Size.Y <= 0) used = new Rect2I(0, 0, 1, 1);
        if ((long)used.Size.X * used.Size.Y * 2 > (long)full.X * full.Y) return new Loaded(image, full, Vector2I.Zero);
        var part = image.GetRegion(used);
        image.Dispose();
        return new Loaded(part, full, used.Position);
    }

    /// <summary>Текстура обрезки с полями до полного размера (<see cref="AtlasTexture.Margin"/>): размер, отрисовка и отражение — как у
    /// целого холста, в видеопамяти — только занятая часть.</summary>
    private static Texture2D Texture(Loaded? loaded)
    {
        var texture = ImageTexture.CreateFromImage(loaded?.Image!);   // null (битый файл) — тот же вызов, что и прежде
        if (loaded is null) return texture;
        var size = loaded.Part;
        if (loaded.Scale > 1) ImageScales.Mark(texture, size);   // HD: текстура логического размера, точки — полные
        if (size == loaded.Full) return texture;
        var atlas = new AtlasTexture
        {
            Atlas = texture, Region = new Rect2(Vector2.Zero, size), Margin = new Rect2(loaded.At, loaded.Full - size),
        };
        if (loaded.Scale > 1) ImageScales.MarkHd(atlas);
        return atlas;
    }

    private static Texture2D Add(string path, Loaded? loaded)
    {
        var image = loaded?.Image;
        long size = image?.GetDataSize() ?? 0;
        // Сверх предела — забыть самые давние (без Dispose: текстуры у живых спрайтов остаются, остальные заберёт сборщик мусора).
        while (_bytes + size > LimitBytes && _order.Last is { } oldest)
        {
            _order.RemoveLast();
            _cache.Remove(oldest.Value.Path);
            _bytes -= oldest.Value.Bytes;
            Evicted++;
        }
        var texture = Texture(loaded);
        image?.Dispose();   // данные уже в текстуре; разобранная картинка не ждёт сборщика мусора (их тут сотни мегабайт разом)
        if (_cache.Remove(path, out var stale))
        {
            _order.Remove(stale);
            _bytes -= stale.Value.Bytes;
        }
        _cache[path] = _order.AddFirst(new Cached(path, texture, size));
        _bytes += size;
        return texture;
    }
}
