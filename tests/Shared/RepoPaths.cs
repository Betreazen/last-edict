namespace LastEdict.Tests;

/// <summary>Корень репозитория для тестов — ближайший вверх от сборки каталог с <c>global.json</c> (не счётом «..» от <c>bin</c>: глубина
/// каталога тестов в эталоне и в Last Edict разная).</summary>
internal static class RepoPaths
{
    public static string Root { get; } = Find();

    private static string Find()
    {
        for (var d = new DirectoryInfo(AppContext.BaseDirectory); d is not null; d = d.Parent)
            if (File.Exists(Path.Combine(d.FullName, "global.json"))) return d.FullName;
        throw new DirectoryNotFoundException($"нет global.json выше {AppContext.BaseDirectory}");
    }
}
