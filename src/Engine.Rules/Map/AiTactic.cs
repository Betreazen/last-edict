using System.Collections.Immutable;
using LastEdict.Rules.Battle;

namespace LastEdict.Rules.Map;

/// <summary>Тип тактического действия отряда ИИ — слот 1 действия (<c>IAiTacticAction</c>), значение прошито в
/// каждом построителе списка (<c>CAi*::build</c>). Ранг 1 по таблице виртуальных функций (разбор зонда 6.5,
/// <c>docs/synthetic-maps/analysis/N-re65-tactics.md</c> в disciples-modding). У 0x18 построителя нет вовсе.</summary>
public enum AiActionType
{
    TakeBag = 0x00,
    DefendCity = 0x01,
    AttackCity = 0x02,
    AttackStack = 0x03,
    SummonAttack = 0x04,
    VisitRuin = 0x05,
    VisitMage = 0x06,
    VisitMerchant = 0x07,
    VisitTemple = 0x08,
    VisitTrainer = 0x09,
    HireUnit = 0x0a,
    HireMercs = 0x0b,
    AttackRod = 0x0c,
    NobleCity = 0x0d,
    NobleStack = 0x0e,
    HealRevive = 0x0f,
    StayInside = 0x10,
    PlantRod = 0x11,
    MoveToStack = 0x12,
    SecureCity = 0x13,
    MoveToLocation = 0x14,
    Guard = 0x15,
    DefendParty = 0x16,
    DefendLocation = 0x17,
    AttackStackBezerk = 0x19,
    AttackCityBezerk = 0x1a,
    AttackParty = 0x1b,
    AttackStackRoam = 0x1c,
    AttackCityRoam = 0x1d,
    TakeBagRoam = 0x1e,
    EnterCityRoam = 0x1f,
    AssistMove = 0x20,
    NobleMerchant = 0x21,
    GiveItems = 0x22,
    Retreat = 0x23,

    /// <summary>Смена приказа (<c>CAiChangeOrderAction</c>, vtable <c>0x6cfbec</c>; оценщик <c>CAiChangeOrderEval</c>
    /// <c>0x44fcb5</c>). Номер порта: слот типа у действия движка зонд читает как 0x1b, фильтр приказа проверяет тип
    /// построителя, который его поставил.</summary>
    ChangeOrder = 0x40,
}

/// <summary>Действие-кандидат с его счётом — узел контейнера <c>CAiTactic +0x51b8</c> (<c>+8</c> действие,
/// <c>+0x10</c> double счёт).</summary>
public readonly record struct AiScored<T>(T Action, double Score);

/// <summary>
/// Выбор действия отряда ИИ на карте (<c>CAiTactic::step</c> <c>0x466388</c>): фильтр приказа, вставка кандидата,
/// выбор лучшего. Всё детерминировано: максимум счёта, при равенстве — первый вставленный. Броски есть только у
/// выбора заклинания перед атакой (<c>0x472142</c>) и у торговца (<c>0x46b800</c>) — сюда они не входят.
/// Счёт кандидатов — <see cref="AiTacticScore"/>.
/// </summary>
public static class AiTactic
{
    /// <summary>Сколько лучших кандидатов проходят проверку пути и остаются в списке (<c>push 4</c> перед
    /// обрезкой <c>0x46868f</c>, счётчик <c>cmp [ebp−0x10], 4</c> у <c>0x4666f9</c>). Ранг 1.</summary>
    public const int TopCount = 4;

    /// <summary>«Нет счёта» — double <c>0x835a28</c>, статический инициализатор <c>0x468ace</c> кладёт туда
    /// <c>0x6d2100</c> = −2000666,666. С ним, а не с нулём, сравнивают вставка (<c>0x46872a</c>), проверка пути
    /// фазы 3 (<c>0x4666b5</c>) и обе поправки (<c>0x466d68</c>, <c>0x466ce7</c>); оценщики начинают счёт с него
    /// (<c>mov ecx, [0x835a28]</c>). Ранг 1; зонд 6.5: 14 выброшенных вставок — все с этим счётом, 9 вставок
    /// со счётом 0 — вставлены.</summary>
    public const double NoScore = -2000666.666;

