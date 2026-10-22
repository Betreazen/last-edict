using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using D2.Data.Battle;
using D2.Rules.Battle;
using D2.Rules.Map;

namespace D2.Data.Play.Commands;

/// <summary>Событие хода, случившееся за команду (то, что раньше знал только наблюдатель хода <see cref="ITurnObserver"/>): показ и
/// журналы строятся по нему, а не по обходу состояния.</summary>
public abstract record GameEvent;

public sealed record PlayerTurnStarted(string Player) : GameEvent;

public sealed record StackMoved(StackMove Move) : GameEvent;

public sealed record SightHappened(TurnSight Sight) : GameEvent;

public sealed record OfferMade(AllianceOffer Offer) : GameEvent;

public sealed record HumanTurnStarted(string Player) : GameEvent;

public sealed record BattleFinished(BattleOutcome Outcome) : GameEvent;

public sealed record ScenarioEventFired(EventFiring Firing) : GameEvent;

/// <summary>Бой собран и отдан наблюдателю (<see cref="ITurnObserver.DecideBattle"/>); <see cref="Played"/> — его сыграли снаружи.</summary>
public sealed record BattleShown(BattleState Battle, bool Played) : GameEvent;

/// <summary>
/// Решение, пришедшее снаружи во время команды (<c>docs/game-commands.md</c> §2), — данными, чтобы журнал повторил команду без
/// наблюдателя. <see cref="Call"/> — номер вызова наблюдателя за команду (с 1), на котором решение пришло.
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "$ans")]
[JsonDerivedType(typeof(BattleAnswer), "battle")]
[JsonDerivedType(typeof(NestedCommand), "command")]
public abstract record CommandAnswer(int Call);

/// <summary>Бой ИИ, сыгранный снаружи (экран боя с человеком): действия по шагам (<see cref="Play"/>) — или, если наблюдатель дал только
/// журнал, сам журнал (<see cref="Log"/>; повтор тогда не возьмёт чисел генератора, которые брал наблюдатель).</summary>
public sealed record BattleAnswer(int Call, BattlePlay? Play, IReadOnlyList<TurnRecord>? Log) : CommandAnswer(Call);

/// <summary>Команда, поданная, пока команда ждала на вызове наблюдателя <see cref="CommandAnswer.Call"/>: ответ человека на предложение
/// ИИ из окна, пока ход ИИ стоит.</summary>
public sealed record NestedCommand(int Call, GameCommand Command) : CommandAnswer(Call);

/// <summary>Итог команды: значение метода сессии, строки отчёта, события по порядку, решения снаружи (<see cref="CommandAnswer"/>).</summary>
public sealed record CommandResult(object? Value, IReadOnlyList<string> Report, IReadOnlyList<GameEvent> Events,
                                   IReadOnlyList<CommandAnswer> Answers);

/// <summary>
/// Счёт вызовов наблюдателя за команду: номер вызова, на котором пришло решение, — общий язык записи (<see cref="EventRecorder"/>) и
/// повтора (<see cref="ReplayObserver"/>). Вложенная команда относится к самому внешнему вызову, который ещё идёт: её собственные
/// вызовы наблюдателя тоже считаются, но своих вложенных у них нет.
/// </summary>
internal abstract class CallCounter
{
    private int _calls, _depth, _outer;

    /// <summary>Сколько вызовов было.</summary>
    protected int Calls => _calls;

    /// <summary>Вызов, внутри которого идёт исполнение сейчас (внешний); 0 — вне вызова.</summary>
    protected int Outer => _depth > 0 ? _outer : 0;

    /// <summary>Идёт вызов наблюдателя (команда ждёт на нём).</summary>
    public bool InCall => _depth > 0;

    protected T Call<T>(Func<int, T> body)
    {
        int call = ++_calls;
        if (_depth++ == 0) _outer = call;
        T result;
        try
        {
            result = body(call);
        }
        finally
        {
            _depth--;
        }
        if (_depth == 0) Done(call);   // не из finally: сбой вызова не заслоняется сбоем вложенной команды
        return result;
    }

    protected void Call(Action body) => Call(_ =>
    {
        body();
        return 0;
    });

    /// <summary>Внешний вызов кончился.</summary>
    protected virtual void Done(int call)
    {
    }
}

/// <summary>Наблюдатель-записчик: пересылает всё основному наблюдателю (показ не меняется), собирает события и решения команды.</summary>
internal sealed class EventRecorder(ITurnObserver? inner) : CallCounter, ITurnObserver
{
    public List<GameEvent> Events { get; } = [];

    public List<CommandAnswer> Answers { get; } = [];

    /// <summary>Вложенная команда исполнена, пока команда ждала на вызове наблюдателя: записать к внешнему вызову.</summary>
    public void Nested(GameCommand command) => Answers.Add(new NestedCommand(Outer, command));

    public void PlayerTurn(string playerId) => Call(() =>
    {
        Events.Add(new PlayerTurnStarted(playerId));
        inner?.PlayerTurn(playerId);
    });

