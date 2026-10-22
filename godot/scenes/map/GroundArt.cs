using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using LastEdict.Data.Gfx;
using Godot;

namespace LastEdict.Godot;

/// <summary>Графика земли для <see cref="GroundComposer"/> из собранных PNG (<c>assets/gfx</c>, опись —
/// <see cref="GameData.Content"/>): узоры <c>Ground</c>, маски и спрайты берега <c>GrBorder</c>, фон воды. Картинки
/// грузятся по первому обращению и держатся в памяти — их немного (26 узоров, 82 маски). Сборщик зовёт её из нескольких потоков
/// сборки кусков — кэши потокобезопасны; число вариантов считается один раз (раньше — перебором описи на каждую точку).</summary>
public sealed class GroundArt : IGroundArt
{
    private readonly GfxAssets _gfx;
    private readonly string _root;
    private readonly ConcurrentDictionary<string, RgbaImage?> _cache = new();
    private readonly ConcurrentDictionary<string, int> _counts = new();

    public GroundArt(GfxAssets gfx, string root)
    {
        _gfx = gfx;
        _root = root;
    }

    public int PatternVariants(GroundType type) => Count("Ground", $"{GroundGeometry.Prefix(type)}_");

    public RgbaImage Pattern(GroundType type, int variant) =>
        Load("Ground", $"{GroundGeometry.Prefix(type)}_{variant:00}")
        ?? throw new FileNotFoundException($"нет узора земли {GroundGeometry.Prefix(type)}_{variant:00}");

    public int MaskVariants(int mask) => Count("GrBorder", $"NE_{mask:00}_");

    public RgbaImage? Mask(int mask, int variant) => Load("GrBorder", $"NE_{mask:00}_{variant:00}");

    public int ShoreVariants(int mask) => Count("GrBorder", $"WA_{mask:00}_");

    public RgbaImage? Shore(int mask, int variant) => Load("GrBorder", $"WA_{mask:00}_{variant:00}");

    public RgbaImage? WaterBackground => Load("Ground", "WA_00");

    private int Count(string archive, string prefix) =>
        _counts.GetOrAdd($"{archive}/{prefix}", _ =>
            _gfx.Archive(archive)?.Images.Count(i => i.Name.StartsWith(prefix, System.StringComparison.OrdinalIgnoreCase)) ?? 0);

    private RgbaImage? Load(string archive, string name) => _cache.GetOrAdd($"{archive}/{name}", _ => Read(archive, name));

    private RgbaImage? Read(string archive, string name)
    {
        RgbaImage? result = null;
        if (_gfx.Image(archive, name) is { } info && GfxAssets.PathOf(info, _root) is { } path && File.Exists(path))
        {
            var image = GfxTextures.Canvas(path);
            image.Convert(Image.Format.Rgba8);
            result = new RgbaImage(image.GetWidth(), image.GetHeight(), image.GetData());
        }
        return result;
    }
}
