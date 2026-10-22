using LastEdict.Rules.Map;
using LastEdict.Rules.Units;

namespace LastEdict.Rules.Magic;

/// <summary>Что предмет делает. Разложено по <b>заполненным колонкам</b>, а не по категории — ровно
/// как у заклинания (<see cref="SpellCast.Effects"/>): категория выбирает конструктор, а
/// конструктор читает свои колонки, и у оружия слот модификатора и слот атаки живут в одном
/// классе.</summary>
public enum ItemEffectKind
{
    /// <summary>Модификатор от надетого предмета — <c>MOD_EQUIP</c>. Действует, пока предмет надет.</summary>
    EquipModifier,
    /// <summary>Модификатор от выпитого зелья — <c>MOD_POTION</c>.</summary>
    PotionModifier,
    /// <summary>Лечение на <c>HP_POTION</c> очков.</summary>
    Heal,
    /// <summary>Воскрешение (<see cref="ItemCategory.PotionRevive"/>) — своей колонки нет,
    /// эффект несёт категория.</summary>
    Revive,
    /// <summary>Заклинание <c>SPELL_ID</c>: свиток применяет его даром, жезл — за
    /// <see cref="Item.CastingCost"/>.</summary>
    Spell,
    /// <summary>Атака <c>ATTACK_ID</c> из <c>Gattacks</c> — оружие, сфера, талисман.</summary>
    Attack,
}

/// <summary>Один эффект предмета. Незначащие поля остаются умолчаниями.</summary>
public readonly record struct ItemEffect(ItemEffectKind Kind, string Id = "", int Amount = 0);

/// <summary>Отказы «нельзя дать это зелье этому юниту».
///
/// <para><b>Ранг 1, дизассемблер GOG.</b> Задание давало эту карту заголовком тулсета
/// (<c>game.h:95-105</c>) и требовало подтвердить её или пометить рангом 2. Подтверждена:
/// <c>canApplyPotionToUnit</c> (Gog <c>0x5de495</c>, адрес взят из третьего блока
/// <c>D2ModdingToolset/mss32/src/game.cpp:429</c>) прочитана целиком до <c>ret 0x10</c> —
/// восемь возвращаемых значений совпали с заголовком **число в число**, и сверх заголовка
/// прочитан <b>порядок</b> проверок, которого в нём нет. Пропущенных значений (2, 4, 6, 7) в теле
/// действительно не встречается.</para>
///
/// <para>Порядок, в котором движок отвечает (он важен: юнит бывает виноват сразу в двух):
/// <list type="number">
/// <item>предмета нет в инвентаре держателя — <see cref="NoItemInInventory"/>
/// (<c>0x5de4a5</c> → <c>0x5de66a</c>: индекс вещи в инвентаре равен −1);</item>
/// <item>категория не одна из четырёх зельевых — <see cref="NotAPotion"/> (<c>0x5de55b</c>);</item>
/// <item>усиление и постоянное зелье (<see cref="ItemCategory.PotionBoost"/>,
/// <see cref="ItemCategory.PotionPermanent"/>): живому, у которого этот модификатор уже висит, —
/// <see cref="AlreadyApplied"/> (<c>0x5de603</c>); мёртвому — <see cref="CannotBoostDead"/>
/// (<c>0x5de60d</c>);</item>
/// <item>лечение (<see cref="ItemCategory.PotionHeal"/>): мёртвому —
/// <see cref="CannotHealDead"/> (<c>0x5de630</c>); целому —
/// <see cref="AlreadyAtFullHp"/> (<c>0x5de639</c>, сравнение <c>hp &gt;= hpMax</c>);</item>
/// <item>воскрешение (<see cref="ItemCategory.PotionRevive"/>): живому —
/// <see cref="AlreadyAlive"/> (<c>0x5de65c</c>);</item>
/// <item>иначе <see cref="Ok"/> (<c>0x5de661</c>: <c>xor eax, eax</c>).</item>
/// </list></para>
///
/// <para><b>«Жив» — это ровно «текущих HP больше нуля»</b> (ранг 1: <c>0x5e98ad</c> —
/// <c>cmp [unit+0x20], 0; setg al</c>, три инструкции). Поэтому в
/// <see cref="ItemUse.CanApplyPotion"/> отдельного признака жизни нет: он выводится из
/// <c>hp</c>.</para></summary>
public enum PotionResult
{
    Ok = 0,
    NoItemInInventory = 1,
    AlreadyAtFullHp = 3,
    AlreadyAlive = 5,
    NotAPotion = 8,
    AlreadyApplied = 9,
    CannotBoostDead = 10,
    CannotHealDead = 11,
}

