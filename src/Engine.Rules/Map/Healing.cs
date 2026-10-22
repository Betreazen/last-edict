namespace D2.Rules.Map;

/// <summary>Коды отказа <c>checkCantReviveUnit</c> (Gog <c>0x5DE06E</c>). Ранг 1 (cities §3.1).</summary>
public enum ReviveCheck
{
    Allowed = 0,
    NotEnoughBank = 2,
    Alive = 5,
    NoTemple = 6,
    NotInFortification = 7,
}

/// <summary>Итог лечения или воскрешения: новое HP и казна; <see cref="Refused"/> непусто — операции не было.</summary>
public sealed record HealResult(int Hp, Bank Purse, string? Refused = null);

/// <summary>
/// Платное лечение и воскрешение в укреплении — план Ф7 §1.1, задача 6.
/// <para><b>Где.</b> Храм строится в столице, а действует в <b>любом</b> укреплении: проверяется «у игрока есть
/// постройка» (<c>CPlayerBuildings</c>, <c>player+0x48</c>) и «отряд внутри укрепления» (<c>stack+0x94</c>). Ранг 1,
/// <c>0x5DE06E</c> (cities §3.1).</para>
/// </summary>
public static class Healing
{
    /// <summary><c>Lbuild.dbf</c>: категория 1 — лечебница (Храм у каждой расы: <c>g000bb0023</c>, <c>…0047</c>,
    /// <c>…0072</c>, <c>…0097</c>, <c>…0118</c>). Ранг 1 — таблица; что код 6 ищет именно по категории, а не по
    /// id, — ранг 2 (адрес проверки внутри <c>0x5DE06E</c> не расписан).</summary>
    public const int TempleCategory = 1;

    /// <summary>Есть ли у игрока постройка категории <see cref="TempleCategory"/>.</summary>
    public static bool HasTemple(IEnumerable<string> buildings, IReadOnlyDictionary<string, BuildingType> catalog) =>
        buildings.Any(b => catalog.TryGetValue(b, out var t) && t.Category == TempleCategory);

    /// <summary>Можно ли воскресить — коды в порядке <c>0x5DE06E</c>: 5 жив, 2 не хватает казны, 6 нет Храма,
    /// 7 отряд не в укреплении. Ранг 1 на коды; порядок проверок — ранг 2 (в таком порядке их перечисляет
    /// разбор, отдельного замера порядка нет).</summary>
    public static ReviveCheck CheckRevive(int hp, Bank purse, Bank price, bool hasTemple, bool insideFort)
    {
        if (hp > 0) return ReviveCheck.Alive;
        if (Treasury.Affordable(purse, price) < 1) return ReviveCheck.NotEnoughBank;
        if (!hasTemple) return ReviveCheck.NoTemple;
        return insideFort ? ReviveCheck.Allowed : ReviveCheck.NotInFortification;
    }

    /// <summary>Воскрешение: плоский <c>REVIVE_C</c>, <b>HP после — 1</b>. Ранг 1: <c>reviveUnit</c> <c>0x5D733F</c>,
    /// прогон 2 — Ангел 1/225 после четырёх воскрешений по 600; вторая большая партия — лечение сразу за
    /// воскрешением списало ровно <c>2 × (HPmax − 1)</c>.</summary>
    public static HealResult Revive(int hp, Bank price, Bank purse, bool hasTemple, bool insideFort)
    {
        var check = CheckRevive(hp, purse, price, hasTemple, insideFort);
        return check == ReviveCheck.Allowed
            ? new HealResult(1, Treasury.Pay(purse, price))
            : new HealResult(hp, purse, $"воскрешение отказано: код {(int)check} ({check})");
    }

    /// <summary>
    /// Лечение: <c>HEAL_C</c> за каждый недостающий HP, пока хватает казны — <see cref="Treasury.Heal"/>
    /// (<c>computeAffordableHealHp</c> <c>0x5DE1C1</c>). Мёртвого и здорового лечить нельзя. Ранг 1: прогон 2 —
    /// полные 40/30/675/597/358/318/372, частичные 255 (HP 1 → 86/225) и «в казне 28 — 9 HP».
    /// <para>Проверку <c>0x5DE32F</c> дизассемблер не расписывал: условия Храма и укрепления взяты те же, что у
    /// воскрешения (ранг 2), кодов у отказа нет — только причина словами.</para>
    /// </summary>
    public static HealResult Heal(int hp, int hpMax, Bank rate, Bank purse, bool hasTemple, bool insideFort)
    {
        if (hp <= 0) return new HealResult(hp, purse, "мёртвого не лечат — сначала воскрешение");
        if (hp >= hpMax) return new HealResult(hp, purse, "юнит здоров");
        if (!hasTemple) return new HealResult(hp, purse, "у игрока нет Храма");
        if (!insideFort) return new HealResult(hp, purse, "отряд не в укреплении");
        var bought = Treasury.Heal(rate, hpMax - hp, purse);
        return bought.Points == 0
            ? new HealResult(hp, purse, "казны не хватает ни на один HP")
            : new HealResult(hp + bought.Points, Treasury.Pay(purse, bought.Cost));
    }
}
