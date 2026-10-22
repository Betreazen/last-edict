using D2.Rules.Magic;
using D2.Rules.Rng;

namespace D2.Rules.Map;

/// <summary>Категория заклинаний с диапазоном уровней — запись списка, по которому ИИ ищет заклинание
/// (<c>0x46e92c</c>, шаг записи <c>0x14</c>: категория, <c>+0xc</c> наименьший уровень, <c>+0x10</c> наибольший).</summary>
public readonly record struct AiSpellWish(SpellCategory Category, int MinLevel, int MaxLevel);

/// <summary>
/// Правила выбора заклинаний ИИ на карте (оперативный слой <c>CAiOperational</c>, <c>docs/ai-spells.md</c>). Чистые
/// функции над уже собранными входами; сборку из карты и литьё делает <c>D2.Data.Play.AiSpells</c>. Ранг 1 — листинг
/// <c>Discipl2.exe</c> (GOG 3.01), если не сказано иное.
/// </summary>
public static class AiSpellRules
{
    /// <summary>Враг ближе стольких клеток (Чебышёв) к своей крепости без визитёра — бить заклинанием (<c>0x46e4f8</c>).</summary>
    public const int FortGuardRange = 7;

    /// <summary>Рамки поиска клетки смены земли вокруг своего жезла (<c>0x46f745</c>: радиусы 1…4, <c>cmp 5</c>).</summary>
    public const int RodTerrainRings = 4;

    /// <summary>Вор или жезлоносец ближе стольких клеток к своей крепости или кристаллу — призыв или удар
    /// (<c>0x46e5e8</c>, <c>0x46fb6d</c>).</summary>
    public const int IntruderRange = 5;

    /// <summary>Сколько сильнейших отрядов каждого игрока входит в списки оперативного слоя (<c>[0x6d2874]</c> = 2,
    /// обрезка <c>0x47667d</c> в <c>0x476190</c>).</summary>
    public const int ForcesPerPlayer = 2;

    /// <summary>Доля юнитов цели с иммунитетом к заклинанию, начиная с которой оно не годится (<c>[0x6d2958]</c> = 0,5,
    /// <c>0x476e12</c>).</summary>
    public const double ImmuneShareLimit = 0.5;

    /// <summary>
    /// Ярус отряда по среднему опыту за убийство его юнитов (<c>0x476ad2</c> → <c>0x476b26</c>: сумма
    /// <c>IUsSoldier +0x3c</c> — <c>XP_KILLED</c> реализации, выросшей уровнем, — целочисленно делится на число юнитов
    /// группы, павшие тоже): ≤ 25 — 1, ≤ 65 — 2, ≤ 125 — 3, ≤ 210 — 4, больше — 5. Пустая группа — 1.
    /// </summary>
    public static int Tier(IReadOnlyCollection<int> xpKilled)
    {
        ArgumentNullException.ThrowIfNull(xpKilled);
        if (xpKilled.Count == 0) return 1;
        int avg = xpKilled.Sum() / xpKilled.Count;
        return avg <= 25 ? 1 : avg <= 65 ? 2 : avg <= 125 ? 3 : avg <= 210 ? 4 : 5;
    }

    /// <summary>Наименьший уровень заклинания по дню (<c>0x46f193</c>): до 10-го — 1, до 20-го — 2, до 40-го — 3, дальше
    /// 4. День — поле <c>CAiOperational +0x2c</c>, которое ставит начало хода (ранг 2: пороги дня и выбор журналов
    /// сходятся только с днём).</summary>
    public static int DayLevel(int day) => day <= 10 ? 1 : day <= 20 ? 2 : day <= 40 ? 3 : 4;

    /// <summary>
    /// Сколько заклинаний бросить во врага и какого уровня (<c>0x476bc4</c> с флагом 1, затем <c>0x46f193</c>): по ярусу
    /// цели — 1: (2; 2…2), 2: (2; 2…3), 3: (2; 3…4), 4: (3; 3…5), 5: (3; 4…99); наименьший уровень не ниже
    /// <see cref="DayLevel"/>, наибольший — не ниже наименьшего.
    /// </summary>
    public static (int Count, int MinLevel, int MaxLevel) StrikePlan(int tier, int day)
    {
        var (count, min, max) = TierPlan(tier, raiseFirst: true);
        min = Math.Max(min, DayLevel(day));
        return (count, min, Math.Max(max, min));
    }

