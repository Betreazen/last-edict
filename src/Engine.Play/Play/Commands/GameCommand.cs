using System.Text.Json.Serialization;
using LastEdict.Rules.Battle;
using LastEdict.Rules.Map;

namespace LastEdict.Data.Play.Commands;

/// <summary>
/// Команда партии — всё, что меняет состояние (<see cref="GameState"/>) или поток случайных чисел, одной записью-данными: её можно
/// записать в журнал (<see cref="CommandJournal"/>), переслать по сети и повторить. Исполняет <see cref="GameSession.Execute"/>, итог —
/// <see cref="CommandResult"/> (значение метода, отчёт, события хода). Этап Э2, задача A (<c>docs/game-commands.md</c>): пока команда —
/// один метод сессии; дальше правила переезжают в ядро по одной команде, а несколько вызовов хода человека сводятся в одну команду с
/// событиями-дельтами.
/// <para>Имя в JSON — поле <c>"$cmd"</c> (<c>"build"</c>, <c>"moveStack"</c>…).</para>
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "$cmd")]
[JsonDerivedType(typeof(PlanMoveCommand), "planMove")]
[JsonDerivedType(typeof(RevealFromCommand), "revealFrom")]
[JsonDerivedType(typeof(MoveStackCommand), "moveStack")]
[JsonDerivedType(typeof(ActInPlaceCommand), "actInPlace")]
[JsonDerivedType(typeof(LeftFortCommand), "leftFort")]
[JsonDerivedType(typeof(LeaveFortCommand), "leaveFort")]
[JsonDerivedType(typeof(EndTurnCommand), "endTurn")]
[JsonDerivedType(typeof(BuildCommand), "build")]
[JsonDerivedType(typeof(HireUnitCommand), "hireUnit")]
[JsonDerivedType(typeof(HireLeaderCommand), "hireLeader")]
[JsonDerivedType(typeof(CastSpellCommand), "castSpell")]
[JsonDerivedType(typeof(CastItemCommand), "castItem")]
[JsonDerivedType(typeof(ResearchSpellCommand), "researchSpell")]
[JsonDerivedType(typeof(GrowCityCommand), "growCity")]
[JsonDerivedType(typeof(DismissUnitCommand), "dismissUnit")]
[JsonDerivedType(typeof(HealUnitCommand), "healUnit")]
[JsonDerivedType(typeof(ReviveUnitCommand), "reviveUnit")]
[JsonDerivedType(typeof(SwapUnitsCommand), "swapUnits")]
[JsonDerivedType(typeof(MoveInGroupCommand), "moveInGroup")]
[JsonDerivedType(typeof(EquipItemCommand), "equipItem")]
[JsonDerivedType(typeof(UnequipItemCommand), "unequipItem")]
[JsonDerivedType(typeof(DrinkPotionCommand), "drinkPotion")]
[JsonDerivedType(typeof(ExchangeItemCommand), "exchangeItem")]
[JsonDerivedType(typeof(DropBagCommand), "dropBag")]
[JsonDerivedType(typeof(CloseBagCommand), "closeBag")]
[JsonDerivedType(typeof(TakeFortItemsCommand), "takeFortItems")]
[JsonDerivedType(typeof(GiveFortItemsCommand), "giveFortItems")]
[JsonDerivedType(typeof(GiveItemsCommand), "giveItems")]
[JsonDerivedType(typeof(ChooseSkillCommand), "chooseSkill")]
[JsonDerivedType(typeof(RenameLeaderCommand), "renameLeader")]
[JsonDerivedType(typeof(ForceDynLevelCommand), "forceDynLevel")]
[JsonDerivedType(typeof(RequestPaperdollCommand), "requestPaperdoll")]
[JsonDerivedType(typeof(PlaceRodCommand), "placeRod")]
[JsonDerivedType(typeof(BreakRodCommand), "breakRod")]
[JsonDerivedType(typeof(BuyItemCommand), "buyItem")]
[JsonDerivedType(typeof(BuySpellCommand), "buySpell")]
[JsonDerivedType(typeof(SellItemCommand), "sellItem")]
[JsonDerivedType(typeof(HireMercenaryCommand), "hireMercenary")]
[JsonDerivedType(typeof(TrainCommand), "train")]
[JsonDerivedType(typeof(ThiefCommand), "thief")]
[JsonDerivedType(typeof(ThiefCancelCommand), "thiefCancel")]
[JsonDerivedType(typeof(GiveGoldCommand), "giveGold")]
[JsonDerivedType(typeof(BreakAllianceCommand), "breakAlliance")]
[JsonDerivedType(typeof(ProposeAllianceCommand), "proposeAlliance")]
[JsonDerivedType(typeof(AnswerAllianceCommand), "answerAlliance")]
[JsonDerivedType(typeof(AnswerTradeCommand), "answerTrade")]
[JsonDerivedType(typeof(AnswerExchangeCommand), "answerExchange")]
[JsonDerivedType(typeof(ProposeTradeCommand), "proposeTrade")]
[JsonDerivedType(typeof(ProposeExchangeMapCommand), "proposeExchangeMap")]
[JsonDerivedType(typeof(EnterCommand), "enter")]
[JsonDerivedType(typeof(StartBattleCommand), "startBattle")]
[JsonDerivedType(typeof(EndBattleCommand), "endBattle")]
[JsonDerivedType(typeof(CommitBattleCommand), "commitBattle")]
[JsonDerivedType(typeof(NextEventBattleCommand), "nextEventBattle")]
[JsonDerivedType(typeof(TakeMessagesCommand), "takeMessages")]
[JsonDerivedType(typeof(WinCommand), "win")]
[JsonDerivedType(typeof(CheatExperienceCommand), "cheatExperience")]
public abstract record GameCommand
{
    /// <summary>Исполнить на сессии; <paramref name="report"/> — строки отчёта методов, которые его пишут.</summary>
    internal abstract object? Run(GameSession s, List<string> report);