/// <summary>Применение предмета: эффекты, цена, отказы.
///
/// <para><b>Кто и когда пьёт зелье — не здесь.</b> Выбор предмета и цели ИИ — Ф7 (об этом прямо
/// сказано в «Что заведомо не входит в Ф6»). Здесь только «можно ли» и «что произойдёт».</para>
///
/// <para><b>Своей цепочки модификаторов предмет не заводит</b> — отдаёт идентификатор в
/// <see cref="EffectiveStats"/>, тот же механизм, что у заклинания и варда.</para></summary>
public static class ItemUse
{
    /// <summary>Эффекты предмета — по заполненным колонкам плюс единственная категория, у которой
    /// колонки полезной нагрузки нет вовсе (<see cref="ItemCategory.PotionRevive"/>: один предмет
    /// ванили, <c>G000IG0001</c>).
    ///
    /// <para><b>Ранг 1 по данным:</b> кросс-таблица «категория × заполненные колонки» по всем 336
    /// живым строкам сходится с тем, какой конструктор движок ставит на категорию (таблица — в
    /// <see cref="ItemCategory"/>), без единого исключения. Сумма по видам: <c>MOD_EQUIP</c> 41,
    /// <c>MOD_POTION</c> 20, <c>HP_POTION</c> 3, воскрешение 1, <c>SPELL_ID</c> 141,
    /// <c>ATTACK_ID</c> 90 — итого 296 эффектов на 296 предметов, ни у одного больше одного.</para>
    ///
    /// <para><b>Пусто — законный ответ для двух категорий</b> и только для них:
    /// <see cref="ItemCategory.Valuable"/> (10 штук — сокровища на продажу) и
    /// <see cref="ItemCategory.Special"/> (30 — сюжетные, у них и цена нулевая). У любой другой
    /// категории пустой список означает испорченные данные, и молчать об этом нельзя.</para></summary>
    public static IReadOnlyList<ItemEffect> Effects(Item item)
    {
        var effects = new List<ItemEffect>(1);
        if (item.EquipModifierId.Length > 0)
            effects.Add(new ItemEffect(ItemEffectKind.EquipModifier, item.EquipModifierId));
        if (item.PotionModifierId.Length > 0)
            effects.Add(new ItemEffect(ItemEffectKind.PotionModifier, item.PotionModifierId));
        if (item.HealHitPoints != 0)
            effects.Add(new ItemEffect(ItemEffectKind.Heal, Amount: item.HealHitPoints));
        if (item.Category is ItemCategory.PotionRevive)
            effects.Add(new ItemEffect(ItemEffectKind.Revive));
        if (item.SpellId.Length > 0)
            effects.Add(new ItemEffect(ItemEffectKind.Spell, item.SpellId));
        if (item.AttackId.Length > 0)
            effects.Add(new ItemEffect(ItemEffectKind.Attack, item.AttackId));

        if (effects.Count > 0) return effects;
        if (item.Category is ItemCategory.Valuable or ItemCategory.Special) return effects;
        throw new InvalidDataException(
            $"предмет {item.Id} (категория {item.Category}) не несёт ни одного эффекта: " +
            "ни одна колонка полезной нагрузки не заполнена");
    }

