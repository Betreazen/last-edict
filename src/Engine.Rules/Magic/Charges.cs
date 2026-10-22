using LastEdict.Rules.Map;

namespace LastEdict.Rules.Magic;

/// <summary>Почему посох не применяется (задача 10 Ф7) — коды <c>0x5dbbe8</c> (ранг 1 по
/// дизассемблеру, отчёт B синтетических карт, ai-q34). Живых отказов зонд не видит: интерфейс зовёт
/// <c>0x5dbbe8</c> напрямую (<c>0x4a82be</c>, <c>0x4ff96b</c>), а зонд стоит на обёртке <c>0x5dbdd2</c>.</summary>
public enum StaffRefusal
{
    Ok = 0,
    /// <summary>Предмет не посох и не свиток (<see cref="ItemCategory.Wand"/>, <see cref="ItemCategory.Scroll"/>) —
    /// у движка код 1 «нет предмета/отряда/лидера/игрока»; зарядов и суточного запрета у других категорий нет вовсе.
    /// Свиток идёт тем же путём (<c>20260919-human</c>: <c>wand_precheck</c>/<c>wand_cast</c> по свиткам
    /// <c>S073IM0069</c> = IG5061, <c>S073IM0054</c> = IG5025, <c>S073IM0058</c> = IG5041, казна не тронута).</summary>
    NotAStaff = 1,
    /// <summary>Не хватает маны <c>CASTING</c> (<c>0x5869e6</c>).</summary>
    NoMana = 9,
    /// <summary>Лидер мёртв (<c>0x5e98ad</c>: hp &gt; 0) или нет способности <c>L_WAND_SCROLL_USE</c>
    /// (категория <c>0x8375a8</c>).</summary>
    CannotUse = 0xE,
    /// <summary>Не Маг: этот экземпляр сегодня уже применяли (ключ — игрок плюс <c>IM</c>).</summary>
    UsedToday = 0x11,
    /// <summary>Маг: этот экземпляр сегодня применяли уже дважды.</summary>
    MageLimit = 0x12,
}

/// <summary>Применение посоха: отказ либо новая казна.</summary>
public sealed record StaffUse(bool Done, StaffRefusal Refusal, Bank Treasury);

/// <summary>
/// Расход боевых предметов — план Ф7 §1.5, задача 10. Три вещи, механически разные:
/// <list type="bullet">
/// <item><b>сфера</b> (и зелье) исчезает сразу, но <b>только применённая</b> —
/// <see cref="Item.IsDestroyedAfterBattle"/>;</item>
/// <item><b>талисман</b> держит заряды и уничтожается на нуле — <see cref="TalismanAfterBattle"/>;</item>
/// <item><b>посох</b> зарядов не имеет: он платит ману <c>CASTING</c> и запрещён до конца суток —
/// <see cref="StaffUses"/>.</item>
/// </list>
/// <para><b>Что считается применением.</b> Сфера и талисман идут в бой атакой <c>ATTACK_ID</c>
/// (<c>getItemAttack</c> <c>0x6449b2</c>), и <b>построение</b> этой атаки применением НЕ является:
/// <c>attack_new</c> с <c>impl</c> вида <c>IM…</c> движок пишет на каждом ходу носителя (ранг 1,
/// П§16.4 — 142 таких строки при нуле применений). Применение — это удар по цели, то есть проверка
/// цели на иммунитет (<c>immune</c> с <c>always_only = false</c>); в модели его отмечает
/// <c>BattleSim.UseBattleItem</c>.</para>
/// </summary>
public static class Charges
{
    /// <summary>Остаток зарядов талисмана после боя: −1, если в этом бою его применяли.
    ///
    /// <para><b>Ранг 1</b> — прогон <c>f7-m17-charges</c> (П§18), восемь точек подряд: 5 → бой с
    /// применением → 4 → день без боёв → 4 → бой без применения → 4 → бои с применением → 3 → 2 → 1
    /// → записи нет. Ноль — <see cref="Destroyed"/>: талисман снимается и уничтожается
    /// (<c>item_unequip</c> прямо на <c>battle_end</c>, запись <c>CMidTalismanCharges</c> пропадает).
    /// Восстановления нет: за шесть ночей заряды ни разу не выросли. Надетый неприменённый не тратит
    /// (второй талисман той же партии стоял на 5 все 15 сохранений).</para>
    ///
    /// <para><b>Повтор в том же бою заряда не тратит</b> (ранг 1, П§18: вторая попытка в раунде 2 боя
    /// 1 второго заряда не сняла) — поэтому вход тут «применяли ли», а не «сколько раз».</para>
    ///
    /// <para>Механизм: <c>0x5eb9b3</c> отдаёт поле <c>+4</c> записи <c>CMidTalismanCharges</c>, а
    /// <c>0x5ebb30</c> его уменьшает (<c>sub [eax+4], ecx</c>); ветка <c>jge</c> на <c>0x6299f4</c>
    /// оставляет талисман жить.</para></summary>
    public static int TalismanAfterBattle(int charges, bool applied) =>
        charges <= 0
            ? throw new ArgumentOutOfRangeException(nameof(charges), charges,
                  "у талисмана без зарядов записи CMidTalismanCharges нет — он уничтожен ещё в прошлом бою")
            : applied ? charges - 1 : charges;

