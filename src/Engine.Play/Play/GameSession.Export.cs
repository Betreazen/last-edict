using D2.Data.Leaders;
using D2.Rules.Battle;
using D2.Rules.Magic;
using D2.Rules.Map;
using D2.Rules.Units;

namespace D2.Data.Play;

/// <summary>Вывоз героя после победы (<c>CCmdExportLeaderMsg</c>; разбор — <c>docs/day-cycle.md</c> §14, заполнение записи
/// <c>0x408307</c>, ранг 1, если не сказано иное).</summary>
public sealed partial class GameSession
{
    /// <summary>Сколько первых записей <c>Grace.LEADER_1..4, NOBLE</c> — нанимаемые герои (вора не вывозят, <c>0x581fb9</c>).</summary>
    private const int HiredLeaderKinds = 4;

    /// <summary>Порядок источников в списках иммунитетов файла (<c>0x408790</c>): оружие, разум, жизнь, смерть, огонь, вода, воздух,
    /// земля (номера <c>LattS</c>).</summary>
    private static readonly int[] ExportSources = [0, 1, 2, 3, 4, 5, 7, 6];

    /// <summary>Порядок классов в списках иммунитетов файла (<c>0x4088ed</c>, 23 класса).</summary>
    private static readonly AttackClass[] ExportClasses =
    [
        AttackClass.Paralyze, AttackClass.Petrify, AttackClass.Damage, AttackClass.Drain, AttackClass.Heal, AttackClass.Fear,
        AttackClass.BoostDamage, AttackClass.LowerDamage, AttackClass.LowerInitiative, AttackClass.Poison, AttackClass.Frostbite,
        AttackClass.Blister, AttackClass.BestowWards, AttackClass.Shatter, AttackClass.Revive, AttackClass.DrainOverflow,
        AttackClass.Cure, AttackClass.Summon, AttackClass.DrainLevel, AttackClass.GiveAttack, AttackClass.Doppelganger,
        AttackClass.TransformSelf, AttackClass.TransformOther,
    ];

    /// <summary>Навыки, которые попадают в файл (<c>0x408c8a</c>, в этом порядке); критического удара (17) среди них нет.</summary>
    private static readonly int[] ExportAbilities = [3, 6, 7, 8, 9, 10, 13, 14, 15, 16];

    /// <summary>Спросить ли о вывозе при конце партии (<c>0x489a63</c>): зритель победил, сценарий последний в кампании (одиночный —
    /// всегда) и есть кого вывезти.</summary>
    public bool ExportOffered(string viewer) =>
        Book is not null && Won(viewer) == true && LastChapter && ExportCandidates(viewer).Count > 0;

    /// <summary>Победа (<c>L_WIN</c>, <c>WinScenario</c> <c>0x421043</c>): засчитывается только первая; <see cref="GameInfo.NoWinner"/> —
    /// партия кончилась без победителя. Как у <c>L_WIN</c>, у всех игроков снимаются записи литья заклинаний и жезлов
    /// (<c>CEffectRemoveSpell(игрок, 9999)</c>, <c>0x43b950</c>). Проверки сценой зовут её напрямую (шаг <c>win</c>).</summary>
    public void Win(string playerId)
    {
        ArgumentException.ThrowIfNullOrEmpty(playerId);
        if (!Over) State = State with { Info = State.Info with { Winner = playerId } };
        State = State with { Looks = Retold(Looks, With(Map, spellCasts: [], itemCasts: []), Looks.Stacks) };
    }

    /// <summary>Лидер отряда <paramref name="stackId"/> для попапа с <c>BOOLVALUE</c>: собственное имя и глобальный тип; нет отряда или
    /// лидера — пусто.</summary>
    private (string Name, string Type, bool Own) PopupLeader(string stackId)
    {
        if (Book is not { } book || stackId.Length == 0 || !Map.Stacks.TryGetValue(stackId, out var stack)
            || !Map.Units.TryGetValue(stack.LeaderId, out var leader)) return ("", "", false);
        // 0x5e97dc: собственное имя — только у героя (IUsLeader, не вор и не призванный); прочим — имя типа (его ставит окно).
        return OwnName(leader.Id, leader.TypeId) is { } name
            ? (name, GlobalType(book, leader.TypeId), true) : ("", GlobalType(book, leader.TypeId), false);
    }

