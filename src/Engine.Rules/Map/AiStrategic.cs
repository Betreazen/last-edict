namespace D2.Rules.Map;

/// <summary>Тип стратегического списка ИИ (<c>vft[1]</c> оценки и действия, поле <c>type</c> событий
/// <c>ai_budget</c>/<c>ai_strategic_step</c>). Ранг 1: <c>docs/synthetic-maps/analysis/O-re65-strategic.md</c>.</summary>
public enum AiStepType
{
    HireLeader = 0,
    UpgradeLeader = 1,
    HireUnit = 2,
    GrowCities = 3,
    Capital = 4,
    HealRevive = 5,
    SpellResearch = 6,
    Diplomacy = 8,
}

/// <summary>Действие стратегии: тип и <paramref name="Id"/> — ключ памяти дня (<c>vft[2]</c>, пишется
/// <c>0x44c825</c> после успешного исполнения); <paramref name="Target"/> — справочно (юнит, постройка…).
/// Счёт действия (HU 7000 + …, Heal 9000 + …) на выбор не влияет и не переносится.</summary>
public sealed record AiStepAction(AiStepType Type, string Id, string Target = "");

/// <summary>Оценка из списка: <paramref name="EvalId"/> — id оценки для памяти дня (пусто — не
/// блокируется); <paramref name="Evaluate"/> — <c>evaluate</c> оценки, добавляет 0..n действий.
/// Бюджет проверяет тот, кто строит список (<c>0x4465ba</c> стоит в построителях, до добавления оценки).</summary>
public sealed record AiEvaluation(string EvalId, Func<IReadOnlyList<AiStepAction>> Evaluate);

/// <summary>
/// Бюджет стратегии на день (<c>CAiBudget</c>): по типу — сумма, которую тип ещё может потратить, и казна
/// на момент последней траты. Отказ <c>0x4465ba</c> — цена больше бюджета типа хоть по одной валюте;
/// трата <c>0x4464ef</c>: <c>budget[type] −= (казна_начала + резерв·казна_магии_начала) − казна_сейчас</c>, казна
/// начала := текущая, казна магии начала (<c>+0x14</c>) не меняется. Резерв — байт <c>+0x2c</c> узла типа, он же
/// <c>flag</c> события <c>ai_budget</c> (у исследования — да: его бюджет — казна плюс казна магии). Ранг 1 по
/// корпусу 20260918-probe65: 266 из 266 списаний (<c>ai_budget_spend</c>), из них 22 с резервом.
/// Начальные суммы дня — <see cref="StartOf"/> (формула <c>J-map-ai-notes.md</c>, сверка — харнесс
/// <c>AiTurnStartHarnessTests.DayBudget_ByType</c>).
/// </summary>
public sealed record AiBudget(IReadOnlyDictionary<AiStepType, Bank> ByType, Bank TurnStart)
{
    /// <summary>Казна магии на начало дня (<c>+0x14</c>, <c>ai_budget.spell_bank</c>).</summary>
    public Bank SpellStart { get; init; } = Bank.Zero;

    /// <summary>Типы с резервом (<c>ai_budget.flag</c>).</summary>
    public IReadOnlySet<AiStepType> Reserved { get; init; } = new HashSet<AiStepType>();

    public bool Allows(AiStepType type, Bank cost) => !Economy.NotEnough(ByType[type], cost);

    public AiBudget With(AiStepType type, Bank value) =>
        this with { ByType = ByType.Where(p => p.Key != type).Append(new(type, value)).ToDictionary() };

