using System.Text;

namespace LastEdict.Data.Import;

/// <summary>Что и куда импортировать: копия игры, слой русификации (необязательно), корень выхода и кодек картинок. Раскладка выхода — как у
/// репозитория: <c>&lt;Out&gt;/data/**</c> (данные порта, выведенные из игры) и <c>&lt;Out&gt;/assets/**</c> (графика, звук, ролики, шрифты),
/// так что разработчик импортирует прямо в репозиторий, а игра — в каталог пользователя (<c>docs/publishing.md</c>).</summary>
public sealed class ImportContext
{
    public ImportContext(string gameDir, string outRoot, IImageEncoder encoder)
    {
        ArgumentException.ThrowIfNullOrEmpty(gameDir);
        ArgumentException.ThrowIfNullOrEmpty(outRoot);
        ArgumentNullException.ThrowIfNull(encoder);
        GameDir = Path.GetFullPath(gameDir);
        OutRoot = Path.GetFullPath(outRoot);
        Encoder = encoder;
    }

    /// <summary>Корень копии игры (там <c>Globals</c>, <c>Interf</c>, <c>Imgs</c>, <c>Exports</c>).</summary>
    public string GameDir { get; }

    /// <summary>Слой русификации (корень с <c>Globals</c>, <c>Interf</c>, <c>Exports</c>, <c>Scens</c> русской копии игры) или <c>null</c>.</summary>
    public string? L10nDir { get; init; }

    public string OutRoot { get; }

    public IImageEncoder Encoder { get; }

    /// <summary>ffmpeg для роликов (Bink → Ogg Theora); <c>null</c> — из <c>PATH</c>, а нет и там — ролики пропускаются.</summary>
    public string? Ffmpeg { get; init; }

    public string? Ffprobe { get; init; }

    /// <summary>Разметка экранов, которой может не быть в копии игры (<c>CustomLobby.dlg</c> набора D2ModdingToolset, GPL-3: лежит в
    /// <c>tools/d2probe/Examples</c> и в сборке игры — <c>data/import</c>).</summary>
    public IReadOnlyList<string> ExtraDlgs { get; init; } = [];

    /// <summary>Потоков сборки картинок.</summary>
    public int Threads { get; init; } = Math.Clamp(Environment.ProcessorCount, 1, 16);

    public IProgress<ImportProgress>? Progress { get; init; }

    public CancellationToken Cancel { get; init; }

    /// <summary>Журнал импорта (строки как у прежних сборщиков: что сколько).</summary>
    public TextWriter Log { get; init; } = TextWriter.Null;

    public string DataDir => Path.Combine(OutRoot, "data");

    public string AssetsDir => Path.Combine(OutRoot, "assets");

    /// <summary>Путь в копии игры.</summary>
    public string Game(params string[] parts) => Path.Combine([GameDir, .. parts]);

    /// <summary>Путь в слое русификации или <c>null</c>.</summary>
    public string? L10n(params string[] parts) => L10nDir is null ? null : Path.Combine([L10nDir, .. parts]);

    public void Report(string step, int done, int total, string? note = null) => Progress?.Report(new ImportProgress(step, done, total, note));

    public void Say(string line)
    {
        lock (Log) Log.WriteLine(line);
    }
}

/// <summary>Ход импорта: шаг, сделано из всего, пояснение.</summary>
public readonly record struct ImportProgress(string Step, int Done, int Total, string? Note);

/// <summary>Запись файлов импорта: целиком через временный файл рядом (недописанной описи не бывает), концы строк — LF, UTF-8 без BOM.</summary>
public static class ImportFiles
{
    private static readonly UTF8Encoding Utf8 = new(false);

    public static void WriteText(string path, string text)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        ArgumentNullException.ThrowIfNull(text);
        WriteBytes(path, Utf8.GetBytes(text.ReplaceLineEndings("\n")));
    }

    public static void WriteBytes(string path, byte[] data)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        ArgumentNullException.ThrowIfNull(data);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string tmp = path + ".tmp";
        File.WriteAllBytes(tmp, data);
        File.Move(tmp, path, overwrite: true);
    }

    /// <summary>Пустой каталог сборки (остаток прерванной сносится).</summary>
    public static string Fresh(string dir)
    {
        if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        Directory.CreateDirectory(dir);
        return dir;
    }

    /// <summary>Сборку прервали между двумя переименованиями <see cref="Swap"/> — вернуть прежний каталог.</summary>
    public static void Recover(string final)
    {
        string old = final + ".old";
        if (!Directory.Exists(final) && Directory.Exists(old)) Rename(old, final);
    }

    /// <summary>Поставить готовый каталог на место прежнего: два переименования (в Windows занятый каталог одним вызовом не заменить),
    /// занятый файл — повтор до ~60 с.</summary>
    public static void Swap(string built, string final)
    {
        string old = final + ".old";
        if (Directory.Exists(old)) Directory.Delete(old, recursive: true);
        if (Directory.Exists(final)) Rename(final, old);
        Rename(built, final);
        if (Directory.Exists(old)) Directory.Delete(old, recursive: true);
    }

    private static void Rename(string from, string to)
    {
        for (int attempt = 0; ; attempt++)
        {
            try
            {
                Directory.Move(from, to);
                return;
            }
            catch (IOException e) when (attempt < 120 && e is not (FileNotFoundException or DirectoryNotFoundException or PathTooLongException))
            {
                Thread.Sleep(500);
            }
            catch (UnauthorizedAccessException) when (attempt < 120)
            {
                Thread.Sleep(500);
            }
        }
    }
}
