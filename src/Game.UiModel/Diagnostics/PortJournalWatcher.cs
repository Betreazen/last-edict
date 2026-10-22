using System.Globalization;
using D2.Data.Play;
using D2.Rules.Battle;
using D2.Rules.Map;

namespace D2.Data.Diagnostics;

/// <summary>
/// Подписчик хода партии для зонда порта (<see cref="GameSession.Watch"/>): чей ход (<c>turn</c>, с днём и казной), шаг отряда
/// (<c>move</c>: откуда, куда, клеток, нападение), бой (<c>battle</c>: кто на кого, где; итог — <c>battle_end</c>), события без хода по
/// клеткам (<c>sight</c>: захват, наём, заклинание, гибель, рост, предложения…), предложения человеку (<c>offer</c>), сработавшие
/// события сценария (<c>event</c>). Подписчик ничего не решает: бой ему сообщается, его ответ не берётся (<see cref="TurnWatchers"/>).
/// <para>Долгая партия: в начале хода человека — снимок мира <c>day</c> (<see cref="DaySnapshot"/>) и сохранение дня
/// <c>&lt;журнал&gt;-dayNNN-&lt;игрок&gt;.d2save</c> рядом с журналом (старые прореживаются, <see cref="KeepDays"/>); в начале хода
/// любого игрока и после каждого боя — инварианты карты (<see cref="MapInvariants"/>, строка <c>invariant</c>; то же нарушение того же
/// объекта — не чаще раза в день). Трасса ИИ (<see cref="IAiTrace"/>: <c>ai_pick</c>, <c>ai_step</c>, <c>ai_turn</c>) — если зонд
/// поставил подписчика и в <see cref="GameSession.AiTrace"/>.</para>
/// <para>Подписчик привязан к своей партии <paramref name="session"/> (загрузка другой — новый подписчик) и не бросает в игру ничего:
/// каждый вызов — под <see cref="Safe"/>, журнал сам не бросает (<see cref="PortJournal.Write"/>). Его зовут из хода ИИ на рабочем
/// потоке, из конца боя и из событий — исключение отсюда порвало бы партию. Состояние сцены (итоги хода окна) он не трогает: сохранение
/// дня — <see cref="GameSession.SnapshotWithoutSummaries"/>.</para>
/// </summary>
public sealed class PortJournalWatcher(PortJournal journal, GameSession session, bool autosave = true) : ITurnObserver, IAiTrace
{
    /// <summary>Сохранения дней: последние <see cref="KeepDays"/> дней — все, старше — только дни, кратные <see cref="KeepEvery"/>
    /// (замер 13.10: карта 96×96 — ~460 КБ на сохранение; 50 дней «горячего стула» на большой карте без прореживания — до ~100 МБ,
    /// с прореживанием — ~17 файлов на человека).</summary>
    public const int KeepDays = 7;

    /// <summary>Шаг сохранений старше <see cref="KeepDays"/> дней.</summary>
    public const int KeepEvery = 5;

    private readonly object _lock = new();

    /// <summary>Нарушение (код и объект) → день, когда записано последний раз.</summary>
    private readonly Dictionary<string, int> _reported = new(StringComparer.Ordinal);

    /// <summary>Карта прошлой проверки инвариантов — для вместимости групп (правило роста, <see cref="MapInvariants"/>).</summary>
    private MapState? _checked;

    public void PlayerTurn(string playerId) => Safe("turn", () =>
    {
        var player = session.Map.Players.GetValueOrDefault(playerId);
        journal.Write("turn", new Dictionary<string, object?>
        {
            ["player"] = playerId, ["day"] = session.Map.Turn, ["gold"] = player?.Bank.Gold,
            ["human"] = session.State.Info.Humans.Contains(playerId, StringComparer.OrdinalIgnoreCase),
        });
        Invariants($"turn {playerId}");
    });

    /// <summary>Ход человека: строка <c>turn</c>, инварианты, снимок <c>day</c> и сохранение дня.</summary>
    public void HumanTurn(string playerId)
    {
        PlayerTurn(playerId);
        Safe("day", () => Day(playerId, attach: false));
    }

    /// <summary>Подписка на партию (загрузка, новая партия): снимок, сохранение и инварианты на текущем состоянии. Снимок помечен
    /// <c>attach</c> (в имени сохранения — <c>-attach</c>): загруженная партия может быть посреди дня, это не начало хода.</summary>
    public void Attached(string playerId)
    {
        // События старта партии (NewGame.Begin) сработали до подписки — строками event с пометкой start.
        foreach (var firing in session.TakeUnwatchedFirings()) Safe("event", () => WriteEvent(firing, start: true));
        Safe("day", () => Day(playerId, attach: true));
        Safe("invariants", () => Invariants($"attach {playerId}"));
    }