    /// <summary>Статус «не пошёл» (<c>ai_tactic_result</c> 1: ни одного шага): сегодня действие больше не
    /// предлагается — память фазы 2 (<c>0x462135</c>) пропускает его до вставки. Ранг 2 (отчёт N + корпус human:
    /// повторная оценка в тот же день кандидата не пишет).</summary>
    public const int StatusStuck = 1;

    /// <summary>Статус «сегодня уже провалено» — кандидат выбрасывается при вставке (<c>0x46871e</c>). Ранг 1.</summary>
    public const int StatusFailed = 3;

    /// <summary>Статус 4 — счёт ×0,5 (<c>0x461a86</c>, константа <c>0x6d16b8</c>). Ранг 1.</summary>
    public const int StatusHalf = 4;

    /// <summary>Статус 5 — вчера продвинулся: ×1,25 при вставке (<c>0x46878a</c>, <c>0x6d20e0</c>) и в счёте
    /// (<c>0x461aa2</c>); после исполнения — захват цели (<c>0x469a37</c>). Ранг 1.</summary>
    public const int StatusProgress = 5;

    /// <summary>Множитель статуса 5 (<c>0x6d20e0</c> = <c>0x6d16a8</c> = 1,25). Ранг 1.</summary>
    public const double ProgressFactor = 1.25;

    /// <summary>
    /// Фильтр приказа (фабрика <c>0x465c90</c>, классы <c>CAiActionFilter*</c>, слот 2 — полная проверка). Ранг 1
    /// по листингам фильтров:
    /// <list type="bullet">
    /// <item><c>L_NORMAL</c> (<c>0x44f5b6</c>) — всё, кроме 1, 0x13–0x17 и 0x19–0x1d;</item>
    /// <item><c>L_STAND</c> (<c>0x5ee07b</c>) — ничего;</item>
    /// <item><c>L_GUARD</c> — 0x15; <c>L_DEFEND_STACK</c> — 0x16; <c>L_BEZERK</c> — 0x19/0x1a;
    /// <c>L_ROAM</c> — 0x1c–0x1f;</item>
    /// <item><c>L_ATTACK_STACK</c> — 0x1b, <c>L_SECURE_CITY</c> — 0x13, <c>L_MOVE_TO_LOCATION</c> — 0x14:
    /// только с целью приказа;</item>
    /// <item><c>L_ASSIST</c> (<c>0x44f649</c>) — 0x20 и 0x22 с целью приказа;</item>
    /// <item><c>L_STEAL</c> (<c>0x44fc1a</c>) — 0x21, 0, 0x22, 0x23;</item>
    /// <item><c>L_DEFEND_CITY</c> (<c>0x44f7de</c>) — 1 с целью приказа, 3 не дальше 7 по Чебышёву от точки
    /// фильтра — это позиция САМОГО отряда (<c>0x5ed9b2(map, id отряда)</c> у <c>0x465f75</c>, конструктор
    /// <c>0x44f767</c> кладёт её в <c>+8/+0xc</c>), а не крепости приказа;</item>
    /// <item><c>L_DEFEND_LOCATION</c> (<c>0x44f8c8</c>) — 0x17 не дальше радиуса <c>GVars +0x11c</c> (геттер
    /// <c>0x5879f4</c>, сравнение <c>jg</c> — радиус включён); радиус <i>действия</i> <c>DefendParty</c> берётся
    /// из соседнего поля <c>+0x118</c> и делится пополам, см. <see cref="AiTacticEval.DefendPartyRadius"/>.</item>
    /// </list>
    /// ponytail: приказы без класса фильтра (<c>None</c>, 12 <c>L_EXPLORE</c>) отказывают во всём — фабрика для них
    /// не разобрана; потолок — такой приказ на карте, поднимать по листингу <c>0x465c90</c>.
    /// </summary>
    /// <param name="targetIsOrderTarget">Цель действия (слот 3) — цель приказа (<c>CMidStack +0xac</c>).</param>
    /// <param name="orderPointDistance">Чебышёв от точки фильтра до точки действия (слот 4); у
    /// <c>L_DEFEND_CITY</c> точка фильтра — позиция отряда.</param>
    /// <param name="defendRadius">Радиус <c>L_DEFEND_LOCATION</c> из <c>GVars</c>.</param>
    public static bool Admits(StackOrder order, AiActionType type, bool targetIsOrderTarget = false,
                              int orderPointDistance = 0, int defendRadius = 0)
    {
        int t = (int)type;
        return order switch
        {
            StackOrder.Normal => !(t == 1 || t is >= 0x13 and <= 0x17 || t is >= 0x19 and <= 0x1d),
            StackOrder.Guard => type == AiActionType.Guard,
            StackOrder.AttackStack => type == AiActionType.AttackParty && targetIsOrderTarget,
            StackOrder.DefendStack => type == AiActionType.DefendParty,
            StackOrder.SecureCity => type == AiActionType.SecureCity && targetIsOrderTarget,
            StackOrder.Roam => t is >= 0x1c and <= 0x1f,
            StackOrder.MoveToLocation => type == AiActionType.MoveToLocation && targetIsOrderTarget,
            StackOrder.DefendLocation => type == AiActionType.DefendLocation && orderPointDistance <= defendRadius,
            StackOrder.Bezerk => type is AiActionType.AttackStackBezerk or AiActionType.AttackCityBezerk,
            StackOrder.Assist => type is AiActionType.AssistMove or AiActionType.GiveItems && targetIsOrderTarget,
            StackOrder.Steal => type is AiActionType.NobleMerchant or AiActionType.TakeBag
                                    or AiActionType.GiveItems or AiActionType.Retreat,
            StackOrder.DefendCity => type == AiActionType.DefendCity && targetIsOrderTarget
                                     || type == AiActionType.AttackStack && orderPointDistance <= DefendCityReach,
            _ => false,
        };
    }

