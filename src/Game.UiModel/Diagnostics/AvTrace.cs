namespace D2.Data.Diagnostics;

/// <summary>
/// Строки звука, музыки, роликов и анимаций в журнале зонда порта — те же имена и поля, что у зонда оригинала (схема 7.3,
/// <c>docs/corpus-schema.md</c>, «Звук, музыка, анимации»), чтобы харнесс <c>AvJournal</c> (<c>D2.Verification</c>) сравнивал журналы двух игр
/// одним разбором. Единицы — движка: громкость звука в процентах 0…100 (<c>vol</c>) и итоговая громкость Miles 0…127 (<c>out</c>,
/// <c>vol × FxVolume / 100</c>), панорама 0…127 (64 — центр), громкость музыки 0…127. Пишет только зонд порта (ставит
/// <c>PortProbe</c>); без журнала объекта нет и затрат нет.
/// </summary>
public sealed class AvTrace
{
    private readonly PortJournal _journal;

    public AvTrace(PortJournal journal)
    {
        ArgumentNullException.ThrowIfNull(journal);
        _journal = journal;
    }

    /// <summary><c>snd</c> — звук запущен (или не нашёлся: <paramref name="ok"/> = false). <paramref name="id"/> — номер звука,
    /// пара к <see cref="SoundEnd"/>; <paramref name="via"/> — кто просил (<c>game</c> — общий проигрыватель карты и окон, <c>battle</c>, <c>capital</c>, <c>region</c>);
    /// <paramref name="top"/> — верхний экран.</summary>
    public void Sound(string name, string? archive, int vol, int pan, bool loop, int? output, long id, bool ok, string via,
        string top) =>
        _journal.Write("snd", new Dictionary<string, object?>
        {
            ["name"] = name, ["archive"] = archive, ["vol"] = vol, ["pan"] = pan, ["loop"] = loop, ["out"] = output, ["id"] = id,
            ["ok"] = ok, ["via"] = via, ["top"] = top,
        });

    /// <summary><c>snd_end</c> — звук кончился (<c>end</c>) или его сняли (<c>stop</c>); громкость Miles за жизнь звука — от и до,
    /// <paramref name="changes"/> — смен громкости и панорамы после старта (фон мест при прокрутке вида).</summary>
    public void SoundEnd(long id, string name, string how, long ms, int volMin, int volMax, int changes) =>
        _journal.Write("snd_end", new Dictionary<string, object?>
        {
            ["id"] = id, ["name"] = name, ["how"] = how, ["ms"] = ms, ["vol_min"] = volMin, ["vol_max"] = volMax,
            ["changes"] = changes,
        });

    /// <summary><c>music</c> — дорожка открыта (<c>open</c>: <paramref name="track"/> — имя без пути и расширения, <paramref name="file"/>),
    /// запущена (<c>start</c>), снята (<c>stop</c>), кончилась сама (<c>end</c>), перемотана (<c>seek</c>, <paramref name="pos"/> —
    /// мс у порта, байты потока у оригинала).</summary>
    public void Music(string action, string track, string? file = null, long? pos = null) =>
        _journal.Write("music", new Dictionary<string, object?>
        {
            ["action"] = action, ["track"] = track, ["file"] = file, ["pos"] = pos,
        });

    /// <summary><c>music</c> с <c>action = volume</c> — смена громкости дорожки: от, до (0…127), за сколько мс и шагов (затухание и
    /// нарастание — одной строкой).</summary>
    public void MusicVolume(string track, int from, int to, long ms, int steps) =>
        _journal.Write("music", new Dictionary<string, object?>
        {
            ["action"] = "volume", ["track"] = track, ["from"] = from, ["to"] = to, ["ms"] = ms, ["steps"] = steps,
        });

    /// <summary><c>music</c> с <c>action = fade</c> — затухание (<c>out</c>) или нарастание (<c>in</c>) дорожки, длительность,
    /// заданная игрой (у движка — <c>CMusicFader</c>), <paramref name="then"/> — что после (<c>stop</c>, <c>call</c>, <c>none</c>).</summary>
    public void MusicFade(string track, string dir, string then, long ms, int? to = null) =>
        _journal.Write("music", new Dictionary<string, object?>
        {
            ["action"] = "fade", ["track"] = track, ["dir"] = dir, ["then"] = then, ["ms"] = ms, ["to"] = to,
        });

