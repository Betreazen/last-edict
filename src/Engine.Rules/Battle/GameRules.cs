using LastEdict.Rules.Units;

namespace LastEdict.Rules.Battle;

/// <summary>Справочники правил, уже прочитанные из .dbf. LastEdict.Rules файлов не читает —
/// собирает эту запись загрузчик из LastEdict.Data (ТЗ §4).</summary>
public sealed record GameRules(
    IReadOnlyDictionary<string, UnitType> Units,
    IReadOnlyDictionary<string, AttackType> Attacks,
    RulesConstants Constants,
    IReadOnlyDictionary<string, IReadOnlyList<string>>? SummonsByAttack = null,
    IReadOnlyDictionary<string, Modifier>? Modifiers = null,
    UnitGenerator? Generator = null,
    // Модификаторы, которые выдаются при повышении уровня (GleaUpg.MODIF_ID) — и только они.
    // Нужны там, где надо отличить «цепочка выросла вместе с уровнем» от «надели предмет»:
    // уровень журнал пишет сам, а момент надевания вещи внутри хода не знает никто
    // (см. LastEdict.Data.Battle.SaveSnapshots.TryChain).
    IReadOnlySet<string>? LeaderUpgradeModifiers = null)
{
    public AttackType? AttackOf(UnitType unit) => Attacks.TryGetValue(unit.AttackId, out var a) ? a : null;

    /// <summary>Реализация юнита по id, каким его называет журнал: строка <c>Gunits</c> либо
    /// порождённая прокачкой (<c>G000UG****</c>, см. <see cref="UnitGenerator"/>). null — id не
    /// опознан ни так, ни этак; вызывающий обязан сказать об этом вслух, а не подставить старый тип.
    /// <para>Одним вызовом, а не тремя (<c>Units[…]</c>, <see cref="AttackOf"/>,
    /// <see cref="Attack2Of"/>), именно потому, что у порождённой реализации растут и тип, и обе
    /// атаки сразу: взять её тип, а атаку базовую — значит получить прокачанного юнита с уроном
    /// новобранца.</para></summary>
    public UnitImpl? ImplOf(string implId)
    {
        if (Units.TryGetValue(implId, out var type))
            return new UnitImpl(type, AttackOf(type), Attack2Of(type));
        if (Generator is null || !Generator.TryDecode(implId, out var globalId, out int level)) return null;
        if (!Units.TryGetValue(globalId, out var baseType)) return null;
        return Generator.Grow(implId, baseType, AttackOf(baseType), Attack2Of(baseType), level);
    }

    /// <summary>Вторая атака; null — её нет ("g000000000") или id не найден в Gattacks.</summary>
    public AttackType? Attack2Of(UnitType unit) => Attacks.TryGetValue(unit.Attack2Id, out var a) ? a : null;

    /// <summary>Варды атаки (<c>Gattacks.WARD1..4</c>) как модификаторы <c>Gmodif</c>/<c>GmodifL</c> —
    /// то, что кладёт на цель класс <c>BestowWards</c> (<c>attack-&gt;getWards()</c>, слот 14
    /// <c>IAttackVftable</c>, <c>call [vft+0x38]</c> на <c>0x65d56c</c>). Задача 8.
    /// <para>Пустой список у атаки — законный ответ; отсутствие таблицы модификаторов или висячая
    /// ссылка — нет, поэтому и то и другое падает с внятным текстом. Ванильных ward-модификаторов
    /// четыре на всю игру, и все четыре разрешаются.</para></summary>
    public IReadOnlyList<Modifier> WardsOf(AttackType attack)
    {
        if (attack.Wards is not { Count: > 0 } ids) return [];
        if (Modifiers is null)
            throw new InvalidOperationException(
                $"варды атаки {attack.Id} — модификаторы Gmodif/GmodifL, а GameRules.Modifiers не заполнен; " +
                "бой собран без таблицы модификаторов");
        var found = new List<Modifier>(ids.Count);
        foreach (var id in ids)
            found.Add(Modifiers.TryGetValue(id, out var m) ? m
                      : throw new InvalidDataException($"вард {id} атаки {attack.Id} не найден в Gmodif.dbf"));
        return found;
    }

    /// <summary>Запасная атака (<c>Gattacks.ALT_ATTACK</c>) — см. <see cref="AttackType.HasAltAttack"/>.
    /// null означает «ссылки нет»; висячая ссылка падает, а не молчит.</summary>
    public AttackType? AltAttackOf(AttackType attack) =>
        !attack.HasAltAttack ? null
        : Attacks.TryGetValue(attack.AltAttackId, out var a) ? a
        : throw new InvalidDataException(
            $"ALT_ATTACK {attack.AltAttackId} атаки {attack.Id} не найдена в Gattacks.dbf");

    /// <summary>Кандидаты на призыв этой атакой — строки <c>Gtransf.dbf</c> (ATTACK_ID → TRANSF_ID),
    /// отфильтрованные по размеру слота: <c>fillAttackTransformIdList(transf, list, attackId,
    /// smallUnit)</c> (GOG <c>0x597b00</c>, зовётся из <c>getSummonUnitImplIdByAttack</c>
    /// <c>0x660a60</c>). Ранг 1 — что список берётся из Gtransf и режется размером; ранг 2 — что
    /// «размер» это <c>Gunits.SIZE_SMALL</c> самого кандидата.
    /// <para>Ванильный счёт (43 строки Gtransf, 21 атака класса Summon): у восемнадцати атак кандидат
    /// ровно один уже до фильтра; у <c>g000aa8007</c> их два, но разного размера — после фильтра один;
    /// многозначны только <c>g000aa6007</c> (три малых) и <c>g000aa6014</c> (пять малых и два
    /// больших).</para></summary>
    public IReadOnlyList<UnitType> SummonCandidates(string attackId, bool smallSlot)
    {
        if (SummonsByAttack is null || !SummonsByAttack.TryGetValue(attackId, out var ids)) return [];
        var found = new List<UnitType>();
        foreach (var id in ids)
            if (Units.TryGetValue(id, out var t) && t.SizeSmall == smallSlot) found.Add(t);
        return found;
    }
}