    /// <summary>До команды экран законно брал числа генераторов партии, которые команда повторит сама (бой по шагам,
    /// <see cref="EndBattleCommand"/>): сдвиг генераторов перед ней — не правка мимо команд.</summary>
    internal virtual bool FollowsScreenDraws => false;
}

/// <summary>Срабатывание события сценария в команде — ссылкой на событие (id), а не его копией: событие берётся из состояния партии.</summary>
public sealed record FiringRef(string Event, string Player, string Triggerer, bool CutOnly = false)
{
    public static FiringRef Of(EventFiring f)
    {
        ArgumentNullException.ThrowIfNull(f);
        return new(f.Event.Id, f.PlayerId, f.TriggererId, f.CutOnly);
    }

    internal EventFiring Resolve(GameState state) =>
        new(state.Events.Events.FirstOrDefault(e => e.Id.Equals(Event, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidDataException($"событие «{Event}» не найдено в партии"), Player, Triggerer) { CutOnly = CutOnly };
}

// ---- ход отряда (ход человека из интерфейса — несколько команд: план с проверкой событий пути, туман по шагам, приход, выход из объекта)

/// <summary>Упреждающая проверка событий пути (<see cref="GameSession.PathEvents"/>): бросает R250 — поэтому команда, а не запрос.</summary>
public sealed record PlanMoveCommand(string Stack, IReadOnlyList<PathPoint> Today) : GameCommand
{
    internal override object? Run(GameSession s, List<string> report)
    {
        var (walk, fired, cut) = s.PathEvents(Stack, Today);
        return new PlannedMove(walk, [.. fired.Select(FiringRef.Of)], cut);
    }
}

/// <summary>Итог <see cref="PlanMoveCommand"/>: сколько клеток пройдёт, сработавшие события, оборван ли путь.</summary>
public sealed record PlannedMove(int Walk, IReadOnlyList<FiringRef> Fired, bool Cut);

/// <summary>Шаг отряда открыл обзор (<see cref="GameSession.RevealFrom"/>).</summary>
public sealed record RevealFromCommand(string Stack, int X, int Y) : GameCommand
{
    internal override object? Run(GameSession s, List<string> report) => s.RevealFrom(Stack, X, Y);
}

public sealed record MoveStackCommand(string Stack, int X, int Y, int Facing, int Spend, IReadOnlyList<FiringRef>? Fired = null,
                                      IReadOnlyList<PathPoint>? Today = null, bool Attack = false) : GameCommand
{
    internal override object? Run(GameSession s, List<string> report)
    {
        s.MoveStack(Stack, X, Y, Facing, Spend, Fired?.Select(f => f.Resolve(s.State)).ToList(), Today, Attack);
        return null;
    }
}

public sealed record ActInPlaceCommand(string Stack, bool Attack, int Spend = 0) : GameCommand
{
    internal override object? Run(GameSession s, List<string> report)
    {
        s.ActInPlace(Stack, Attack, Spend);
        return null;
    }
}

public sealed record LeftFortCommand(string Stack) : GameCommand
{
    internal override object? Run(GameSession s, List<string> report)
    {
        s.LeftFort(Stack);
        return null;
    }
}

public sealed record LeaveFortCommand(string Stack) : GameCommand
{
    internal override object? Run(GameSession s, List<string> report) => s.LeaveFort(Stack);
}

/// <summary>Конец хода человека: ходы ИИ, смена дня, начало хода людей (<see cref="GameSession.EndTurn"/>).</summary>
public sealed record EndTurnCommand : GameCommand
{
    internal override object? Run(GameSession s, List<string> report) => s.EndTurn();
}

// ---- город, найм, магия

public sealed record BuildCommand(string Player, string Building) : GameCommand
{
    internal override object? Run(GameSession s, List<string> report) => s.Build(Player, Building);
}

public sealed record HireUnitCommand(string Fort, string UnitType, int? Cell = null) : GameCommand
{
    internal override object? Run(GameSession s, List<string> report) => s.HireUnit(Fort, UnitType, Cell);
}

public sealed record HireLeaderCommand(string Fort, string LeaderType, string Name = "") : GameCommand
{
    internal override object? Run(GameSession s, List<string> report) => s.HireLeader(Fort, LeaderType, name: Name);
}

public sealed record CastSpellCommand(string Player, string Spell, int X, int Y, bool UseSpellBank, string? SummonTarget = null) : GameCommand
{
    internal override object? Run(GameSession s, List<string> report) => s.CastSpell(Player, Spell, X, Y, UseSpellBank, SummonTarget);
}

public sealed record CastItemCommand(string Stack, string Item, int X, int Y, string? SummonTarget = null) : GameCommand
{
    internal override object? Run(GameSession s, List<string> report) => s.CastItem(Stack, Item, X, Y, SummonTarget);
}

public sealed record ResearchSpellCommand(string Player, string Spell) : GameCommand
{
    internal override object? Run(GameSession s, List<string> report) => s.ResearchSpell(Player, Spell);
}

public sealed record GrowCityCommand(string Village) : GameCommand
{
    internal override object? Run(GameSession s, List<string> report) => s.GrowCity(Village);
}

// ---- отряд и предметы

public sealed record DismissUnitCommand(string Group, string Unit) : GameCommand
{
    internal override object? Run(GameSession s, List<string> report) => s.DismissUnit(Group, Unit);
}

/// <summary>Лечение юнита гарнизона или гостя в городе за золото (<see cref="GameSession.Heal"/>).</summary>
public sealed record HealUnitCommand(string Fort, string Unit) : GameCommand
{
    internal override object? Run(GameSession s, List<string> report) => s.Heal(Fort, Unit);
}

/// <summary>Воскрешение юнита в городе за золото (<see cref="GameSession.Revive"/>).</summary>
public sealed record ReviveUnitCommand(string Fort, string Unit) : GameCommand
{
    internal override object? Run(GameSession s, List<string> report) => s.Revive(Fort, Unit);
}

public sealed record SwapUnitsCommand(string FromGroup, int FromPos, string ToGroup, int ToPos) : GameCommand
{
    internal override object? Run(GameSession s, List<string> report) => s.SwapUnits(FromGroup, FromPos, ToGroup, ToPos);
}

public sealed record MoveInGroupCommand(string Group, int FromPos, int ToPos) : GameCommand
{
    internal override object? Run(GameSession s, List<string> report) => s.MoveInGroup(Group, FromPos, ToPos);
}

public sealed record EquipItemCommand(string Stack, string Item, int Slot) : GameCommand
{
    internal override object? Run(GameSession s, List<string> report) => s.EquipItem(Stack, Item, Slot);
}

public sealed record UnequipItemCommand(string Stack, int Slot) : GameCommand
{
    internal override object? Run(GameSession s, List<string> report)
    {
        s.UnequipItem(Stack, Slot);
        return null;
    }
}

public sealed record DrinkPotionCommand(string Holder, string Item, string Unit) : GameCommand
{
    internal override object? Run(GameSession s, List<string> report) => s.DrinkPotion(Holder, Item, Unit);
}

public sealed record ExchangeItemCommand(string From, string To, string Item) : GameCommand
{
    internal override object? Run(GameSession s, List<string> report) => s.ExchangeItem(From, To, Item);
}

/// <summary>Бросить мешок; значение — (отказ, id мешка).</summary>
public sealed record DropBagCommand(string Stack, int X, int Y) : GameCommand
{
    internal override object? Run(GameSession s, List<string> report) => (s.DropBag(Stack, X, Y, out string bag), bag);
}

public sealed record CloseBagCommand(string Bag) : GameCommand
{
    internal override object? Run(GameSession s, List<string> report)
    {
        s.CloseBag(Bag);
        return null;
    }
}

public sealed record TakeFortItemsCommand(string Stack) : GameCommand
{
    internal override object? Run(GameSession s, List<string> report)
    {
        s.TakeFortItems(Stack);
        return null;
    }
}

public sealed record GiveFortItemsCommand(string Stack) : GameCommand
{
    internal override object? Run(GameSession s, List<string> report)
    {
        s.GiveFortItems(Stack);
        return null;
    }
}

public sealed record GiveItemsCommand(string From, string To) : GameCommand
{
    internal override object? Run(GameSession s, List<string> report)
    {
        s.GiveItems(From, To);
        return null;
    }
}

public sealed record ChooseSkillCommand(string Stack, string Modifier) : GameCommand
{
    internal override object? Run(GameSession s, List<string> report) => s.ChooseSkill(Stack, Modifier);
}

public sealed record RenameLeaderCommand(string Stack, string Name) : GameCommand
{
    internal override object? Run(GameSession s, List<string> report) => s.RenameLeader(Stack, Name);
}

public sealed record ForceDynLevelCommand(string Unit) : GameCommand
{
    internal override object? Run(GameSession s, List<string> report) => s.ForceDynLevel(Unit);
}

public sealed record RequestPaperdollCommand(string Stack) : GameCommand
{
    internal override object? Run(GameSession s, List<string> report) => s.RequestPaperdoll(Stack);
}

public sealed record PlaceRodCommand(string Stack, int X, int Y) : GameCommand
{
    internal override object? Run(GameSession s, List<string> report) => s.PlaceRod(Stack, X, Y);
}

public sealed record BreakRodCommand(string Stack, string Rod) : GameCommand
{
    internal override object? Run(GameSession s, List<string> report) => s.BreakRod(Stack, Rod);
}

// ---- объекты карты

public sealed record BuyItemCommand(string Stack, string Site, string ItemType) : GameCommand
{
    internal override object? Run(GameSession s, List<string> report) => s.BuyItem(Stack, Site, ItemType);
}

public sealed record BuySpellCommand(string Stack, string Site, string Spell) : GameCommand
{
    internal override object? Run(GameSession s, List<string> report) => s.BuySpell(Stack, Site, Spell);
}

public sealed record SellItemCommand(string Stack, string Site, string Item) : GameCommand
{
    internal override object? Run(GameSession s, List<string> report) => s.SellItem(Stack, Site, Item);
}

public sealed record HireMercenaryCommand(string Stack, string Site, string UnitType, int? Cell = null) : GameCommand
{
    internal override object? Run(GameSession s, List<string> report) => s.HireMercenary(Stack, Site, UnitType, Cell);
}

/// <summary>Обучение у тренера; значение — (отказ, заплачено).</summary>
public sealed record TrainCommand(string Stack, string Site, string Unit) : GameCommand
{
    internal override object? Run(GameSession s, List<string> report) => (s.Train(Stack, Site, Unit, out var paid), paid);
}

/// <summary>Действие вора; бросок — потоком карты (<see cref="GameSession.MapRng"/>).</summary>
public sealed record ThiefCommand(string Stack, string Target, ThiefAction Action, string? Pick = null) : GameCommand
{
    internal override object? Run(GameSession s, List<string> report) => s.Thief(Stack, Target, Action, s.MapRng, Pick);
}

public sealed record ThiefCancelCommand(string Stack, bool Empty) : GameCommand
{
    internal override object? Run(GameSession s, List<string> report)
    {
        s.ThiefCancel(Stack, Empty);
        return null;
    }
}

public sealed record EnterCommand(string Stack, string Site, bool Events = true, bool BattleWon = false) : GameCommand
{
    internal override object? Run(GameSession s, List<string> report) => s.Enter(Stack, Site, Events, BattleWon);
}

// ---- дипломатия

public sealed record GiveGoldCommand(string From, string To, long Amount) : GameCommand
{
    internal override object? Run(GameSession s, List<string> report) => s.GiveGold(From, To, Amount, report);
}

public sealed record BreakAllianceCommand(string Player, string Target) : GameCommand
{
    internal override object? Run(GameSession s, List<string> report) => s.BreakAllianceBy(Player, Target, report);
}

public sealed record ProposeAllianceCommand(string Player, string Target) : GameCommand
{
    internal override object? Run(GameSession s, List<string> report) => s.ProposeAllianceBy(Player, Target, report);
}

public sealed record AnswerAllianceCommand(string From, string Human, bool Accept) : GameCommand
{
    internal override object? Run(GameSession s, List<string> report) => s.AnswerAllianceOffer(From, Human, Accept, report);
}

public sealed record AnswerTradeCommand(string From, string Human, bool Accept) : GameCommand
{
    internal override object? Run(GameSession s, List<string> report) => s.AnswerTradeOffer(From, Human, Accept, report);
}

public sealed record AnswerExchangeCommand(string From, string Human, bool Accept) : GameCommand
{
    internal override object? Run(GameSession s, List<string> report) => s.AnswerExchangeOffer(From, Human, Accept, report);
}

public sealed record ProposeTradeCommand(string Player, string Target, AiGiftKind Kind, string Thing, int Gold, string? Fort = null) : GameCommand
{
    internal override object? Run(GameSession s, List<string> report) => s.ProposeTradeBy(Player, Target, Kind, Thing, Gold, Fort, report);
}

public sealed record ProposeExchangeMapCommand(string Player, string Target) : GameCommand
{
    internal override object? Run(GameSession s, List<string> report) => s.ProposeExchangeMapBy(Player, Target, report);
}

// ---- бой на карте (ходы внутри экрана боя — команды боя, следующий шаг задачи A)

public sealed record StartBattleCommand(string Attacker, string Target, bool Duel = false) : GameCommand
{
    internal override object? Run(GameSession s, List<string> report) => s.StartBattle(Attacker, Target, Duel);
}

/// <summary>Конец боя на открытом экране боя: журнал ходов модели боя (<see cref="TurnRecord"/>) и, если бой вёл экран по шагам, его
/// действия (<see cref="Play"/>). Экран играет бой на генераторе партии до команды; повтор журнала (<see cref="GameSession.Replaying"/>)
/// играет его здесь по действиям — и берёт те же числа.</summary>
public sealed record EndBattleCommand(IReadOnlyList<TurnRecord> Log, LastEdict.Data.Battle.BattlePlay? Play = null) : GameCommand
{
    internal override object? Run(GameSession s, List<string> report)
    {
        s.EndBattle(s.Replaying && Play is { } play ? s.ReplayBattle(play) : Log);
        return null;
    }

    internal override bool FollowsScreenDraws => Play is not null;
}

public sealed record CommitBattleCommand : GameCommand
{
    internal override object? Run(GameSession s, List<string> report) => s.CommitBattle();
}

public sealed record NextEventBattleCommand : GameCommand
{
    internal override object? Run(GameSession s, List<string> report) => s.NextEventBattle();
}

// ---- прочее

/// <summary>Попапы событий игрока показаны — сняты из состояния.</summary>
public sealed record TakeMessagesCommand(string Player) : GameCommand
{
    internal override object? Run(GameSession s, List<string> report) => s.TakeMessages(Player);
}

public sealed record WinCommand(string Player) : GameCommand
{
    internal override object? Run(GameSession s, List<string> report)
    {
        s.Win(Player);
        return null;
    }
}

public sealed record CheatExperienceCommand(string Player, bool FullLevel) : GameCommand
{
    internal override object? Run(GameSession s, List<string> report)
    {
        s.CheatExperience(Player, FullLevel);
        return null;
    }
}
