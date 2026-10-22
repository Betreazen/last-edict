using LastEdict.Rules.Battle;

namespace LastEdict.Rules.Map;

/// <summary>Веса тактики ИИ — столбцы <c>I_*</c> строки <c>GAI.dbf</c> (строка — <c>CATEGORY</c>, отношение ИИ).
/// Геттеры движка — <c>0x58f716…0x58f79b</c> (<c>GAI +0x5c…+0xa0</c>), <c>I_H_MERCS</c> (<c>+0x88</c>) читает общий
/// геттер <c>0x56ee29</c>. <see cref="NeutralCities"/>/<see cref="NeutralStacks"/> — столбцы <c>I_N_CITIES</c>/
/// <c>I_N_STACK</c>: это веса ЗНАТНОГО (<c>NobleCity</c>, <c>NobleMerchant</c>), не нейтралов. Прямая проекция,
/// ранг 1.</summary>
public sealed record AiWeights(int Category, int CitiesN, int CitiesR, int CitiesP, int StacksN, int StacksR,
                               int StacksP, int Bags, int Ruins, int Towers, int Merchant, int HireUnits,
                               int HireMercs, int Heal, int PlantRod, int AttackRod, int NeutralCities,
                               int NeutralStacks, int Trainer);

/// <summary>Вид оценщика атаки — у каждого свой порог (слот 6): обычный <c>0x45137e</c>/<c>0x4505c9</c>, берсерк
/// <c>0x451617</c>/<c>0x450825</c>, бродяга <c>0x451841</c>/<c>0x4509ce</c>, «на отряд приказа»
/// <c>0x450c65</c> (только отряд).</summary>
public enum AttackEvalKind { Normal, Bezerk, Roam, Party }

/// <summary>Факты об атаке отряда, которые счёт <c>0x453585</c> берёт у карты и у других подсистем.</summary>
/// <param name="Status">Память тактики на сегодня (<c>0x453ac0</c> → <c>0x462135</c>).</param>
/// <param name="TargetNeutral">Хозяин цели — нейтрал (<c>0x5e5358</c>).</param>
/// <param name="TargetFlag88">Хозяин цели — человек: байт <c>CMidPlayer +0x88</c> (<c>0x5e532b</c>; ранг 2 — поле
/// <c>isHuman</c> по заголовку UnveN).</param>
/// <param name="TargetRaceNeutral"><c>0x5e53a6</c> от расы хозяина цели: истина, если раса НЕ из пяти играбельных
/// (Human, Heretic, Undead, Dwarf, Elf) — порог 0,7 вместо 0,6.</param>
/// <param name="Influence">Значение сетки у цели, уже ×4 (<see cref="AiTacticScore.Influence"/>).</param>
/// <param name="Distance">Чебышёв до цели (слот 12 обёртки = <c>fild</c> слота 13 <c>0x4678cb</c>).</param>
/// <param name="Adjust">Множитель поправок: <see cref="AiTacticScore.AttackModifiers"/> от 1.
/// <see cref="AiTactic.NoScore"/> здесь означает «поправки уже уронили счёт» (вид записи 1 вне плана).</param>
/// <param name="Strength">Сила <see cref="AiTacticScore.Strength"/> (<c>0x5dda25</c>).</param>
/// <param name="NeutralNear">Нейтрал сторожит мешок — <see cref="AiTacticScore.GuardsBag"/> (<c>0x4538a1</c>).</param>
/// <param name="NeutralCount">Слот 12 <c>IAiTacticInternal</c> (<c>0x466f57</c>: число отрядов игрока с особым
/// лидером, ранг 3) — ветка ×0,75 требует больше одного.</param>
/// <param name="AttackerNeutral">Хозяин АТАКУЮЩЕГО — нейтрал (<c>0x5e5358</c> в пороге <c>0x45137e</c>).</param>
/// <param name="Kind">Вид оценщика — порог.</param>
public sealed record AttackStackFacts(int Status, bool TargetNeutral, bool TargetFlag88, bool TargetRaceNeutral,
                                      int Influence, double Distance, double Adjust, double Strength,
                                      bool NeutralNear, int NeutralCount, bool AttackerNeutral = false,
                                      AttackEvalKind Kind = AttackEvalKind.Normal);

/// <summary>Поправки атаки отряда <c>0x453aee</c>.</summary>
/// <param name="TargetOwnerIsTacticEnemy">Хозяин цели совпал с игроком в <c>CAiTactic +0x51c0</c> (слот 11
/// <c>0x4670c9</c>; ранг 2 — главный враг) — ×1,5.</param>
/// <param name="InOperationTargets">Цель есть в плане <c>CAiTactic +0x5158</c> (<c>0x472fbb</c>).</param>
/// <param name="IsMoveToTarget">Цель — отряд «подойти» того же плана (<c>0x473049</c>): ×1,4, иначе ×1,15.</param>
/// <param name="RecordKind1">Поле вида записи кандидата <c>+4</c> = 1 (<c>cmp [edi+4], 1</c> у <c>0x453cf7</c>;
/// ранг 1 по листингу, смысл вида — ранг 3): цели НЕТ в плане — счёт становится <see cref="AiTactic.NoScore"/>
/// (движок пишет туда <c>0x835a28</c>, а не ноль), то есть кандидат выбрасывается при вставке.</param>
/// <param name="TargetInside">Цель в крепости (<c>+0x94</c>); вместе с не-нейтралом — ×0,75.</param>
public sealed record AttackModifierFacts(bool TargetOwnerIsTacticEnemy, bool InOperationTargets,
                                         bool IsMoveToTarget, bool RecordKind1, bool TargetInside,
                                         bool TargetNeutral, bool CapturedByOther, int StatusToday,
                                         int StatusYesterday);

