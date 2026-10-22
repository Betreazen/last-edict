using LastEdict.Data.Content;
using LastEdict.Data.Gfx;
using Xunit;
using Xunit.Abstractions;

namespace LastEdict.Data.Tests;

/// <summary>Опись графики карты и боя (<c>data/gfx</c>, <c>tools/gfx-assets</c>). Числа — из сборки по копии игры
/// GOG 3.01 (19.09.2026).</summary>
public class GfxAssetsTests(ITestOutputHelper output)
{
    private static string Data => Path.Combine(AppContext.BaseDirectory, "data", "gfx");

    private static readonly Lazy<GfxAssets> LoadedGfx = new(() => GfxAssets.Load(Data));

    /// <summary>Опись графики игры; без неё (публичное дерево до импорта) тест пропускается.</summary>
    private static GfxAssets Gfx
    {
        get
        {
            ImportedData.Require("gfx");
            return LoadedGfx.Value;
        }
    }

    /// <summary>Опись как есть — без данных игры пустая: проверкам слияния мода её содержимое не важно.</summary>
    private static GfxAssets BaseGfx => LoadedGfx.Value;

    /// <summary>Собранная графика импорта (<see cref="ImportedData.AssetsDir"/>): <c>gfx</c>.</summary>
    private static string Root => Path.Combine(ImportedData.AssetsDir, "gfx");

    /// <summary>Смешение кадров по <c>opacity</c> (<c>-IMAGES.OPT</c>, <c>docs/ai-screen.md</c> §13): тени отрядов и лодок и
    /// <c>SPLAREAFOG</c> — половина (128), призрачные юниты и большинство заклинаний — сложение (300), шесть заклинаний — вычитание
    /// (400), прочее — копия. Числа — по сборке 27.09.</summary>
    [SkippableFact]
    public void Opacity_GivesEngineBlend()
    {
        Assert.Equal(GfxBlend.Half, Gfx.BlendOf("IsoUnit", "G000RR0000SBOA0", anim: true));
        Assert.Equal(GfxBlend.Half, Gfx.BlendOf("IsoCmon", "SPLAREAFOG", anim: false));
        Assert.Equal(GfxBlend.Add, Gfx.BlendOf("IsoUnit", "G000UU0023MOV20", anim: true));
        Assert.Equal(GfxBlend.Subtract, Gfx.BlendOf("IsoSpell", "G000SS0052O2999", anim: true));
        Assert.Equal(GfxBlend.Opaque, Gfx.BlendOf("IsoCmon", "нет такой", anim: false));
        Assert.Equal((GfxBlend.Hidden, GfxBlend.Half), (GfxAssets.BlendOf(0), GfxAssets.BlendOf(1)));
        var spells = Gfx.Archives.First(a => a.Name == "IsoSpell").Anims;
        Assert.Equal((187, 6), (spells.Count(a => a.Opacity == 300), spells.Count(a => a.Opacity == 400)));
        Assert.Equal(5683, Gfx.Archives.Sum(a => a.Images.Count(i => i.Opacity != 255) + a.Anims.Count(n => n.Opacity != 255)));
    }

    /// <summary>Смешение кадра интерфейса (<see cref="LastEdict.Data.Ui.UiFrameRef.BlendOf"/>): отметка изученного заклинания в списках книги
    /// и исследования — сложение (opacity 300 у <c>Icons:ICNSY001</c>; копией её чёрный фон закрывал значок, замечание 23 прогона
    /// 11.10); знак «+» — сложение; подкрашенный кадр, кадр интерфейса и портрет — копия.</summary>
    [SkippableFact]
    public void UiFrameBlend_KnownSpellMarkIsAdditive()
    {
        Assert.Equal(GfxBlend.Add, LastEdict.Data.Ui.UiFrameRef.BlendOf("Icons:ICNSY001", Gfx));
        Assert.Equal(GfxBlend.Add, LastEdict.Data.Ui.UiFrameRef.BlendOf("+DRAGDROP_BIGFACE_LEFT", Gfx));
        Assert.Equal(GfxBlend.Opaque, LastEdict.Data.Ui.UiFrameRef.BlendOf("Icons:CITYHU", Gfx));
        Assert.Equal(GfxBlend.Opaque, LastEdict.Data.Ui.UiFrameRef.BlendOf("Icons:ICNSY001#FF0000", Gfx));
        Assert.Equal(GfxBlend.Opaque, LastEdict.Data.Ui.UiFrameRef.BlendOf("DLG_CITY_STACK_ADDSTACK", Gfx));
    }

