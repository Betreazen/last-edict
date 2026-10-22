using D2.Rules.Map;
using D2.Rules.Units;

namespace D2.Rules.Magic;

/// <summary>Что заклинание делает. Разложено по <b>заполненным колонкам</b>, а не по категории:
/// у <c>g000ss0100</c> («Swift») эффекта два — возврат очков хода и модификатор.</summary>
public enum SpellEffectKind
{
    /// <summary>Урон <c>DAMAGE_QTY</c> источника <c>DAMAGE_SRC</c>.</summary>
    Damage,
    /// <summary>Лечение <c>HEAL_QTY</c>.</summary>
    Heal,
    /// <summary>Модификатор <c>MODIF_ID</c> — тот же механизм, что у предмета и варда
    /// (<see cref="Modifier"/>, <see cref="EffectiveStats"/>).</summary>
    Modifier,
    /// <summary>Призыв юнита <c>UNIT_ID</c>.</summary>
    Summon,
    /// <summary>Возврат очков хода <c>RESTORE_M</c>.</summary>
    RestoreMove,
    /// <summary>Напустить туман войны.</summary>
    Fog,
    /// <summary>Развеять туман войны.</summary>
    Unfog,
    /// <summary>Невидимость.</summary>
    Invisibility,
    /// <summary>Снятие жезла («Sinkhole», единственное в ванили).</summary>
    RemoveRod,
    /// <summary>Смена земли на <c>GROUND_CAT</c>.</summary>
    ChangeGround,
    /// <summary>Оберег <c>WARD1..4</c> (модификатор <c>Gmodif</c>) юнитам отряда в клетке — категория <see cref="SpellCategory.GiveWards"/>.</summary>
    Ward,
}

/// <summary>Один эффект заклинания. Незначащие поля остаются умолчаниями: у урона это
/// <see cref="Amount"/> и <see cref="Source"/>, у модификатора и призыва — <see cref="Id"/>,
/// у смены земли — <see cref="Ground"/>.</summary>
public readonly record struct SpellEffect(
    SpellEffectKind Kind, int Amount = 0, int Source = -1, string Id = "", int Ground = -1);

