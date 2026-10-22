using D2.Rules.Battle;

namespace D2.Rules.Units;

/// <summary>Ссылки <c>Gunits</c> на уровневый рост одного типа: <c>DYN_UPG1</c> действует до
/// уровня <c>DYN_UPG_LV</c> включительно, дальше <c>DYN_UPG2</c>. Пустая строка — ссылки нет
/// (<c>g000000000</c>), прибавка нулевая.</summary>
public sealed record GrowthRef(string DynUpg1Id, int DynUpgLevel, string DynUpg2Id);

/// <summary>Реализация юнита в том виде, в каком её отдаёт движок: тип плюс обе атаки. Для
/// глобального типа (<c>UU</c>) это строки <c>Gunits</c>/<c>Gattacks</c> как есть, для
/// порождённого (<c>UG</c>) — те же строки, выращенные до уровня.</summary>
public sealed record UnitImpl(UnitType Type, AttackType? Attack, AttackType? Attack2);

/// <summary><c>CUnitGenerator</c> — порождённые реализации юнитов. Когда юнит перерастает
/// собственный уровень типа и улучшать его больше не во что, движок не пишет модификатор, а
/// создаёт <b>новую реализацию</b> с id вида <c>G000UG1e06</c> (<c>IdType::UnitGenerated</c> = 7) и
/// кладёт в бой именно её. В <c>Gunits</c> такого id нет и быть не может — он вычисляется.
///
/// <para><b>Ранг 1 — прочитано в дизассемблере <c>game-pristine/Discipl2.exe</c> (GOG 3.01,
/// ImageBase 0x400000).</b> <c>CUnitGenerator::generateUnitImplId</c> (<c>0x598645</c>,
/// <c>ret 0xc</c>) считает так:
/// <list type="bullet">
/// <item><c>0x5986b1</c> — <c>index</c> базовой реализации в коллекции глобальных юнитов;</item>
/// <item><c>0x5986b8</c> → <c>0x5986ed</c> — <c>levelMax = 0xFFFF / количество глобальных юнитов</c>
/// (<c>mov eax, 0xffff; cdq; idiv ecx</c>, где <c>ecx</c> — размер коллекции, геттер
/// <c>0x479bd6</c>);</item>
/// <item><c>0x5986bd</c>–<c>0x5986c1</c> — <c>typeIndex = (levelMax − 1)·index + level − 2</c>
/// (<c>dec eax; imul eax, ebx; lea eax, [eax + esi − 2]</c>);</item>
/// <item><c>0x5986c6</c>–<c>0x5986d0</c> — <c>fromParts(Global, 0, UnitGenerated, typeIndex)</c>
/// (<c>push 7</c> — это <c>IdType::UnitGenerated</c>, шестая позиция занята <c>UnitGlobal</c>).</item>
/// </list>
/// Обратное преобразование лежит рядом и им же и пользуются: <c>0x59875e</c> —
/// <c>index = typeIndex / (levelMax − 1)</c>, а <c>getUnitLevelByImplId</c> (<c>0x59870b</c>,
/// ветка «в глобальных не нашлось») — <c>level = typeIndex − (levelMax − 1)·index + 2</c>
/// (<c>0x598737</c>–<c>0x59874c</c>).</para>
///
/// <para><b>Ванильные числа.</b> В <c>Gunits.dbf</c> 359 строк, из них три помечены удалёнными и
/// дублируют живые, то есть глобальных реализаций <b>356</b>; <c>levelMax = 0xFFFF / 356 = 184</c>,
/// шаг <c>183</c>. Это и есть тот самый множитель, по которому <c>G000UU0044</c> (сортированный
/// индекс 42) на уровне 2 превращается в <c>G000UG1e06</c>: <c>183·42 + 2 − 2 = 0x1e06</c>.</para>
///
/// <para><b>Ранг 1 на порядок коллекции — измерено, а не выведено.</b> Движок читает индекс из
/// самой записи (<c>movzx eax, word ptr [eax+0x14]</c> на <c>0x588c1a</c>), а не считает позицию;
/// что этот индекс равен позиции id в <b>отсортированном</b> списке (а не порядку строк .dbf —
/// он не отсортирован, <c>g000uu5037</c> стоит перед <c>g000uu0043</c>), проверено на журнале
/// <c>corpus/20260910-passive/passive-004.jsonl</c>: <b>60</b> различных <c>UG</c>-типов,
/// <b>1663</b> снимка <c>battle_units</c>, у всех декодированные база и уровень совпали с тем,
/// что журнал пишет сам, — ноль исключений.</para>
///
/// <para><b>Границы (<c>0x598672</c>–<c>0x598692</c>).</b> Запрошенный уровень, равный уровню самой
/// реализации, возвращает её же id; уровень ниже собственного или выше <see cref="LevelMax"/> даёт
/// <c>invalidId</c>. Здесь то же самое: <see cref="GeneratedId"/> возвращает пустую строку.</para>
/// </summary>
public sealed class UnitGenerator
{
    /// <summary>Числитель из <c>0x598702</c> (<c>mov eax, 0xffff</c>) — весь диапазон
    /// <c>typeIndex</c>, младших 16 бит id, поделённый между реализациями поровну.</summary>
    private const int TypeIndexRange = 0xFFFF;

    /// <summary>Смещение уровня из <c>lea eax, [eax + esi − 2]</c> (<c>0x5986c1</c>): нулевой слот
    /// каждой реализации отдан уровню 2 — первому, на котором вообще бывает порождение.</summary>
    private const int FirstGeneratedLevel = 2;

    private const string GeneratedKind = "UG";

