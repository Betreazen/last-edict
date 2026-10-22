namespace LastEdict.Data.Play;

/// <summary>Быстрое сохранение — клавиша Q на карте.
/// <para>Движок: обработчик клавиш карты (<c>0x48e8e3</c>) на <c>'Q'</c>/<c>'q'</c> (<c>0x48e967</c>) зовёт запись партии по имени
/// (<c>0x48fa11</c>) со строкой <c>"QuickSave"</c> (<c>0x6d49f8</c>) — один файл <c>SaveGame\QuickSave.sg</c>, каждое быстрое сохранение его
/// перезаписывает (<c>game-dev/SaveGame</c>: один <c>QuickSave.sg</c>; корпус — 572 события <c>save «QuickSave»</c>, перед каждым
/// <c>input key vk=81</c> без Ctrl). Код клавиши без учёта Ctrl — значит, Ctrl+Q то же самое; обёртка DisciplesGL-HD шлёт на F5 тот же
/// <c>WM_KEYDOWN 'Q'</c> (<c>Window.cpp</c>, режим WASD). На <c>'L'</c> тот же обработчик грузит быстрое сохранение — в порт пока не
/// перенесено.</para>
/// <para>Что видит игрок — строки чата карты (<see cref="LastEdict.Data.Map.IsoChatLogic"/>): перед записью <c>X005TA0581</c> «Сохраняется
/// %SAVEGAME%.sg...», после — <c>X005TA0582</c> «Игра записана под именем %SAVEGAME%» (корпус: <c>ui_text DLG_ISO</c> 0581 → <c>save</c> →
/// 0582 в 16 сессиях из 16, где строки попали в журнал). Так же — у автосохранения (та же запись <c>0x48fa11</c>, <c>0x48f999</c>).</para></summary>
public static class QuickSave
{
    /// <summary>Имя слота и сохранения (<c>0x6d49f8</c>).</summary>
    public const string Name = "QuickSave";

    /// <summary>Строка чата перед записью (<c>X005TA0581</c>, <c>0x48fa64</c>).</summary>
    public const string SavingText = "X005TA0581";

    /// <summary>Строка чата после записи (<c>X005TA0582</c>, <c>0x4921dc</c>).</summary>
    public const string SavedText = "X005TA0582";

    /// <summary>Записать партию в слот <see cref="Name"/> каталога <paramref name="dir"/> (тот же файл каждый раз). Возвращает путь.</summary>
    public static string Write(string dir, GameState state)
    {
        string path = SaveSlots.PathFor(dir, Name);
        GameSave.Write(path, state, Name);
        return path;
    }

    /// <summary>Строки чата вокруг записи под именем <paramref name="saveName"/>: «сохраняется…» и «записана…» (<c>%SAVEGAME%</c>).</summary>
    public static (string Saving, string Saved) Messages(Func<string, string> text, string saveName)
    {
        ArgumentNullException.ThrowIfNull(text);
        return (text(SavingText).Replace("%SAVEGAME%", saveName, StringComparison.Ordinal),
                text(SavedText).Replace("%SAVEGAME%", saveName, StringComparison.Ordinal));
    }
}