    /// <summary>Кончились ли заряды: талисман снят и уничтожен, записи в сохранении больше нет.</summary>
    public static bool Destroyed(int charges) => charges <= 0;
}

/// <summary>
/// Суточный список применений посоха — второй список <c>CMidSpellCast</c> сохранения
/// (<see cref="MapState.ItemCasts"/>).
///
/// <para><b>Ранг 1 — ключ пара «игрок + экземпляр <c>IM</c>»</b>, а не тип посоха и не герой
/// (П§19, прогон <c>f7-m21-staff</c>): два <b>одинаковых</b> посоха <c>IG6001</c> у разных героев
/// одного игрока сработали в один день оба, по 100 маны жизни каждый. Одинаковые посохи у ОДНОГО
/// героя лежат в инвентаре стопкой, и интерфейс применяет из стопки один и тот же экземпляр —
/// отсюда впечатление «второй не срабатывает» (П§18). Применённый посох, переданный другому герою
/// того же игрока, в тот же день не применяется: запрет идёт за предметом.</para>
///
/// <para><b>Ранг 1 — сброс по игроку, в начале ЕГО хода</b> (§3 пп. 36, 54): запись несёт
/// <c>TURN_STOP</c> = ход + 1 (11/11), а <c>CEffectRemoveSpell(игрок, ход)</c> из начала хода игрока
/// (<c>0x443d01</c>) через <c>0x6070c4</c> снимает записи ЭТОГО игрока при <c>ход ≥ TURN_STOP</c>
/// (<c>0x607115</c> владелец, <c>0x607132</c> срок) — в обоих списках <c>CMidSpellCast</c>. В журнале
/// <c>spell_expire</c> стоит сразу перед <c>income</c> того же игрока — 704/0; в сохранениях 22/22 записи
/// лежат с <c>TURN_STOP</c> больше последнего снятия у владельца, и все 9 записей с
/// <c>TURN_STOP = CUR_TURN</c> — у игроков, чей ход в этом дне ещё не начался. Для самого игрока это то же
/// «раз в сутки» (П§18: 2 → 0), но чужие записи в его ход ещё лежат.
/// Зарядов у посоха нет — все три посоха партии остались на месте во всех 15 сохранениях.</para>
/// </summary>
public sealed class StaffUses
{
    /// <summary>Сколько раз в сутки лорд-Маг применяет один экземпляр (<c>0x606f50</c>: Маг при ≥ 2
    /// → 0x12, остальные при ≥ 1 → 0x11). Ранг 1 по дизассемблеру; живого второго применения Магом
    /// в корпусе нет.</summary>
    public const int MageUsesPerDay = 2;

    // Список, а не множество: 0x606f50 считает записи пары, и у Мага пара лежит в списке дважды.
    // ponytail: что вторая запись Мага — отдельная строка CMidSpellCast, следует из счёта, но в
    // сохранении не видано; снимет посох Мага, применённый дважды за день.
    private readonly List<(string Player, string Item, int TurnStop)> _used = new();

    /// <summary>Сколько записей в списке — длина второго списка <c>CMidSpellCast</c>.</summary>
    public int Count => _used.Count;

    /// <summary>Пары «игрок, экземпляр» в списке (с повторами — по записи на применение).</summary>
    public IReadOnlyCollection<(string Player, string Item)> Keys => [.. _used.Select(u => (u.Player, u.Item))];

    /// <summary>Сколько записей у этого экземпляра этого игрока (<c>0x606f50</c>).</summary>
    public int UsesToday(string playerId, string instanceId)
    {
        var key = Key(playerId, instanceId);
        return _used.Count(k => (k.Player, k.Item) == key);
    }

    /// <summary>Применяли ли сегодня именно этот экземпляр этого игрока.</summary>
    public bool UsedToday(string playerId, string instanceId) => UsesToday(playerId, instanceId) > 0;

