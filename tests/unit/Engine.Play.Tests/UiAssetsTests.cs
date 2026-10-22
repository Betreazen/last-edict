using System.Buffers.Binary;
using D2.Data.Ui;
using Xunit;
using Xunit.Abstractions;

namespace D2.Data.Tests;

/// <summary>Манифест интерфейсной графики: <c>data/ui/assets.json</c> лежит в git, сами картинки (WebP без потерь) — нет.
/// Числа сверены распаковкой архивов GOG 3.01 (<c>tools/ui-assets/build_ui_assets.py</c>).</summary>
public class UiAssetsTests(ITestOutputHelper output)
{
    private static string RepoRoot
    {
        get
        {
            var d = new DirectoryInfo(AppContext.BaseDirectory);
            while (d is not null && !File.Exists(Path.Combine(d.FullName, "LastEdict.sln"))) d = d.Parent;
            return d?.FullName ?? "";
        }
    }

    private static string ManifestPath
    {
        get
        {
            ImportedData.RequireIn(Path.Combine(RepoRoot, "data"), "ui/assets.json");
            return Path.Combine(RepoRoot, "data", "ui", "assets.json");
        }
    }
    /// <summary>Распаковка импорта (<see cref="ImportedData.AssetsDir"/>, в рабочих копиях <c>../dg-wt-*</c> — <c>D2_ASSETS_DIR</c>): <c>ui</c>.</summary>
    private static string AssetsRoot => Path.Combine(ImportedData.AssetsDir, "ui");

    /// <summary>Манифест читается, кадры все на месте и опознаются парой архив+имя.</summary>
    [SkippableFact]
    public void Manifest_ParsesWithFrameCountPerArchive()
    {
        var assets = UiAssets.Load(ManifestPath);
        var byArchive = assets.GroupBy(a => a.Archive).ToDictionary(g => g.Key, g => g.Count());
        output.WriteLine($"кадров {assets.Count}: " + string.Join(", ", byArchive.Select(a => $"{a.Key}={a.Value}")));

        Assert.Equal(1940, assets.Count);
        Assert.Equal(1710, byArchive["Interf.ff"]);
        Assert.Equal(229, byArchive["IntfScen.ff"]);
        Assert.Equal(1, byArchive["MenuImgs.ff"]);

        Assert.Empty(assets.GroupBy(a => $"{a.Archive}/{a.Name}", StringComparer.OrdinalIgnoreCase)
                           .Where(g => g.Count() > 1).Select(g => g.Key));
        Assert.All(assets, a =>
        {
            Assert.NotEmpty(a.Name);
            Assert.True(a.W > 0 && a.H > 0, $"{a.Name}: размер {a.W}×{a.H}");
            Assert.Matches("^[0-9a-f]{16}$", a.Sha256);
            Assert.EndsWith(".webp", a.File, StringComparison.Ordinal);
        });

        // Подложка окна: тот же кадр 573×586, что и фон DLG_ACTION_RESULT в разметке экранов.
        var back = assets.Single(a => a.Name == "_PG0250IX");
        Assert.Equal("Interf.ff", back.Archive);
        Assert.Equal((573, 586), (back.W, back.H));
        Assert.Equal("Interf/frames/_PG0250IX.webp", back.File);
    }

    /// <summary>Одно имя в двух архивах — два разных кадра; поиск по имени берёт первый архив,
    /// мод накладывается поверх по паре архив+имя.</summary>
    [SkippableFact]
    public void Overlay_ReplacesByArchiveAndName_AndLookupPrefersFirstArchive()
    {
        var assets = UiAssets.Load(ManifestPath);
        var same = assets.Where(a => a.Name == "DRAGDROP_SMALL_VALID").ToList();
        Assert.Equal(2, same.Count);
        Assert.Equal([(74, 89), (70, 85)], same.Select(a => (a.W, a.H)));
        Assert.Same(same[0], UiAssets.ByName(assets)["dragdrop_small_valid"]);

        var mod = new List<UiAsset>
        {
            new() { Name = "DRAGDROP_SMALL_VALID", Archive = "Interf.ff", File = "mod/x.PNG",
                    W = 148, H = 178, Sha256 = "0123456789abcdef" },
            new() { Name = "MOD_ONLY", Archive = "mymod", File = "mod/y.PNG",
                    W = 1, H = 1, Sha256 = "fedcba9876543210" },
        };
        var merged = UiAssets.Merge(assets, mod);
        Assert.Equal(assets.Count + 1, merged.Count);
        Assert.Equal(148, merged.Single(a => a.Archive == "Interf.ff" && a.Name == "DRAGDROP_SMALL_VALID").W);
        Assert.Equal((70, 85), merged.Where(a => a.Name == "DRAGDROP_SMALL_VALID")
                                     .Select(a => (a.W, a.H)).Last());  // кадр из IntfScen не тронут
        Assert.Equal("MOD_ONLY", merged[^1].Name);
    }

    /// <summary>Распаковка на диске совпадает с манифестом: выборка кадров — WebP без потерь (<c>VP8L</c>) с размером из
    /// манифеста. Хеш в манифесте — от пикселей, его сверяет сам сборщик (выборка файлов разбирается заново после записи); здесь
    /// декодера WebP нет. Без <c>assets/ui</c> (в git его нет) проверять нечего.</summary>
    [SkippableFact]
    public void Webp_FilesOnDisk_MatchManifest()
    {
        Skip.IfNot(Directory.Exists(AssetsRoot), $"нет распаковки: {AssetsRoot}");
        var assets = UiAssets.Load(ManifestPath);
        var sample = assets.Where((_, i) => i % 97 == 0).ToList();
        foreach (var a in sample)
        {
            var path = UiAssets.PathOf(a, AssetsRoot);
            Assert.True(File.Exists(path), $"{a.Name}: нет файла {path}");
            Assert.Equal((a.W, a.H), WebpLosslessSize(File.ReadAllBytes(path)));
        }
        output.WriteLine($"сверено кадров {sample.Count} из {assets.Count}");
        Assert.Equal(20, sample.Count);
    }

    /// <summary>Размер из заголовка WebP без потерь: <c>RIFF</c>, длина, <c>WEBP</c>, кусок <c>VP8L</c> и его длина, подпись 0x2F,
    /// дальше 14 бит ширины−1 и 14 бит высоты−1 (младшие биты первыми). Кусок не <c>VP8L</c> (с потерями) — провал.</summary>
    internal static (int W, int H) WebpLosslessSize(byte[] webp)
    {
        Assert.Equal("RIFF", System.Text.Encoding.ASCII.GetString(webp, 0, 4));
        Assert.Equal("WEBPVP8L", System.Text.Encoding.ASCII.GetString(webp, 8, 8));
        Assert.Equal(0x2F, webp[20]);
        uint bits = BinaryPrimitives.ReadUInt32LittleEndian(webp.AsSpan(21, 4));
        return ((int)(bits & 0x3FFF) + 1, (int)((bits >> 14) & 0x3FFF) + 1);
    }
}
