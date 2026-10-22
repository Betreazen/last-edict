using System.Text.Json;
using LastEdict.Data.Play;

namespace LastEdict.Data.Leaders;

/// <summary>Герой в формате порта: JSON <c>d2-port-leader</c> с версией (<c>&lt;каталог героев&gt;/&lt;имя&gt;.json</c>).</summary>
public static class LeaderFile
{
    public const string Format = "d2-port-leader";

    /// <summary>Версия формата. Меняется, когда старый файл нельзя прочитать новой моделью как есть.</summary>
    public const int Version = 1;

    public const string Extension = ".json";

    private sealed record Envelope(string Format, int Version, ExportedLeader Leader);

    public static string ToJson(ExportedLeader leader) => JsonSerializer.Serialize(new Envelope(Format, Version, leader), SaveJson.Options);

    /// <exception cref="InvalidDataException">Не герой порта или версия новее, чем умеет сборка.</exception>
    public static ExportedLeader FromJson(string json)
    {
        Envelope? e;
        try
        {
            e = JsonSerializer.Deserialize<Envelope>(json, SaveJson.Options);
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException($"герой не читается: {ex.Message}", ex);
        }
        if (e is null || e.Format != Format || e.Leader is null) throw new InvalidDataException("не герой порта");
        if (e.Version > Version) throw new InvalidDataException($"герой версии {e.Version}, сборка знает до {Version}");
        return e.Leader;
    }

    public static void Write(string path, ExportedLeader leader)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        string temp = path + ".tmp";
        File.WriteAllText(temp, ToJson(leader));
        File.Move(temp, path, overwrite: true);
    }

    public static ExportedLeader Read(string path) => FromJson(File.ReadAllText(path));
}

/// <summary>Герой в каталоге героев: файл и запись.</summary>
public sealed record StoredLeader(string Path, ExportedLeader Leader)
{
    /// <summary>Имя файла без расширения — так окно импорта называет героя (<c>0x409c67</c>: <c>_splitpath</c>).</summary>
    public string FileName => System.IO.Path.GetFileNameWithoutExtension(Path);
}

/// <summary>
/// Каталог героев игрока (<c>user://leaders</c>): герои порта (<see cref="LeaderFile"/>). Герои игры (<c>Exports\*.ldr</c>)
/// попадают сюда импортом (<see cref="Import"/>), в рантайме порт от <c>.ldr</c> не зависит.
/// </summary>
public static class LeaderStore
{
    /// <summary>Герои каталога, как в окне импорта (<c>0x4c70d1</c>): по расе — Империя, Нежить, Легионы, Кланы, Эльфы, прочие,
    /// затем по имени файла. Нечитаемый файл пропускается.</summary>
    public static IReadOnlyList<StoredLeader> List(string dir)
    {
        if (!Directory.Exists(dir)) return [];
        var leaders = new List<StoredLeader>();
        foreach (string path in Directory.GetFiles(dir, "*" + LeaderFile.Extension))
        {
            try
            {
                leaders.Add(new StoredLeader(path, LeaderFile.Read(path)));
            }
            catch (Exception e) when (e is InvalidDataException or IOException)
            {
                // Чужой или битый файл — не герой порта.
            }
        }
        return [.. leaders.OrderBy(l => RaceOrder(l.Leader.Race)).ThenBy(l => l.FileName, StringComparer.OrdinalIgnoreCase)];
    }

    /// <summary>Порядок рас окна импорта: Империя (0), Нежить (1), Легионы (2), Кланы (3), Эльфы (5), прочие.</summary>
    private static int RaceOrder(int race) => race switch { 0 => 0, 1 => 1, 2 => 2, 3 => 3, 5 => 4, _ => 5 };

    /// <summary>Герои игры из <paramref name="exportsDir"/> (<c>*.ldr</c>) — в каталог порта под тем же именем; уже переведённые и
    /// нечитаемые пропускаются. Возвращает число переведённых.</summary>
    public static int Import(string exportsDir, string dir)
    {
        if (!Directory.Exists(exportsDir)) return 0;
        int count = 0;
        foreach (string ldr in Directory.GetFiles(exportsDir, "*" + LdrFile.Extension))
        {
            string name = Path.GetFileNameWithoutExtension(ldr);
            string target = Path.Combine(dir, (name.Length == 0 ? "_" : name) + LeaderFile.Extension);
            if (File.Exists(target)) continue;
            try
            {
                LeaderFile.Write(target, LdrFile.Read(File.ReadAllBytes(ldr)));
                count++;
            }
            catch (Exception e) when (e is InvalidDataException or IOException)
            {
                // Битый файл игры — не переводится.
            }
        }
        return count;
    }

    /// <summary>Вывезти героя в каталог (ОК окна вывоза, <c>0x4081bc</c>): файл — имя героя как есть; занято — с номером
    /// (<c>имя2</c>, <c>имя3</c>…; пустое имя — <c>.json</c>, <c>2.json</c>, как <c>.ldr</c> движка). Знаки, запрещённые в именах
    /// файлов, движок не чистит — запись не удаётся; здесь так же. Возвращает путь.</summary>
    /// <exception cref="IOException">Файл не записался или имя героя не годится в имя файла.</exception>
    public static string Save(string dir, ExportedLeader leader)
    {
        ArgumentNullException.ThrowIfNull(leader);
        if (leader.Name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0) throw new IOException($"имя героя не годится в имя файла: {leader.Name}");
        string path = Path.Combine(dir, leader.Name + LeaderFile.Extension);
        for (int n = 2; File.Exists(path); n++) path = Path.Combine(dir, $"{leader.Name}{n}{LeaderFile.Extension}");
        LeaderFile.Write(path, leader);
        return path;
    }

    /// <summary>Удалить героя из каталога (<c>BTN_DELETE</c> окна импорта).</summary>
    public static void Delete(StoredLeader leader)
    {
        ArgumentNullException.ThrowIfNull(leader);
        if (File.Exists(leader.Path)) File.Delete(leader.Path);
    }
}