    /// <summary>Собственное имя юнита для <c>CMidUnit::getName</c> (<c>0x5e97dc</c>, ранг 1 по листингу): у реализации
    /// <c>IUsStackLeader</c>, кроме <c>IUsNoble</c> и <c>IUsSummon</c> (<c>0x40a41f</c>, <c>0x41b399</c>, <c>0x431d8c</c>), то есть у
    /// категории «лидер», — имя юнита из сохранения как есть, даже пустое; у прочих (воин, вор, призванный) — null: движок берёт имя типа
    /// (реализация типа, слот +4). Тип — текущий (<paramref name="typeId"/>: у превращённого в бою — облик). Имя порт хранит у отряда
    /// (<see cref="D2.Data.Map.StackLook.LeaderName"/>), поэтому собственное есть только у лидера отряда; герой не во главе отряда — null.</summary>
    public string? OwnName(string unitId, string typeId)
    {
        ArgumentNullException.ThrowIfNull(unitId);
        ArgumentNullException.ThrowIfNull(typeId);
        if (Book is not { } book || book.CategoryOf(typeId) != UnitCategory.Leader) return null;
        if (Map.Stacks.Values.FirstOrDefault(s => s.LeaderId.Equals(unitId, StringComparison.OrdinalIgnoreCase)) is not { } stack) return null;
        return (Looks.Stacks.FirstOrDefault(s => s.Id.Equals(stack.Id, StringComparison.OrdinalIgnoreCase))?.LeaderName ?? "").Trim();
    }

    /// <summary>Сценарий последний в своей кампании; у одиночного — всегда.</summary>
    public bool LastChapter => State.Info.Campaign?.LastChapter ?? true;

    /// <summary>Отряды игрока, чьих героев предлагает окно вывоза (<c>0x4ca350</c>): все отряды игрока (и в городах), у которых
    /// глобальный базовый тип лидера — нанимаемый герой его расы (<c>Grace.LEADER_1..4</c>), в порядке отрядов карты.</summary>
    public IReadOnlyList<MapStack> ExportCandidates(string playerId)
    {
        var book = RequireBook();
        if (!Map.Players.TryGetValue(playerId, out var player)) return [];
        var kinds = book.RaceLeaders.GetValueOrDefault(player.RaceId)?.Take(HiredLeaderKinds).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (kinds is null) return [];
        return [.. Map.Stacks.Values.Where(s => s.Owner.Equals(playerId, StringComparison.OrdinalIgnoreCase)
                                             && Map.Units.TryGetValue(s.LeaderId, out var u) && kinds.Contains(GlobalType(book, u.TypeId)))];
    }

    /// <summary>Отказ «ОК» окна вывоза (id строки) или null: герой не нанят в своём городе (<c>CREAT_LVL</c> ≠ 1) — <c>X005TA0726</c>;
    /// раса типа героя не играбельная — <c>X005TA0729</c>.</summary>
    public string? ExportRefusal(MapStack stack)
    {
        ArgumentNullException.ThrowIfNull(stack);
        if (stack.CreationLevel != 1) return "X005TA0726";
        var book = RequireBook();
        bool playable = Map.Units.TryGetValue(stack.LeaderId, out var leader)
                        && book.Extras.Units.TryGetValue(GlobalType(book, leader.TypeId), out var extra)
                        && book.Economy.Races.TryGetValue(extra.Race, out var race) && race.Playable;
        return playable ? null : "X005TA0729";
    }