    /// <summary><c>av_settings</c> — настройки показа, от которых зависят звук и анимации (только у порта: у оригинала они в
    /// <c>Disciple.ini</c> копии игры): скорость боя и его дополнительная анимация, шаг отряда, плавность хода и прокрутки. Сверка
    /// (<c>AvJournal</c> (<c>D2.Verification</c>)) сравнивает только то, что сыграно при одних настройках.</summary>
    public void Settings(int playerSpeed, int opponentSpeed, int battleSpeed, bool battleAnim, bool stackSmoothScroll, bool smoothScroll,
        bool animatedIso) =>
        _journal.Write("av_settings", new Dictionary<string, object?>
        {
            ["player_speed"] = playerSpeed, ["opponent_speed"] = opponentSpeed, ["battle_speed"] = battleSpeed, ["battle_anim"] = battleAnim,
            ["stack_smooth_scroll"] = stackSmoothScroll, ["smooth_scroll"] = smoothScroll, ["animated_iso"] = animatedIso,
        });

    /// <summary><c>vol_set</c> — громкость из настроек: <c>fx</c> и <c>music</c> 0…127, <c>voice</c> 0…100.</summary>
    public void VolumeSet(string kind, int value) =>
        _journal.Write("vol_set", new Dictionary<string, object?> { ["kind"] = kind, ["value"] = value });

    /// <summary><c>video</c> — ролик открыт (<c>open</c>: кадров и кадров в секунду) или закрыт (<c>close</c>: показано кадров, мс).</summary>
    public void Video(string action, string name, long frames, double rate, long shown, long ms) =>
        _journal.Write("video", new Dictionary<string, object?>
        {
            ["action"] = action, ["name"] = name, ["frames"] = frames, ["rate"] = Math.Round(rate, 3), ["shown"] = shown,
            ["ms"] = ms,
        });

    /// <summary><c>map_anim</c> — анимация карты (<c>CMidAnim2*</c>, имя класса движка) кончилась (<c>end</c>): мс от старта, тиков своей
    /// группы аниматора (66 или 33 мс), кадров картинки; клетка и объект — если есть; <paramref name="group"/> — группа часов задачи, как
    /// байт группы <c>CMidAnim2Base</c> у зонда оригинала (0 — медленная, 1 — быстрая: ход отряда при <c>StackSmoothScroll</c>).</summary>
    public void MapAnim(string action, string cls, int? x, int? y, string? obj, long ms, long ticks, int? frames = null, int group = 0) =>
        _journal.Write("map_anim", new Dictionary<string, object?>
        {
            ["action"] = action, ["class"] = cls, ["x"] = x, ["y"] = y, ["obj"] = obj, ["ms"] = ms, ["ticks"] = ticks,
            ["frames"] = frames, ["group"] = group,
        });

    /// <summary><c>bat_anim</c> — юниту боя поставлена анимация <paramref name="kind"/> (<c>HMOV</c>, <c>HHIT</c>, <c>DEATH</c>,
    /// <c>IDLE</c>…) из <paramref name="frames"/> кадров; <paramref name="unit"/> — id юнита, <paramref name="type"/> — его тип.</summary>
    public void BattleAnim(string unit, string? type, string kind, int frames) =>
        _journal.Write("bat_anim", new Dictionary<string, object?>
        {
            ["unit"] = unit, ["type"] = type, ["kind"] = kind, ["frames"] = frames,
        });

    /// <summary><c>bat_fx</c> — эффект боя (<c>HEFF</c> ходящего, <c>TUCH</c> на цели, эффект <c>Battle.ff</c> по коду): картинка и
    /// кадры.</summary>
    public void BattleFx(string kind, string unit, string? name, int frames) =>
        _journal.Write("bat_fx", new Dictionary<string, object?>
        {
            ["kind"] = kind, ["unit"] = unit, ["name"] = name, ["frames"] = frames,
        });

    /// <summary><c>bat_text</c> — надпись над юнитом боя (урон, «промах», статус): текст как на экране (схема 7.3).</summary>
    public void BattleText(string unit, string text) =>
        _journal.Write("bat_text", new Dictionary<string, object?> { ["unit"] = unit, ["text"] = text });

