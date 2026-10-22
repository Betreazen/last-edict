using LastEdict.Rules.Battle;

namespace LastEdict.Rules.Units;

/// <summary>Ссылки юнита на уровневый рост одного типа: <c>DynUpg1Id</c> действует до уровня <c>DynUpgLevel</c> включительно, дальше
/// <c>DynUpg2Id</c>. Пустая строка — ссылки нет, прибавка нулевая.</summary>
public sealed record GrowthRef(string DynUpg1Id, int DynUpgLevel, string DynUpg2Id);

/// <summary>Реализация юнита в бою: тип плюс обе атаки. У юнита своего уровня это тип и атаки содержимого как есть, у выросшего уровнем
/// выше своего типа — те же, выращенные до уровня (<see cref="UnitGenerator.Grow"/>).</summary>
public sealed record UnitImpl(UnitType Type, AttackType? Attack, AttackType? Attack2);

/// <summary>
/// Реализации юнитов, выросших уровнем выше своего типа (<c>CUnitGenerator</c> оригинала). Когда юнит перерастает уровень типа и улучшать
/// его не во что, тип не меняется: юнит воюет тем же типом, выращенным шагами роста до своего уровня, — тип и обе атаки
/// (<see cref="UnitGrowth"/>). Реализация — тип с ключом базового типа, уровнем роста <see cref="UnitType.GrownLevel"/>.
/// <para>Оригинал называл такую реализацию вычисляемым id (<c>G000UG1e06</c>: номер базового типа в отсортированной коллекции и уровень
/// в младших 16 битах); в Last Edict ключ — id содержимого, уровень — отдельное поле, и потолка уровня от числа типов
/// (<c>0xFFFF / число типов</c>, у ванили 184) нет.</para>
/// </summary>
public sealed class UnitGenerator
{
    private readonly IReadOnlyDictionary<string, GrowthRef> _growth;
    private readonly IReadOnlyDictionary<string, DynUpgrade> _upgrades;

    /// <param name="growth">Ссылки на рост по ключу типа.</param>
    /// <param name="upgrades">Шаги роста по ключу.</param>
    public UnitGenerator(IReadOnlyDictionary<string, GrowthRef> growth, IReadOnlyDictionary<string, DynUpgrade> upgrades)
    {
        _growth = growth;
        _upgrades = upgrades;
    }

    /// <summary>Тип и обе атаки, выращенные <see cref="UnitGrowth"/> до <paramref name="level"/>. Прибавки те же, что движок кладёт в саму
    /// реализацию (<c>0x598b68</c> и <c>0x5998f5</c>), поэтому расти обязаны и тип, и атаки — иначе прокачанный юнит бьёт как новобранец.
    /// <para>Ключи атак остаются базовыми: по ним ищутся превращения и обереги. Двойник не копирует только сам тип из списка оригинала —
    /// движок сравнивал с ним id реализации, а у выросшей он порождённый, поэтому выросший копируется
    /// (<see cref="UnitType.Copyable"/>).</para></summary>
    public UnitImpl Grow(UnitType type, AttackType? attack, AttackType? attack2, int level)
    {
        ArgumentNullException.ThrowIfNull(type);
        if (!_growth.TryGetValue(type.Id, out var refs))
            throw new InvalidDataException($"у типа {type.Id} нет ссылок на рост — вырастить его уровнем выше не из чего");
        var first = Upgrade(refs.DynUpg1Id, type.Id);
        var second = Upgrade(refs.DynUpg2Id, type.Id);
        var steps = UnitGrowth.StepsFor(type.Level, refs.DynUpgLevel, level);
        var grown = UnitGrowth.Of(type, refs.DynUpgLevel, level, first, second);
        return new UnitImpl(
            grown.Type with { GrownLevel = level, Copyable = true },
            attack is null ? null : UnitGrowth.OfAttack(attack, steps, first, second),
            attack2 is null ? null : UnitGrowth.OfAttack(attack2, steps, first, second));
    }

    private DynUpgrade? Upgrade(string id, string unitId)
    {
        if (id.Length == 0) return null;                       // проверка на emptyId, 0x598acb
        return _upgrades.TryGetValue(id, out var u) ? u
            : throw new InvalidDataException($"шаг роста {id} юнита {unitId} не найден в содержимом");
    }
}
