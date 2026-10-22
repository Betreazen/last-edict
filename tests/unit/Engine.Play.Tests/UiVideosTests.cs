using LastEdict.Data.Ui;
using Xunit;
using Xunit.Abstractions;

namespace LastEdict.Data.Tests;

/// <summary>Опись роликов: <c>data/video/videos.json</c> лежит в git, сами <c>.ogv</c> — нет.
/// Числа сверены переводом роликов GOG 3.01 (<c>tools/video-import/build_videos.py</c>).</summary>
public class UiVideosTests(ITestOutputHelper output)
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
            ImportedData.RequireIn(Path.Combine(RepoRoot, "data"), "video/videos.json");
            return Path.Combine(RepoRoot, "data", "video", "videos.json");
        }
    }
    /// <summary>Ролики: <c>D2_ASSETS_DIR/video</c> (рабочие копии <c>../dg-wt-*</c>), иначе <c>assets/video</c> репозитория.</summary>
    private static string VideoRoot => Environment.GetEnvironmentVariable("D2_ASSETS_DIR") is { Length: > 0 } assets
        ? Path.Combine(assets, "video") : Path.Combine(RepoRoot, "assets", "video");

    /// <summary>Опись читается, ролики все на месте, имена уникальны во всех источниках.</summary>
    [SkippableFact]
    public void Manifest_ParsesWithCountPerSource()
    {
        var videos = UiVideos.Load(ManifestPath);
        var bySource = videos.GroupBy(v => v.Source).ToDictionary(g => g.Key, g => g.Count());
        output.WriteLine($"роликов {videos.Count}: " + string.Join(", ", bySource.Select(s => $"{s.Key}={s.Value}")));

        Assert.Equal(110, videos.Count);
        Assert.Equal(52, bySource["MenuAnim"]);
        Assert.Equal(50, bySource["Briefing"]);   // 42 ролика и 8 дорожек озвучки кампании эльфов
        Assert.Equal(8, bySource["Video"]);
        Assert.Equal(8, videos.Count(v => !v.HasVideo));

        Assert.Empty(videos.GroupBy(v => v.Name, StringComparer.OrdinalIgnoreCase)
                           .Where(g => g.Count() > 1).Select(g => g.Key));
        Assert.All(videos, v =>
        {
            Assert.Equal(v.Name.ToUpperInvariant(), v.Name);
            Assert.Matches("^[0-9a-f]{16}$", v.Sha256);
            Assert.StartsWith(v.Source + "/", v.File);
            Assert.True(v.Duration > 0, $"{v.Name}: длительность {v.Duration}");
            if (v.HasVideo)
            {
                Assert.EndsWith(".ogv", v.File);
                Assert.True(v.Frames > 0 && v.Fps > 0, $"{v.Name}: кадров {v.Frames}, {v.Fps}/с");
                Assert.Equal(v.Frames / v.Fps, v.Duration, 0.001);
            }
            else
            {
                Assert.EndsWith(".ogg", v.File);
                Assert.True(v.Audio);
            }
        });
    }

    /// <summary>Ролики, на которые ссылаются экраны меню: переход главное→одиночная игра, смена расы на выборе
    /// бога, лицо лорда, переход к выбору лорда.</summary>
    [SkippableFact]
    public void KnownMenuClips_HaveOriginalSizeAndLength()
    {
        var byName = UiVideos.ByName(UiVideos.Load(ManifestPath));

        var main = byName["trans_main2single"];  // регистр не важен
        Assert.Equal(("MenuAnim", "MenuAnim/TRANS_MAIN2SINGLE.ogv"), (main.Source, main.File));
        Assert.Equal((800, 600, 15.0, 15, 1.0), (main.W, main.H, main.Fps, main.Frames, main.Duration));
        Assert.False(main.Audio);

        var god = byName["GOD_HU2UN"];
        Assert.Equal((800, 600, 30), (god.W, god.H, god.Frames));

        var face = byName["FACE_HU_0"];
        Assert.Equal((197, 347, 12), (face.W, face.H, face.Frames));

        var lord = byName["TRANS_GOD2LORD_HU"];
        Assert.Equal((800, 600, 31), (lord.W, lord.H, lord.Frames));

        var intro = byName["INTRO"];
        Assert.Equal(("Video", 640, 360, 942, true), (intro.Source, intro.W, intro.H, intro.Frames, intro.Audio));

        var voice = byName["BRIEFEL1"];
        Assert.Equal(("Briefing/BRIEFEL1.ogg", false, true), (voice.File, voice.HasVideo, voice.Audio));

        Assert.Equal(15.002, byName["NOIR"].Fps);
    }

    /// <summary>Мод заменяет ролик по имени без учёта регистра и добавляет свои.</summary>
    [SkippableFact]
    public void Overlay_ReplacesByName()
    {
        var videos = UiVideos.Load(ManifestPath);
        var mod = new List<UiVideo>
        {
            new() { Name = "intro", Source = "mymod", File = "mymod/INTRO.ogv", W = 1920, H = 1080, Fps = 30,
                    Frames = 60, Duration = 2, Sha256 = "0123456789abcdef" },
            new() { Name = "MOD_ONLY", Source = "mymod", File = "mymod/X.ogv", W = 1, H = 1, Fps = 15,
                    Frames = 1, Duration = 0.067, Sha256 = "fedcba9876543210" },
        };
        var merged = UiVideos.Merge(videos, mod);
        Assert.Equal(videos.Count + 1, merged.Count);
        Assert.Equal(1920, UiVideos.ByName(merged)["INTRO"].W);
        Assert.Equal("MOD_ONLY", merged[^1].Name);
        Assert.Equal(Path.Combine("root", "mymod", "X.ogv"), UiVideos.PathOf(merged[^1], "root"));
    }

    /// <summary>Файлы на диске совпадают с описью по заголовку Ogg: у <c>.ogv</c> первый пакет — заголовок Theora
    /// с размером картинки и частотой кадров, у <c>.ogg</c> — заголовок Vorbis. Без <c>assets/video</c>
    /// (в git его нет) проверять нечего.</summary>
    [SkippableFact]
    public void Files_OnDisk_MatchManifest()
    {
        Skip.IfNot(Directory.Exists(VideoRoot), $"нет перевода роликов: {VideoRoot}");
        var videos = UiVideos.Load(ManifestPath);
        foreach (var v in videos)
        {
            var path = UiVideos.PathOf(v, VideoRoot);
            Assert.True(File.Exists(path), $"{v.Name}: нет файла {path}");
            var packet = FirstOggPacket(path);
            if (!v.HasVideo)
            {
                Assert.Equal("vorbis", System.Text.Encoding.Latin1.GetString(packet, 0, 7));
                continue;
            }
            Assert.Equal("theora", System.Text.Encoding.Latin1.GetString(packet, 0, 7));
            // Заголовок Theora: версия (3), размер в макроблоках (2+2), затем картинка PICW/PICH по 24 бита,
            // смещение PICX/PICY по байту, частота FRN/FRD по 32 бита — всё big-endian.
            Assert.Equal((v.W, v.H), (U24(packet, 14), U24(packet, 17)));
            var fps = (double)U32(packet, 22) / U32(packet, 26);
            Assert.Equal(v.Fps, fps, 0.001);
        }
        output.WriteLine($"сверено файлов {videos.Count}");
    }

    /// <summary>Первый пакет первой страницы Ogg: 27 байт заголовка страницы, таблица сегментов, дальше данные.</summary>
    private static byte[] FirstOggPacket(string path)
    {
        using var f = File.OpenRead(path);
        var head = new byte[27 + 255];
        var n = f.Read(head, 0, head.Length);
        Assert.True(n >= 27, $"{path}: короткий файл");
        Assert.Equal("OggS", System.Text.Encoding.ASCII.GetString(head, 0, 4));
        int segs = head[26];
        var len = 0;
        for (var i = 0; i < segs; i++)
        {
            len += head[27 + i];
            if (head[27 + i] < 255) break;
        }
        var packet = new byte[len];
        f.Seek(27 + segs, SeekOrigin.Begin);
        f.ReadExactly(packet);
        return packet;
    }

    private static int U24(byte[] b, int i) => (b[i] << 16) | (b[i + 1] << 8) | b[i + 2];
    private static uint U32(byte[] b, int i) => (uint)((b[i] << 24) | (b[i + 1] << 16) | (b[i + 2] << 8) | b[i + 3]);
}