    [SkippableFact]
    public void AllArchives_WithExpectedCounts()
    {
        var counts = Gfx.Archives.ToDictionary(a => a.Name, a => (a.Images.Count, a.Anims.Count, a.Anims.Sum(n => n.Frames.Count)));
        foreach (var (name, c) in counts.OrderBy(p => p.Key)) output.WriteLine($"{name}: {c}");
        Assert.Equal(23, counts.Count);
        Assert.Equal(2446, counts.Values.Sum(c => c.Item1));
        Assert.Equal(134210, counts.Values.Sum(c => c.Item3));
        Assert.Equal((26, 0, 0), counts["Ground"]);
        Assert.Equal((82, 0, 0), counts["GrBorder"]);
        Assert.Equal(339, counts["IsoTerrn"].Item1);
    }

    /// <summary>То, что берёт сборщик земли: узоры рас по 4 варианта, маски границ 1–15 и 17–31, спрайты берега,
    /// фон воды, 16 картинок дорог.</summary>
    [SkippableFact]
    public void GroundArt_IsComplete()
    {
        foreach (string race in new[] { "DW", "EL", "HE", "HU", "NE", "UN" })
            for (int v = 0; v < 4; v++)
                Assert.Equal(192, Gfx.Image("Ground", $"{race}_{v:00}")?.W);
        Assert.NotNull(Gfx.Image("Ground", "BL_00"));
        Assert.Equal(128, Gfx.Image("Ground", "WA_00")?.W);
        foreach (int mask in Enumerable.Range(1, 15).Concat(Enumerable.Range(17, 15)))
            Assert.NotNull(Gfx.Image("GrBorder", $"NE_{mask:00}_00"));
        Assert.True(Gfx.Archive("GrBorder")!.Images.Count(i => i.Name.StartsWith("WA_", StringComparison.Ordinal)) > 0);
        Assert.Equal(16, Gfx.Archive("IsoTerrn")!.Images.Count(i => i.Name.StartsWith("ROAD", StringComparison.Ordinal)));
    }

    [SkippableFact]
    public void Lookup_IgnoresCase_AndAnimFramesInOrder()
    {
        Assert.Same(Gfx.Image("Ground", "HU_00"), Gfx.Image("ground", "hu_00"));
        var anim = Gfx.Archive("Battle")!.Anims[0];
        var paths = GfxAssets.FramePaths(anim, "R");
        Assert.Equal(anim.Frames.Count, paths.Count);
        Assert.EndsWith($"{anim.Frames[0]}.webp", paths[0]);
        Assert.Equal(".webp", anim.Ext);
    }

    [SkippableFact]
    public void Merge_ModImageReplacesAndGetsFullPath()
    {
        var mod = GfxAssets.Merge(Gfx, ModOf("""{"archive":"Ground.ff","images":[{"name":"HU_00","file":"Ground/plain/HU_00.PNG","w":192,"h":192}],"anims":[]}"""), @"C:\mod\assets\gfx");
        var image = mod.Image("Ground", "HU_00")!;
        Assert.StartsWith(@"C:\mod\assets\gfx", GfxAssets.PathOf(image, "IGNORED"));
        Assert.Equal(26, mod.Archive("Ground")!.Images.Count);
        Assert.Equal(2446, mod.Archives.Sum(a => a.Images.Count));
    }

