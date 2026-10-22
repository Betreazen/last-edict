using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.ExceptionServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace LastEdict.Data.Import;

/// <summary>Ролики игры (Bink <c>.BIK</c>) → Ogg Theora через внешний ffmpeg (перенос <c>tools/video-import/build_videos.py</c>; опись — те же
/// байты): 52 записи <c>Interf/MenuAnim.ff</c> → <c>assets/video/MenuAnim/&lt;ИМЯ&gt;.ogv</c>, <c>Briefing/*.bik</c> → <c>Briefing/&lt;ИМЯ&gt;.ogv</c>,
/// <c>Briefing/briefel*.wav</c> (озвучка брифингов эльфов, звук без картинки) → <c>Briefing/&lt;ИМЯ&gt;.ogg</c>, <c>Video/*.bik</c> →
/// <c>Video/&lt;ИМЯ&gt;.ogv</c>, опись <c>data/video/videos.json</c>. Все числа описи — из ИСХОДНИКА (ffprobe по .BIK), а не из результата:
/// опись воспроизводится байт в байт при любой версии ffmpeg. Нет ffmpeg/ffprobe или кодировщик Theora не прошёл самопроверку — шаг
/// пропускается (игра работает без роликов): сборка ffmpeg 8.1 (gyan.dev) пишет межкадровые блоки Theora так, что их не читает ни декодер
/// ffmpeg, ни Godot.</summary>
public static class VideoImport
{
    public const string Step = "video";
    private const int ShaLength = 16, SelfTestFrames = 30;
    // Качество Theora 0..10: 8 — без заметных на глаз потерь (сверка с Bink — PSNR ≥ 35 дБ на кадр в Godot); Vorbis 6 ≈ 192 кбит/с.
    private const string VideoQ = "8", AudioQ = "6";
    private static readonly string[] Sources = ["MenuAnim", "Briefing", "Video"];

    /// <summary>Ролик к сборке: имя (ВЕРХНИЙ регистр, без расширения), источник, исходный файл, звук ли без картинки.</summary>
    internal sealed record Job(string Name, string Source, string Src, bool IsAudio)
    {
        public string Rel => $"{Source}/{Name}.{(IsAudio ? "ogg" : "ogv")}";
    }

    /// <summary>Перекодировать ролики и записать опись. <c>true</c> — сделано, <c>false</c> — пропущено (нет ffmpeg/ffprobe или кодировщик негоден).</summary>
    public static bool Run(ImportContext ctx) => Run(ctx, manifestOnly: false);