    private readonly string[] _globals;
    private readonly Dictionary<string, int> _indexOf;
    private readonly IReadOnlyDictionary<string, GrowthRef> _growth;
    private readonly IReadOnlyDictionary<string, DynUpgrade> _upgrades;

    /// <summary><c>getGeneratedUnitImplLevelMax</c> (<c>0x5986ed</c>) — потолок уровня порождённой
    /// реализации, он же шаг между соседними реализациями плюс один.</summary>
    public int LevelMax { get; }

    private int Stride => LevelMax - 1;

    /// <param name="globalUnitIds">Все id <c>Gunits</c> (регистр не важен): их число задаёт
    /// <see cref="LevelMax"/>, а порядок сортировки — индекс реализации.</param>
    /// <param name="growth">Ссылки на рост по id типа (<c>Gunits.DYN_UPG1/DYN_UPG_LV/DYN_UPG2</c>).</param>
    /// <param name="upgrades">Строки <c>GDynUpgr</c> по их id.</param>
    public UnitGenerator(IEnumerable<string> globalUnitIds, IReadOnlyDictionary<string, GrowthRef> growth,
                         IReadOnlyDictionary<string, DynUpgrade> upgrades)
    {
        // Коллекция движка отсортирована по ЧИСЛОВОМУ значению id; у всех строк Gunits совпадают
        // категория и тип, поэтому порядок задаёт один typeIndex.
        _globals = globalUnitIds.Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(TypeIndex).ToArray();
        _indexOf = new Dictionary<string, int>(_globals.Length, StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < _globals.Length; i++) _indexOf[_globals[i]] = i;
        _growth = growth;
        _upgrades = upgrades;
        LevelMax = _globals.Length == 0 ? 0 : TypeIndexRange / _globals.Length;
    }

    /// <summary>Id порождённой реализации базового типа на уровне <paramref name="level"/>; пустая
    /// строка — движок вернул бы <c>invalidId</c> (тип не глобальный либо уровень вне границ).</summary>
    public string GeneratedId(string globalId, int level)
    {
        if (level < FirstGeneratedLevel || level > LevelMax) return "";
        if (!_indexOf.TryGetValue(globalId, out int index)) return "";
        return $"G000{GeneratedKind}{Stride * index + level - FirstGeneratedLevel:x4}";
    }

    /// <summary>Разбор id порождённой реализации на базовый тип и уровень. false — id не
    /// порождённый либо индекс вышел за коллекцию (чужие глобалы, битый журнал).</summary>
    public bool TryDecode(string implId, out string globalId, out int level)
    {
        globalId = "";
        level = 0;
        if (!IsGenerated(implId) || Stride <= 0) return false;
        int typeIndex = TypeIndex(implId);
        int index = typeIndex / Stride;
        if (index >= _globals.Length) return false;
        globalId = _globals[index];
        level = typeIndex - Stride * index + FirstGeneratedLevel;
        return true;
    }

    /// <summary>Порождённая реализация: тип и обе атаки, выращенные <see cref="UnitGrowth"/> до
    /// <paramref name="level"/>. Прибавки те же самые, что движок кладёт в саму реализацию
    /// (<c>0x598b68</c> и <c>0x5998f5</c>), поэтому расти обязаны и тип, и атаки — иначе прокачанный
    /// юнит бьёт как новобранец.
    /// <para>Id атак остаются базовыми (<c>g000aa0044</c>), хотя движок порождает и их
    /// (<c>G000DA1e06</c> — тот же <c>typeIndex</c>, что у юнита). Данные за этими id одни и те же,
    /// а по id атаки ищутся <c>Gtransf</c> и варды — с базовым id они находятся, с порождённым нет.</para></summary>
    public UnitImpl Grow(string implId, UnitType type, AttackType? attack, AttackType? attack2, int level)
    {
        if (!_growth.TryGetValue(type.Id, out var refs))
            throw new InvalidDataException($"у типа {type.Id} нет строки Gunits с DYN_UPG1/DYN_UPG2 — " +
                                           "вырастить порождённую реализацию не из чего");
        var first = Upgrade(refs.DynUpg1Id, type.Id);
        var second = Upgrade(refs.DynUpg2Id, type.Id);
        var steps = UnitGrowth.StepsFor(type.Level, refs.DynUpgLevel, level);
        var grown = UnitGrowth.Of(type, refs.DynUpgLevel, level, first, second);
        return new UnitImpl(
            grown.Type with { Id = implId, GlobalId = type.Id },
            attack is null ? null : UnitGrowth.OfAttack(attack, steps, first, second),
            attack2 is null ? null : UnitGrowth.OfAttack(attack2, steps, first, second));
    }

    private DynUpgrade? Upgrade(string id, string unitId)
    {
        if (id.Length == 0) return null;                       // проверка на emptyId, 0x598acb
        return _upgrades.TryGetValue(id, out var u) ? u
            : throw new InvalidDataException($"прибавка роста {id} юнита {unitId} не найдена в GDynUpgr.dbf");
    }

    /// <summary><c>IdType</c> порождённой реализации — две буквы <c>UG</c> в позициях 4..5 десяти-
    /// значного id (<c>G000UG1e06</c>).</summary>
    public static bool IsGenerated(string implId) =>
        implId.Length == 10 && implId.AsSpan(4, 2).Equals(GeneratedKind, StringComparison.OrdinalIgnoreCase);

    /// <summary>Младшие 16 бит id — <c>CMidgardIDApi::getTypeIndex</c> (GOG <c>0x57f3d2</c>).</summary>
    private static int TypeIndex(string id)
    {
        if (id.Length != 10) throw new ArgumentException($"id не 10 символов: '{id}'", nameof(id));
        return Convert.ToInt32(id.Substring(6, 4), 16);
    }
}