    /// <summary>Начало хода игрока <paramref name="playerId"/> на ходу <paramref name="turn"/>: снимаются его
    /// записи с <c>turn ≥ TURN_STOP</c> (<c>0x6070c4</c>, описание класса).</summary>
    public void StartTurn(string playerId, int turn)
    {
        var player = playerId.ToUpperInvariant();
        _used.RemoveAll(u => u.Player == player && turn >= u.TurnStop);
    }

    /// <summary>Список целиком пуст — так движок чистит его в конце партии (<c>spell_expire</c> с ходом
    /// 9999 по каждому игроку). Между днями — <see cref="StartTurn"/>.</summary>
    public void NewDay() => _used.Clear();

    /// <summary>Одно применение посоха. Отказ казну не меняет и в список ничего не кладёт.
    /// <para><b>Порядок калиток — ранг 1 по дизассемблеру <c>0x5dbbe8</c></b> (отчёт B, ai-q34):
    /// (1) не посох; (2) 0xE — лидер мёртв или нет <c>L_WAND_SCROLL_USE</c>; (3) 9 — мана;
    /// (4) «сегодня» — счёт по паре игрок + экземпляр, не Маг при ≥ 1 → 0x11, Маг при ≥
    /// <see cref="MageUsesPerDay"/> → 0x12. Мана раньше «сегодня» согласуется со словами игрока
    /// («на третьем — ошибка маны»); живых кодов отказа в корпусе нет — зонд их не видит.</para></summary>
    /// <param name="leaderAlive">Жив ли лидер отряда-носителя.</param>
    /// <param name="hasWandScrollUse">Есть ли у лидера <c>L_WAND_SCROLL_USE</c> (7). По умолчанию —
    /// есть: вызывающий без набора способностей (харнесс по совершённым применениям) калитку не видит.</param>
    /// <param name="lord">Категория лорда игрока; по умолчанию — не Маг.</param>
    /// <param name="turn">Ход применения: запись получает <c>TURN_STOP</c> = ход + 1
    /// (<see cref="SpellCast.TurnStop"/>).</param>
    public StaffUse Use(Item item, string playerId, string instanceId, Bank treasury,
                        bool leaderAlive = true, bool hasWandScrollUse = true,
                        LordCategory lord = LordCategory.Warrior, int turn = 0)
    {
        ArgumentNullException.ThrowIfNull(item);
        // Свиток — тем же путём и в тот же список (ранг 1 по сохранению: 20260919-human, seq 8461390 wand_cast
        // S073IM0069 = свиток G000IG5061, в t33-061 запись S073PL000A/S073IM0069; seq 15504148 и 15519893 — свитки
        // S073IM0058/S073IM0054, в t62-103 обе записи, а самих свитков в снимке уже нет). CASTING у свитка пуст —
        // казна не меняется, как и в строке wand_cast (before = after).
        if (item.Category is not (ItemCategory.Wand or ItemCategory.Scroll)) return Refuse(StaffRefusal.NotAStaff, treasury);
        if (!leaderAlive || !hasWandScrollUse) return Refuse(StaffRefusal.CannotUse, treasury);
        if (Economy.NotEnough(treasury, item.CastingCost)) return Refuse(StaffRefusal.NoMana, treasury);
        int uses = UsesToday(playerId, instanceId);
        if (lord == LordCategory.Mage)
        {
            if (uses >= MageUsesPerDay) return Refuse(StaffRefusal.MageLimit, treasury);
        }
        else if (uses >= 1) return Refuse(StaffRefusal.UsedToday, treasury);

        var (p, i) = Key(playerId, instanceId);
        _used.Add((p, i, SpellCast.TurnStop(turn)));
        return new StaffUse(true, StaffRefusal.Ok, SpellCast.PayCasting(treasury, item));
    }

    private static StaffUse Refuse(StaffRefusal why, Bank treasury) => new(false, why, treasury);

    /// <summary>Записи из сохранения: <c>PlayerId</c> плюс <c>ORIGIN</c> (экземпляр <c>IM</c>).</summary>
    public void Load(IEnumerable<CastRecord> itemCasts)
    {
        ArgumentNullException.ThrowIfNull(itemCasts);
        foreach (var r in itemCasts)
        {
            var (p, i) = Key(r.PlayerId, r.Origin);
            _used.Add((p, i, r.TurnStop));
        }
    }

    // Идентификаторы в сохранениях встречаются в обоих регистрах (S143IM0004 / s143im0004) — ключ
    // приводится к одному виду, иначе один и тот же экземпляр даст две записи.
    private static (string, string) Key(string playerId, string instanceId) =>
        (playerId.ToUpperInvariant(), instanceId.ToUpperInvariant());
}