    /// <summary>Можно ли применить зелье. Порядок проверок — движковый, см.
    /// <see cref="PotionResult"/>; отдельного признака жизни нет, потому что движок считает живым
    /// того, у кого <paramref name="hp"/> больше нуля.</summary>
    /// <param name="inInventory">Лежит ли предмет в инвентаре держателя. Считается по состоянию
    /// отряда — <see cref="Equipment.Carried"/> (инвентарь <c>MidStack.ITEM_ID</c> плюс семь слотов
    /// снаряжения): прежняя оговорка «инвентаря у правил Ф6 нет» снята задачей 9, инвентарь читается
    /// из <c>.sg</c> (<see cref="Map.MapStack.Items"/>, задача 2). Остаётся признаком, а не отрядом,
    /// потому что зелье пьют и в бою, где держателя описывает не <c>MapStack</c>.</param>
    /// <param name="modifierAlreadyApplied">Висит ли на юните уже этот самый модификатор
    /// (<c>0x5eacaa</c> в движке). Осмыслен только для зелий усиления и постоянных.</param>
    public static PotionResult CanApplyPotion(Item item, bool inInventory, int hp, int hpMax,
                                              bool modifierAlreadyApplied = false)
    {
        if (!inInventory) return PotionResult.NoItemInInventory;
        if (!item.IsPotion) return PotionResult.NotAPotion;

        bool alive = hp > 0;
        return item.Category switch
        {
            ItemCategory.PotionBoost or ItemCategory.PotionPermanent =>
                !alive ? PotionResult.CannotBoostDead
                : modifierAlreadyApplied ? PotionResult.AlreadyApplied
                : PotionResult.Ok,
            ItemCategory.PotionHeal =>
                !alive ? PotionResult.CannotHealDead
                : hp >= hpMax ? PotionResult.AlreadyAtFullHp
                : PotionResult.Ok,
            ItemCategory.PotionRevive => alive ? PotionResult.AlreadyAlive : PotionResult.Ok,
            _ => PotionResult.Ok,
        };
    }

    /// <summary>Сколько HP вернёт лечебное зелье: <c>min(HP_POTION, HpMax − Hp)</c>.
    /// <para><b>Ранг 1</b> (отчёт B синтетических карт, ai-q33, 2/0): зелье +50 (<c>G000IG0005</c>)
    /// в бою дало 83 → 100 и 50 → 100 — упор в потолок.</para></summary>
    public static int HealAmount(Item item, int hp, int hpMax) =>
        Math.Max(0, Math.Min(item.HealHitPoints, hpMax - hp));

    /// <summary>Хватает ли казны на одно применение жезла. Отдельной арифметики нет: сравнение
    /// шести валют уже живёт в <c>Economy.NotEnough</c> — то же правило, что у найма и постройки.</summary>
    public static bool CanPay(Bank treasury, Item item) => !Economy.NotEnough(treasury, item.CastingCost);

    /// <summary>Списать цену применения. Возвращается новая казна — прежняя не меняется.</summary>
    public static Bank Pay(Bank treasury, Item item) =>
        CanPay(treasury, item)
            ? treasury - item.CastingCost
            : throw new InvalidOperationException(
                  $"предмет {item.Id}: на применение не хватает казны " +
                  $"(нужно {item.CastingCost}, есть {treasury})");

    /// <summary>Модификаторы предмета из общего справочника <c>Gmodif</c>/<c>GmodifL</c> — оба слота
    /// сразу, в порядке «надетое, затем выпитое».
    /// <para>Ранг 1 по данным: все 41 <c>MOD_EQUIP</c> и все 20 <c>MOD_POTION</c> ванили
    /// разрешаются, висячих ноль. Пусто — не ошибка (у 275 предметов модификатора нет вовсе);
    /// ошибка — только ссылка, которая никуда не ведёт.</para></summary>
    public static IReadOnlyList<Modifier> ModifiersOf(Item item,
                                                      IReadOnlyDictionary<string, Modifier> catalogue)
    {
        var found = new List<Modifier>(1);
        foreach (var id in new[] { item.EquipModifierId, item.PotionModifierId })
        {
            if (id.Length == 0) continue;
            found.Add(catalogue.TryGetValue(id, out var modifier)
                ? modifier
                : throw new InvalidDataException(
                      $"предмет {item.Id}: модификатор \"{id}\" в Gmodif не найден"));
        }
        return found;
    }

    /// <summary>Заклинание свитка или жезла из каталога <c>Gspells</c>. Пусто — <c>null</c>;
    /// висячая ссылка — исключение. Ранг 1: все 141 ссылка ванили разрешается.</summary>
    public static Spell? SpellOf(Item item, IReadOnlyDictionary<string, Spell> catalogue)
    {
        if (item.SpellId.Length == 0) return null;
        return catalogue.TryGetValue(item.SpellId, out var spell)
            ? spell
            : throw new InvalidDataException(
                  $"предмет {item.Id}: заклинание \"{item.SpellId}\" в Gspells не найдено");
    }
}