    /// <summary>Радиус атаки отряда при <c>L_DEFEND_CITY</c> (<c>cmp [eax], 7</c> у <c>0x44f843</c>). Ранг 1.</summary>
    public const int DefendCityReach = 7;

    /// <summary>
    /// Вставка кандидата (<c>0x4686cf</c>, ранг 1): статус сегодня <see cref="StatusFailed"/> или счёт
    /// <see cref="NoScore"/> — выброс (счёт 0 вставляется); статус вчера (<c>turn − 1</c>)
    /// <see cref="StatusProgress"/> — счёт ×1,25 (статусы — <see cref="AiTacticMemo"/>). Возвращает новый список:
    /// исходный не меняется. Порядок вставки сохраняется — от него зависит выбор при равенстве.
    /// </summary>
    public static IReadOnlyList<AiScored<T>> Insert<T>(IReadOnlyList<AiScored<T>> list, T action, double score,
                                                       int statusToday, int statusYesterday)
    {
        ArgumentNullException.ThrowIfNull(list);
        if (statusToday == StatusFailed || score == NoScore) return list;
        if (statusYesterday == StatusProgress) score *= ProgressFactor;
        return [.. list, new AiScored<T>(action, score)];
    }

    /// <summary>
    /// Фаза 3 (<c>0x4665d4</c>, ранг 1): устойчивая сортировка по убыванию (<c>0x468860</c>); по порядку каждому
    /// кандидату пересчитывается счёт проверкой пути (слот 6 действия, <c>call [edx+0x18]</c>, получает текущий
    /// счёт) и записывается в узел; <see cref="NoScore"/> — кандидат удаляется (<c>0x46880e</c>); стоп после
    /// <see cref="TopCount"/> оставшихся.
    /// Затем обрезка до четырёх (<c>0x46868f</c>) и снова сортировка. Первый элемент итога — выбор (фаза 4
    /// исполняет его слотом 7).
    /// </summary>
    /// <param name="rescore">Проверка пути действия: новый счёт (<see cref="NoScore"/> — пути нет),
    /// <see cref="AiTacticEval.PathRescore"/>.</param>
    public static IReadOnlyList<AiScored<T>> Top<T>(IReadOnlyList<AiScored<T>> inserted,
                                                    Func<T, double, double> rescore)
    {
        ArgumentNullException.ThrowIfNull(inserted);
        ArgumentNullException.ThrowIfNull(rescore);
        var kept = new List<AiScored<T>>(TopCount);
        foreach (var c in SortDescending(inserted))
        {
            if (kept.Count == TopCount) break;
            double s = rescore(c.Action, c.Score);
            if (s != NoScore) kept.Add(c with { Score = s });
        }
        return SortDescending(kept);
    }