    /// <summary>Снимок мира (<c>day</c>), затем сохранение дня — сбой сохранения строку не съедает (отдельная строка <c>day_save</c>).
    /// Посреди боя (карта в полубое) — ничего.</summary>
    private void Day(string playerId, bool attach)
    {
        if (session.InBattle) return;
        var fields = DaySnapshot.Of(session, playerId);
        string? path = autosave ? SavePath(playerId, attach) : null;
        if (attach) fields["attach"] = true;
        fields["save"] = path;
        journal.Write("day", fields);
        if (path is null) return;
        if (Save(path, playerId) is { } failure)
            journal.Write("day_save", new Dictionary<string, object?> { ["day"] = session.Map.Turn, ["save"] = path, ["error"] = failure });
        else if (!attach) Thin(session.Map.Turn);
    }

    private string Stem => Path.GetFileNameWithoutExtension(journal.Path);

    private string Dir => Path.GetDirectoryName(Path.GetFullPath(journal.Path))!;

    /// <summary><c>&lt;журнал&gt;-dayNNN-&lt;игрок&gt;[-attach].d2save</c> рядом с журналом.</summary>
    private string SavePath(string playerId, bool attach) =>
        Path.Combine(Dir, $"{Stem}-day{session.Map.Turn.ToString("000", CultureInfo.InvariantCulture)}-{playerId}{(attach ? "-attach" : "")}.d2save");

    /// <summary>Записать сохранение (без итогов хода окна — их держит сцена на главном потоке). Сбой — текст, недописанный
    /// <c>.tmp</c> удаляется.</summary>
    private string? Save(string path, string playerId)
    {
        try
        {
            GameSave.Write(path, session.SnapshotWithoutSummaries(), $"зонд: день {session.Map.Turn} {playerId}");
            return null;
        }
        catch (Exception e)
        {
            try
            {
                File.Delete(path + ".tmp");
            }
            catch (Exception)
            {
                // .tmp не удалился — следующая запись того же дня его перезапишет
            }
            return $"{e.GetType().Name}: {e.Message}";
        }
    }

    /// <summary>Прореживание: сохранения дня <c>today − KeepDays</c> (не кратного <see cref="KeepEvery"/>) удаляются — у всех людей.</summary>
    private void Thin(int today)
    {
        int old = today - KeepDays;
        if (old < 0 || old % KeepEvery == 0) return;
        foreach (string file in Directory.EnumerateFiles(Dir, $"{Stem}-day{old.ToString("000", CultureInfo.InvariantCulture)}-*.d2save"))
            File.Delete(file);
    }

    /// <summary>Инварианты карты: новые нарушения (или прежние — в новый день) — строками <c>invariant</c>.</summary>
    private void Invariants(string when)
    {
        var map = session.Map;
        IReadOnlyList<Violation> found;
        lock (_lock)
        {
            found = MapInvariants.Check(map, session.Book, _checked);
            _checked = map;
        }
        foreach (var v in found)
        {
            string key = $"{v.Code}|{v.Object}";
            lock (_lock)
            {
                if (_reported.TryGetValue(key, out int day) && day == map.Turn) continue;
                _reported[key] = map.Turn;
            }
            journal.Write("invariant", new Dictionary<string, object?>
            {
                ["code"] = v.Code, ["object"] = v.Object, ["detail"] = v.Detail, ["day"] = map.Turn, ["when"] = when,
            });
        }
    }

    public void StackMoving(StackMove move) => Safe("move", () =>
    {
        var from = move.Cells.Count > 0 ? move.Cells[0] : (-1, -1);
        var to = move.Cells.Count > 0 ? move.Cells[^1] : (-1, -1);
        journal.Write("move", new Dictionary<string, object?>
        {
            ["stack"] = move.StackId, ["owner"] = move.Owner, ["from"] = new[] { from.Item1, from.Item2 },
            ["to"] = new[] { to.Item1, to.Item2 }, ["cells"] = move.Cells.Count, ["attack"] = move.Attack,
        });
    });

    public IReadOnlyList<TurnRecord>? Battle(BattleState state)
    {
        Safe("battle", () => journal.Write("battle", new Dictionary<string, object?>
        {
            ["attacker"] = state.AttackerGroup, ["defender"] = state.DefenderGroup, ["day"] = session.Map.Turn,
        }));
        return null;
    }

    /// <summary>Итог боя (<c>battle_end</c>, поля — как у <c>battle_end</c> журнала оригинала, где можно) и инварианты после него.</summary>
    public void BattleEnded(BattleOutcome outcome) => Safe("battle_end", () =>
    {
        journal.Write("battle_end", new Dictionary<string, object?>
        {
            ["attacker"] = outcome.Attacker, ["attacker_owner"] = outcome.AttackerOwner, ["defender"] = outcome.Defender,
            ["defender_owner"] = outcome.DefenderOwner, ["winner"] = outcome.Winner,
            ["winner_owner"] = outcome.AttackerWon ? outcome.AttackerOwner : outcome.DefenderOwner, ["rounds"] = outcome.Rounds,
            ["fallen"] = new[] { outcome.AttackersFallen, outcome.DefendersFallen }, ["xp"] = new[] { outcome.AttackersXp, outcome.DefendersXp },
            ["day"] = outcome.Day, ["classes"] = new[] { outcome.AttackerClasses, outcome.DefenderClasses }, ["items"] = outcome.ItemsUsed,
            ["retreated"] = new[] { outcome.AttackersRetreated, outcome.DefendersRetreated },
            ["retreated_units"] = outcome.RetreatedUnits,
        });
        Invariants($"battle {outcome.Attacker}>{outcome.Defender}");
    });