/// <summary>Применение заклинания: цена, площадь, эффекты.
///
/// <para><b>Кто и когда кастует — не здесь.</b> Выбор заклинания и цели — политика ИИ, Ф7. Здесь
/// только применение уже выбранного.</para>
///
/// <para><b>Ветвь боя пуста.</b> Все 120 вани́льных заклинаний — заклинания карты
/// (<see cref="Spell"/>, ранг 1). Поэтому проверяются они диффом <c>.sg</c>, а не журналом боя, и
/// сплошной проход по корпусу (74 файла схемы v3, 07.09.2026) дал по заклинаниям <b>ноль</b> следов
/// — ни события, ни поля, ни идентификатора <c>g000ss*</c>. Молчание корпуса при этом ничего не
/// доказывает и доказать не может: событий <c>attack_new</c> и <c>immune</c> в схеме v3 нет вовсе,
/// они заведены только в v4 (задача 4), а журналов v4 ещё не существует — их даст прогон
/// задачи 13.</para>
///
/// <para><b>Обереги заклинание карты не тратит — и потратить в ванили не может (задача 26 Ф7).</b>
/// Вики утверждает обратное (разбор §4 Н19: «заклинание на карте тратит вард, и в бою в тот же ход
/// защиты уже не будет», «защита — один раз за игровой ход, а не за бой»). В порт это не пошло, и
/// вот почему: (1) колонки <c>Gspells.WARD1..4</c> пусты у всех 120 строк ванили, то есть на карте
/// оберег выдать нечем — сверяет счётчик <c>spell_ward_columns</c>; (2) остаток одноразового
/// оберега живёт в <c>BattleMsgData</c> (байт <c>+0x24</c> записи варда, <c>0x62190F…0x62196C</c>,
/// см. <see cref="D2.Rules.Battle.Immunity"/>), а не в <c>.sg</c>: ни у <c>CMidUnit</c>
/// (<c>midunit.h:38-55</c>, 80 байт), ни у <c>CMidStack</c> поля «оберег потрачен» нет вовсе,
/// и переносить расход через день физически некуда. Поэтому «раз за бой» остаётся как есть, а
/// утверждение вики уходит открытым пунктом §3 плана Ф7: закрывает его живой замер — два боя одним
/// носителем оберега в один день.</para></summary>
public static class SpellCast
{
    /// <summary>
    /// Сколько дней держится эффект, наложенный на карте, — <b>один</b>: запись получает
    /// <c>TURN_STOP = CUR_TURN + 1</c>.
    ///
    /// <para><b>Ранг 1 — литерал найден в обоих списках сохранения (задача 26 Ф7).</b> Ход берётся
    /// из <c>CScenarioInfo.currentTurn</c> (поле <c>+0x20</c>, геттер <c>0x47A91A</c>;
    /// <c>scenarioinfo.h:41</c>), к нему прибавляется единица (<c>inc eax</c>) и результат уезжает
    /// в визитор:
    /// <list type="bullet">
    /// <item><c>0x5DC4A0</c> → <c>visitorAddSpellCast</c> (<c>0x5E7EC5</c>) →
    /// <c>CVisitorAddSpellCast</c> (<c>0x613558</c>, <c>apply 0x613613</c>) → запись
    /// <c>PLAYER_ID / ORIGIN / TURN_STOP</c> в <c>CMidSpellCast</c> (<c>0x606F64</c>) — это
    /// <see cref="D2.Rules.Map.MapState.SpellCasts"/> и
    /// <see cref="D2.Rules.Map.MapState.ItemCasts"/>;</item>
    /// <item><c>0x5D7187</c> → <c>visitorAddSpellEffect</c> (<c>0x5E7DDA</c>) →
    /// <c>CVisitorAddSpellEffect</c> (<c>0x6131CF</c>) → запись
    /// <c>UNIT_ID / ORIGIN_ID / MODIF_ID / CASTER_ID / TURN_STOP</c> в <c>CMidSpellEffects</c> —
    /// это сам модификатор на юните.</item>
    /// </list>
    /// Вики («до начала вашего следующего хода», разбор §4 Н18) с этим сходится, а «зелье,
    /// выпитое в бою, действует и после» — то же самое правило: запись живёт до следующего дня
    /// владельца независимо от того, кто её сделал.</para>
    ///
    /// <para><b>Ранг 1 — момент снятия</b> (§3 п. 54): в начале хода ВЛАДЕЛЬЦА записи —
    /// <c>CEffectRemoveSpell(игрок, ход)</c> из <c>0x443d01</c>, фильтр <c>0x6070c4</c> (игрок и
    /// <c>ход ≥ TURN_STOP</c>) на оба списка <c>CMidSpellCast</c>; эффекты на юнитах (<c>0x43b9ef</c>) — тем же
    /// сроком, владелец — <c>CASTER_ID</c>, пустой — хозяин юнита (<c>0x43baa9</c>). Запись дня N
    /// действует и в день N+1, пока владелец не начал ход: 704/0 строк <c>spell_expire</c> перед его
    /// <c>income</c>, 9/9 записей с <c>TURN_STOP = CUR_TURN</c> в сохранениях — у ещё не ходивших.
    /// Харнесс <c>map_effect_turn_stop</c> (окно <c>[CUR_TURN, CUR_TURN + 1]</c>) остаётся верным.</para>
    /// </summary>
    public const int MapEffectDays = 1;

    /// <summary><c>TURN_STOP</c> записи, сделанной на ходу <paramref name="turn"/>.</summary>
    public static int TurnStop(int turn) => turn + MapEffectDays;

