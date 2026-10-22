using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace D2.Data.Gfx;

/// <summary>Картинка графики мира и боя: имя в архиве игры, путь к файлу (от <c>assets/gfx</c>; база — WebP без потерь, у мода —
/// и <c>.png</c>, и <c>.webp</c>), размер холста и хеш пикселей RGBA холста. Прозрачность уже вырезана при сборке (<c>tools/gfx-assets</c>),
/// ключа-мадженты в файле нет.</summary>
public sealed record GfxImage
{
    [JsonPropertyName("name")] public required string Name { get; init; }
    [JsonPropertyName("file")] public required string File { get; init; }
    [JsonPropertyName("w")] public int W { get; init; }
    [JsonPropertyName("h")] public int H { get; init; }
    [JsonPropertyName("sha256")] public string Sha256 { get; init; } = "";
    /// <summary>Смешение кадра (<c>-IMAGES.OPT</c>, <see cref="GfxAssets.BlendOf"/>); в описи только когда не 255.</summary>
    [JsonPropertyName("opacity")] public int Opacity { get; init; } = GfxAssets.Opaque;
    /// <summary>Угол файла в холсте <see cref="W"/>×<see cref="H"/>, <c>[x, y]</c>: сборка пишет только занятую часть кадра (Г19,
    /// <see cref="GfxPlace"/>). Нет поля — файл и есть холст (мод, прежняя сборка): занятую часть ищет рантайм.</summary>
    [JsonPropertyName("at")] public IReadOnlyList<int>? At { get; init; }
}

/// <summary>Анимация: каталог кадров (от <c>assets/gfx</c>), размер холста первого кадра и имена кадров по порядку —
/// файлы <c>&lt;каталог&gt;/&lt;имя&gt;&lt;ext&gt;</c>. Хеш — от хешей кадров по порядку.</summary>
public sealed record GfxAnim
{
    [JsonPropertyName("name")] public required string Name { get; init; }
    [JsonPropertyName("dir")] public required string Dir { get; init; }
    [JsonPropertyName("w")] public int W { get; init; }
    [JsonPropertyName("h")] public int H { get; init; }
    [JsonPropertyName("frames")] public IReadOnlyList<string> Frames { get; init; } = [];
    /// <summary>Расширение файлов кадров с точкой: сборка пишет <c>.webp</c>; мод с кадрами PNG указывает <c>".png"</c>.</summary>
    [JsonPropertyName("ext")] public string Ext { get; init; } = GfxAssets.FrameExt;
    [JsonPropertyName("sha256")] public string Sha256 { get; init; } = "";
    /// <summary>Смешение кадра (<c>-IMAGES.OPT</c>, <see cref="GfxAssets.BlendOf"/>); в описи только когда не 255.</summary>
    [JsonPropertyName("opacity")] public int Opacity { get; init; } = GfxAssets.Opaque;
    /// <summary>Углы файлов кадров в холсте <see cref="W"/>×<see cref="H"/> подряд, <c>[x0, y0, x1, y1, …]</c> (Г19, <see cref="GfxPlace"/>).
    /// Нет поля (мод, прежняя сборка, кадры с разным холстом) — файлы и есть холсты: занятую часть ищет рантайм.</summary>
    [JsonPropertyName("at")] public IReadOnlyList<int>? At { get; init; }
}

/// <summary>Где файл кадра лежит в холсте: сборка (<c>tools/gfx-assets</c>, Г19 <c>docs/perf-log.md</c>) пишет только занятую часть
/// кадра (по альфе, когда она не больше половины холста — правило рантайма Г7), угол — в описи. Рантайм по нему строит текстуру с полями
/// до холста без поиска занятой части, а кто смотрит точки в координатах холста — восстанавливает холст. <see cref="W"/>×<see cref="H"/> —
/// холст; файл размером с холст — кадр как есть.</summary>
public readonly record struct GfxPlace(int X, int Y, int W, int H);

/// <summary>Как кадр ложится на то, что под ним (<c>docs/ai-screen.md</c> §13, DirectDraw — рендер пользователя, <c>UseD3D=0</c>).</summary>
public enum GfxBlend
{
    /// <summary>255 — копия (прозрачные точки пропущены).</summary>
    Opaque,

    /// <summary>1…254 — ровно половина: <c>⌊d/2⌋+⌊s/2⌋</c> (<c>0x678c7b</c>), само значение не важно.</summary>
    Half,