    /// <summary>
    /// Число и уровни заклинаний по ярусу цели (<c>0x476bc4</c>): 1 — (2; 1…2), 2 — (2; 2…3), 3 — (2; 3…4), 4 —
    /// (3; 3…5), 5 — (3; 4…99); флаг <paramref name="raiseFirst"/> (третий аргумент) поднимает наименьший уровень
    /// первого яруса до 2. Оперативный удар зовёт с флагом 1, заклинание перед атакой (<c>0x472142</c>) — с 0.
    /// </summary>
    public static (int Count, int MinLevel, int MaxLevel) TierPlan(int tier, bool raiseFirst) => tier switch
    {
        1 => (2, raiseFirst ? 2 : 1, 2),
        2 => (2, 2, 3),
        3 => (2, 3, 4),
        4 => (3, 3, 5),
        _ => (3, 4, 99),
    };

    /// <summary>Отряд тяжело ранен (<c>0x46d7cf</c>): среднее текущее здоровье (сумма / число юнитов группы, целое) не
    /// больше половины среднего наибольшего (целое деление, затем пополам к нулю).</summary>
    public static bool BadlyHurt(IReadOnlyCollection<(int Hp, int HpMax)> units)
    {
        ArgumentNullException.ThrowIfNull(units);
        if (units.Count == 0) return false;
        int hp = units.Sum(u => u.Hp) / units.Count;
        int max = units.Sum(u => u.HpMax) / units.Count / 2;
        return hp <= max;
    }

    /// <summary>Уровень сценария, с которого оперативный слой бросает заклинания на всю карту (<c>0x46d97a</c>) и ведёт
    /// «натиск» на людей (<c>0x46fe35</c>): <c>0x61eb31</c> — <c>SUGG_LVL ≥ 10</c>.</summary>
    public const int HighScenarioLevel = 10;

    /// <summary>Мана, которой должно быть больше в каждой из четырёх валют, чтобы бросать заклинания на всю карту
    /// (<c>0x46d9aa</c>, <c>cmp ax, 0x3e8</c>).</summary>
    public const int WholeMapManaFloor = 1000;

    /// <summary>Сколько отрядов вне крепостей нужно заклинанию на всю карту (усиление, невидимость — своих; удар — чужих не
    /// союзных; <c>0x46dcf2</c>, <c>0x46ddc6</c>: <c>cmp 2; jge</c>).</summary>
    public const int WholeMapMinStacks = 2;

    /// <summary>
    /// Хватает ли маны на заклинания на всю карту (<c>0x46d98a</c>): адской, жизни, рун и смерти (<c>0x586a2e</c> с 1, 2, 4,
    /// 3 — рощи нет) каждой больше <see cref="WholeMapManaFloor"/>; казна движка — 16-битные слова со знаком.
    /// </summary>
    public static bool RichInMana(Bank bank)
    {
        ArgumentNullException.ThrowIfNull(bank);
        static bool Over(long v) => unchecked((short)v) > WholeMapManaFloor;
        return Over(bank.Infernal) && Over(bank.Life) && Over(bank.Runic) && Over(bank.Death);
    }

    /// <summary>
    /// Стоит ли бросать известное заклинание на всю карту (<c>0x46da60</c>…<c>0x46ddc6</c>): лечение — у своего отряда вне
    /// крепости есть юнит со здоровьем ниже наибольшего (павший тоже, <paramref name="ownWounded"/>); усиление и невидимость —
    /// своих отрядов вне крепостей не меньше <see cref="WholeMapMinStacks"/>; удар — столько же чужих не союзных вне крепостей
    /// (нейтралы тоже); прочие категории — нет.
    /// </summary>
    public static bool WholeMapWorth(SpellCategory category, bool ownWounded, int ownOutside, int foesOutside) => category switch
    {
        SpellCategory.Heal => ownWounded,
        SpellCategory.Boost or SpellCategory.Invisibility => ownOutside >= WholeMapMinStacks,
        SpellCategory.Attack => foesOutside >= WholeMapMinStacks,
        _ => false,
    };

    /// <summary>«Натиск» на людей (<c>0x46fe5e</c>): свой отряд ближе стольких клеток (Чебышёв) к цели — цель «рядом»
    /// (<c>0x470012</c>: <c>cmp 0xa; jle</c>).</summary>
    public const int HarassNear = 10;

    /// <summary>Идёт ли проход «натиска» (<c>0x46fe7d</c>): <c>rng(100) ≤ 49</c> — один бросок R250 на проход.</summary>
    public static bool HarassGo(IRng rng)
    {
        ArgumentNullException.ThrowIfNull(rng);
        return rng.Next(100) <= 49;
    }

