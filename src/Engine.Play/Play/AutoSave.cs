using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace D2.Data.Play;

/// <summary>Автосохранение — флажок «Автосохранение» настроек (<c>TOG_AUTOSAVE</c>, <c>MapSettings.AutoSave</c>).
/// <para>Когда — как у движка: по «Конец хода» человека, до хода остальных (корпус <c>20260919-human</c>: <c>ui_click BTN_END_TURN</c>,
/// seq 1876314, → <c>save «Автосохранение» turn=2</c>, seq 1876315, → затем <c>spell_expire</c>, доход, ходы ИИ; в «горячем стуле» — перед
/// концом хода каждого человека, <c>docs/scenario-events.md</c>). В файле — партия на этот миг: загрузка вернёт тот же ход того же дня.</para>
/// <para>Имя и чистка — как у обёртки DisciplesGL-HD (решение пользователя 15.10; <c>tools/GLWrapper/DisciplesGL-HD/src/DisciplesGL/Hooks.cpp</c>:
/// <c>CopyAutoSave</c>, <c>TrimAutoSaveArchives</c>, <c>IsAutoSaveArchiveName</c>). Движок пишет всегда в один файл <c>X005TA0769</c>
/// («Автосохранение.sg»); обёртка копирует его в архив <c>"%s T%04u"</c> — название сценария из заголовка <c>.sg</c> (смещение
/// <c>0x141</c> у <c>D2EESFISIG</c>: то же название, что у сценария в окне загрузки, — сверено на четырёх сохранениях <c>game-dev/SaveGame</c>)
/// и ход четырьмя цифрами; без названия — «AutoSave». После записи по этому сценарию остаётся не больше <see cref="ArchiveLimit"/>
/// архивов: лишние — самые старые по времени записи, при равенстве — с меньшим ходом. Порт пишет архив сразу своим сохранением
/// (<c>.d2save</c>) в обычный каталог сохранений; тот же день — тот же файл (второй человек «горячего стула» перезаписывает первого).</para>
/// <para>Отличие от обёртки: та считает архивом и любое «AutoSave… T####» чужого сценария; порт чистит только архивы своего названия —
/// другие сохранения каталога не трогаются никогда.</para></summary>
public static class AutoSave
{
    /// <summary>Сколько архивов одного сценария держит каталог (<c>AUTO_SAVE_ARCHIVE_LIMIT</c> обёртки).</summary>
    public const int ArchiveLimit = 15;

    /// <summary>Имя архива без названия сценария (<c>"AutoSave T%04u.sg"</c> обёртки).</summary>
    public const string Unnamed = "AutoSave";

    /// <summary>Имя сохранения партии по умолчанию в окне записи: «&lt;название карты&gt; — день N».</summary>
    public static string Title(GameState state) =>
        string.Create(CultureInfo.InvariantCulture, $"{state.Info.ScenarioName} — день {state.Looks.Map.Turn}");

    /// <summary>Название сценария для имени архива (<c>SanitizeAutoSaveName</c>): знаки, запрещённые в именах файлов, и управляющие — пробел,
    /// пробелы подряд и в начале — один, пробелы и точки в конце сняты; пусто — <see cref="Unnamed"/>.</summary>
    public static string ArchiveBase(string scenario)
    {
        var name = new StringBuilder(scenario.Length);
        bool lastSpace = true;
        foreach (char raw in scenario)
        {
            char c = raw < ' ' || "<>:\"/\\|?*".Contains(raw, StringComparison.Ordinal) ? ' ' : raw;
            if (c == ' ' && lastSpace) continue;
            lastSpace = c == ' ';
            name.Append(c);
        }
        string trimmed = name.ToString().TrimEnd(' ', '.');
        return trimmed.Length == 0 ? Unnamed : trimmed;
    }

    /// <summary>Имя архива партии: «&lt;название сценария&gt; T&lt;день, 4 цифры&gt;» (<c>"%s T%04u"</c>).</summary>
    public static string ArchiveName(GameState state) =>
        string.Create(CultureInfo.InvariantCulture, $"{ArchiveBase(state.Info.ScenarioName)} T{state.Looks.Map.Turn:D4}");

    /// <summary>Записать автосохранение в каталог <paramref name="dir"/> и убрать лишние архивы сценария; выключено или каталога нет —
    /// ничего. Возвращает путь файла.</summary>
    public static string? Write(string? dir, bool enabled, GameState state)
    {
        if (!enabled || string.IsNullOrEmpty(dir)) return null;
        string title = ArchiveName(state);
        string path = SaveSlots.PathFor(dir, title);
        GameSave.Write(path, state, title);
        Trim(dir, ArchiveBase(state.Info.ScenarioName));
        return path;
    }

    /// <summary>Архивы сценария <paramref name="name"/> в каталоге: «<paramref name="name"/> T####.d2save» (без учёта регистра, ровно четыре
    /// цифры — <c>IsAutoSaveArchiveName</c>) с ходом и временем записи.</summary>
    public static IReadOnlyList<(string Path, int Turn, DateTime Written)> Archives(string dir, string name)
    {
        if (!Directory.Exists(dir)) return [];
        var pattern = new Regex($"^{Regex.Escape(name)} T(?<turn>[0-9]{{4}}){Regex.Escape(SaveSlots.Extension)}$",
                                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        var archives = new List<(string Path, int Turn, DateTime Written)>();
        foreach (string path in Directory.GetFiles(dir, "*" + SaveSlots.Extension))
            if (pattern.Match(Path.GetFileName(path)) is { Success: true } m)
                archives.Add((path, int.Parse(m.Groups["turn"].Value, CultureInfo.InvariantCulture), File.GetLastWriteTimeUtc(path)));
        return archives;
    }

    /// <summary>Оставить в <paramref name="dir"/> не больше <see cref="ArchiveLimit"/> архивов сценария <paramref name="name"/>
    /// (<see cref="Archives"/>): удаляются самые старые по времени записи, при равенстве — с меньшим ходом (<c>TrimAutoSaveArchives</c>).
    /// Прочие файлы каталога не трогаются. Возвращает удалённые пути.</summary>
    public static IReadOnlyList<string> Trim(string dir, string name)
    {
        var archives = Archives(dir, name);
        var stale = archives.OrderBy(a => a.Written).ThenBy(a => a.Turn).Take(Math.Max(0, archives.Count - ArchiveLimit))
                            .Select(a => a.Path).ToList();
        foreach (string path in stale) File.Delete(path);
        return stale;
    }
}
