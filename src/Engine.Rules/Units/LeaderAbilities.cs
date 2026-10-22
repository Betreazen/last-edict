using LastEdict.Rules.Magic;

namespace LastEdict.Rules.Units;

/// <summary>Способности лидера, разложенные по трём источникам (план Ф7 §1.5, levels §3.4).
/// <para>Разложение нужно не для красоты: гейт экипировки спрашивают <b>до</b> того, как предмет
/// надет, а в сохранении его <c>MOD_EQUIP</c> уже лежит в цепочке <c>MidUnit.MODIF_ID</c> вместе с
/// уровневыми навыками. Различить их можно только вычитанием надетого — этим и занят
/// <see cref="LeaderAbilities.Split"/>.</para></summary>
/// <param name="Innate">Врождённые — <c>GLabi.dbf</c> по типу юнита.</param>
/// <param name="FromLevels">Выбранные при повышении — цепочка <c>MODIF_ID</c> за вычетом
/// <c>MOD_EQUIP</c> надетых предметов (<c>GleaUpg.MODIF_ID</c> с <c>GmodifL.TYPE = 11</c>).</param>
/// <param name="FromItems">Данные надетыми предметами — <c>MOD_EQUIP</c> с тем же
/// <c>TYPE = 11</c>.</param>
public sealed record AbilitySources(
    IReadOnlySet<int> Innate, IReadOnlySet<int> FromLevels, IReadOnlySet<int> FromItems)
{
    /// <summary>Объединение трёх источников — то, что видит <c>IUsStackLeader::hasAbility</c>.</summary>
    public IReadOnlySet<int> All => Innate.Concat(FromLevels).Concat(FromItems).ToHashSet();

    /// <summary>Откуда взялась способность — для отчёта харнесса; пусто, если её нет вовсе.</summary>
    public string Where(int ability) => string.Join("+",
        new[] { (Innate, "GLabi"), (FromLevels, "уровень"), (FromItems, "предмет") }
            .Where(s => s.Item1.Contains(ability)).Select(s => s.Item2));
}

/// <summary>Три источника способностей лидера: <c>GLabi</c> ∪ навыки уровня ∪ <c>MOD_EQUIP</c>
/// надетого предмета (план Ф7 §1.5, ранг 1: П§11.3, levels §3.1, §3.3, §3.4).
///
/// <para><b>Способность — это элемент модификатора, а не поле юнита.</b> Механизм один для второго
/// и третьего источников: строка <c>GmodifL</c> с <c>TYPE = 11</c> (<c>LmodifE.dbf</c>
/// <c>L_LEADER_ABILITY</c>) кладёт число из колонки <c>ABILITY</c>; движок собирает их
/// виртуальным <c>IUsStackLeader::hasAbility</c> поверх <c>TUsStackLeaderImplData::abililies</c>
/// (врождённые) и <c>CUmStackData::leaderAbility</c> (цепочка) — <c>0x005DF7B6</c>. Тот же элемент
/// читает и <see cref="EffectiveStats"/> в своём единственном проходе по цепочке
/// (<see cref="EffectiveStats.LeaderAbilities"/>) — правило одно, потребителей два.</para>
///
/// <para><b>Снятый предмет свою способность забирает</b> (план Ф7 §1.5): она не «выучена», а висит
/// на модификаторе, и вместе с <c>MOD_EQUIP</c> уходит из <c>MODIF_ID</c>
/// (<c>unEquipItem 0x005E746D</c>). Отсюда цепочка «книга даёт <c>L_TALISMAN_USE</c> → снять книгу →
/// талисман надеть больше нельзя».</para></summary>
public static class LeaderAbilities
{
    /// <summary>Врождённые способности типа юнита — <c>GLabi.dbf</c>. Нет строки — пустой набор
    /// (у воров врождённых нет ни одной, levels §3.1).</summary>
    public static IReadOnlySet<int> Innate(string unitTypeId,
                                           IReadOnlyDictionary<string, IReadOnlyList<int>> innate) =>
        innate.TryGetValue(unitTypeId, out var list) ? list.ToHashSet() : new HashSet<int>();

    /// <summary>Способности, которые кладёт цепочка модификаторов: элементы
    /// <see cref="ModifierElementType.LeaderAbility"/>.</summary>
    public static IReadOnlySet<int> FromChain(IEnumerable<Modifier> chain) =>
        chain.SelectMany(m => m.Elements)
             .Where(e => e.Type == ModifierElementType.LeaderAbility)
             .Select(e => e.Ability ?? throw new InvalidDataException(
                         "GmodifL.dbf: TYPE 11 без колонки ABILITY"))
             .ToHashSet();

    /// <summary>Способности от надетых предметов: <c>MOD_EQUIP</c> каждого — в общий справочник
    /// <c>Gmodif</c>. Висячая ссылка — исключение (то же правило, что у
    /// <see cref="ItemUse.ModifiersOf"/>); пустой <c>MOD_EQUIP</c> — не ошибка: у сфер и талисманов
    /// он пуст у всех 84 (см. <see cref="ItemCategory"/>).</summary>
    public static IReadOnlySet<int> FromItems(IEnumerable<Item> items,
                                              IReadOnlyDictionary<string, Modifier> catalogue) =>
        FromChain(items.Select(i => i.EquipModifierId).Where(id => id.Length > 0)
                       .Select(id => catalogue.TryGetValue(id, out var m) ? m
                           : throw new InvalidDataException($"MOD_EQUIP \"{id}\" в Gmodif не найден")));

    /// <summary>Разложить состояние лидера из сохранения на три источника.
    /// <para><paramref name="equipped"/> — <b>все</b> надетые предметы отряда: их <c>MOD_EQUIP</c>
    /// уже лежит в <paramref name="chain"/>, и уровневая часть — это остаток цепочки.
    /// <paramref name="granting"/> — те из них, чью способность лидер к нужному моменту уже
    /// получил; при проверке гейта проверяемый предмет из этого списка выкидывают (в момент
    /// проверки он ещё не надет, и сам себе разрешения не даёт).</para>
    /// <para>Модификатор цепочки, которого нет в <c>Gmodif</c>, — исключение: молча пропустить его
    /// значит занизить набор способностей и получить ложный отказ гейта.</para></summary>
    public static AbilitySources Split(string unitTypeId,
                                       IReadOnlyDictionary<string, IReadOnlyList<int>> innate,
                                       IReadOnlyList<string> chain,
                                       IReadOnlyList<Item> equipped,
                                       IReadOnlyList<Item> granting,
                                       IReadOnlyDictionary<string, Modifier> catalogue)
    {
        var fromEquipment = equipped.Select(i => i.EquipModifierId).Where(id => id.Length > 0)
                                    .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var levels = chain.Where(id => !fromEquipment.Contains(id))
                          .Select(id => catalogue.TryGetValue(id, out var m) ? m
                              : throw new InvalidDataException($"MODIF_ID \"{id}\" в Gmodif не найден"));
        return new AbilitySources(Innate(unitTypeId, innate), FromChain(levels),
                                  FromItems(granting, catalogue));
    }
}