    /// <summary>Мод с кадрами анимации в PNG указывает <c>ext</c>; без него кадры — формата сборки (<c>.webp</c>).</summary>
    [Fact]
    public void Merge_ModAnimWithPngFrames()
    {
        var mod = GfxAssets.Merge(BaseGfx, ModOf("""{"archive":"Battle.ff","images":[],"anims":[{"name":"MOD_PNG","dir":"Battle/anims/MOD_PNG","ext":".png","w":8,"h":8,"frames":["000_A","001_A"]},{"name":"MOD_WEBP","dir":"Battle/anims/MOD_WEBP","w":8,"h":8,"frames":["000_A"]}]}"""), @"C:\mod\assets\gfx");
        var png = GfxAssets.FramePaths(mod.Anim("Battle", "MOD_PNG")!, "IGNORED");
        Assert.Equal([@"C:\mod\assets\gfx\Battle\anims\MOD_PNG\000_A.png", @"C:\mod\assets\gfx\Battle\anims\MOD_PNG\001_A.png"], png);
        Assert.EndsWith("000_A.webp", GfxAssets.FramePaths(mod.Anim("Battle", "MOD_WEBP")!, "IGNORED")[0]);
    }

    /// <summary>Выборка файлов на диске — WebP без потерь размером с холст из описи или, у обрезанных сборкой (Г19, поле <c>at</c>), — часть,
    /// что лежит в холсте по углу (картинки и первые кадры анимаций).</summary>
    [SkippableFact]
    public void Files_AreLosslessWebpOfManifestSize()
    {
        Skip.IfNot(Directory.Exists(Root), $"нет {Root}: python tools/gfx-assets/build_gfx_assets.py");
        var sample = Gfx.Archives.SelectMany(a => a.Images.Select(i => (Path: GfxAssets.PathOf(i, Root), i.W, i.H))
                                                  .Concat(a.Anims.Select(n => (Path: GfxAssets.FramePaths(n, Root)[0], n.W, n.H))))
                                 .Where((_, i) => i % 89 == 0).ToList();
        int trimmed = 0;
        foreach (var (path, w, h) in sample)
        {
            var (fw, fh) = UiAssetsTests.WebpLosslessSize(File.ReadAllBytes(path));
            if (GfxAssets.PlaceOf(path) is not { } place || (fw, fh) == (w, h))
            {
                Assert.Equal((w, h), (fw, fh));
                continue;
            }
            trimmed++;
            Assert.Equal((w, h), (place.W, place.H));
            Assert.True(place.X >= 0 && place.Y >= 0 && place.X + fw <= w && place.Y + fh <= h, $"{path}: {fw}×{fh} в {place}");
            Assert.True(fw * fh * 2 <= w * h, $"{path}: обрезка {fw}×{fh} больше половины холста {w}×{h}");
        }
        output.WriteLine($"выборка {sample.Count}, обрезанных {trimmed}");
        Assert.True(sample.Count > 100, $"выборка {sample.Count}");
    }