    /// <summary><c>bat_text</c> схемы 7.6 — надпись на портрете панели с местом (<see cref="PortraitText"/>): источник (<c>target</c>,
    /// <c>actor</c>, <c>heal</c>, <c>xp</c>), стойкая ли (опыт в конце боя), видна ли группа, место надписи и строки — как показаны и по
    /// правилу переноса движка шрифтом порта.</summary>
    public void BattleText(string unit, string text, PortraitText place) =>
        _journal.Write("bat_text", new Dictionary<string, object?>
        {
            ["unit"] = unit, ["text"] = text, ["src"] = place.Src, ["persist"] = place.Persist, ["on"] = place.On, ["big"] = place.Big,
            ["w"] = place.Width, ["h"] = place.Height, ["lines"] = place.Lines, ["lines_rule"] = place.RuleLines,
            ["text_w"] = place.TextWidth,
        });

    /// <summary><c>bat_text_end</c> — надпись со сроком снята: <c>end</c> (срок вышел), <c>cut</c> (новая на том же портрете), <c>gone</c>.</summary>
    public void BattleTextEnd(string unit, string how, long ms) =>
        _journal.Write("bat_text_end", new Dictionary<string, object?> { ["unit"] = unit, ["state"] = -1, ["how"] = how, ["ms"] = ms });

    /// <summary><c>bat_flash</c> — портрет панели сменил состояние (<see cref="PortraitFlash"/>): <paramref name="mode"/> <c>flash</c> — на
    /// <paramref name="ticks"/> тиков панели, <c>set</c> — стойко; <paramref name="src"/> — <c>target</c>, <c>actor</c>, <c>heal</c>,
    /// <c>xp</c>, <c>xp_up</c>; <paramref name="hold"/> — остаётся после конца вспышки; <paramref name="on"/> — группа портрета видна.</summary>
    public void BattleFlash(string unit, int state, string mode, string src, bool hold, bool on, bool? big, int ticks) =>
        _journal.Write("bat_flash", new Dictionary<string, object?>
        {
            ["unit"] = unit, ["state"] = state, ["color"] = PortraitFlash.ColorOf(state), ["mode"] = mode, ["src"] = src, ["hold"] = hold,
            ["on"] = on, ["big"] = big, ["ticks"] = ticks,
        });

    /// <summary><c>bat_flash_end</c> — вспышка кончилась (<c>end</c>, <c>cut</c>, <c>gone</c>); <paramref name="after"/> — состояние портрета
    /// после неё (null — держит вспышку).</summary>
    public void BattleFlashEnd(string unit, int state, string how, long ms, int? after) =>
        _journal.Write("bat_flash_end", new Dictionary<string, object?>
        {
            ["unit"] = unit, ["state"] = state, ["how"] = how, ["ms"] = ms, ["after"] = after, ["hold"] = after is null,
        });

    private (int X, int Y)? _selShown;
    private string? _pathShown;

    /// <summary>Метки карты (схема 7.6): эллипс выбранного отряда (<c>map_sel</c>) и флажки пути (<c>map_path</c>) — строка только на
    /// смену видимого, как у зонда оригинала. <paramref name="marks"/> — имя картинки (<c>SELECTED</c>, <c>MOVE*</c>) и клетка, по порядку
    /// пути; <paramref name="stack"/> — чей путь показан (как <c>stack</c> у зонда оригинала, <c>ShowMovementPath</c>; до 21.10 порт писал null).</summary>
    public void MapMarks(IEnumerable<(string Name, int X, int Y)> marks, string? stack = null)
    {
        ArgumentNullException.ThrowIfNull(marks);
        var list = marks.ToList();
        (int X, int Y)? sel = list.FirstOrDefault(m => m.Name == "SELECTED") is { Name: not null } s ? (s.X, s.Y) : null;
        if (sel != _selShown)
        {
            _journal.Write("map_sel", new Dictionary<string, object?>
            {
                ["action"] = sel is null ? "clear" : "show", ["x"] = sel?.X, ["y"] = sel?.Y, ["via"] = "port",
            });
            _selShown = sel;
        }
        var flags = list.Where(m => m.Name.StartsWith("MOVE", StringComparison.Ordinal)).ToList();
        // Сводка с клетками начала и конца: путь того же состава, но в другие клетки — тоже смена (у зонда оригинала — first/last).
        string? path = flags.Count == 0 ? null
            : FormattableString.Invariant($"{stack}:{MapPathSummary(flags)}@{flags[0].X},{flags[0].Y}-{flags[^1].X},{flags[^1].Y}");
        if (path == _pathShown) return;
        if (path is null)
            _journal.Write("map_path", new Dictionary<string, object?> { ["action"] = "clear", ["via"] = "port" });
        else
            _journal.Write("map_path", new Dictionary<string, object?>
            {
                ["action"] = "show", ["stack"] = stack, ["len"] = flags.Count + 1, ["flags"] = flags.Count, ["names"] = MapPathSummary(flags),
                ["first"] = new[] { flags[0].X, flags[0].Y }, ["last"] = new[] { flags[^1].X, flags[^1].Y }, ["via"] = "show",
            });
        _pathShown = path;
    }