    /// <summary>Выбранное действие — первое из <see cref="Top{T}"/>; пусто — отряд ничего не делает.</summary>
    public static AiScored<T>? Pick<T>(IReadOnlyList<AiScored<T>> inserted, Func<T, double, double> rescore)
        where T : notnull
    {
        var top = Top(inserted, rescore);
        return top.Count > 0 ? top[0] : null;
    }

    /// <summary>LINQ-сортировка устойчива — равные сохраняют порядок вставки, как у слияния списка движка.</summary>
    private static IReadOnlyList<AiScored<T>> SortDescending<T>(IEnumerable<AiScored<T>> items) =>
        [.. items.OrderByDescending(c => c.Score)];
}

/// <summary>Запись памяти тактики: отряд, цель, точка, день и статус.</summary>
public sealed record AiTacticRecord(string Stack, string Target, int X, int Y, int Turn, int Status);

/// <summary>
/// Память результатов тактики (<c>CAiTactic</c>, хеш-таблица): запись
/// <c>0x461fb2(stack, target, pos, turn, status)</c> после исполнения, чтение <c>0x462135(stack, target, pos, turn)</c> при вставке и в счёте. Ранг 1 по листингам:
/// запись ищет перебором узел с теми же отрядом, целью и точкой (<c>0x462011…0x462045</c>) и, если нашла,
/// ПЕРЕЗАПИСЫВАЕТ в нём день и статус (<c>0x46207a</c>, <c>0x462085</c>), иначе вставляет новый; чтение ищет по
/// ключу вместе с днём (<c>0x4625ab</c>), нет узла — 0. Поэтому на одну тройку хранится один день: вчерашний
/// статус 5 пропадает, как только отряд записал сегодняшний (зонд 6.5: 40 вставок из 40 при вчерашнем 5 и уже
/// сделанной сегодня записи — без ×1,25).
/// Неизменяемая: <see cref="With"/> возвращает новую память.
/// </summary>
public sealed class AiTacticMemo
{
    public static readonly AiTacticMemo Empty =
        new(ImmutableDictionary<(string, string, int, int), (int, int)>.Empty);

    private readonly ImmutableDictionary<(string Stack, string Target, int X, int Y), (int Turn, int Status)> _last;

    private AiTacticMemo(ImmutableDictionary<(string, string, int, int), (int, int)> last) => _last = last;

    public int Status(string stack, string target, int x, int y, int turn) =>
        _last.TryGetValue((Key(stack), Key(target), x, y), out var r) && r.Turn == turn ? r.Status : 0;

    public AiTacticMemo With(string stack, string target, int x, int y, int turn, int status) =>
        new(_last.SetItem((Key(stack), Key(target), x, y), (turn, status)));

    /// <summary>Записи памяти — для сохранения партии, по порядку ключа: словарь неупорядочен, а хеш строк в .NET свой у каждого процесса,
    /// и без порядка одна и та же память давала разное сохранение (и хеш состояния, <c>StateHash</c>) от запуска к запуску.</summary>
    public IEnumerable<AiTacticRecord> Records =>
        _last.OrderBy(p => p.Key.Stack, StringComparer.Ordinal).ThenBy(p => p.Key.Target, StringComparer.Ordinal)
             .ThenBy(p => p.Key.X).ThenBy(p => p.Key.Y)
             .Select(p => new AiTacticRecord(p.Key.Stack, p.Key.Target, p.Key.X, p.Key.Y, p.Value.Turn, p.Value.Status));

    /// <summary>Память из записей сохранения.</summary>
    public static AiTacticMemo From(IEnumerable<AiTacticRecord> records) =>
        (records ?? throw new ArgumentNullException(nameof(records)))
            .Aggregate(Empty, (m, r) => m.With(r.Stack, r.Target, r.X, r.Y, r.Turn, r.Status));

    private static string Key(string id) => id.ToUpperInvariant();
}