    /// <summary>То же; <paramref name="manifestOnly"/> — только опись (ffprobe и хеши исходников, без кодирования и без <c>assets/video</c>).</summary>
    /// <exception cref="FileNotFoundException">Нет <c>Interf/MenuAnim.ff</c>, <c>Briefing</c> или <c>Video</c> в копии игры.</exception>
    /// <exception cref="InvalidOperationException">ffprobe или ffmpeg не разобрал исходник.</exception>
    public static bool Run(ImportContext ctx, bool manifestOnly)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        var missing = new[] { "Interf/MenuAnim.ff", "Briefing", "Video" }.Where(p => !Path.Exists(ctx.Game(p))).ToList();
        if (missing.Count > 0) throw new FileNotFoundException($"нет в {ctx.GameDir}: {string.Join(", ", missing)}");
        string ffmpeg = ctx.Ffmpeg ?? "ffmpeg", ffprobe = ctx.Ffprobe ?? "ffprobe";
        var clock = Stopwatch.StartNew();
        string tmp = Path.Combine(Path.GetTempPath(), "d2-video-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tmp);
        try
        {
            string? why = Unusable(ffprobe, "ffprobe") ?? (manifestOnly ? null : Unusable(ffmpeg, "ffmpeg") ?? CheckEncoder(ffmpeg, tmp, ctx.Cancel));
            if (why is not null)
            {
                ctx.Say($"ролики пропущены: {why}");
                return false;
            }
            var jobs = Collect(ctx.GameDir, tmp);
            int dup = jobs.Count - jobs.Select(j => (j.Source, j.Name)).Distinct().Count();
            if (dup > 0) throw new InvalidDataException($"{dup} повторов пары источник+имя в роликах");
            string final = Path.Combine(ctx.AssetsDir, "video");
            string? built = null;
            if (!manifestOnly)
            {
                ImportFiles.Recover(final);
                built = ImportFiles.Fresh(final + ".new");
            }
            var rows = new PyObj[jobs.Count];
            int done = 0;
            ctx.Report(Step, 0, jobs.Count);
            // libtheora однопоточный — ролики кодируются параллельно, по одному на поток.
            try
            {
                Parallel.For(0, jobs.Count, new ParallelOptions { MaxDegreeOfParallelism = ctx.Threads, CancellationToken = ctx.Cancel }, i =>
                {
                    var job = jobs[i];
                    var meta = Probe(ffprobe, job, ctx.Cancel);
                    if (built is not null) Encode(ffmpeg, job, (bool)meta["audio"]!, Path.Combine(built, job.Rel), ctx.Cancel);
                    var row = new PyObj { ["name"] = job.Name, ["source"] = job.Source, ["file"] = job.Rel };
                    foreach (var (k, v) in meta) row[k] = v;
                    row["sha256"] = Sha(job.Src);
                    rows[i] = row;
                    ctx.Report(Step, Interlocked.Increment(ref done), jobs.Count);
                });
            }
            catch (AggregateException e) when (e.InnerExceptions.Count > 0)
            {
                ExceptionDispatchInfo.Capture(e.InnerExceptions[0]).Throw();
            }
            var sorted = rows.OrderBy(r => (string)r["source"]!, StringComparer.Ordinal).ThenBy(r => (string)r["name"]!, StringComparer.Ordinal).ToList();
            if (built is not null)
            {
                foreach (string source in Sources)
                {
                    var part = sorted.Where(r => (string)r["source"]! == source).ToList();
                    long size = part.Sum(r => new FileInfo(Path.Combine(built, (string)r["file"]!)).Length);
                    ctx.Say(string.Create(CultureInfo.InvariantCulture, $"{source}: роликов {part.Count}, {size / 1048576.0:0.0} МиБ"));
                }
                ImportFiles.Swap(built, final);
            }
            // По записи на строку: обычный JSON, но читаемый diff в git.
            ImportFiles.WriteText(Path.Combine(ctx.DataDir, "video", "videos.json"), "[\n" + string.Join(",\n", sorted.Select(PyJson.Compact)) + "\n]\n");
            ctx.Say(string.Create(CultureInfo.InvariantCulture, $"data/video/videos.json: {sorted.Count} записей, {clock.Elapsed.TotalSeconds:0} с"));
            return true;
        }
        finally
        {
            try
            {
                Directory.Delete(tmp, recursive: true);
            }
            catch (IOException e)
            {
                ctx.Say($"не удалён временный каталог {tmp}: {e.Message}");
            }
            catch (UnauthorizedAccessException e)
            {
                ctx.Say($"не удалён временный каталог {tmp}: {e.Message}");
            }
        }
    }

    /// <summary>Все ролики трёх источников. Записи <c>MenuAnim.ff</c> выкладываются в <paramref name="tmp"/>: ffmpeg читает файл.</summary>
    internal static List<Job> Collect(string gameDir, string tmp)
    {
        var jobs = new List<Job>();
        using (var ff = new FfArchive(Path.Combine(gameDir, "Interf", "MenuAnim.ff")))
        {
            string dir = Path.Combine(tmp, "MenuAnim");
            Directory.CreateDirectory(dir);
            foreach (var e in ff.Entries)
            {
                var (stem, ext) = SplitExt(e.Name);
                if (!string.Equals(ext, ".BIK", StringComparison.OrdinalIgnoreCase)) continue;
                string raw = Path.Combine(dir, e.Name);
                File.WriteAllBytes(raw, ff.Raw(e));
                jobs.Add(new Job(PyCompat.Upper(stem), "MenuAnim", raw, false));
            }
        }
        foreach (string source in Sources[1..])
            foreach (string p in Directory.GetFiles(Path.Combine(gameDir, source)).Order(StringComparer.OrdinalIgnoreCase))
            {
                string ext = Path.GetExtension(p).ToLowerInvariant();
                if (ext is ".bik" or ".wav") jobs.Add(new Job(PyCompat.Upper(Path.GetFileNameWithoutExtension(p)), source, p, ext == ".wav"));
            }
        return jobs;
    }

