using System;
using System.Linq;
using LastEdict.Data.Map;
using Godot;

namespace LastEdict.Godot;

/// <summary>
/// Крупный интерфейс (Э5, Р11; <see cref="MapSettings.LargeUi"/>): на маленьком экране (Steam Deck, 7" и 1280×800) системный курсор игры не
/// растёт вместе с экраном и теряется, а подсказки кнопок мелкие, — в крупном режиме курсор ×<see cref="LargeCursor"/>, подсказки крупнее.
/// «Сам» включает режим на Steam Deck (переменная окружения <c>SteamDeck=1</c> игрового режима SteamOS или геймпад «Steam Deck»).
/// </summary>
public static class UiScale
{
    public const float LargeCursor = 1.5f;

    private static int? _mode;

    /// <summary>Крупный режим сейчас.</summary>
    public static bool Large { get; private set; }

    /// <summary>Режим сменился (курсор и подсказки перестраиваются).</summary>
    public static event Action? Changed;

    /// <summary>Множитель курсора.</summary>
    public static float CursorScale
    {
        get
        {
            Ensure();
            return Large ? LargeCursor : 1f;
        }
    }

    public static void Apply(MapSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        _mode = settings.LargeUi;
        bool large = settings.LargeUi == MapSettings.LargeUiOn || (settings.LargeUi == MapSettings.LargeUiAuto && SteamDeck());
        if (large == Large) return;
        Large = large;
        Changed?.Invoke();
    }

    /// <summary>Посмотреть «сам» заново (подключили геймпад Steam Deck).</summary>
    public static void Recheck()
    {
        if (_mode is { } mode) Apply(new MapSettings(LargeUi: mode));
    }

    /// <summary>Игра на Steam Deck.</summary>
    public static bool SteamDeck() =>
        OS.GetEnvironment("SteamDeck") == "1"
        || Input.GetConnectedJoypads().Any(id => Input.GetJoyName(id).Contains("Steam Deck", StringComparison.OrdinalIgnoreCase));

    /// <summary>Хозяин настроек режим ещё не ставил: из файла настроек (<c>--settings</c> или пользователя), в проверках — «сам».</summary>
    private static void Ensure()
    {
        if (_mode is not null) return;
        string[] args = OS.GetCmdlineUserArgs();
        int at = Array.IndexOf(args, "--settings");
        string? path = at >= 0 && at + 1 < args.Length ? args[at + 1]
            : args.Any(a => a is "--check" or "--input") ? null : ProjectSettings.GlobalizePath("user://settings.json");
        Apply(path is null ? new MapSettings() : MapSettings.Load(path));
    }
}