    public void StackMoving(StackMove move) => Call(() =>
    {
        Events.Add(new StackMoved(move));
        inner?.StackMoving(move);
    });

    public IReadOnlyList<TurnRecord>? Battle(BattleState state) => DecideBattle(state)?.Log;

    public BattleDecision? DecideBattle(BattleState state) => Call(call =>
    {
        var decision = inner?.DecideBattle(state);
        if (decision is not null) Answers.Add(new BattleAnswer(call, decision.Play, decision.Play is null ? decision.Log : null));
        Events.Add(new BattleShown(state, decision is not null));
        return decision;
    });

    public void Happened(TurnSight sight) => Call(() =>
    {
        Events.Add(new SightHappened(sight));
        inner?.Happened(sight);
    });

    public void Offered(AllianceOffer offer) => Call(() =>
    {
        Events.Add(new OfferMade(offer));
        inner?.Offered(offer);
    });

    public void HumanTurn(string playerId) => Call(() =>
    {
        Events.Add(new HumanTurnStarted(playerId));
        inner?.HumanTurn(playerId);
    });

    public void BattleEnded(BattleOutcome outcome) => Call(() =>
    {
        Events.Add(new BattleFinished(outcome));
        inner?.BattleEnded(outcome);
    });

    public void EventFired(EventFiring firing) => Call(() =>
    {
        Events.Add(new ScenarioEventFired(firing));
        inner?.EventFired(firing);
    });
}

/// <summary>Наблюдатель повтора: решения команды — из журнала. Бой — повтор действий на генераторе партии (<see cref="GameSession.ReplayBattle"/>),
/// вложенные команды — после своего вызова, как их подал человек, пока ход ждал.</summary>
internal sealed class ReplayObserver(GameSession session, IReadOnlyList<CommandAnswer> answers) : CallCounter, ITurnObserver
{
    public void PlayerTurn(string playerId) => Call(() => { });

    public void StackMoving(StackMove move) => Call(() => { });

    public IReadOnlyList<TurnRecord>? Battle(BattleState state) => DecideBattle(state)?.Log;

    public BattleDecision? DecideBattle(BattleState state) => Call(call =>
        answers.OfType<BattleAnswer>().FirstOrDefault(a => a.Call == call) switch
        {
            { Play: { } play } => new BattleDecision(session.ReplayBattle(play, state), play),
            { Log: { } log } => new BattleDecision(log),
            _ => null,
        });

    public void Happened(TurnSight sight) => Call(() => { });

    public void Offered(AllianceOffer offer) => Call(() => { });

    public void HumanTurn(string playerId) => Call(() => { });

    public void BattleEnded(BattleOutcome outcome) => Call(() => { });

    public void EventFired(EventFiring firing) => Call(() => { });

    protected override void Done(int call)
    {
        foreach (var nested in answers.OfType<NestedCommand>().Where(a => a.Call == call)) session.Execute(nested.Command);
    }
}

/// <summary>Хеш состояния партии — SHA-256 каноничного JSON снимка (<see cref="GameSession.SnapshotWithoutSummaries"/>: состояние и потоки
/// случайных чисел; запись сохранения с постоянным временем, ключи объектов по порядку). Итоги хода людей (<see cref="GameState.Summaries"/>)
/// в хеш не входят: их собирает сцена для окон, правила их не читают. Две партии с одним хешем неотличимы для правил: так журнал команд
/// проверяет повтор, а сеть — расхождение хоста и клиента.</summary>
public static class StateHash
{
    public static string Of(GameState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        var node = JsonNode.Parse(GameSave.ToJson(state with { Summaries = new Dictionary<string, PendingSummary>() }, "", DateTime.UnixEpoch));
        var canonical = Sorted(node)?.ToJsonString(Canonical) ?? "null";
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)))[..32].ToLowerInvariant();
    }

    public static string Of(GameSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        return Of(session.SnapshotWithoutSummaries());
    }

    private static readonly JsonSerializerOptions Canonical = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    private static JsonNode? Sorted(JsonNode? node) => node switch
    {
        JsonObject obj => new JsonObject(obj.OrderBy(p => p.Key, StringComparer.Ordinal).Select(p => KeyValuePair.Create(p.Key, Sorted(p.Value)))),
        JsonArray arr => new JsonArray([.. arr.Select(Sorted)]),
        _ => node?.DeepClone(),
    };
}

/// <summary>Запись журнала: команда, хеш состояния после неё и решения снаружи, если были. <see cref="Outside"/> — перед командой партия
/// менялась мимо <see cref="GameSession.Execute"/>: повтор с этого места невозможен.</summary>
public sealed record JournalEntry(GameCommand Command, string Hash, IReadOnlyList<CommandAnswer>? Answers = null,
                                  [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] bool Outside = false);