/// <summary>Факты об атаке крепости (<c>0x45275f</c>).</summary>
/// <param name="Hostile">Не союзник (<c>0x4652bf</c> = ¬<c>0x5f7ebd</c>).</param>
/// <param name="TargetHuman">Хозяин крепости — человек (<c>0x5e532b</c>).</param>
/// <param name="MaxInfluence">Максимум <c>4g</c> по клеткам крепости (<see cref="AiTacticScore.MaxInfluence"/>).</param>
/// <param name="Distance">Чебышёв до позиции крепости (<c>0x5e94eb</c>).</param>
/// <param name="Occupied">В крепости стоит отряд (<c>+0x8c</c>).</param>
/// <param name="GarrisonCount"><c>0x5f509a</c> от гарнизона (<c>+0x3c</c>).</param>
/// <param name="Strength">Сила против крепости <c>0x5ddcb3</c>.</param>
/// <param name="SummonAttacker">Атакует отряд класса <c>CAiStackSummon</c> (<c>dynamic_cast</c> у <c>0x452add</c>).</param>
/// <param name="AttackerRaceNeutral"><c>0x5e53a6</c> от расы хозяина атакующего.</param>
/// <param name="TargetRaceNeutral"><c>0x5e53a6</c> от расы хозяина крепости.</param>
public sealed record AttackCityFacts(bool Hostile, bool TargetHuman, int MaxInfluence, double Distance,
                                     bool Occupied, int GarrisonCount, double Strength, bool CapturedByOther,
                                     int StatusToday, int StatusYesterday, bool SummonAttacker = false,
                                     AttackEvalKind Kind = AttackEvalKind.Normal, bool AttackerRaceNeutral = false,
                                     bool TargetRaceNeutral = false);

/// <summary>
/// Счёт тактических действий ИИ на карте и выбор клетки жезла. Константы — <c>.rdata</c> GOG 3.01, прочитаны по
/// адресам из листингов (разбор 6.5, <c>N-re65-tactics.md</c>). Все функции чистые.
/// <para><b>Шаблон счёта</b> (листинг <c>0x461978</c>, ранг 1): <c>S = I + 4g − 3d</c>, затем множители хвоста
/// <see cref="Tail"/>. Проверок «шаг только при ненулевом счёте» нет: движок сравнивает счёт с
/// <see cref="AiTactic.NoScore"/>, а не с нулём (корпус 6.5 — <c>AiTacticEvalHarnessTests</c>), и «нет кандидата»
/// возвращается тем же флагом.</para>
/// </summary>
public static class AiTacticScore
{

    /// <summary>Радиус столицы (<c>push 0xd</c> у <c>0x461d43</c>) и база радиуса города. Ранг 1.</summary>
    public const int CapitalRadius = 13;

    /// <summary>Потолок клетки сетки — байт (<c>cmp eax, 0xff</c> у <c>0x461ef4</c>). Ранг 1.</summary>
    public const int GridCap = 255;

    private const double DistanceWeight = 3.0;      // 0x6d16a0, 0x6d04b8
    private const double GoldCrystalFactor = 1.25;  // 0x6d16a8
    private const double CapturedFactor = 0.1;      // 0x6d16b0
    private const double HalfFactor = 0.5;          // 0x6d16b8
    private const double StrengthHalf = 0.5;        // 0x6edfb8
    private const double NearNeutral = 3.0;         // 0x6d04b8
    private const double NearOther = 7.0;           // 0x6d04c8
    private const double WeakPenalty = 10.0;        // 0x6d04d0
    private const double NeutralMinStrength = 0.7;  // 0x6d0078
    private const double HumanMinStrength = 0.5;    // 0x6d0080
    private const double RaceMinStrength = 0.7;     // 0x6d0078
    private const double OtherMinStrength = 0.6;    // 0x6d0090
    private const double WeakFactor = 0.01;         // 0x6d0088
    private const double SureStrength = 0.9;        // 0x6d04d8
    private const double SureDistance = 10.0;       // 0x6d04d0
    private const double SureFactor = 1.3;          // 0x6d04e0
    private const double CrowdFactor = 0.75;        // 0x6d04f0
    private const double InSetBonus = 100.0;        // 0x6d1e90 = −100, вычитается
    private const double PartyBonus = -0.0001;      // 0x6cff58, вычитается
    private const double EnemyFactor = 1.5;         // 0x6d04f8
    private const double MoveTargetFactor = 1.4;    // 0x6d0500
    private const double PlanFactor = 1.15;         // 0x6d0508
    private const double CityStrongFactor = 1.5;    // 0x6d0368
    private const double SummonReach = 5.0;         // 0x6d0370 (и 6 = 0x6d0378)
    private const double SummonFactor = 0.16;       // 0x6d0380

    /// <summary>Клетка отсчёта крепости для сетки — <c>0x5e9563(pos, size)</c> = <c>pos + size − 1</c>: правый
    /// нижний угол, а не центр. Ранг 1.</summary>
    public static (int X, int Y) AnchorCell(int x, int y, int size) => (x + size - 1, y + size - 1);

    /// <summary>Радиус своей крепости: столица 13, город <c>13 + [+0x90]</c>. Ранг 1 — ветвление и 13; поле
    /// <c>+0x90</c> — <c>CMidVillage::tierLevel</c> (ранг 2: заголовок UnveN, хозяин <c>+0x84</c> и деревня с бунтом
    /// <c>+0x98</c> сходятся с листингами).</summary>
    public static int FortRadius(bool capital, int cityField90) => capital ? CapitalRadius : CapitalRadius + cityField90;