    /// <summary>Флажки сериями «ИМЯ*n» по порядку пути — как <c>names</c> у зонда оригинала.</summary>
    public static string MapPathSummary(IReadOnlyList<(string Name, int X, int Y)> flags)
    {
        ArgumentNullException.ThrowIfNull(flags);
        var runs = new List<string>();
        for (int i = 0; i < flags.Count;)
        {
            int j = i;
            while (j < flags.Count && flags[j].Name == flags[i].Name) j++;
            runs.Add(FormattableString.Invariant($"{flags[i].Name}*{j - i}"));
            i = j;
        }
        return string.Join(",", runs);
    }

    /// <summary><c>ui_enable</c> (схема 7.6) — кнопка или переключатель погасли (<paramref name="on"/> = false) или зажглись: как у зонда
    /// оригинала (<c>CButtonInterf::setEnabled</c> <c>0x531cca</c>), строка — только на смену; <paramref name="screen"/> — разметка
    /// (<c>DLG_*</c>), <paramref name="element"/> — имя элемента из неё, <paramref name="top"/>/<paramref name="topClass"/> — верхний экран.</summary>
    public void UiEnable(string screen, string element, bool on, string top, string? topClass) =>
        _journal.Write("ui_enable", new Dictionary<string, object?>
        {
            ["screen"] = screen, ["element"] = element, ["child"] = -1, ["on"] = on, ["top"] = top, ["top_class"] = topClass,
        });

    /// <summary><c>ui_click</c> — сработала кнопка (<c>button</c>) или переключатель (<c>toggle</c>, <paramref name="isChecked"/> — новое
    /// состояние) экрана: те же поля, что у зонда оригинала (<c>0x5318e1</c>, <c>0x534ba3</c>), строка — до обработчика. Нужна сверке
    /// <c>ui_enable</c> «вокруг одного действия» (<c>AvJournal.UiEnables</c>).</summary>
    public void UiClick(string kind, string screen, string element, bool hotkey, bool? isChecked, string top, string? topClass) =>
        _journal.Write("ui_click", new Dictionary<string, object?>
        {
            ["kind"] = kind, ["screen"] = screen, ["element"] = element, ["hotkey"] = hotkey, ["checked"] = isChecked, ["top"] = top,
            ["top_class"] = topClass,
        });

    /// <summary><c>cursor</c> — сменилась форма курсора (имя картинки).</summary>
    public void Cursor(string name, string top) =>
        _journal.Write("cursor", new Dictionary<string, object?> { ["name"] = name, ["top"] = top });

    /// <summary><c>frames</c> — кадры за окно <paramref name="ms"/>: число, самый долгий кадр (мс).</summary>
    public void Frames(long n, long ms, long maxMs, string top) =>
        _journal.Write("frames", new Dictionary<string, object?> { ["n"] = n, ["ms"] = ms, ["max_ms"] = maxMs, ["top"] = top });
}

/// <summary>Место надписи на портрете панели боя для строки <c>bat_text</c> 7.6: источник, стойкая ли, видна ли группа, большой ли портрет,
/// место (у движка <c>CImage2Text</c> 150 или 70 на 85, <c>0x64d160</c>), строк показано и строк по правилу переноса движка, ширина
/// самой длинной строки — точки.</summary>
public sealed record PortraitText(string Src, bool Persist, bool On, bool Big, int Width, int Height, int Lines, int RuleLines, int TextWidth);
