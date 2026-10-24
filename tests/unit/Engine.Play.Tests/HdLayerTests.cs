using LastEdict.Data.Gfx;
using LastEdict.Data.Ui;
using Xunit;

namespace LastEdict.Data.Tests;

/// <summary>Слой HD (Р19) без движка: кэш с пределом по байтам (<see cref="ByteLru{TKey,TValue}"/>), множитель файла в описи
/// (<see cref="GfxAssets.ScaleOf"/>, <see cref="GfxAssets.AnyScaled"/>), размеры окна и разрешения настроек (<see cref="DisplaySizes"/>).
/// Описи — выдуманные, во временном каталоге.</summary>
public sealed class HdLayerTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "le-hd-" + Guid.NewGuid().ToString("N"));

    public HdLayerTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }

    [Fact]
    public void ByteLru_EvictsOldestOverLimit_ReturnsEvicted()
    {
        var lru = new ByteLru<string, string>(10);
        Assert.Empty(lru.Add("a", "A", 4));
        Assert.Empty(lru.Add("b", "B", 4));
        Assert.True(lru.TryGet("a", out _));   // «a» — последняя нужная: вытесняется «b»
        Assert.Equal(["B"], lru.Add("c", "C", 4));
        Assert.Equal((2, 8L), (lru.Count, lru.Bytes));
        Assert.False(lru.ContainsKey("b"));
        Assert.Equal(["C", "A"], lru.Values);
    }

    [Fact]
    public void ByteLru_ReplaceReturnsStale_OversizeStillKept()
    {
        var lru = new ByteLru<string, string>(10, StringComparer.OrdinalIgnoreCase);
        lru.Add("a", "old", 3);
        Assert.Equal(["old"], lru.Add("A", "new", 5));   // ключ без учёта регистра: прежнее — вызывающему (снять его текстуру)
        Assert.Equal(5, lru.Bytes);
        Assert.Equal(["new"], lru.Add("big", "BIG", 50));   // больше предела — всё же в кэше, один
        Assert.True(lru.TryGet("big", out string? big) && big == "BIG");
        lru.Clear();
        Assert.Equal((0, 0L), (lru.Count, lru.Bytes));
    }

    private GfxAssets Write(string json)
    {
        File.WriteAllText(Path.Combine(_dir, "Faces.json"), json);
        return GfxAssets.Load(_dir);
    }

    [Fact]
    public void AnyScaled_SeesImagesAndAnims()
    {
        Assert.False(Write("""{"archive": "Faces.ff", "images": [{"name": "P", "file": "Faces/P.webp", "w": 2, "h": 2}]}""").AnyScaled);
        Assert.True(Write("""{"archive": "Faces.ff", "images": [{"name": "P", "file": "Faces/P.png", "w": 2, "h": 2, "scale": 4}]}""").AnyScaled);
        Assert.True(Write("""{"archive": "Faces.ff", "anims": [{"name": "A", "dir": "Faces/A", "w": 2, "h": 2, "frames": ["0"], "scale": 2}]}""")
                        .AnyScaled);
    }

    /// <summary>Множитель за путём файла: картинка мода с <c>scale</c> — её множитель; та же опись без <c>scale</c> (слой снят, путь тот
    /// же) — множитель забывается, иначе загрузчик уменьшил бы обычный файл в 4 раза.</summary>
    [Fact]
    public void ScaleOf_FollowsLatestManifest()
    {
        var hd = Write("""{"archive": "Faces.ff", "images": [{"name": "Q", "file": "Faces/Q.png", "w": 2, "h": 2, "scale": 4}]}""");
        string path = GfxAssets.PathOf(hd.Image("Faces", "Q")!, _dir);
        Assert.Equal(4, GfxAssets.ScaleOf(path));
        var plain = Write("""{"archive": "Faces.ff", "images": [{"name": "Q", "file": "Faces/Q.png", "w": 2, "h": 2}]}""");
        Assert.Equal(path, GfxAssets.PathOf(plain.Image("Faces", "Q")!, _dir));
        Assert.Equal(1, GfxAssets.ScaleOf(path));
    }

    [Fact]
    public void UiAsset_FactorIsScaleAtLeastOne()
    {
        UiAsset Asset(int? scale) => new() { Name = "N", Archive = "Interf.ff", File = "f.png", Sha256 = "x", Scale = scale };
        Assert.Equal((1, 1, 4), (Asset(null).Factor, Asset(1).Factor, Asset(4).Factor));
    }

    [Fact]
    public void DisplaySizes_EncodeDecodeKnown()
    {
        int fullHd = DisplaySizes.Encode(1920, 1080);
        Assert.Equal(19201080, fullHd);
        Assert.Equal((1920, 1080), DisplaySizes.Decode(fullHd));
        Assert.Null(DisplaySizes.Decode(0));
        Assert.Equal(fullHd, DisplaySizes.Known(fullHd));
        Assert.Equal(0, DisplaySizes.Known(DisplaySizes.Encode(1234, 567)));   // правка файла руками — «по умолчанию»
        Assert.Equal("1920 × 1080", DisplaySizes.Label((1920, 1080)));
    }
}