    /// <summary>
    /// Начальная сумма типа на день (<c>0x446292</c>): <c>⌊казна·pct/100⌋</c> по каждой валюте,
    /// <c>pct = min(B_типа + 5·(день − день последней траты типа), 100)</c>; у HealRevive, SpellResearch и Diplomacy —
    /// 100 %, при резерве (<c>flag</c>, у исследования) база — казна плюс казна магии. Ранг 1 по корпусу (харнесс
    /// <c>AiTurnStartHarnessTests.DayBudget_ByType</c>, три партии, 549 строк на тип): типы 1–6 и 8 — 0 расхождений.
    /// <para>Базы найма лидера, найма юнита и столицы считают сами списки (слот 2) — в партии их даёт
    /// <c>AiEconomy.BudgetRules</c> (<see cref="AiStrategicRules.LeaderBudgetBase"/> и соседи); таблица
    /// <see cref="AiBudgetRules.Vanilla"/> с её «первым днём» — приближение для харнессов без карты.
    /// Резерв исследования включается, пока известных заклинаний меньше половины предела
    /// (<see cref="AiStrategicRules.ResearchReserve"/>, <c>0x44cd3e</c>).</para>
    /// </summary>
    /// <param name="lastSpend">День последней траты этого типа; 0 — не тратил.</param>
    /// <param name="reserve">Резерв типа (<c>ai_budget.flag</c>): база — казна плюс казна магии.</param>
    public static Bank StartOf(AiStepType type, int day, int lastSpend, Bank bank, Bank spellBank, AiBudgetRules rules,
                               bool reserve = false)
    {
        ArgumentNullException.ThrowIfNull(rules);
        if (!rules.Base.TryGetValue(type, out int b)) return reserve ? bank + spellBank : bank;
        // «Первый день» таблицы — приближение баз списков (день 1: найм юнита 100 — 0x44bc27, столица 90 + 5 — 0x446a8e).
        int pct = day == 1 && lastSpend == 0 && rules.FirstDay.TryGetValue(type, out int first)
            ? first
            : Math.Min(b + (rules.PerIdleDay * (day - lastSpend)), 100);
        return bank.Map(v => v * pct / 100);
    }

    /// <summary>Трата <c>0x4464ef</c>: разница казны — <c>0x43786b</c> (копия начала, <c>Bank::subtract 0x586578</c>), затем
    /// <c>budget −= разница</c> тем же <c>0x586578</c>; каждое вычитание обрезает валюты в [0, 9999] (<c>0x5865ac</c>…), сложение с
    /// казной магии — <c>0x586642</c> с той же обрезкой. Казна, выросшая с прошлой траты (сделка ИИ принята — золото пришло
    /// после списания её типа 8), бюджет поэтому не увеличивает: карта 15 (<c>20261018-port-synth</c>, seq 3656201) — Легионы,
    /// начало 90, казна после найма лидера 390, разница 0, бюджет найма лидера 418 остаётся (без обрезки было 718).</summary>
    public AiBudget Spend(AiStepType type, Bank bankNow)
    {
        var start = Reserved.Contains(type) ? Economy.Clamp(TurnStart + SpellStart) : TurnStart;
        var spent = Economy.Clamp(start - bankNow);
        return With(type, Economy.Clamp(ByType[type] - spent)) with { TurnStart = bankNow };
    }
}

/// <summary>Доли бюджета дня по типам (<c>GAI.B_*</c>, в процентах казны) и прибавка за день без траты.
/// <see cref="Vanilla"/> — таблица ванили: HireUnit 75, GrowCities 30, Capital 50, HireLeader 15 (<c>B_LEADERS</c>, см.
/// ponytail в <see cref="AiBudget.StartOf"/>), UpgradeLeader 0; HealRevive, SpellResearch, Diplomacy — всегда 100.</summary>
/// <param name="FirstDay">Доля в день 1 до первой траты, если она не по формуле.</param>
public sealed record AiBudgetRules(IReadOnlyDictionary<AiStepType, int> Base, IReadOnlyDictionary<AiStepType, int> FirstDay,
                                   int PerIdleDay = 5)
{
    public static readonly AiBudgetRules Vanilla = new(
        new Dictionary<AiStepType, int>
        {
            [AiStepType.HireLeader] = 15, [AiStepType.UpgradeLeader] = 0, [AiStepType.HireUnit] = 75,
            [AiStepType.GrowCities] = 30, [AiStepType.Capital] = 50,
        },
        new Dictionary<AiStepType, int> { [AiStepType.HireUnit] = 100, [AiStepType.HireLeader] = 95, [AiStepType.Capital] = 95 });
}