    /// <summary>
    /// Сетка влияния игрока (<c>0x461c85</c> → слияние <c>0x461dfc</c>, ранг 1). Строится РАЗ на ход тактики: по
    /// корпусу 6.5 деревня, захваченная посреди хода, в сетку этого хода не попадает, а выросшая до хода — попадает.
    /// По каждой СВОЕЙ крепости
    /// (<c>+0x84</c> хозяин) к клеткам <c>x ∈ [max(cx−r, 0), min(cx+r, size))</c> (верхняя граница не включена) и
    /// так же по <c>y</c> прибавляется <c>max(0, r − trunc(√(dx²+dy²)))</c>, сумма — не выше 255. Корень — табличный движка
    /// (<see cref="FastSqrt"/>), не точный. Сетка — по размеру карты (<see cref="InfluenceMap"/>; у движка — 144², потолок его карт).
    /// </summary>
    public static InfluenceMap InfluenceGrid(int mapSize, IEnumerable<(int X, int Y, int Radius)> forts)
    {
        ArgumentNullException.ThrowIfNull(forts);
        var grid = new InfluenceMap(mapSize);
        foreach (var (cx, cy, r) in forts)
            for (int y = Math.Max(cy - r, 0); y < Math.Min(cy + r, mapSize); y++)
                for (int x = Math.Max(cx - r, 0); x < Math.Min(cx + r, mapSize); x++)
                {
                    int dx = cx - x, dy = cy - y;
                    int add = Math.Max(0, r - FastSqrt(dx * dx + dy * dy));
                    grid[x, y] = (byte)Math.Min(GridCap, grid[x, y] + add);
                }
        return grid;
    }

    /// <summary>
    /// Корень сетки (<c>0x51f35e</c>, ранг 1) с отсечением <c>_ftol</c>: табличный корень float — порядок пополам
    /// (<c>sar</c>), мантисса результата — 7 бит из таблицы <c>0x6e0890</c> по старшим 7 битам мантиссы входа и
    /// чётности порядка: <c>t[i] = ⌊(√((1 + (i &amp; 127)/128)·(i ≥ 128 ? 2 : 1)) − 1)·128⌋</c> (формула сверена со
    /// всеми 256 словами таблицы). Итог — <c>2^⌊e/2⌋·(1 + t/128)</c>; до 800 от точного <c>⌊√n⌋</c> он отличается
    /// только у точных квадратов от 17² и соседей (√289 → 16, √529…531 → 22): они и занижаются. Ранг 1 по листингу;
    /// в корпусе 6.5 клеток с таким расстоянием не встретилось, расхождение по нему не измерено.
    /// </summary>
    public static int FastSqrt(int n)
    {
        if (n <= 0) return 0;
        int e = System.Numerics.BitOperations.Log2((uint)n);
        int top = (e >= 7 ? n >> (e - 7) : n << (7 - e)) & 127;
        return ((128 + SqrtTable[top | (e & 1) << 7]) << (e >> 1)) >> 7;
    }

    /// <summary>Тот же табличный корень <c>0x51f35e</c> без отсечения (float): <c>2^⌊e/2⌋·(1 + t/128)</c>. Эвристика A* пути
    /// (<c>0x680a6b</c>) берёт его как есть, во float; ноль — ноль.</summary>
    public static float FastSqrtF(int n)
    {
        if (n <= 0) return 0f;
        int e = System.Numerics.BitOperations.Log2((uint)n);
        int top = (e >= 7 ? n >> (e - 7) : n << (7 - e)) & 127;
        return ((128 + SqrtTable[top | (e & 1) << 7]) << (e >> 1)) / 128f;
    }

    private static readonly int[] SqrtTable = [.. Enumerable.Range(0, 256).Select(i =>
        (int)Math.Floor((Math.Sqrt((1 + (i & 127) / 128.0) * (i >= 128 ? 2 : 1)) - 1) * 128))];

    /// <summary><c>0x461db8</c>: байт сетки ×4 — это и есть <c>4g</c> шаблона. Ранг 1.</summary>
    public static int Influence(InfluenceMap grid, int x, int y)
    {
        ArgumentNullException.ThrowIfNull(grid);
        return grid[x, y] * 4;
    }

    /// <summary>Шаблон без хвоста: <c>I</c>, <c>+4g</c> при <c>I ≠ 0</c>, <c>−3d</c> при <c>S ≠ 0</c>.</summary>
    /// <param name="influence">Уже ×4 (<see cref="Influence"/>).</param>
    public static double Generic(int weight, int influence, double distance) =>
        weight + influence - DistanceWeight * distance;

    /// <summary>Хвост шаблона (<c>0x461a53…0x461ac2</c>, ранг 1): цель захвачена другим отрядом
    /// (<c>0x469a75</c>) ×0,1; статус 4 ×0,5; статус 5 ×1,25.</summary>
    public static double Tail(double s, bool capturedByOther, int status)
    {
        if (capturedByOther) s *= CapturedFactor;
        if (status == AiTactic.StatusHalf) s *= HalfFactor;
        if (status == AiTactic.StatusProgress) s *= AiTactic.ProgressFactor;
        return s;
    }