    /// <summary><c>os.path.splitext</c>: расширение — с последней точки, если до неё есть не только точки.</summary>
    private static (string Stem, string Ext) SplitExt(string name)
    {
        int dot = name.LastIndexOf('.');
        int lead = 0;
        while (lead < name.Length && name[lead] == '.') lead++;
        return dot > lead ? (name[..dot], name[dot..]) : (name, "");
    }

    private static string Sha(string path)
    {
        using var file = File.OpenRead(path);
        return Convert.ToHexStringLower(SHA256.HashData(file))[..ShaLength];
    }

    /// <summary>Размер, частота кадров, число кадров, длительность, звук — из исходника.</summary>
    internal static PyObj Probe(string ffprobe, Job job, CancellationToken cancel)
    {
        ArgumentNullException.ThrowIfNull(job);
        string name = Path.GetFileName(job.Src);
        if (job.IsAudio)
        {
            // Внутри .wav — MP3, и длительность из заголовка (по средней скорости) короче на ≈0.5 с. Точная — сумма длительностей пакетов в
            // единицах времени потока.
            var a = Exec(ffprobe, ["-v", "error", "-select_streams", "a:0", "-show_entries", "stream=time_base:packet=duration", "-of", "json", job.Src], cancel);
            if (a.Code != 0) throw new InvalidOperationException($"{name}: ffprobe: {Clip(a.Err, 200)}");
            using var doc = JsonDocument.Parse(a.Out);
            long ticks = doc.RootElement.GetProperty("packets").EnumerateArray().Sum(p => Long(p.GetProperty("duration")));
            var (num, den) = Ratio(doc.RootElement.GetProperty("streams")[0].GetProperty("time_base").GetString(), name);
            return new PyObj { ["w"] = 0, ["h"] = 0, ["fps"] = 0, ["frames"] = 0, ["duration"] = PyCompat.Round(Div(ticks * num, den), 3), ["audio"] = true };
        }
        var r = Exec(ffprobe, ["-v", "error", "-count_packets", "-show_entries", "stream=codec_type,width,height,r_frame_rate,nb_read_packets",
                               "-of", "json", job.Src], cancel);
        if (r.Code != 0) throw new InvalidOperationException($"{name}: ffprobe: {Clip(r.Err, 200)}");
        using var json = JsonDocument.Parse(r.Out);
        var streams = json.RootElement.GetProperty("streams").EnumerateArray().ToList();
        var v = streams.FirstOrDefault(s => s.GetProperty("codec_type").GetString() == "video");
        if (v.ValueKind != JsonValueKind.Object) throw new InvalidOperationException($"{name}: ffprobe не нашёл видеопотока");
        var (rn, rd) = Ratio(v.GetProperty("r_frame_rate").GetString(), name);
        // Кадр Bink — пакет. В .ogv неизменный кадр пишется пустым пакетом (повтор предыдущего), поэтому сверять результат надо тоже по
        // пакетам: декодер ffmpeg пустые пакеты кадрами не считает.
        long frames = Long(v.GetProperty("nb_read_packets"));
        double fps = PyCompat.Round(Div(rn, rd), 3);
        return new PyObj
        {
            ["w"] = (int)Long(v.GetProperty("width")), ["h"] = (int)Long(v.GetProperty("height")),
            ["fps"] = fps == Math.Truncate(fps) ? (object)(long)fps : fps,
            ["frames"] = frames, ["duration"] = PyCompat.Round(Div(frames * rd, rn), 3),
            ["audio"] = streams.Any(s => s.GetProperty("codec_type").GetString() == "audio"),
        };
    }

    /// <summary>Перекодировать один ролик в <paramref name="output"/>.</summary>
    internal static void Encode(string ffmpeg, Job job, bool hasAudio, string output, CancellationToken cancel)
    {
        ArgumentNullException.ThrowIfNull(job);
        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        List<string> codec = job.IsAudio
            ? ["-map", "0:a:0", "-c:a", "libvorbis", "-q:a", AudioQ]
            : ["-map", "0:v:0", "-c:v", "libtheora", "-q:v", VideoQ, .. hasAudio ? new[] { "-map", "0:a:0", "-c:a", "libvorbis", "-q:a", AudioQ } : []];
        var r = Exec(ffmpeg, ["-v", "error", "-y", "-i", job.Src, .. codec, "-map_metadata", "-1", output], cancel);
        if (r.Code != 0 || !File.Exists(output)) throw new InvalidOperationException($"{job.Rel}: ffmpeg: {Clip(r.Err, 300)}");
    }