    /// <summary>Угол файла в холсте (Г19) запоминается за путём, который выдали <see cref="GfxAssets.PathOf"/> и
    /// <see cref="GfxAssets.FramePaths"/>; без поля <c>at</c> или с <c>at</c> не по числу кадров — угла нет (рантайм обрезает сам).</summary>
    [Fact]
    public void Place_RememberedByPath()
    {
        var mod = GfxAssets.Merge(BaseGfx, ModOf("""{"archive":"Battle.ff","images":[{"name":"MOD_AT","file":"Battle/frames/MOD_AT.webp","w":800,"h":600,"at":[12,34]},{"name":"MOD_NOAT","file":"Battle/frames/MOD_NOAT.webp","w":8,"h":8}],"anims":[{"name":"MOD_ANIM","dir":"Battle/anims/MOD_ANIM","w":360,"h":360,"frames":["000_A","001_A"],"at":[1,2,3,4]},{"name":"MOD_BAD","dir":"Battle/anims/MOD_BAD","w":8,"h":8,"frames":["000_A","001_A"],"at":[1,2]}]}"""), @"C:\place-test\assets\gfx");
        Assert.Equal(new GfxPlace(12, 34, 800, 600), GfxAssets.PlaceOf(GfxAssets.PathOf(mod.Image("Battle", "MOD_AT")!, "IGNORED")));
        Assert.Null(GfxAssets.PlaceOf(GfxAssets.PathOf(mod.Image("Battle", "MOD_NOAT")!, "IGNORED")));
        var frames = GfxAssets.FramePaths(mod.Anim("Battle", "MOD_ANIM")!, "IGNORED");
        Assert.Equal([new GfxPlace(1, 2, 360, 360), new GfxPlace(3, 4, 360, 360)], frames.Select(f => GfxAssets.PlaceOf(f)!.Value));
        Assert.All(GfxAssets.FramePaths(mod.Anim("Battle", "MOD_BAD")!, "IGNORED"), f => Assert.Null(GfxAssets.PlaceOf(f)));
        // Опись перезагружена, у того же файла угла больше нет — прежний угол забыт, а не сдвигает картинку молча.
        var reloaded = GfxAssets.Merge(BaseGfx, ModOf("""{"archive":"Battle.ff","images":[{"name":"MOD_AT","file":"Battle/frames/MOD_AT.webp","w":800,"h":600}],"anims":[]}"""), @"C:\place-test\assets\gfx");
        Assert.Null(GfxAssets.PlaceOf(GfxAssets.PathOf(reloaded.Image("Battle", "MOD_AT")!, "IGNORED")));
    }

    /// <summary>Сборка пишет угол у всех картинок и у всех анимаций, кроме одной в IsoSpell с кадрами разного холста (Г19).</summary>
    [SkippableFact]
    public void Manifest_HasPlaces()
    {
        var images = Gfx.Archives.SelectMany(a => a.Images).ToList();
        var anims = Gfx.Archives.SelectMany(a => a.Anims).ToList();
        Assert.All(images, i => Assert.True(i.At is [>= 0, >= 0], i.Name));
        Assert.Equal(["G000SS0063O2000"], anims.Where(a => a.At is null).Select(a => a.Name));
        Assert.All(anims.Where(a => a.At is not null), a => Assert.Equal(2 * a.Frames.Count, a.At!.Count));
    }

    [SkippableFact]
    public void GameContent_LoadsGfx() =>
        Assert.Equal(23, SharedContent.Load(Path.Combine(AppContext.BaseDirectory, "data")).Gfx.Archives.Count);

    /// <summary>Файлы на диске — те, что в описи: у каждой самостоятельной картинки и первого кадра каждой анимации
    /// есть PNG. Пропускается, когда <c>assets/gfx</c> не собран.</summary>
    [SkippableFact]
    public void Files_ExistOnDisk()
    {
        Skip.IfNot(Directory.Exists(Root), $"нет {Root}: python tools/gfx-assets/build_gfx_assets.py");
        var missing = Gfx.Archives.SelectMany(a => a.Images.Select(i => GfxAssets.PathOf(i, Root))
                                                   .Concat(a.Anims.Select(n => GfxAssets.FramePaths(n, Root)[0])))
                                  .Where(p => !File.Exists(p)).Take(5).ToList();
        Assert.True(missing.Count == 0, string.Join(", ", missing));
    }