    /// <summary>300 — сложение с насыщением (<c>0x678db7</c>).</summary>
    Add,

    /// <summary>400 — вычитание с насыщением (<c>0x678eb4</c>).</summary>
    Subtract,

    /// <summary>0 — не рисуется.</summary>
    Hidden,
}

/// <summary>Опись одного архива игры (<c>data/gfx/&lt;Архив&gt;.json</c>).</summary>
public sealed record GfxArchive
{
    [JsonPropertyName("archive")] public required string Archive { get; init; }
    [JsonPropertyName("images")] public IReadOnlyList<GfxImage> Images { get; init; } = [];
    [JsonPropertyName("anims")] public IReadOnlyList<GfxAnim> Anims { get; init; } = [];

    /// <summary>Имя архива без расширения — так к нему обращаются (<c>IsoTerrn</c>, <c>BatUnits</c>).</summary>
    [JsonIgnore] public string Name => Path.GetFileNameWithoutExtension(Archive);
}

/// <summary>Графика карты и боя: описи архивов <c>Imgs/*.ff</c>, собранные <c>tools/gfx-assets</c>. Картинка и
/// анимация опознаются парой «архив + имя» (имена в разных архивах повторяются), регистр не значим. Мод кладёт
/// свои описи рядом (<c>data/gfx/*.json</c> мода, файлы — в его <c>assets/gfx</c>) — запись с той же парой
/// заменяет базовую, новая добавляется.</summary>
public sealed class GfxAssets
{
    private readonly Dictionary<string, GfxArchive> _archives;
    private readonly Dictionary<(string, string), GfxImage> _images = [];
    private readonly Dictionary<(string, string), GfxAnim> _anims = [];

    private GfxAssets(IEnumerable<GfxArchive> archives)
    {
        _archives = archives.ToDictionary(a => a.Name, StringComparer.OrdinalIgnoreCase);
        foreach (var a in _archives.Values)
        {
            foreach (var i in a.Images) _images[Key(a.Name, i.Name)] = i;
            foreach (var n in a.Anims) _anims[Key(a.Name, n.Name)] = n;
        }
    }

    public static GfxAssets Empty { get; } = new([]);

    /// <summary>Углы файлов по полному пути, на весь процесс: пути кадров собирают десятки мест рантайма, а загрузчик (кэш текстур) видит только
    /// путь. Пополняется из потоков сцен и рабочих потоков; ключ — полный путь как его строят <see cref="PathOf"/> и
    /// <see cref="FramePaths"/> (с учётом регистра): путь, собранный в обход них, угла не найдёт.</summary>
    private static readonly ConcurrentDictionary<string, GfxPlace> _places = new(StringComparer.Ordinal);

    public IReadOnlyCollection<GfxArchive> Archives => _archives.Values;

    public GfxArchive? Archive(string name) => _archives.GetValueOrDefault(name);

    public GfxImage? Image(string archive, string name) => _images.GetValueOrDefault(Key(archive, name));

    public GfxAnim? Anim(string archive, string name) => _anims.GetValueOrDefault(Key(archive, name));

    public const int Opaque = 255, AddOpacity = 300, SubtractOpacity = 400;

    /// <summary>Расширение кадров анимации, если в описи нет <c>ext</c>: формат сборки (<c>tools/gfx-assets</c>).</summary>
    public const string FrameExt = ".webp";

    /// <summary>Смешение по <c>opacity</c> кадра (<c>CDisplayDDraw</c> blit <c>0x671fb0</c>, ранг 1).</summary>
    public static GfxBlend BlendOf(int opacity) => opacity switch
    {
        0 => GfxBlend.Hidden,
        Opaque => GfxBlend.Opaque,
        AddOpacity => GfxBlend.Add,
        SubtractOpacity => GfxBlend.Subtract,
        _ => GfxBlend.Half,
    };

    /// <summary>Смешение картинки или анимации архива; нет в описи — копия.</summary>
    public GfxBlend BlendOf(string archive, string name, bool anim) =>
        BlendOf((anim ? Anim(archive, name)?.Opacity : Image(archive, name)?.Opacity) ?? Opaque);

    /// <summary>Путь к файлу картинки: корень <c>assets/gfx</c> плюс <see cref="GfxImage.File"/> (у картинок мода путь
    /// уже полный — <c>Path.Combine</c> отдаёт его как есть). Угол файла в холсте из описи запоминается за путём (<see cref="PlaceOf"/>).</summary>
    public static string PathOf(GfxImage image, string root)
    {
        ArgumentNullException.ThrowIfNull(image);
        string path = Combine(root, image.File);
        Note(path, image.At is [var x, var y] ? new GfxPlace(x, y, image.W, image.H) : null);
        return path;
    }