    /// <summary>
    /// Запись героя отряда для файла героя (<see cref="ExportedLeader"/>). Характеристики — с цепочкой модификаторов лидера, как
    /// в бою (предметы и заклинания входят): здоровье — максимум, атаки — действующие, ход, обзор и лидерство — лидера, без
    /// надбавок отряда. Модификаторы — без предметов и заклинаний. Имя — своё имя героя (пусто — пусто).
    /// <paramref name="attackName"/> — название атаки по id.
    /// </summary>
    public ExportedLeader ExportOf(MapStack stack, Func<string, string> attackName)
    {
        ArgumentNullException.ThrowIfNull(stack);
        ArgumentNullException.ThrowIfNull(attackName);
        var book = RequireBook();
        var leader = Map.Units[stack.LeaderId];
        var grown = book.Grown(leader.TypeId, leader.Level) ?? throw new InvalidDataException($"нет типа {leader.TypeId}");
        string type = GlobalType(book, leader.TypeId);
        var worn = WornItems(book, stack);
        var chain = leader.Modifiers.Select(m => book.Rules.Modifiers?.GetValueOrDefault(m)).OfType<Modifier>().ToList();
        var stats = EffectiveStats.Of(grown.Type, chain);
        var first = EffectiveStats.OfAttack(book.Rules.Attacks[grown.Type.AttackId], chain).Attack;
        var second = book.Rules.Attacks.TryGetValue(grown.Type.Attack2Id, out var a2) ? EffectiveStats.OfAttack(a2, chain).Attack : null;
        int cap = Formulas.DamageMax(book.Rules.Constants, type);
        var move = MoveOptionsOf(stack);
        var sources = Immunities(grown.Type.ImmunitiesBySource, stats.Immunities);
        var classes = Immunities(grown.Type.ImmunitiesByClass, stats.ClassImmunities);
        var abilities = LeaderAbilitiesOf(stack);
        var temporary = book.Spells.Values.Select(s => s.ModifierId).Where(m => m.Length > 0)
                            .Concat(worn.Select(w => book.Items.GetValueOrDefault(w.ItemType)?.EquipModifierId ?? ""))
                            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        string race = Map.Players.TryGetValue(stack.Owner, out var owner) ? owner.RaceId : "";
        return new ExportedLeader
        {
            Name = Looks.Stacks.FirstOrDefault(s => s.Id.Equals(stack.Id, StringComparison.OrdinalIgnoreCase))?.LeaderName ?? "",
            Race = book.Economy.Races.TryGetValue(race, out var info) ? info.RaceType : 0,
            UnitType = type,
            Level = leader.Level,
            Leadership = Leadership(stack),
            Xp = leader.Xp,
            XpNext = grown.Type.XpNext,
            BattlesWon = stack.BattlesWon,
            HitPoints = stats.HitPoints,
            Armor = stats.Armor,
            Attack = new LeaderAttack(Up(first.Id), (int)first.Class, attackName(first.Id), first.Power, AttackLevel(first),
                                      Damage(first, cap), first.Source, first.Initiative, first.Reach),
            Attack2 = Up(second?.Id ?? ""),
            Attack2Name = second is null ? "" : attackName(second.Id),
            Attack2Power = second?.Power ?? 0,
            AttacksTwice = grown.Type.AttackTwice,
            Move = DayAllowance(stack),
            Scout = ScoutRange(stack),
            Negotiate = book.Extras.Units.TryGetValue(type, out var extra) ? extra.Negotiate : 0,
            TerrainBonus = move.PlainsBonus && move.ForestBonus && move.WaterBonus,
            ImmuneSources = [.. ExportSources.Where(s => sources.GetValueOrDefault(s) == ImmunityLevel.Always)],
            ImmuneClasses = [.. ExportClasses.Select(c => (int)c).Where(c => classes.GetValueOrDefault(c) == ImmunityLevel.Always)],
            WardSources = [.. ExportSources.Where(s => sources.GetValueOrDefault(s) == ImmunityLevel.Once)],
            WardClasses = [.. ExportClasses.Select(c => (int)c).Where(c => classes.GetValueOrDefault(c) == ImmunityLevel.Once)],
            Abilities = [.. ExportAbilities.Where(abilities.Contains)],
            Modifiers = [.. leader.Modifiers.Where(m => !temporary.Contains(m)).Select(Up)],
            Equipment = worn,
            UpgradeCount = stack.UpgradeCount,
        };
    }

    /// <summary>Уровень иммунитета по категории: врождённый и от цепочки — сильнейший.</summary>
    private static Dictionary<int, ImmunityLevel> Immunities(IReadOnlyList<ImmunityGrant>? innate, IReadOnlyList<ImmunityGrant> chain) =>
        (innate ?? []).Concat(chain).GroupBy(g => g.Category).ToDictionary(g => g.Key, g => g.Max(x => x.Level));

    /// <summary>«Уровень» атаки в файле (<c>0x40904a</c>): только у усиления, ослабления и замедления.</summary>
    private static int AttackLevel(AttackType a) =>
        a.Class is AttackClass.BoostDamage or AttackClass.LowerDamage or AttackClass.LowerInitiative ? a.Level : 0;

    /// <summary>Урон в файле (<c>0x61db00</c>): урон и высасывание — не больше потолка типа (<see cref="Formulas.DamageMax"/>),
    /// лечение — сила лечения, прочие классы — 0.</summary>
    private static int Damage(AttackType a, int cap) => a.Class switch
    {
        AttackClass.Damage or AttackClass.Drain or AttackClass.DrainOverflow => Math.Min(a.QtyDamage, cap),
        AttackClass.Heal => a.QtyHeal,
        _ => 0,
    };

    /// <summary>Надетое (<c>0x408f56</c>): слот, глобальный тип предмета и заряды — только у талисмана.</summary>
    private IReadOnlyList<LeaderItem> WornItems(GameBook book, MapStack stack) =>
        [.. stack.Equipment.Select((id, slot) => (id, slot)).Where(p => p.id.Length > 0 && Map.ItemTypes.ContainsKey(p.id))
             .Select(p => (p.slot, p.id, type: Map.ItemTypes[p.id]))
             .Select(p => new LeaderItem(p.slot, Up(p.type),
                 book.Items.GetValueOrDefault(p.type)?.Category == ItemCategory.Talisman ? Map.TalismanCharges.GetValueOrDefault(p.id) : 0))];

    /// <summary>Глобальный тип юнита, id заглавными — как в файле героя движка (<c>G000UU0019</c>).</summary>
    private static string GlobalType(GameBook book, string typeId) =>
        Up(book.TryGlobal(typeId, out var id, out _) ? id : typeId);

    private static string Up(string id) => id.ToUpperInvariant();
}