    /// <summary>Счёт постановки жезла (<c>0x461978</c>, ранг 1): <c>I_P_ROD + 4g − 3d</c> В ТОЧКЕ КРИСТАЛЛА
    /// (<c>[+0xc]</c>, не в выбранной клетке жезла — сетка и расстояние берутся у кристалла), ×1,25 у кристалла
    /// золота (<c>[+0x2c] == L_GOLD</c>), затем хвост со статусом на СЕГОДНЯ. Корпус 6.5: 671 из 671
    /// (<c>AiTacticEvalHarnessTests</c>); с точкой жезла вместо кристалла расходились 459 из 465 на ai-q09.</summary>
    public static double PlantRod(AiWeights w, int influence, double distance, bool goldCrystal,
                                  bool capturedByOther, int status)
    {
        ArgumentNullException.ThrowIfNull(w);
        double s = Generic(w.PlantRod, influence, distance);
        if (goldCrystal) s *= GoldCrystalFactor;
        return Tail(s, capturedByOther, status);
    }

    /// <summary>
    /// Сила для решения об атаке (<c>0x5dda25</c>, ранг 1): своя группа не может ранить (<c>0x5dcd53</c>) — 0;
    /// иначе <c>X = A + C</c> при <c>A ≥ 0,5·B</c>, иначе <c>max(A, C)</c>; <c>p = min(1, 0,5·B/X)</c>. Сравнение
    /// с единицей — как в движке: NaN проходит как есть.
    /// </summary>
    public static double Strength(double a, double b, double c, bool canDamage)
    {
        if (!canDamage) return 0.0;
        double x = a >= b * StrengthHalf ? a + c : (a < c ? c : a);
        double r = b / x * StrengthHalf;
        return r >= 1.0 ? 1.0 : r;
    }

    /// <summary>
    /// Сила против крепости (<c>0x5ddcb3</c>, хвост <c>0x5ddefd</c>, ранг 1): <c>p = B / D · 0,5</c>, не больше единицы;
    /// сравнение <c>jb</c>, поэтому NaN (0/0) проходит как есть, а деление на пустую защиту даёт единицу. Может ли своя
    /// группа ранить гарнизон (<c>0x5dcd53</c>, иначе 0) и из чего складываются B и D — решает вызывающий.
    /// </summary>
    public static double CityStrength(double b, double d)
    {
        double r = b / d * StrengthHalf;
        return r >= 1.0 ? 1.0 : r;
    }

    /// <summary>
    /// Атака отряда (<c>0x453585</c>, ранг 1 по листингу): статус 3 — 0; база <c>I_STACKS_N</c> (нейтрал) /
    /// <c>_P</c> (<c>+0x88</c>) / <c>_R</c>; шаблон; ×2 ближе 3 клеток (нейтрал) или 7; поправки
    /// <see cref="AttackModifiers"/>; сила: нейтрал <c>S·p</c>, прочие <c>S − 10(1 − p)</c>; порог
    /// <see cref="Threshold"/> по виду оценщика; у нейтральной цели ×1,3 (сторожит мешок, <c>p &gt; 0,9</c>,
    /// <c>d &lt; 10</c>) или ×0,75 (не сторожит, <c>p = 1</c>, число больше одного).
    /// </summary>
    public static double AttackStack(AiWeights w, AttackStackFacts f)
    {
        ArgumentNullException.ThrowIfNull(w);
        ArgumentNullException.ThrowIfNull(f);
        if (f.Status == AiTactic.StatusFailed) return AiTactic.NoScore;
        int weight = f.TargetNeutral ? w.StacksN : f.TargetFlag88 ? w.StacksP : w.StacksR;
        double s = Generic(weight, f.Influence, f.Distance);
        if (f.Distance < (f.TargetNeutral ? NearNeutral : NearOther)) s *= 2;
        // Поправка «вид записи 1 вне плана» кладёт в счёт сам флаг (0x453d01), и дальше все шаги 0x453aee и
        // 0x453760 его пропускают: кандидат доходит до вставки с NoScore и выбрасывается.
        if (f.Adjust == AiTactic.NoScore) return AiTactic.NoScore;
        s *= f.Adjust;
        double p = f.Strength;
        s = f.TargetNeutral ? s * p : s - WeakPenalty * (1.0 - p);
        s = Threshold(s, p, f.Kind, f.AttackerNeutral, f.TargetFlag88, f.TargetRaceNeutral);
        // Хвост нейтральной цели (0x453817, 0x45385d) тоже сравнивает счёт с флагом, а не с нулём.
        if (!f.TargetNeutral || s == AiTactic.NoScore) return s;
        if (f.NeutralNear)
            return p > SureStrength && f.Distance < SureDistance ? s * SureFactor : s;
        // ponytail: p == 1.0 — точное сравнение double, как fcomp движка у 0x45384a.
        return p == 1.0 && f.NeutralCount > 1 ? s * CrowdFactor : s;
    }

    /// <summary>Порог силы атаки отряда (слот 6 оценщика, ранг 1). Обычный <c>0x45137e</c>: атакует НЕЙТРАЛ
    /// (хозяин отряда <c>[this+0x10]</c>, то есть атакующего) — ниже 0,7 счёт 0; иначе ниже 0,5 (цель — человек),
    /// 0,7 (раса хозяина цели неиграбельна) или 0,6 — ×0,01. Берсерк <c>0x451617</c>: <c>p = 0</c> — 0. Бродяга
    /// <c>0x451841</c>: ниже 0,7 — 0. На отряд приказа <c>0x450c65</c>: ненулевой счёт + 0,0001.</summary>
    public static double Threshold(double s, double p, AttackEvalKind kind, bool attackerNeutral, bool targetHuman,
                                   bool targetRaceNeutral) => kind switch
    {
        AttackEvalKind.Bezerk => p == 0 ? AiTactic.NoScore : s,
        AttackEvalKind.Roam => p < NeutralMinStrength ? AiTactic.NoScore : s,
        AttackEvalKind.Party => s - PartyBonus,
        _ when attackerNeutral => p < NeutralMinStrength ? AiTactic.NoScore : s,
        _ => p < (targetHuman ? HumanMinStrength : targetRaceNeutral ? RaceMinStrength : OtherMinStrength)
            ? s * WeakFactor : s,
    };