    private static GfxAssets ModOf(string json)
    {
        string dir = Path.Combine(Path.GetTempPath(), $"gfxmod-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            File.WriteAllText(Path.Combine(dir, "Ground.json"), json);
            return GfxAssets.Load(dir);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    /// <summary>Портрет панели команд карты (пометка 1 прогона 14.10: у лидера-ассасина <c>G000UU5354</c>, выданного сценарием, слот
    /// был пуст). Своего <c>{тип}FACE</c> в <c>Faces.ff</c> нет у 100 типов из 356; движок (<c>0x5b1e6a</c>) берёт базовый тип
    /// (<c>BASE_UNIT</c>) — по нему портрет есть у каждого типа <c>Gunits</c>.</summary>
    [SkippableFact]
    public void UnitFaces_EveryUnitTypeHasSmallFaceByBase()
    {
        var faces = LastEdict.Data.Ui.Windows.UnitFaces.Load(ImportedData.PathOf("rules"));
        Assert.Equal("G000UU0154FACE", faces.SmallName("g000uu5354"));
        Assert.Null(Gfx.Image(LastEdict.Data.Ui.Windows.UnitFaces.Archive, "G000UU5354FACE"));
        Assert.NotNull(Gfx.Image(LastEdict.Data.Ui.Windows.UnitFaces.Archive, faces.SmallName("G000UU5354")));

        var types = LastEdict.Data.Dbf.GameTables.Read(ImportedData.PathOf("rules"), "Gunits.dbf").Rows
            .Select(r => r.GetString("UNIT_ID")).ToList();
        Assert.Equal(356, types.Count);
        Assert.Equal(100, types.Count(t => Gfx.Image(LastEdict.Data.Ui.Windows.UnitFaces.Archive, t.ToUpperInvariant() + "FACE") is null));
        Assert.DoesNotContain(types, t => Gfx.Image(LastEdict.Data.Ui.Windows.UnitFaces.Archive, faces.SmallName(t)) is null);
    }

    /// <summary>Портрет панелей у большого юнита (пометка 3 партии 15.10: призванный лидер <c>G000UU0102</c> на панели команд — сжатый
    /// широкий <c>FACE</c> 150×85): <c>0x5b1e6a</c> → <c>0x5c6718</c> берёт <c>{база}FACES</c> 70×85, нет её — <c>{база}FACE</c>
    /// (<c>0x5c680d</c>). <c>FACES</c> есть у 41 картинки — все у больших типов; у малых и у выросших по базе — прежний <c>FACE</c>.</summary>
    [SkippableFact]
    public void UnitFaces_PanelTakesSmallFaceOfBigUnit()
    {
        string rules = ImportedData.PathOf("rules");
        var faces = LastEdict.Data.Ui.Windows.UnitFaces.Load(rules, name => Gfx.Image(LastEdict.Data.Ui.Windows.UnitFaces.Archive, name) is not null);
        Assert.Equal("G000UU0102FACES", faces.PanelName("g000uu0102"));
        Assert.Equal("Faces:G000UU0102FACES", faces.Panel("g000uu0102"));
        var panel = Gfx.Image(LastEdict.Data.Ui.Windows.UnitFaces.Archive, faces.PanelName("g000uu0102"))!;
        var wide = Gfx.Image(LastEdict.Data.Ui.Windows.UnitFaces.Archive, faces.SmallName("g000uu0102"))!;
        Assert.Equal((70, 85, 150, 85), (panel.W, panel.H, wide.W, wide.H));
        Assert.Equal("G000UU0154FACE", faces.PanelName("g000uu5354"));
        Assert.Equal("G000UU0101FACE", faces.PanelName("g000uu0101"));
        Assert.Equal("G000UU0102FACE", LastEdict.Data.Ui.Windows.UnitFaces.Load(rules).PanelName("g000uu0102"));   // без проверки картинок — FACE

        var units = LastEdict.Data.Dbf.GameTables.Read(rules, "Gunits.dbf").Rows.ToList();
        var withS = units.Where(r => faces.PanelName(r.GetString("UNIT_ID")).EndsWith("FACES", StringComparison.Ordinal)).ToList();
        Assert.NotEmpty(withS);
        Assert.All(withS, r => Assert.False(r.GetBool("SIZE_SMALL")));
        Assert.DoesNotContain(units, r => Gfx.Image(LastEdict.Data.Ui.Windows.UnitFaces.Archive, faces.PanelName(r.GetString("UNIT_ID"))) is null);
    }
}