    /// <summary>Пути кадров анимации по порядку; углы файлов в холсте из описи запоминаются за путями (<see cref="PlaceOf"/>).</summary>
    public static IReadOnlyList<string> FramePaths(GfxAnim anim, string root)
    {
        ArgumentNullException.ThrowIfNull(anim);
        var paths = new string[anim.Frames.Count];
        var at = anim.At is { } list && list.Count == 2 * paths.Length ? list : null;   // не по числу кадров — опись не та: как без поля
        for (int i = 0; i < paths.Length; i++)
        {
            paths[i] = Combine(root, $"{anim.Dir}/{anim.Frames[i]}{anim.Ext}");
            Note(paths[i], at is null ? null : new GfxPlace(at[2 * i], at[(2 * i) + 1], anim.W, anim.H));
        }
        return paths;
    }

    /// <summary>Где файл лежит в холсте (<see cref="GfxPlace"/>) — по пути, который выдали <see cref="PathOf"/> или <see cref="FramePaths"/>;
    /// загрузчики кадров берут только пути от них. Нет — файл и есть холст (мод, прежняя сборка): занятую часть ищет рантайм.</summary>
    public static GfxPlace? PlaceOf(string path) => _places.TryGetValue(path, out var place) ? place : null;

    /// <summary>Запомнить угол файла по последней описи, что выдала путь; в ней угла нет — забыть прежний (перезагрузка описей, мод без
    /// <c>at</c> поверх того же пути): устаревший угол сдвинул бы картинку молча.</summary>
    private static void Note(string path, GfxPlace? place)
    {
        if (place is { } p) _places[path] = p;
        else if (!_places.IsEmpty) _places.TryRemove(path, out _);
    }

    public static GfxArchive Parse(string json) =>
        JsonSerializer.Deserialize<GfxArchive>(json) ?? throw new FormatException("пустая опись графики");

    /// <summary>Все описи каталога (<c>*.json</c>); нет каталога — пусто.</summary>
    public static GfxAssets Load(string dir) =>
        new(Directory.Exists(dir)
            ? Directory.GetFiles(dir, "*.json").OrderBy(p => p, StringComparer.Ordinal)
                       .Select(p => Parse(File.ReadAllText(p)))
            : []);

    /// <summary>Наложить описи мода; <paramref name="root"/> — его <c>assets/gfx</c>: пути мода разворачиваются в
    /// полные, чтобы <see cref="PathOf"/> не подставлял базовый корень.</summary>
    public static GfxAssets Merge(GfxAssets baseSet, GfxAssets overlay, string root)
    {
        ArgumentNullException.ThrowIfNull(baseSet);
        ArgumentNullException.ThrowIfNull(overlay);
        var result = baseSet._archives.ToDictionary(p => p.Key, p => p.Value, StringComparer.OrdinalIgnoreCase);
        foreach (var mod in overlay._archives.Values)
        {
            var images = mod.Images.Select(i => i with { File = Combine(root, i.File) }).ToList();
            var anims = mod.Anims.Select(a => a with { Dir = Combine(root, a.Dir) }).ToList();
            result[mod.Name] = result.TryGetValue(mod.Name, out var b)
                ? b with { Images = Overlay(b.Images, images, i => i.Name), Anims = Overlay(b.Anims, anims, a => a.Name) }
                : mod with { Images = images, Anims = anims };
        }
        return new(result.Values);
    }

    private static IReadOnlyList<T> Overlay<T>(IReadOnlyList<T> baseSet, IReadOnlyList<T> overlay, Func<T, string> key)
    {
        var over = overlay.ToDictionary(key, StringComparer.OrdinalIgnoreCase);
        var result = baseSet.Select(b => over.Remove(key(b), out var o) ? o : b).ToList();
        // Порядок мода, а не словаря: после удалений порядок обхода Dictionary не гарантирован.
        result.AddRange(overlay.Where(o => over.ContainsKey(key(o))));
        return result;
    }

    private static (string, string) Key(string archive, string name) => (archive.ToUpperInvariant(), name.ToUpperInvariant());

    private static string Combine(string root, string relative) =>
        Path.GetFullPath(Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar)));
}