    /// <summary>Байт «порог взят» обычного оценщика атаки отряда (<c>0x45137e</c>, пятый аргумент): атакует нейтрал —
    /// <c>p ≥ 0,7</c>; иначе <c>p</c> не ниже порога <see cref="Threshold"/> (0,5 / 0,7 / 0,6). Счёт он не трогает, его
    /// читает подготовка к атаке (<see cref="AiBeforeAttack"/>). Только для обычного оценщика: у бродяги и берсерка
    /// порог свой (<see cref="Threshold"/>), и подготовки у них нет.</summary>
    public static bool ThresholdPassed(double p, bool attackerNeutral, bool targetHuman, bool targetRaceNeutral) =>
        p >= (attackerNeutral ? NeutralMinStrength
              : targetHuman ? HumanMinStrength : targetRaceNeutral ? RaceMinStrength : OtherMinStrength);

    /// <summary>Байт «порог взят» обычного оценщика атаки крепости (<c>0x4505c9</c>): <c>p</c> не ниже порога
    /// <see cref="CityThreshold"/>. Только для обычного оценщика (не бродяги и не берсерка).</summary>
    public static bool CityThresholdPassed(double p, bool attackerRaceNeutral, bool targetHuman, bool targetRaceNeutral) =>
        p >= (attackerRaceNeutral ? RaceMinStrength
              : targetHuman ? OtherMinStrength : targetRaceNeutral ? RaceMinStrength : OtherMinStrength);

    /// <summary>Порог силы атаки крепости (ранг 1, сверено с листингом построчно 18.09). Обычный <c>0x4505c9</c>:
    /// раса хозяина атакующего неиграбельна (<c>0x5e53a6</c> от <c>0x5e4727</c>) — ниже 0,7 ×0,01; эта ветка идёт
    /// ПЕРВОЙ и важнее «цель — человек». Иначе хозяин крепости (<c>+0x84</c> → <c>MidPlayer +0x88</c>) человек —
    /// ниже 0,6 (<c>0x6cfcc8</c>; у отряда на этом месте 0,5), раса хозяина крепости неиграбельна — ниже 0,7
    /// (<c>0x6cfcb8</c>), прочие — ниже 0,6. Сравнения строгие (<c>jb</c> и <c>jae</c>): ровно порог проходит.
    /// Берсерк <c>0x450825</c> (<c>p == 0</c> → <see cref="AiTactic.NoScore"/>) и бродяга <c>0x4509ce</c>
    /// (ниже 0,7 → <c>NoScore</c>) — как у отряда.
    /// <para>Пятый аргумент движка — байт «порог взят» (<c>[ebp+0x18]</c>, 1 при проходе, 0 при ×0,01 и при
    /// <c>NoScore</c>); он уходит наружу из <see cref="AttackCity"/> (<c>[ebp+0x20]</c>) и на сам счёт не влияет,
    /// поэтому не переносится. Само ×0,01 движок применяет только к счёту, не равному <c>NoScore</c>
    /// (<c>0x450670</c>) — до порога счёт им быть не может.</para></summary>
    public static double CityThreshold(double s, double p, AttackEvalKind kind, bool attackerRaceNeutral,
                                       bool targetHuman, bool targetRaceNeutral) => kind switch
    {
        AttackEvalKind.Bezerk => p == 0 ? AiTactic.NoScore : s,
        AttackEvalKind.Roam => p < NeutralMinStrength ? AiTactic.NoScore : s,
        _ => p < (attackerRaceNeutral ? RaceMinStrength
                  : targetHuman ? OtherMinStrength
                  : targetRaceNeutral ? RaceMinStrength : OtherMinStrength)
            ? s * WeakFactor : s,
    };

    /// <summary>
    /// Поправки атаки отряда (<c>0x453aee</c>, ранг 1): главный враг — ×1,5; в плане — ×1,4 (цель «подойти») или
    /// ×1,15, вне плана при записи вида 1 — 0; цель в крепости и не нейтрал — ×0,75; захват ×0,1; статус сегодня 4
    /// — ×0,5; вчера 4 — ×0,5, вчера 5 — ×1,25. Все шаги — множители, поэтому <c>AttackModifiers(1, f)</c> и есть
    /// <see cref="AttackStackFacts.Adjust"/>.
    /// </summary>
    public static double AttackModifiers(double s, AttackModifierFacts f)
    {
        ArgumentNullException.ThrowIfNull(f);
        if (f.TargetOwnerIsTacticEnemy && s != AiTactic.NoScore) s *= EnemyFactor;
        if (f.InOperationTargets)
        {
            if (s != AiTactic.NoScore) s *= f.IsMoveToTarget ? MoveTargetFactor : PlanFactor;
        }
        else if (f.RecordKind1) s = AiTactic.NoScore;
        if (f.TargetInside && !f.TargetNeutral && s != AiTactic.NoScore) s *= CrowdFactor;
        if (f.CapturedByOther && s != AiTactic.NoScore) s *= CapturedFactor;
        if (f.StatusToday == AiTactic.StatusHalf && s != AiTactic.NoScore) s *= HalfFactor;
        if (f.StatusYesterday == AiTactic.StatusHalf && s != AiTactic.NoScore) s *= HalfFactor;
        if (f.StatusYesterday == AiTactic.StatusProgress && s != AiTactic.NoScore) s *= AiTactic.ProgressFactor;
        return s;
    }