/// <summary>
/// Журнал команд партии: хеш начального состояния и команды по порядку с хешем после каждой. По нему партию повторяют (<see cref="Replay"/>):
/// баг-репорт — сохранение плюс журнал, реплей, проверка детерминизма; сеть сверяет хеши. Пишется строками JSON (<see cref="ToJsonLines"/>).
/// Решения, пришедшие снаружи команды (бой человека в ходе ИИ, ответ на предложение), записаны при ней (<see cref="JournalEntry.Answers"/>).
/// </summary>
public sealed class CommandJournal(string startHash)
{
    private readonly List<JournalEntry> _entries = [];

    public string StartHash { get; } = startHash;

    /// <summary>Настройка сессии, от которой зависят правила (<see cref="GameSession.EventBattlesOnScreen"/>): повтор ставит её так же.</summary>
    public bool EventBattlesOnScreen { get; init; }

    /// <summary>Моды содержимого партии по порядку загрузки: повтор без них разошёлся бы не по вине правил.</summary>
    public IReadOnlyList<string> Mods { get; init; } = [];

    /// <summary>Журнал с этого мига партии: хеш начала, настройки сессии и моды содержимого.</summary>
    public static CommandJournal Start(GameSession session, IReadOnlyList<string>? mods = null)
    {
        ArgumentNullException.ThrowIfNull(session);
        return new CommandJournal(StateHash.Of(session)) { EventBattlesOnScreen = session.EventBattlesOnScreen, Mods = mods ?? [] };
    }

    public IReadOnlyList<JournalEntry> Entries => _entries;

    public void Add(GameCommand command, string hash, IReadOnlyList<CommandAnswer>? answers = null, bool outside = false) =>
        _entries.Add(new JournalEntry(command, hash, answers is { Count: > 0 } ? answers : null, outside));

    public static readonly JsonSerializerOptions Json = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() },
    };

    public string ToJsonLines()
    {
        var sb = new StringBuilder();
        sb.Append(JsonSerializer.Serialize(new { start = StartHash, eventBattlesOnScreen = EventBattlesOnScreen, mods = Mods }, Json)).Append('\n');
        foreach (var e in _entries) sb.Append(JsonSerializer.Serialize(e, Json)).Append('\n');
        return sb.ToString();
    }

    /// <summary>Почему запись могла не повториться: бой, решённый одним журналом ходов без действий, — числа генератора, которые брал
    /// бой, повтор не берёт.</summary>
    private static string Hint(JournalEntry entry) =>
        entry.Command is EndBattleCommand { Play: null } || entry.Answers?.OfType<BattleAnswer>().Any(a => a.Play is null) == true
            ? " — бой записан журналом ходов без действий, числа генератора не повторены" : "";

    public static CommandJournal FromJsonLines(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var lines = text.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        if (lines.Length == 0) throw new InvalidDataException("пустой журнал команд");
        var head = JsonNode.Parse(lines[0]);
        var start = head?["start"]?.GetValue<string>() ?? throw new InvalidDataException("журнал без начального хеша");
        var journal = new CommandJournal(start)
        {
            EventBattlesOnScreen = head?["eventBattlesOnScreen"]?.GetValue<bool>() ?? false,
            Mods = head?["mods"]?.AsArray().Select(m => m!.GetValue<string>()).ToList() ?? [],
        };
        foreach (var line in lines.Skip(1))
        {
            var e = JsonSerializer.Deserialize<JournalEntry>(line, Json) ?? throw new InvalidDataException("пустая строка журнала");
            journal._entries.Add(e);
        }
        return journal;
    }

    /// <summary>Повторить журнал на сессии, начатой с того же состояния: хеш до и после каждой команды обязан совпасть. Решения снаружи
    /// — из журнала (наблюдатель сессии на время повтора заменён). Первое расхождение — <see cref="InvalidDataException"/> с номером команды.</summary>
    public void Replay(GameSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        string start = StateHash.Of(session);
        if (start != StartHash) throw new InvalidDataException($"начальное состояние другое: {start}, в журнале {StartHash}");
        var (observer, journal) = (session.MainObserver, session.Journal);
        try
        {
            session.Replaying = true;
            session.Journal = null;   // повтор не пишет сам себя
            session.EventBattlesOnScreen = EventBattlesOnScreen;
            for (int i = 0; i < _entries.Count; i++)
            {
                var entry = _entries[i];
                if (entry.Outside)
                    throw new InvalidDataException($"перед командой {i} ({entry.Command.GetType().Name}) партия менялась мимо команд — повтор невозможен");
                session.MainObserver = new ReplayObserver(session, entry.Answers ?? []);
                session.Execute(entry.Command);
                string hash = StateHash.Of(session);
                if (hash != entry.Hash)
                    throw new InvalidDataException($"команда {i} ({entry.Command.GetType().Name}): хеш {hash}, в журнале {entry.Hash}{Hint(entry)}");
            }
        }
        finally
        {
            session.Replaying = false;
            session.Journal = journal;
            session.MainObserver = observer;
        }
    }
}
