using Microsoft.Win32;

namespace LastEdict.Data.Import;

/// <summary>Где искать копию Disciples II для импорта без вопросов: установка GOG (реестр <c>GOG.com\Games</c>: id 1207663713 —
/// «Disciples II: Rise of the Elves» 3.01, иначе любая игра с «Disciples II» в имени) и обычные папки GOG. Годность папки проверяет
/// <see cref="GameImporter.CheckGameDir"/>.</summary>
public static class GameDirFinder
{
    public const string GogId = "1207663713";

    /// <summary>Папки-кандидаты по порядку (без проверки годности, без повторов).</summary>
    public static IReadOnlyList<string> Candidates()
    {
        var found = new List<string>();
        if (OperatingSystem.IsWindows())
        {
            foreach (var view in new[] { RegistryView.Registry32, RegistryView.Registry64 })
            {
                try
                {
                    using var hive = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view);
                    using var games = hive.OpenSubKey(@"SOFTWARE\GOG.com\Games");
                    if (games is null) continue;
                    var ids = games.GetSubKeyNames().OrderBy(id => id == GogId ? 0 : 1).ToList();
                    foreach (string id in ids)
                    {
                        using var game = games.OpenSubKey(id);
                        if (game?.GetValue("path") is not string path || string.IsNullOrWhiteSpace(path)) continue;
                        string name = game.GetValue("gameName") as string ?? "";
                        if (id == GogId || name.Contains("Disciples II", StringComparison.OrdinalIgnoreCase)) found.Add(path);
                    }
                }
                catch (Exception e) when (e is System.Security.SecurityException or UnauthorizedAccessException or IOException)
                {
                    // реестр недоступен — остаются обычные папки
                }
            }
        }
        foreach (string root in new[] { @"C:\GOG Games", @"C:\Program Files (x86)\GOG Galaxy\Games", @"C:\Program Files\GOG Galaxy\Games", @"D:\GOG Games" })
        {
            try
            {
                if (Directory.Exists(root)) found.AddRange(Directory.GetDirectories(root, "Disciples II*"));
            }
            catch (Exception e) when (e is UnauthorizedAccessException or IOException)
            {
                // нет доступа — дальше
            }
        }
        var full = new List<string>();
        foreach (string dir in found)
        {
            try
            {
                full.Add(Path.GetFullPath(dir));
            }
            catch (Exception e) when (e is ArgumentException or NotSupportedException or PathTooLongException)
            {
                // негодный путь в реестре — мимо
            }
        }
        return [.. full.Distinct(StringComparer.OrdinalIgnoreCase)];
    }

    /// <summary>Первая годная папка игры среди кандидатов или <c>null</c>.</summary>
    public static string? Find() => Candidates().FirstOrDefault(d => GameImporter.CheckGameDir(d) is null);
}