    /// <summary>
    /// «Нейтрал сторожит мешок» (<c>0x4538a1</c>, ранг 1; тип 0x27 — <c>IdType::Bag</c>): при действующем приказе
    /// <c>L_DEFEND_LOCATION</c> — мешок в прямоугольнике локации (<c>0x453a08</c>, границы включены); иначе при
    /// приказе не <c>L_STAND</c> и не <c>L_GUARD</c> — мешок в коробке <see cref="BagBox"/>.
    /// </summary>
    public static bool GuardsBag(StackOrder effectiveOrder, bool bagInLocation, bool bagInBox) =>
        effectiveOrder == StackOrder.DefendLocation && bagInLocation
        || effectiveOrder is not (StackOrder.Stand or StackOrder.Guard) && bagInBox;

    /// <summary>Коробка поиска мешка у нейтрала: <c>[max(x−4, 0), min(x+5, size))</c>, так же по y (верхние
    /// границы не включены; размер карты — <c>0x5f61f2</c> <c>+0xc</c>).</summary>
    public static (int X0, int X1, int Y0, int Y1) BagBox(int x, int y, int mapSize) =>
        (Math.Max(x - 4, 0), Math.Min(x + 5, mapSize), Math.Max(y - 4, 0), Math.Min(y + 5, mapSize));

    /// <summary><c>0x452b40</c>: максимум <see cref="Influence"/> по клеткам крепости
    /// <c>[x, x+sizeX) × [y, y+sizeY)</c>, начиная с 0.</summary>
    public static int MaxInfluence(InfluenceMap grid, int x, int y, int sizeX, int sizeY)
    {
        ArgumentNullException.ThrowIfNull(grid);
        int best = 0;
        for (int yy = y; yy < y + sizeY; yy++)
            for (int xx = x; xx < x + sizeX; xx++)
                best = Math.Max(best, Influence(grid, xx, yy));
        return best;
    }

    /// <summary>
    /// Атака крепости (<c>0x45275f</c>, ранг 1): союзник — 0; вес <c>I_CITIES_P</c> (человек) или <c>_R</c> —
    /// ветка <c>I_CITIES_N</c> у <c>0x4527c6</c> тут же перетирается (<c>0x4527d7</c>), нейтральный вес не
    /// используется; 0 — 0; есть влияние — <c>2S + max4g</c>; <c>−3d</c>; пустая крепость без гарнизона ближе 10 —
    /// ×2; <c>p = 0</c> — 0; ×p; порог <see cref="CityThreshold"/>, 0 — 0; захват ×0,1; статус сегодня 4 — ×0,5;
    /// <c>p &gt; 0,9</c> — ×1,5 (строго, <c>0x452a43</c> <c>jbe</c>); ВЧЕРА статус 4 — ×0,5 (не 5); атакует призыв:
    /// дальше 5 — 0 (строго, <c>0x452af2</c> <c>jbe</c>), иначе <c>(6 − d)·S·0,16</c>. Оценщик (<c>0x452baf</c>)
    /// затем ставит заклинания и помощь перед атакой, каждое следующее — на 0,0001 меньше.
    /// <para>Сверено с листингом построчно 18.09: расхождений с портом нет. В корпусе 20260918-probe65 кандидатов
    /// этого типа не было, так что проверка — только листинг и <c>AiTacticTests</c>.</para>
    /// </summary>
    public static double AttackCity(AiWeights w, AttackCityFacts f)
    {
        ArgumentNullException.ThrowIfNull(w);
        ArgumentNullException.ThrowIfNull(f);
        if (!f.Hostile) return AiTactic.NoScore;
        double s = f.TargetHuman ? w.CitiesP : w.CitiesR;
        if (s == 0) return AiTactic.NoScore;   // 0x452805: вес сравнивается с настоящим нулём 0x6d0330
        if (f.MaxInfluence > 0) s = s + s + f.MaxInfluence;
        double d = f.Distance;
        s -= DistanceWeight * d;
        if (!f.Occupied && f.GarrisonCount == 0 && d < SureDistance) s += s;
        double p = f.Strength;
        if (p == 0) return AiTactic.NoScore;   // 0x452924: настоящий ноль 0x6d0330
        s *= p;
        s = CityThreshold(s, p, f.Kind, f.AttackerRaceNeutral, f.TargetHuman, f.TargetRaceNeutral);
        // 0x452992 и 0x4529aa: после порога проверяются ОБА — и флаг, и настоящий ноль.
        if (s == AiTactic.NoScore || s == 0) return AiTactic.NoScore;
        if (f.CapturedByOther) s *= CapturedFactor;
        if (f.StatusToday == AiTactic.StatusHalf) s *= HalfFactor;
        if (p > SureStrength) s *= CityStrongFactor;
        if (f.StatusYesterday == AiTactic.StatusHalf) s *= HalfFactor;
        if (!f.SummonAttacker) return s;
        return d > SummonReach ? AiTactic.NoScore : (SummonReach + 1 - d) * s * SummonFactor;
    }