    /// <summary>Пробный ролик с движением: кодируем в Theora и читаем обратно; любая ошибка декодера — кодировщик негоден (текст причины).</summary>
    internal static string? CheckEncoder(string ffmpeg, string tmp, CancellationToken cancel)
    {
        string probe = Path.Combine(tmp, "selftest.ogv");
        string duration = PyJson.Float(SelfTestFrames / 15.0);
        var enc = Exec(ffmpeg, ["-v", "error", "-y", "-f", "lavfi", "-i", $"testsrc=size=320x240:rate=15:duration={duration}",
                                "-c:v", "libtheora", "-q:v", VideoQ, probe], cancel);
        if (enc.Code != 0) return $"ffmpeg не кодирует Theora: {Clip(enc.Err, 300)}";
        var dec = Exec(ffmpeg, ["-v", "error", "-i", probe, "-f", "null", "-"], cancel);
        string err = dec.Err.Trim();
        if (dec.Code != 0 || err.Length > 0)
            return $"кодировщик libtheora этого ffmpeg пишет нечитаемые межкадровые блоки ({Clip(err.Split('\n')[0], 120)}); укажите другой ffmpeg";
        return null;
    }

    /// <summary>Причина, по которой программу не запустить (нет файла, нет в PATH), иначе <c>null</c>.</summary>
    private static string? Unusable(string exe, string what)
    {
        try
        {
            var r = Exec(exe, ["-version"], CancellationToken.None);
            return r.Code == 0 ? null : $"{what} ({exe}) завершился с кодом {r.Code}";
        }
        catch (Win32Exception e)
        {
            return $"нет {what} ({exe}): {e.Message}";
        }
    }

    private readonly record struct Result(int Code, string Out, string Err);

    /// <summary>Запустить программу и дождаться; вывод — UTF-8, неразборные байты — U+FFFD (как <c>errors="replace"</c>).</summary>
    private static Result Exec(string exe, IReadOnlyList<string> args, CancellationToken cancel)
    {
        var info = new ProcessStartInfo(exe)
        {
            RedirectStandardOutput = true, RedirectStandardError = true, RedirectStandardInput = true, UseShellExecute = false,
            CreateNoWindow = true, StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8,
        };
        foreach (string a in args) info.ArgumentList.Add(a);
        using var p = Process.Start(info) ?? throw new InvalidOperationException($"не запустился {exe}");
        p.StandardInput.Close();
        var stdout = p.StandardOutput.ReadToEndAsync(cancel);
        var stderr = p.StandardError.ReadToEndAsync(cancel);
        try
        {
            p.WaitForExitAsync(cancel).GetAwaiter().GetResult();
        }
        catch (OperationCanceledException)
        {
            try
            {
                p.Kill(entireProcessTree: true);
            }
            catch (InvalidOperationException)
            {
                // уже завершился
            }
            throw;
        }
        return new Result(p.ExitCode, stdout.GetAwaiter().GetResult(), stderr.GetAwaiter().GetResult());
    }

    private static string Clip(string s, int n)
    {
        s = s.Trim();
        return s.Length <= n ? s : s[..n];
    }

    /// <summary>Число из JSON ffprobe: оно бывает и числом, и строкой (<c>"nb_read_packets": "52"</c>).</summary>
    private static long Long(JsonElement e) =>
        e.ValueKind == JsonValueKind.Number ? e.GetInt64() : long.Parse(e.GetString() ?? "", NumberStyles.Integer, CultureInfo.InvariantCulture);

    /// <summary>Дробь «a/b» ffprobe (частота кадров, единица времени).</summary>
    private static (long Num, long Den) Ratio(string? text, string name)
    {
        string[] parts = (text ?? "").Split('/');
        if (parts.Length == 2 && long.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out long a)
            && long.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out long b) && a != 0 && b != 0)
            return (a, b);
        throw new InvalidOperationException($"{name}: ffprobe дал дробь «{text}»");
    }

    /// <summary><c>float(Fraction(a, b))</c>: частное двух целых, округлённое один раз (оба точны в double до 2^53).</summary>
    private static double Div(long a, long b) => (double)a / b;
}