    /// <summary>Действует ли ещё запись с таким <c>TURN_STOP</c> на ходу <paramref name="turn"/>.</summary>
    /// <param name="ownerStarted">Владелец записи уже начал ход <paramref name="turn"/>. По умолчанию —
    /// да: так смотрит сам владелец, и тогда в день <c>TURN_STOP</c> записи уже нет.</param>
    public static bool Live(int turnStop, int turn, bool ownerStarted = true) =>
        turn < turnStop || (turn == turnStop && !ownerStarted);

    /// <summary>Хватает ли казны на применение. Сравниваются все шесть валют: мана бывает пяти
    /// видов, и не та мана не выручает.</summary>
    public static bool CanPay(Bank treasury, Spell spell)
    {
        ArgumentNullException.ThrowIfNull(treasury);
        return treasury.Covers(spell.CastingCost);
    }

    /// <summary>Списать цену применения. Возвращается новая казна — прежняя не меняется.
    /// Нехватка — отказ, а не отрицательный остаток.</summary>
    public static Bank Pay(Bank treasury, Spell spell) =>
        CanPay(treasury, spell)
            ? treasury - spell.CastingCost
            : throw new InvalidOperationException(
                  $"заклинание {spell.Id}: на применение не хватает казны " +
                  $"(нужно {spell.CastingCost}, есть {treasury})");

    /// <summary>
    /// Хватает ли на применение с правом брать из второго кошелька (<c>SPELL_BANK</c>): основной казны, либо — при
    /// <paramref name="useSpellBank"/> — основной и второго кошелька вместе, по каждой валюте.
    /// </summary>
    public static bool CanPay(Bank treasury, Bank spellBank, Spell spell, bool useSpellBank)
    {
        ArgumentNullException.ThrowIfNull(spell);
        if (CanPay(treasury, spell)) return true;
        return useSpellBank && CanPay(treasury + spellBank, spell);
    }

    /// <summary>
    /// Оплата применения (<c>0x5dc334</c>, <c>docs/corpus-schema.md</c> «строки <c>bank</c> внутри»): хватает основной
    /// казны — она уменьшается на цену. Иначе, с правом второго кошелька, недостача по каждой валюте
    /// (<c>max(0, цена − казна)</c>) снимается со второго кошелька, а основная казна обнуляется целиком, золото тоже.
    /// Ранг 2: схема и журнал (human, день 44: казна смерти 149 при цене 200 — второй кошелёк −51, казна в ноль).
    /// </summary>
    public static (Bank Treasury, Bank SpellBank) Pay(Bank treasury, Bank spellBank, Spell spell, bool useSpellBank)
    {
        ArgumentNullException.ThrowIfNull(spell);
        if (CanPay(treasury, spell)) return (treasury - spell.CastingCost, spellBank);
        if (!CanPay(treasury, spellBank, spell, useSpellBank))
            throw new InvalidOperationException($"заклинание {spell.Id}: на применение не хватает казны " +
                                                $"(нужно {spell.CastingCost}, есть {treasury} и {spellBank})");
        var c = spell.CastingCost;
        var shortfall = c.Zip(treasury, (need, have) => Math.Max(0, need - have));
        return (Bank.Zero, spellBank - shortfall);
    }