    /// <summary>Постоянный счёт действий без шаблона (ранг 1): <c>DefendCity</c> 150 (225 — отряд уже в крепости,
    /// <c>0x454afd</c>), <c>Guard</c>, <c>MoveToLocation</c>, <c>DefendParty</c>, <c>DefendLocation</c>,
    /// <c>AssistMove</c>, <c>MoveToStack</c>, <c>SecureCity</c> 150; <c>GiveItems</c> 500; <c>Retreat</c> 2000.
    /// Прочие — <c>null</c>: у <c>EnterCityRoam</c> счёт не постоянный, см.
    /// <see cref="AiTacticEval.EnterCityRoam"/>. Условия выдачи — <see cref="AiTacticEval"/>.</summary>
    public static double? Constant(AiActionType type, bool defendCityHigh = false) => type switch
    {
        AiActionType.DefendCity => defendCityHigh ? 225 : 150,
        AiActionType.Guard or AiActionType.MoveToLocation or AiActionType.DefendParty
            or AiActionType.DefendLocation or AiActionType.AssistMove or AiActionType.MoveToStack
            or AiActionType.SecureCity => 150,
        AiActionType.GiveItems => 500,
        AiActionType.Retreat => 2000,
        _ => null,
    };

    /// <summary>Порог силы бегства — <see cref="AiTacticEval.RetreatStrength"/>.</summary>
    public const double RetreatStrength = AiTacticEval.RetreatStrength;

    /// <summary>Приоритет редактора по умолчанию (<c>AIPRIORITY</c> = 3): поправки нет (<c>0x466e2a</c>).</summary>
    public const int DefaultPriority = 3;

    /// <summary>
    /// Поправка приоритета редактора (<c>0x466d5c</c>, ранг 1): счёт <see cref="AiTactic.NoScore"/> или у цели нет
    /// приоритета (<c>IAiPriority</c>) — без изменений; приоритет 0 — <see cref="AiTactic.NoScore"/>, если цель
    /// действия не цель приказа (<c>+0xac</c>); 1…6 — −66, −33, 0, +33, +66, +100 (<c>0x6d1eb0…0x6d1e90</c>,
    /// вычитаются). Сравнение — с <see cref="AiTactic.NoScore"/> (<c>0x835a28</c>), не с нулём: счёт 0 поправка
    /// двигает. Единственный вызов — цикл фазы 2 (<c>0x466894</c>), до +100 множества целей (<c>0x4668ae</c>); отряд
    /// <c>+0xac</c> — выбранный отряд окна (слот 2 <c>IAiStack</c> <c>CAiTactic +0x5170</c>), не исполнитель действия.
    /// </summary>
    public static double EditorPriority(double s, int? priority, bool targetIsOrderTarget)
    {
        if (s == AiTactic.NoScore || priority is not { } p) return s;
        return p switch
        {
            0 => targetIsOrderTarget ? s : AiTactic.NoScore,
            1 => s - 66,
            2 => s - 33,
            4 => s + 33,
            5 => s + 66,
            6 => s + 100,
            _ => s,
        };
    }

    /// <summary>+100 цели из множества <c>CAiTactic +0x51dc</c> (<c>0x466cd8</c>, ранг 1): поиск цели действия в
    /// множестве (<c>0x53207e</c>) и сравнение с концом — нашлась, значит <c>s − (−100)</c>. Счёт
    /// <see cref="AiTactic.NoScore"/> не трогается (<c>0x466ce7</c>, <c>0x466d3e</c>).</summary>
    public static double TargetSetBonus(double s, bool inSet) => s != AiTactic.NoScore && inSet ? s + InSetBonus : s;

    /// <summary>Сколько целей каждого вида держит кэш ближайших целей — <c>push 3</c> перед каждым
    /// <c>0x45f793</c> (ранг 1).</summary>
    public const int TargetSetPerKind = 3;

    /// <summary>
    /// С какого дня множество целей тактики (<c>CAiTactic +0x51dc</c>, <see cref="ScenarioObjectives.AiTargets"/>) не пусто —
    /// <c>0x4662d7</c> (ранг 1): сложность партии <c>DIFFGAME</c> (<c>CScenarioInfo +0x40</c>, <c>0x5e39d5</c>) против категорий
    /// <c>0x837988</c>/<c>0x8379b8</c>/<c>0x8379a8</c>/<c>0x837998</c> — 60, 50, 40, 30 дней; другая — порог 0. День меньше порога —
    /// множество пусто.
    /// </summary>
    public static int WinTargetsDay(Difficulty difficulty) => difficulty switch
    {
        Difficulty.Easy => 60,
        Difficulty.Average => 50,
        Difficulty.Hard => 40,
        Difficulty.VeryHard => 30,
        _ => 0,
    };

    /// <summary>
    /// Отбор трёх вида кэша (<c>0x45f793</c>, ранг 1) из уже отсортированных не исключённых целей: каждая идёт в вектор
    /// (<c>0x45fa74</c>), а счётчик растёт (<c>0x45f8b9</c>) только у целей не из множества <c>CAiTactic +0x51dc</c>; стоп, когда
    /// счётчик дошёл до трёх. Цели множества идут сверх тройки (legions дни 52–56: <c>FT0002, FT0007, FT0006, FT000C</c> —
    /// <c>FT0007</c> из множества).
    /// </summary>
    public static IReadOnlyList<T> PickThree<T>(this IEnumerable<T> ordered, Func<T, bool> inSet)
    {
        ArgumentNullException.ThrowIfNull(ordered);
        ArgumentNullException.ThrowIfNull(inSet);
        var picked = new List<T>();
        int counted = 0;
        foreach (var item in ordered)
        {
            picked.Add(item);
            if (!inSet(item) && ++counted == TargetSetPerKind) break;
        }
        return picked;
    }