    /// <summary>Событие на карте без хода (<c>sight</c>); у захвата — ещё <c>occupy</c>: сообщение <c>OCCUPY</c> захватчику (у его расы
    /// этой деревни раньше не было, <c>MapSite.PreviousOwners</c>).</summary>
    public void Happened(TurnSight sight) => Safe("sight", () => journal.Write("sight", new Dictionary<string, object?>
    {
        ["kind"] = sight.Kind.ToString(), ["player"] = sight.PlayerId, ["at"] = new[] { sight.X, sight.Y }, ["object"] = sight.Id,
        ["occupy"] = sight.Kind == TurnSightKind.Capture ? sight.Occupy : null,
    }));

    public void Offered(AllianceOffer offer) => Safe("offer", () => journal.Write("offer", new Dictionary<string, object?>
    {
        ["from"] = offer.From, ["to"] = offer.To, ["what"] = offer.Trade is not null ? "trade" : offer.Exchange ? "exchange" : "alliance",
    }));

    /// <summary>Сработавшее событие сценария: id, игрок, триггерер, день, эффекты по порядку (имена <c>L_*</c>).</summary>
    public void EventFired(EventFiring firing) => Safe("event", () => WriteEvent(firing, start: false));

    /// <summary>Строка <c>event</c>; <paramref name="start"/> — событие старта партии, записанное при подписке (<c>start</c> = true).</summary>
    private void WriteEvent(EventFiring firing, bool start)
    {
        var fields = new Dictionary<string, object?>
        {
            ["id"] = firing.Event.Id, ["player"] = firing.PlayerId, ["triggerer"] = firing.TriggererId, ["day"] = session.Map.Turn,
            ["effects"] = firing.Event.Effects.Select(e => e.Name).ToList(),
        };
        if (start) fields["start"] = true;
        journal.Write("event", fields);
    }

    // ------------------------------------------------------------------ трасса ИИ

    /// <summary><c>ai_pick</c> (у оригинала — <c>ai_tactic_pick</c> фазы 4): отряд, вид действия, цель и её клетка, счёт, кандидатов.</summary>
    public void Picked(string playerId, int day, AiTacticOption action, double score, int candidates) =>
        Safe("ai_pick", () => journal.Write("ai_pick", new Dictionary<string, object?>
        {
            ["player"] = playerId, ["turn"] = day, ["stack"] = action.Stack, ["action"] = action.Type.ToString(), ["target"] = action.Target,
            ["at"] = new[] { action.X, action.Y }, ["score"] = Math.Round(score, 2), ["count"] = candidates,
        }));

    /// <summary><c>ai_step</c> (у оригинала — <c>ai_strategic_step</c>): тип списка числом и словом, цель (<c>id</c> — ключ памяти дня),
    /// справочная цель, проход и номер шага в ходе.</summary>
    public void Stepped(string playerId, int day, AiExecuted step, int number) =>
        Safe("ai_step", () => journal.Write("ai_step", new Dictionary<string, object?>
        {
            ["player"] = playerId, ["turn"] = day, ["type"] = (int)step.Action.Type, ["kind"] = step.Action.Type.ToString(),
            ["id"] = step.Action.Id, ["target"] = step.Action.Target, ["pass"] = step.Pass, ["step"] = number,
        }));

    /// <summary><c>ai_turn</c> (у оригинала — <c>ai_turn</c> с <c>phase = end</c>): длительность хода ИИ в мс, шагов, выборов.</summary>
    public void TurnEnded(string playerId, int day, long ms, int steps, int picks) =>
        Safe("ai_turn", () => journal.Write("ai_turn", new Dictionary<string, object?>
        {
            ["player"] = playerId, ["turn"] = day, ["ms"] = ms, ["steps"] = steps, ["picks"] = picks,
        }));

    /// <summary>Сбой подписчика — ошибкой журнала (журнал сам не бросает), не исключением в ход; ничего не пробрасывается.</summary>
    private void Safe(string what, Action action)
    {
        try
        {
            action();
        }
        catch (Exception e)
        {
            try
            {
                journal.Error("probe", $"зонд ({what}): {e}", "PortJournalWatcher");
            }
            catch (Exception)
            {
                // журнал не бросает; это последняя страховка — игра важнее строки
            }
        }
    }
}