    /// <summary>Сколько заклинаний бросить за проход «натиска» (<c>0x46fe94</c>): в далёкие цели — 1 (без броска), в близкие —
    /// <c>rng(4) + 2</c>, от 2 до 5.</summary>
    public static int HarassCount(bool far, IRng rng)
    {
        ArgumentNullException.ThrowIfNull(rng);
        return far ? 1 : rng.Next(4) + 2;
    }

    /// <summary>Категории «натиска» (<c>0x47009a</c>…<c>0x4700c3</c>): в далёкие цели — только ослабление; в близкие —
    /// ослабление, удар и призыв.</summary>
    public static IReadOnlyList<SpellCategory> HarassCategories(bool far) =>
        far ? [SpellCategory.Lower] : [SpellCategory.Lower, SpellCategory.Attack, SpellCategory.Summon];

    /// <summary>Годится ли заклинание против отряда по иммунитетам (<c>0x476c55</c>): доля юнитов группы, у которых
    /// иммунитет не <c>L_NOTIMMUNE</c>, меньше <see cref="ImmuneShareLimit"/>.</summary>
    public static bool FewImmune(int immune, int units) => units == 0 || (float)immune / units < ImmuneShareLimit;

    /// <summary>
    /// Отбор по уровню (<c>0x46ef83</c>): найти наибольший уровень; затем по списку с начала убирать заклинания ниже
    /// него, пока в списке больше <paramref name="keep"/>.
    /// </summary>
    public static void KeepHighest<T>(List<T> spells, Func<T, int> level, int keep)
    {
        ArgumentNullException.ThrowIfNull(spells);
        ArgumentNullException.ThrowIfNull(level);
        if (spells.Count == 0) return;
        int top = spells.Max(level);
        for (int i = 0; i < spells.Count && spells.Count > keep;)
        {
            if (level(spells[i]) >= top) i++;
            else spells.RemoveAt(i);
        }
    }

    /// <summary>Перемешивание движка (<c>0x43949e</c>): для <c>i</c> от 1 до конца — обмен с элементом <c>rng(i)</c>. На
    /// <c>n</c> элементов — <c>n − 1</c> бросков R250 (<c>rng(1)</c> тоже бросок).</summary>
    public static void Shuffle<T>(List<T> items, IRng rng)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(rng);
        for (int i = 1; i < items.Count; i++)
        {
            int j = rng.Next(i);
            (items[i], items[j]) = (items[j], items[i]);
        }
    }

    /// <summary>
    /// Сортировка списка MSVC <c>std::list::sort</c> (<c>0x47088c</c>, слияние через 64 корзины) с предикатом
    /// <paramref name="less"/> — повторена буквально: у предиката выбора (<see cref="SummonsLast"/>) нет строгого
    /// порядка, и результат зависит от самого алгоритма.
    /// </summary>
    public static List<T> ListSort<T>(IReadOnlyList<T> items, Func<T, T, bool> less)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(less);
        if (items.Count < 2) return [.. items];
        var bins = new List<List<T>>();
        foreach (var item in items)
        {
            var carry = new List<T> { item };
            int i = 0;
            for (; i < bins.Count && bins[i].Count > 0; i++)
            {
                var merged = Merge(bins[i], carry, less);
                bins[i] = [];
                carry = merged;
            }
            if (i == bins.Count) bins.Add(carry);
            else bins[i] = carry;
        }
        var result = bins[0];
        for (int i = 1; i < bins.Count; i++) result = Merge(bins[i], result, less);
        return result;
    }

    /// <summary><c>list::merge</c>: элементы <paramref name="other"/> встают перед первым элементом <paramref name="self"/>,
    /// для которого <c>less(other, self)</c>, иначе в конец.</summary>
    private static List<T> Merge<T>(List<T> self, List<T> other, Func<T, T, bool> less)
    {
        var result = new List<T>(self.Count + other.Count);
        int a = 0, b = 0;
        while (a < self.Count && b < other.Count)
        {
            if (less(other[b], self[a])) result.Add(other[b++]);
            else result.Add(self[a++]);
        }
        while (a < self.Count) result.Add(self[a++]);
        while (b < other.Count) result.Add(other[b++]);
        return result;
    }

    /// <summary>Предикат порядка литья (<c>0x46ed41</c>): «меньше» всё, кроме призыва перед не-призывом. Строгого порядка
    /// нет (сам с собой — «меньше»); на списке без призывов <see cref="ListSort"/> с ним разворачивает список.</summary>
    public static bool SummonsLast(SpellCategory a, SpellCategory b) =>
        a != SpellCategory.Summon || b == SpellCategory.Summon;
}