    /// <summary>Списать ману за одно применение посоха — колонка <c>CASTING</c> предмета
    /// (<see cref="Item.CastingCost"/>).
    ///
    /// <para><b>Ранг 1, П§18 и §19:</b> у посоха зарядов нет вовсе, платой служит мана, и цена
    /// берётся из его собственной колонки, а не из заклинания (у двух жезлов ванили они расходятся —
    /// <see cref="Item.CastingCost"/>). Замер: <c>f7-m17-charges</c> — <c>IG6001</c> снял 100 маны
    /// жизни, <c>IG6011</c> — по 150 смерти и рун; <c>f7-m21-staff</c> — три применения подряд
    /// 100 / 100 / 150+150.</para>
    ///
    /// <para>Обрезка по девяти тысячам девятистам девяноста девяти здесь не нужна: вычитание казну
    /// только уменьшает (<see cref="Economy.Clamp"/> нужен сложению). Суточный запрет и категория —
    /// <see cref="StaffUses.Use"/>, одна точка входа применения.</para></summary>
    /// <exception cref="InvalidOperationException">Маны не хватает — нехватка это отказ, а не
    /// отрицательный остаток (как у <see cref="Pay(Bank, Spell)"/>).</exception>
    public static Bank PayCasting(Bank treasury, Item item)
    {
        ArgumentNullException.ThrowIfNull(item);
        return Economy.NotEnough(treasury, item.CastingCost)
            ? throw new InvalidOperationException(
                  $"предмет {item.Id}: на применение не хватает казны " +
                  $"(нужно {item.CastingCost}, есть {treasury})")
            : treasury - item.CastingCost;
    }

    /// <summary>Эффекты заклинания — по заполненным колонкам плюс те четыре категории, у которых
    /// колонки полезной нагрузки нет вовсе (туман, снятие тумана, невидимость, снятие жезла).
    ///
    /// <para><b>Ранг 1 по данным:</b> кросс-таблица «категория × заполненные колонки» по всем 120
    /// живым строкам ванили сходится без исключений (таблица — в <see cref="SpellCategory"/>), и
    /// ровно она заставляет разбирать по колонкам, а не по категории: у <c>g000ss0100</c> «Swift»
    /// (категория <see cref="SpellCategory.RestoreMove"/>) заполнены и <c>RESTORE_M</c> = 30, и
    /// <c>MODIF_ID</c> = <c>g000um7538</c> (<c>TYPE 7</c> MoveAllowance +30 %). Разбор по категории
    /// потерял бы половину этого заклинания молча.</para>
    ///
    /// <para><b>Ранг 3 — порядок эффектов внутри списка</b> (здесь: урон, лечение, модификатор,
    /// призыв, очки хода, земля). У единственного двухэффектного заклинания ванили порядок
    /// ненаблюдаем — эффекты независимы. Снимет: дифф <c>.sg</c> задачи 13 после «Swift» на отряде
    /// с нулевым остатком хода, если движок сперва вернёт 30 очков, а потом добавит 30 % от
    /// вернувшегося (39) вместо 30 % от полного запаса.</para></summary>
    public static IReadOnlyList<SpellEffect> Effects(Spell spell)
    {
        // Обереги — свой результат категории (CSpellResultGiveWards 0x41ce93): он читает только WARD1..4 (0x582b37), MODIF_ID и
        // прочие колонки — нет; у остальных категорий WARD1..4 движком не трогаются. В ванили пусты у всех 120 строк — заклинания модов.
        if (spell.Category == SpellCategory.GiveWards)
            return spell.Wards.Count > 0
                ? [.. spell.Wards.Select(w => new SpellEffect(SpellEffectKind.Ward, Id: w))]
                : throw new InvalidDataException($"заклинание {spell.Id} категории GiveWards без оберегов (WARD1..4 пусты)");

        var effects = new List<SpellEffect>(2);
        if (spell.DamageQty != 0)
            effects.Add(new SpellEffect(SpellEffectKind.Damage, spell.DamageQty, Source: spell.DamageSource));
        if (spell.HealQty != 0)
            effects.Add(new SpellEffect(SpellEffectKind.Heal, spell.HealQty));
        if (spell.ModifierId.Length > 0)
            effects.Add(new SpellEffect(SpellEffectKind.Modifier, Id: spell.ModifierId));
        if (spell.SummonUnitId.Length > 0)
            effects.Add(new SpellEffect(SpellEffectKind.Summon, Id: spell.SummonUnitId));
        if (spell.RestoreMovePercent != 0)
            effects.Add(new SpellEffect(SpellEffectKind.RestoreMove, spell.RestoreMovePercent));

        // Земля — по категории, а не по величине: GROUND_CAT = 0 это равнина (Lground L_PLAIN),
        // законное значение, и «ноль» здесь не значит «пусто».
        SpellEffect? byCategory = spell.Category switch
        {
            SpellCategory.ChangeTerrain => new SpellEffect(SpellEffectKind.ChangeGround, Ground: spell.Ground),
            SpellCategory.Fog => new SpellEffect(SpellEffectKind.Fog),
            SpellCategory.Unfog => new SpellEffect(SpellEffectKind.Unfog),
            SpellCategory.Invisibility => new SpellEffect(SpellEffectKind.Invisibility),
            SpellCategory.RemoveRod => new SpellEffect(SpellEffectKind.RemoveRod),
            _ => null,
        };
        if (byCategory is { } categoryEffect) effects.Add(categoryEffect);

        return effects.Count > 0
            ? effects
            : throw new InvalidDataException(
                  $"заклинание {spell.Id} (категория {spell.Category}) не несёт ни одного эффекта: " +
                  "ни одна колонка полезной нагрузки не заполнена");
    }