/// <summary>Исполненный шаг: проход (с 1), действие, исход исполнения.</summary>
public sealed record AiExecuted(int Pass, AiStepAction Action, bool Ok);

/// <summary>Итог хода стратегии: шаги, бюджет после трат, память дня (ключ — id и тип действия, значение — день).</summary>
public sealed record AiTurnResult(IReadOnlyList<AiExecuted> Steps, AiBudget Budget,
                                  IReadOnlyDictionary<(string Id, AiStepType Type), int> Memo);

/// <summary>
/// Экономический шаг ИИ на карте — <c>CAiStrategic::step 0x44e7e4</c> (ранг 1 по листингу 17.09.2026).
/// Шаг — машина состояний: за вызов строится один список или оценивается одна оценка; когда оценено всё,
/// исполняется <b>первое</b> действие (списки идут в порядке <see cref="ListOrder"/>, внутри списка — в порядке
/// добавления <c>0x44dd87</c>, без сортировки: счёт на выбор не влияет). После исполнения — сброс
/// <c>0x44e777</c> при любом исходе; при успехе ещё запись памяти и трата бюджета. Нет действий — стратегия
/// хода закончена. Порядок типов в ходе поэтому «плавает»: какие списки непусты на каждом проходе.
/// </summary>
public static class AiStrategic
{
    /// <summary>Порядок списков <c>0x44ec96</c> (= порядок событий <c>ai_budget</c>): 5, 1, 2, 0, 4, 3, 6, 8.</summary>
    public static readonly IReadOnlyList<AiStepType> ListOrder =
    [
        AiStepType.HealRevive, AiStepType.UpgradeLeader, AiStepType.HireUnit, AiStepType.HireLeader,
        AiStepType.Capital, AiStepType.GrowCities, AiStepType.SpellResearch, AiStepType.Diplomacy,
    ];

    /// <summary>Место типа в <see cref="ListOrder"/>.</summary>
    public static int Rank(AiStepType type)
    {
        for (int i = 0; i < ListOrder.Count; i++)
            if (ListOrder[i] == type) return i;
        throw new ArgumentOutOfRangeException(nameof(type), type, "не стратегический список");
    }

    /// <summary>
    /// Оценки, которые память дня блокирует: у них id оценки (<c>vft[2]</c>) совпадает с id действия — цель
    /// (<c>id4</c> обоих): улучшение лидера — отряд, найм лидера — город, рост — деревня, дипломатия — игрок.
    /// У найма юнита, столицы, лечения и исследования id различаются, и повтор в тот же день не блокируется.
    /// Ранг 1 по корпусу 20260918-probe65: 31 оценка дипломатии с запомненной целью — ни одного действия;
    /// 64 оценки найма юнита и 1 лечения с запомненным <c>id4</c> — действие есть. Вызывающий ставит
    /// <see cref="AiEvaluation.EvalId"/> = цель для этих типов и пусто для прочих.
    /// </summary>
    public static bool MemoBlocks(AiStepType type) =>
        type is AiStepType.UpgradeLeader or AiStepType.HireLeader or AiStepType.GrowCities or AiStepType.Diplomacy;

    /// <summary>
    /// Один проход: все списки по порядку, все оценки (кроме запомненных сегодня — id оценки не пуст и
    /// <c>memo[(id, type)] ≥ день</c>). Возвращает действия в порядке добавления; исполняется первое.
    /// Оценки вычисляются все, даже когда действие уже есть, — так тратятся броски дипломатии (run17 q09).
    /// </summary>
    /// <param name="lists">Построитель списка по типу; получает текущий бюджет для проверки <c>0x4465ba</c>.</param>
    public static IReadOnlyList<AiStepAction> Pass(int day, Func<AiStepType, AiBudget, IEnumerable<AiEvaluation>> lists,
        AiBudget budget, IReadOnlyDictionary<(string Id, AiStepType Type), int> memo)
    {
        ArgumentNullException.ThrowIfNull(lists);
        ArgumentNullException.ThrowIfNull(memo);
        var actions = new List<AiStepAction>();
        foreach (var type in ListOrder)
            foreach (var eval in lists(type, budget))
            {
                if (eval.EvalId.Length > 0 && memo.TryGetValue((eval.EvalId, type), out int seen) && seen >= day)
                    continue;
                actions.AddRange(eval.Evaluate());
            }
        return actions;
    }

