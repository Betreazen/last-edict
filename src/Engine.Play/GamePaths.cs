namespace D2.Data;

/// <summary>Копия игры (только для импорта и проверок): переменная <c>D2_GAME_DIR</c>, иначе соседний <c>disciples-modding/game-pristine</c>.
/// Игра после импорта знает свою копию из отметки импорта и ставит её <see cref="Use"/> (герои <c>Exports/*.ldr</c> и кампании
/// <c>Campaign/*.csg</c> меню берутся оттуда).</summary>
public static class GamePaths
{
    public static string GameDir { get; private set; } =
        Environment.GetEnvironmentVariable("D2_GAME_DIR")
        ?? Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "..", "disciples-modding", "game-pristine"));
    public static string Globals => Path.Combine(GameDir, "Globals");
    public static string Scens => Path.Combine(GameDir, "Scens");
    public static string Exports => Path.Combine(GameDir, "Exports");
    public static bool IsAvailable => Directory.Exists(Globals);

    /// <summary>Взять копию игры <paramref name="gameDir"/>, если переменная <c>D2_GAME_DIR</c> не задана (она главнее).</summary>
    public static void Use(string gameDir)
    {
        ArgumentException.ThrowIfNullOrEmpty(gameDir);
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("D2_GAME_DIR"))) GameDir = Path.GetFullPath(gameDir);
    }
}