    /// <summary>
    /// Снятие жезла заклинанием — <see cref="SpellEffectKind.RemoveRod"/>, в ванили единственное
    /// («Sinkhole» <c>g000ss0119</c>, <see cref="SpellCategory.RemoveRod"/>). Отличий от сноса
    /// отрядом (<see cref="Rods.Break"/>) три, все ранг 1:
    /// <list type="bullet">
    /// <item><b>свой жезл — отказ</b> (<see cref="Rods.SpellRefusal"/>), и отказ ДО списания: мана
    /// не убывает (run2 §2.4);</item>
    /// <item>очки хода отряда не при чём — заклинание кастует игрок, а не отряд, поэтому
    /// <see cref="RodCommand.MoveLeft"/> здесь всегда <c>-1</c> «отряда в команде нет»;</item>
    /// <item>штрафа <c>D_ROD</c> нет: <c>CEffectBreakRod</c> пути заклинания строится с пустым
    /// виновником (<c>0x41CD7E</c>), а штраф берёт только непустой (run4 §4.6).</item>
    /// </list>
    /// Землю снос не возвращает и здесь: она замерзает (<see cref="Rods.Break"/>).
    /// </summary>
    /// <exception cref="ArgumentException">Заклинание не той категории.</exception>
    public static RodCommand RemoveRod(Spell spell, Bank treasury, MapRod rod, string casterPlayerId)
    {
        ArgumentNullException.ThrowIfNull(spell);
        if (spell.Category != SpellCategory.RemoveRod)
            throw new ArgumentException($"заклинание {spell.Id} категории {spell.Category}, " +
                                        "а не RemoveRod", nameof(spell));
        if (Rods.SpellRefusal(rod, casterPlayerId) is { } why)
            return new RodCommand(NoStack, treasury, Refused: why);
        return new RodCommand(NoStack, Pay(treasury, spell), Removed: rod.Id);
    }

    /// <summary>«Отряда в этой команде нет» в <see cref="RodCommand.MoveLeft"/>: заклинание тратит
    /// ману игрока, а не очки хода отряда.</summary>
    private const int NoStack = -1;