    /// <summary>
    /// Стратегия одного хода ИИ: проходы <see cref="Pass"/>, пока есть действия; исполняется первое.
    /// Успех — <c>memo[(id, type)] = день</c> и <see cref="AiBudget.Spend"/> по казне после исполнения.
    /// Неудача — сброс без памяти и без траты: <b>ранг 1</b> по листингу <c>0x44e7e4</c> — исполнение
    /// <c>call [eax+0xc]</c> у <c>0x44ea89</c>, затем <c>test al, al; je 0x44eaca</c> перепрыгивает разом и
    /// запись памяти (<c>0x44c825</c> в <c>+0x2c</c>), и отметку траты (<c>0x4464ef</c> в <c>+0x24</c>);
    /// сброс итераторов <c>0x44e777</c> стоит за меткой и потому идёт при любом исходе. Движок может крутиться
    /// вечно, здесь — исключение по <paramref name="maxPasses"/>.
    /// </summary>
    /// <param name="execute">Исполнение действия (<c>vft[3]</c>), <c>true</c> — успех.</param>
    /// <param name="bank">Казна игрока сейчас (после исполнения).</param>
    /// <param name="maxPasses">Предел проходов вместо вечного цикла движка.</param>
    public static AiTurnResult RunTurn(int day, Func<AiStepType, AiBudget, IEnumerable<AiEvaluation>> lists,
        Func<AiStepAction, bool> execute, Func<Bank> bank, AiBudget budget,
        IReadOnlyDictionary<(string Id, AiStepType Type), int> memo, int maxPasses = 1000)
    {
        ArgumentNullException.ThrowIfNull(execute);
        ArgumentNullException.ThrowIfNull(bank);
        ArgumentNullException.ThrowIfNull(budget);
        ArgumentNullException.ThrowIfNull(memo);
        var remembered = memo.ToDictionary();
        var steps = new List<AiExecuted>();
        for (int pass = 1; ; pass++)
        {
            if (pass > maxPasses)
                throw new InvalidOperationException(
                    $"стратегия дня {day} не закончилась за {maxPasses} проходов (неудачное исполнение повторяется)");
            var actions = Pass(day, lists, budget, remembered);
            if (actions.Count == 0) return new AiTurnResult(steps, budget, remembered);
            var first = actions[0];
            bool ok = execute(first);
            steps.Add(new AiExecuted(pass, first, ok));
            if (!ok) continue;
            remembered[(first.Id, first.Type)] = day;
            budget = budget.Spend(first.Type, bank());
        }
    }

    /// <summary>
    /// Список 8 — дипломатия (<c>0x4486f5</c>): по оценке на цель в порядке id, id оценки и действия — цель
    /// (одно действие на цель в день), <see cref="AiStepAction.Target"/> — вид действия (<see cref="AiDiploAction"/>).
    /// Решение даёт вызывающий, обычно <see cref="AiDiplomacy.Evaluate"/>.
    /// </summary>
    // ponytail: из решения берётся одно действие. У движка ненависть и подарок одной цели — два действия со счётом
    // 1000 (0x6cf580), исполняется первое добавленное (ненависть), подарок снимает память дня цели — исход тот же;
    // счёт выбор не решает (см. AiDiplomacy.RunPasses, харнесс Execution_FirstAddedAction).
    public static IEnumerable<AiEvaluation> DiplomacyEvaluations(IEnumerable<string> targets,
                                                                 Func<string, AiDiploDecision> decide)
    {
        ArgumentNullException.ThrowIfNull(targets);
        ArgumentNullException.ThrowIfNull(decide);
        return targets.Order(StringComparer.Ordinal).Select(target => new AiEvaluation(target, () =>
            decide(target).Action is var action && action == AiDiploAction.None
                ? []
                : [new AiStepAction(AiStepType.Diplomacy, target, action.ToString())]));
    }
}