    /// <summary>
    /// Кэш ближайших целей <c>CMidServerLogicData2</c> (это НЕ множество +100 — то <c>CAiTactic+0x51dc</c>, строит
    /// <c>0x4662d7</c>; <c>docs/ai-tactic-candidates.md</c> §6). Наполняет <c>0x45e4c7</c> перед каждой оценкой
    /// отряда (<c>0x466b36</c>): шесть раз подряд «собрать вид → взять три ближайших»
    /// (<c>0x45f793(3, …)</c> из отсортированного по расстоянию списка <c>0x45fad6</c>). Виды в порядке движка
    /// (RTTI дескрипторов dynamic_cast, ранг 1): отряды <c>0x45e7cb</c>, крепости <c>0x45eb14</c>, места
    /// <c>CMidSite</c> <c>0x45efa4</c>, руины <c>CMidRuin</c> <c>0x45f0ae</c>, мешки <c>CMidBag</c>
    /// <c>0x45f1e3</c>, кристаллы <c>CMidCrystal</c> <c>0x45f327</c> — последний вид собирается только при
    /// <c>0x5d6469</c> (лидер умеет ставить жезл, ранг 2), иначе список чистится (<c>0x45e6d8</c>).
    /// Ключ сортировки — евклидово расстояние <c>0x45eadf</c> = <c>√(dx² + dy²)</c> (ранг 1), а не Чебышёв
    /// шаблона счёта; у крепости, которая и есть цель действующего приказа, ключ 0 (<c>fldz</c> у
    /// <c>0x45eba2</c>), то есть она первая. Ранг 2 — отборы внутри каждого вида (<c>0x45ec77</c>, <c>0x45e98a</c>
    /// и прочие) не разобраны: здесь вызывающий отдаёт уже отобранные цели.
    /// </summary>
    public static IReadOnlyList<T> NearestTargets<T>(IEnumerable<(T Target, double Distance)> candidates) =>
        [.. (candidates ?? throw new ArgumentNullException(nameof(candidates)))
            .OrderBy(c => c.Distance).Take(TargetSetPerKind).Select(c => c.Target)];

    /// <summary>Евклидово расстояние ключа множества +100 (<c>0x45eadf</c>). Ранг 1.</summary>
    public static double SetDistance(int dx, int dy) => Math.Sqrt((double)(dx * dx + dy * dy));

    /// <summary>Порядок соседей кристалла — таблица <c>0x6d1660</c> (пары dx, dy). Ранг 1.</summary>
    public static readonly IReadOnlyList<(int Dx, int Dy)> RodNeighbours =
        [(-1, -1), (0, -1), (1, -1), (-1, 0), (1, 0), (-1, 1), (0, 1), (1, 1)];

    /// <summary>
    /// Клетка жезла у кристалла (<c>0x46164d</c>, ранг 1): соседи в порядке <see cref="RodNeighbours"/>; годна, если
    /// все три проверки дали 0; из годных — минимальное расстояние Чебышёва от отряда (слот 13 обёртки,
    /// <c>0x4678cb</c>), строго меньше — при равенстве первая. Годных нет — <c>null</c>.
    /// </summary>
    /// <param name="rodNear">Отказ <c>0x5d9bdb</c> — жезл в коробке 5×5.</param>
    /// <param name="allyLand">Отказ <c>0x5d9d1d</c> — земля союзника.</param>
    /// <param name="blocked">Отказ <c>0x5cd11f</c>: сама клетка и восемь соседей (таблица <c>0x7b0718</c>) проверяются
    /// <c>0x5cd1c7</c> — отряд (<c>0x5cd301</c>) и крепость (тип 0x1d). Ранг 2: обход по листингу, условие «чужой» внутри
    /// <c>0x5cd301</c> не разобрано; корпус ai-q09 — 36/36 при «отряд другого игрока в 3×3»
    /// (<c>AiTacticHarnessTests</c>).</param>
    public static (int X, int Y)? RodCell(int crystalX, int crystalY, int stackX, int stackY,
                                         Func<int, int, bool> rodNear, Func<int, int, bool> allyLand,
                                         Func<int, int, bool> blocked)
    {
        ArgumentNullException.ThrowIfNull(rodNear);
        ArgumentNullException.ThrowIfNull(allyLand);
        ArgumentNullException.ThrowIfNull(blocked);
        (int X, int Y)? best = null;
        int bestDistance = 9999;   // 0x270f
        foreach (var (dx, dy) in RodNeighbours)
        {
            int x = crystalX + dx, y = crystalY + dy;
            if (rodNear(x, y) || allyLand(x, y) || blocked(x, y)) continue;
            int d = Math.Max(Math.Abs(x - stackX), Math.Abs(y - stackY));
            if (d < bestDistance) (best, bestDistance) = ((x, y), d);
        }
        return best;
    }
}

/// <summary>
/// Сетка влияния игрока (<see cref="AiTacticScore.InfluenceGrid"/>): байт на клетку, сторона — размер карты. У движка сетка — 144²
/// (<c>memset(…, 0, 0x5100)</c> у <c>0x461dd4</c>, индекс <c>y·144 + x</c>), то есть потолок его карт; клетка вне карты читается
/// нулём, как нулевой хвост той сетки за краем карты меньше 144.
/// </summary>
public sealed class InfluenceMap(int side)
{
    private readonly byte[] _cells = new byte[side * side];

    public int Side { get; } = side;

    public byte this[int x, int y]
    {
        get => (uint)x < (uint)Side && (uint)y < (uint)Side ? _cells[y * Side + x] : (byte)0;
        set => _cells[y * Side + x] = value;
    }
}