    /// <summary>Модификатор заклинания из общего справочника <c>Gmodif</c>/<c>GmodifL</c>.
    /// <para>Своей цепочки заклинание не заводит: оно кладёт эффект <b>тем же</b> механизмом, что
    /// предмет, вард и способность лидера, — модификатором поверх базового юнита
    /// (<see cref="EffectiveStats"/>, задача 2). Ранг 1 по данным: все 46 непустых
    /// <c>Gspells.MODIF_ID</c> ванили разрешаются в <c>Gmodif</c>, висячих ноль.</para>
    /// <para>Пусто — не ошибка: у 74 из 120 заклинаний модификатора нет, и это отсутствие ссылки
    /// (<c>g000000000</c>), а не висячая ссылка. Ошибка — только ссылка, которая никуда не ведёт.</para></summary>
    public static Modifier? ModifierOf(Spell spell, IReadOnlyDictionary<string, Modifier> catalogue)
    {
        if (spell.ModifierId.Length == 0) return null;
        return catalogue.TryGetValue(spell.ModifierId, out var modifier)
            ? modifier
            : throw new InvalidDataException(
                  $"заклинание {spell.Id}: модификатор \"{spell.ModifierId}\" в Gmodif не найден");
    }

    /// <summary>Клетки, накрытые заклинанием: квадрат со стороной <c>AREA</c> с центром в
    /// <paramref name="centerX"/>, <paramref name="centerY"/>, обрезанный краем карты.
    ///
    /// <para><b>Ранг 1 — величина.</b> <c>AREA</c> читается как обычное целое без границ и без
    /// таблицы категорий (см. <see cref="Spell.Area"/>), в ванили принимает только нечётные 1, 3, 5,
    /// 7, 9 и 999. Нечётность — признак стороны с центром, а не радиуса. 999 не особый случай: ни
    /// одного сравнения с <c>0x3e7</c> в коде нет, число просто больше стороны любой карты, и
    /// обрезка краем сама превращает его в «вся карта» — потому здесь и нет ветки под него.</para>
    ///
    /// <para><b>Ранг 3 — форма.</b> Квадрат против ромба (манхэттенского радиуса) данными не
    /// различается: сторона у обоих нечётная, а тексты самой игры говорят лишь «в центре области
    /// заклинания» и «малая»/«большая» область (<c>Tglobal</c>, описания <c>g000ss0081</c>,
    /// <c>g000ss0074</c>). Взят квадрат — он согласован с квадратной клеточной сеткой карты и с
    /// обзором (<c>Fog</c>). Снимает задача 13 счётом клеток: «Create Forest» (<c>g000ss0120</c>,
    /// <c>AREA</c> 7) в центре карты меняет 49 клеток квадратом и 25 ромбом — дифф <c>.sg</c>
    /// по слою земли отвечает однозначно.</para></summary>
    public static IEnumerable<(int X, int Y)> Tiles(Spell spell, int centerX, int centerY,
                                                   int mapWidth, int mapHeight)
    {
        int r = spell.AreaRadius;
        int x0 = Math.Max(0, centerX - r), x1 = Math.Min(mapWidth - 1, centerX + r);
        int y0 = Math.Max(0, centerY - r), y1 = Math.Min(mapHeight - 1, centerY + r);
        for (int y = y0; y <= y1; y++)
            for (int x = x0; x <= x1; x++)
                yield return (x, y);
    }

    /// <summary>Сколько HP вернёт лечащее заклинание юниту: <c>min(HEAL_QTY, HpMax − Hp)</c>.
    /// <para><b>Ранг 3.</b> Потолок взят у боевого лечения, где он измерен на живом журнале
    /// (<c>corpus/20260906-b</c>: Oracle Elf, 60 с обрезкой по недостающим HP — см.
    /// <c>Attacks.ApplyHeal</c>); что заклинание карты считает так же, журналом не проверено, а его
    /// величины крупнее (<c>g000ss0020</c> «Major healing» — 150). Снимает дифф <c>.sg</c> задачи 13:
    /// «Major healing» на отряде, где раненому не хватает до полного меньше 150.</para>
    /// <para><c>Math.Max</c> — на случай <c>Hp &gt; HpMax</c> в снимке: лечение не должно отнимать.</para></summary>
    public static int HealAmount(Spell spell, int hp, int hpMax) =>
        Math.Max(0, Math.Min(spell.HealQty, hpMax - hp));
}
