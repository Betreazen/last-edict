using LastEdict.Rules.Map;

namespace LastEdict.Data.Play;

/// <summary>
/// Реализации юнитов, выросших уровнем выше своего типа (<see cref="LastEdict.Rules.Units.UnitGenerator"/>; оригинал звал их порождёнными,
/// <c>G000UG…</c>), уже созданные в этом запуске игры: тип и уровень. От них
/// зависит опыт лидера у порога (И230): прибавка <c>changeUnitXpCheckUpgrade 0x5d7412</c> ищет цель повышения лидера <c>0x5d89c6</c> без
/// создания (<c>push 0</c>, <c>0x5d747d</c>) — поиск <c>0x5d7ef8</c> идёт по коллекции реализаций глобальных данных; нашлась — прибавка целиком
/// (опыт садится на <c>XP_NEXT</c>, следом <c>0x5d8b17</c> повышает), нет — на 1 меньше (<c>0x5d749e</c>). Создаёт реализацию повышение
/// (<c>0x5d8b17</c> с <c>push 1</c> → <c>0x5d8514</c>) и загрузка юнита уровнем выше своего типа. В сохранении коллекции нет: у движка она
/// живёт с входа в партию.
/// <para>Ранг 1 по корпусу: все 175 поисков <c>0x5d89c6(…, 0)</c> (<c>upgrade_target</c> с возвратом <c>0x5d7488</c>) — «нашлась» ровно тогда,
/// когда id цели уже встречался в журнале с входа в партию (64 из 64), «нет» — когда ещё не встречался и создан следом (111 из 111).
/// replay-legions, день 50: G000UG911d (G000UU5131 уровня 2) создан повышением S073UN0048 (seq 1865936), и нейтральные S073UN004b, 00e5,
/// 01df с опытом 245 + 5 садятся на 250 и растут (seq 4814954); у порта, звавшего всегда «не создан», стояли на 249.</para>
/// </summary>
internal static class GeneratedImpls
{
    /// <summary>Реализации юнитов карты уровнем выше своего типа — их движок создал, когда они появились (загрузка, наём, рост).</summary>
    public static void Observe(ISet<string> created, MapState map, GameBook book)
    {
        foreach (var unit in map.Units.Values) Add(created, unit.TypeId, unit.Level, book);
    }

    /// <summary>Юнит стал реализацией <paramref name="typeId"/> уровня <paramref name="level"/> — она теперь создана.</summary>
    public static void Add(ISet<string> created, string typeId, int level, GameBook book)
    {
        if (book.Rules.Generator is not null && book.Rules.Units.TryGetValue(typeId, out var type) && level > type.Level)
            created.Add(Key(type.Id, level));
    }

    /// <summary>Юнит вырос из <paramref name="before"/> в <paramref name="after"/> — каждое повышение (<c>0x5d8b17</c>) создало реализацию
    /// своего уровня; у сменившего тип — уровни нового типа до его уровня.</summary>
    public static void AddUpTo(ISet<string> created, MapUnit before, MapUnit after, GameBook book)
    {
        bool sameType = after.TypeId.Equals(before.TypeId, StringComparison.OrdinalIgnoreCase);
        for (int level = sameType ? before.Level + 1 : 1; level <= after.Level; level++) Add(created, after.TypeId, level, book);
    }

    /// <summary>Создан ли тип следующего уровня лидера — ответ <c>0x5d89c6(…, 0)</c>: лидер ниже потолка <c>MAX_LEADER</c>
    /// (<c>0x5d8a48</c>) и реализация уровнем выше уже есть.</summary>
    public static bool NextTierCreated(ISet<string> created, string typeId, int level, int maxLeader, GameBook book) =>
        (maxLeader <= 0 || level < maxLeader) && book.Rules.Generator is not null
        && book.Rules.Units.TryGetValue(typeId, out var type) && level + 1 > type.Level && created.Contains(Key(type.Id, level + 1));

    /// <summary>Реализация в наборе: ключ типа и уровень.</summary>
    private static string Key(string typeId, int level) => $"{typeId.ToLowerInvariant()}@{level}";
}
