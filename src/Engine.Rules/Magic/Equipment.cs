using D2.Rules.Map;
using D2.Rules.Units;

namespace D2.Rules.Magic;

/// <summary>Отказ надеть предмет — код, который возвращает <c>checkEquip</c>
/// (Gog <c>0x005DF6B7</c>) и по которому обработчик UI (<c>0x0050416B</c>, тело
/// <c>0x005041B1…0x00504216</c>) выбирает текст. <b>Ранг 1:</b> тело проверки прочитано целиком,
/// восемь кодов совпали с текстами <c>Interf\TApp.DBF</c> (levels §1.2, §1.4).
/// <para>Пропущенные значения (1, 10) в этом пути не встречаются: они принадлежат гейту зелья
/// (<see cref="PotionResult"/>, <c>0x005DE495</c>) — разные функции с разными перечнями кодов.</para></summary>
public enum EquipRefusal
{
    /// <summary>Можно (<c>xor eax, eax</c>).</summary>
    Ok = 0,
    /// <summary>Нет <c>L_WEAPON_ARMOR_USE</c> (8) — броня и оружие, текст <c>X005TA0601</c>.</summary>
    NeedWeaponArmorUse = 2,
    /// <summary>Нет <c>L_JEWELRY_USE</c> (10) — книга, <c>X005TA0602</c>.</summary>
    NeedJewelryUse = 3,
    /// <summary>Нет <c>L_BANNER_USE</c> (9) — знамя, <c>X005TA0603</c>.</summary>
    NeedBannerUse = 4,
    /// <summary>Нет <c>L_ORB_USE</c> (14) — сфера, <c>X005TA0696</c>.</summary>
    NeedOrbUse = 5,
    /// <summary>Нет <c>L_TALISMAN_USE</c> (15) — талисман, <c>X005TA0697</c>.</summary>
    NeedTalismanUse = 6,
    /// <summary>Нет <c>L_TRAVEL_ITEM_USE</c> (16) — сапоги, <c>X005TA0698</c>.</summary>
    NeedTravelItemUse = 7,
    /// <summary>Лидер мёртв (<c>isAlive 0x005E98AD</c>) — <c>X005TA0651</c>.</summary>
    DeadLeader = 8,
    /// <summary>Вещь не надевается вовсе или не в этот слот — <c>X005TA0608</c>. Одним кодом обе
    /// калитки: и «не экипировка и не зелье» (<c>0x005DFB08</c>, <c>0x005DF8E8</c>), и отказ карты
    /// слотов в конце (<c>0x005EC309</c>).</summary>
    NotEquippable = 9,
    /// <summary>Предмет отряду ничего не даст (<c>0x005EC48F</c>) — <c>X005TA0810</c>.</summary>
    NoEffect = 0xB,
}

/// <summary>Итог надевания или снятия: отказ, новые семь слотов и кому лёг <c>MOD_EQUIP</c>
/// (или от кого ушёл).</summary>
public sealed record EquipOutcome(EquipRefusal Refusal, IReadOnlyList<string> Slots,
                                  IReadOnlyList<string> Bearers, string ModifierId)
{
    public bool Done => Refusal == EquipRefusal.Ok;
}

/// <summary>Что у лидера уже есть для отказа 0xB, кроме способностей: земли с бонусом хода
/// (<c>Lground</c>, <see cref="EffectiveStats.MoveAbilities"/>) и иммунитеты по источнику и классу — тип
/// (<c>Gimmu</c>/<c>GimmuC</c>) плюс цепочка без проверяемого предмета.
/// <para>ponytail: база типа <c>TUsStackLeaderImplData.moveBonuses</c> не читается — таблицы-источника в
/// exe нет (строк <c>G*.dbf</c> с ходом по земле нет), в ванили бонус земли даёт только цепочка.</para></summary>
public sealed record LeaderEffects(IReadOnlySet<int> MoveGrounds, IReadOnlyList<ImmunityGrant> BySource,
                                   IReadOnlyList<ImmunityGrant> ByClass)
{
    public static readonly LeaderEffects None = new(new HashSet<int>(), [], []);
}

/// <summary>Надевание и снятие: слот, категория, способность, цепочка <c>MOD_EQUIP</c>
/// (план Ф7 §1.5, задача 9).
///
/// <para><b>Три функции движка, три ранга 1.</b> <c>slotAcceptsCategory</c> (<c>0x005EC309</c>) —
/// карта «слот ↔ категория», прочитана целиком; <c>checkEquip</c> (<c>0x005DF6B7</c>) — гейт
/// способности с кодами отказа; <c>CMidStack::canEquip</c> (<c>0x005EC14C</c>) — тот же предикат для
/// визитора <c>CVisitorEquipItem::canApply</c> (<c>0x00610DF9</c>). <b>Порядок проверок у двух
/// последних разный</b> (UI: способность → эффект → слот; логика: слот → экипируемость → эффект →
/// способность), но ответ «да/нет» один и тот же — здесь взят порядок UI, потому что только у него
/// известны коды. Кто отказал первым, на <see cref="CanEquip"/> не влияет.</para>
///
/// <para><b>Чего в гейте НЕТ</b> (ранг 1, отрицательный результат полного разбора
/// <c>0x005DF6B7…0x005DF8E5</c> и <c>0x005EC14C…0x005EC306</c>, levels §1.4): уровня героя, его
/// расы и подрасы, категории лидера <c>LleadC</c>. Уровень входит косвенно — навык выдаётся при
/// повышении (<see cref="LeaderAbilities"/>).</para>
///
/// <para><b>Свиток и посох не надеваются</b> — код <see cref="EquipRefusal.NotEquippable"/>; их
/// применение идёт отдельными функциями (<c>0x005DE7D7</c> свиток, <c>0x005DBCC9</c> посох) и
/// требует <c>L_WAND_SCROLL_USE</c>. <b>У зелий гейта способности нет вовсе</b>: ветки сравнения их
/// категорий в <c>checkEquip</c> не существует, поэтому они доходят до карты слотов и занимают
/// боевые 2 и 3 наравне со сферами (315 наблюдений корпуса).</para></summary>
public static class Equipment
{
    /// <summary>Слоты снаряжения лидера — порядок полей <c>MidStack</c> и
    /// <c>EquippedItemIdx</c> тулсета (<c>midstack.h:44-53</c>), он же порядок
    /// <see cref="MapStack.Equipment"/>. Ранг 1: номера проверяет <c>0x005EC309</c> сравнением с
    /// этими же числами.</summary>
    public const int SlotBanner = 0, SlotTome = 1, SlotBattle1 = 2, SlotBattle2 = 3;
    public const int SlotArtifact1 = 4, SlotArtifact2 = 5, SlotBoots = 6, SlotCount = 7;

    /// <summary>Категория → (нужная способность <c>LleadA</c>, код отказа). Ранг 1: семь ветвей
    /// <c>checkEquip</c> в том же порядке, в каком их сравнивает движок
    /// (<c>0x005DF787</c> … <c>0x005DF88A</c>). Броня и оружие делят одну способность.</summary>
    private static readonly IReadOnlyDictionary<ItemCategory, (int Ability, EquipRefusal Refusal)> Gate =
        new Dictionary<ItemCategory, (int, EquipRefusal)>
        {
            [ItemCategory.Armor] = (8, EquipRefusal.NeedWeaponArmorUse),
            [ItemCategory.Weapon] = (8, EquipRefusal.NeedWeaponArmorUse),
            [ItemCategory.Jewel] = (10, EquipRefusal.NeedJewelryUse),
            [ItemCategory.Banner] = (9, EquipRefusal.NeedBannerUse),
            [ItemCategory.Orb] = (14, EquipRefusal.NeedOrbUse),
            [ItemCategory.Talisman] = (15, EquipRefusal.NeedTalismanUse),
            [ItemCategory.TravelItem] = (16, EquipRefusal.NeedTravelItemUse),
        };

    /// <summary>Какая способность нужна категории; null — никакая (зелья, свитки, посохи,
    /// драгоценности, квестовые). Таблица levels §1.2.</summary>
    public static int? RequiredAbility(ItemCategory category) =>
        Gate.TryGetValue(category, out var g) ? g.Ability : null;

    /// <summary>Надевается ли категория в принципе — <c>CItemEquipment</c> (<c>0x005DFB08</c>).
    /// Зелье не «экипировка», но в боевой слот кладётся: его пропускает вторая калитка
    /// <c>0x005DF8E8</c> (<see cref="Item.IsPotion"/>).</summary>
    public static bool Equippable(Item item) => Gate.ContainsKey(item.Category) || item.IsPotion;

    /// <summary>Принимает ли слот категорию — <c>slotAcceptsCategory</c> (<c>0x005EC309</c>),
    /// прочитана целиком: 0 знамя, 1 книга, 2–3 зелья и сферы с талисманами, 4–5 броня и оружие,
    /// 6 сапоги, остальное — нет. Слот выбирает UI (куда бросили), функция только проверяет пару.</summary>
    public static bool SlotAccepts(ItemCategory category, int slot) => slot switch
    {
        SlotBanner => category is ItemCategory.Banner,
        SlotTome => category is ItemCategory.Jewel,
        SlotBattle1 or SlotBattle2 => category is ItemCategory.PotionBoost or ItemCategory.PotionHeal
            or ItemCategory.PotionRevive or ItemCategory.PotionPermanent
            or ItemCategory.Orb or ItemCategory.Talisman,
        SlotArtifact1 or SlotArtifact2 => category is ItemCategory.Armor or ItemCategory.Weapon,
        SlotBoots => category is ItemCategory.TravelItem,
        _ => false,
    };

    /// <summary>Можно ли надеть — порядок отказов движкового UI.</summary>
    /// <param name="abilities">Способности лидера из трёх источников
    /// (<see cref="LeaderAbilities.Split"/>). Проверяемый предмет в них не входит.</param>
    /// <param name="leaderAlive">Жив ли лидер отряда: <c>hp &gt; 0</c> (<c>0x005E98AD</c>).</param>
    /// <param name="modifiers">Справочник <c>Gmodif</c> — для отказа <see cref="EquipRefusal.NoEffect"/>
    /// («предмет ничего не даст», <see cref="GivesNothing"/>). Справочника нет (<c>null</c>) — правило не
    /// проверяется.</param>
    /// <param name="has">Что у лидера уже есть помимо способностей: земли с бонусом хода и иммунитеты
    /// (без проверяемого предмета). <c>null</c> — ничего.</param>
    public static EquipRefusal Check(Item item, int slot, IReadOnlySet<int> abilities,
                                     bool leaderAlive, IReadOnlyDictionary<string, Modifier>? modifiers = null,
                                     LeaderEffects? has = null)
    {
        if (!Equippable(item)) return EquipRefusal.NotEquippable;
        if (!leaderAlive) return EquipRefusal.DeadLeader;
        if (Gate.TryGetValue(item.Category, out var gate) && !abilities.Contains(gate.Ability))
            return gate.Refusal;
        if (GivesNothing(item, abilities, has ?? LeaderEffects.None, modifiers)) return EquipRefusal.NoEffect;
        return SlotAccepts(item.Category, slot) ? EquipRefusal.Ok : EquipRefusal.NotEquippable;
    }

    /// <summary>Ответ «да/нет» — то же, что <c>CMidStack::canEquip</c> (<c>0x005EC14C</c>): у него
    /// другой порядок проверок, но не другой ответ.</summary>
    public static bool CanEquip(Item item, int slot, IReadOnlySet<int> abilities,
                               bool leaderAlive, IReadOnlyDictionary<string, Modifier>? modifiers = null,
                               LeaderEffects? has = null) =>
        Check(item, slot, abilities, leaderAlive, modifiers, has) == EquipRefusal.Ok;

    /// <summary>
    /// Отказ 0xB — <c>0x005EC48F</c> (<c>__thiscall</c> на отряде, <c>ret 8</c>), тело прочитано целиком, ранг 1.
    /// <para>Проверяются только <b>сапоги, книга и знамя</b> (<c>0x5EC529</c>–<c>0x5EC566</c>: id категорий
    /// <c>0x8376e8</c>/<c>0x837688</c>/<c>0x837678</c>); прочее, в том числе зелья и пустой <c>MOD_EQUIP</c>, —
    /// «даст». Затем элементы <c>MOD_EQUIP</c>, отказ по <b>первому</b> сработавшему:</para>
    /// <list type="bullet">
    /// <item>бонус хода по земле (флаг 8), которая у лидера уже есть (<c>hasMovementBonus</c>, <c>0x5EC615</c>);</item>
    /// <item>способность лидера (0x10), которая уже есть (<c>hasAbility</c>, <c>0x5EC667</c>);</item>
    /// <item>иммунитет Always (0x800 источник, 0x2000 класс) при уже Always (<c>0x5EC6C2</c>, <c>0x5EC725</c>);</item>
    /// <item>иммунитет Once (0x1000, 0x4000) при уже Once или Always (<c>!= notimmune</c>, <c>0x5EC783</c>, <c>0x5EC7E2</c>).</item>
    /// </list>
    /// <para>Статы (HP, броня, ход, лидерство) проверку не останавливают. Корпус: живые <c>equip_check</c>
    /// 3/0 (<c>IG4006</c> при врождённой 14 → 0xB, <c>IG4007</c> → 0, книга оберега <c>IG4001</c> → 0);
    /// надетые книги и сапоги во всех сохранениях 55/0 (лидер без предмета бонуса не имел).</para>
    /// <para>ponytail: движок берёт <b>одно</b> число на весь модификатор (<c>getFirstElementValue</c>, слот 8,
    /// первое заданное поле по порядку CUmStack/CUmUnit) и сверяет с ним все флаги; для одноэлементных
    /// модификаторов (все книги, сапоги и знамёна ванили) это то же, что поэлементно. Многоэлементный
    /// <c>MOD_EQUIP</c> в моде — переписать на «число первого элемента».</para>
    /// </summary>
    private static bool GivesNothing(Item item, IReadOnlySet<int> abilities, LeaderEffects has,
                                     IReadOnlyDictionary<string, Modifier>? modifiers)
    {
        if (modifiers is null || item.EquipModifierId.Length == 0
            || item.Category is not (ItemCategory.TravelItem or ItemCategory.Jewel or ItemCategory.Banner))
            return false;
        if (!modifiers.TryGetValue(item.EquipModifierId, out var modifier))
            throw new InvalidDataException($"MOD_EQUIP \"{item.EquipModifierId}\" в Gmodif не найден");
        return modifier.Elements.Any(e => e.Type switch
        {
            ModifierElementType.MoveAbility => e.Move is { } g && has.MoveGrounds.Contains(g),
            ModifierElementType.LeaderAbility => e.Ability is { } a && abilities.Contains(a),
            ModifierElementType.Immunity => Redundant(e, has.BySource),
            ModifierElementType.Immunityclass => Redundant(e, has.ByClass),
            _ => false,
        });
    }

    /// <summary>Иммунитет элемента уже перекрыт: Always — при Always, Once — при любом, кроме NotImmune
    /// (<c>getImmuneBy*</c> отдаёт сильнейший уровень цепочки, <c>0x5A4720</c>).</summary>
    private static bool Redundant(ModifierElement e, IReadOnlyList<ImmunityGrant> have)
    {
        if (e.Immunity is not { } category) return false;
        var level = have.Where(g => g.Category == category).Select(g => g.Level)
                        .DefaultIfEmpty(ImmunityLevel.NotImmune).Max();
        return (ImmunityLevel?)e.ImmuneCat switch
        {
            ImmunityLevel.Always => level == ImmunityLevel.Always,
            ImmunityLevel.Once => level != ImmunityLevel.NotImmune,
            _ => false,
        };
    }

    /// <summary>Кому лёг бы <c>MOD_EQUIP</c> предмета: знамя — <b>всем юнитам отряда</b>, остальное —
    /// одному лидеру (план Ф7 §1.5, harness-saves §1). Ранг 1 по корпусу: знамя
    /// <c>G000IG1007</c> на 47-м ходу первой большой партии дало <c>G000UM2107</c> сразу пяти
    /// юнитам отряда <c>S073KC0070</c>, а артефакт <c>G000IG3001</c> того же окна — только
    /// лидеру.</summary>
    public static IReadOnlyList<string> Bearers(Item item, MapStack stack) =>
        item.Category is ItemCategory.Banner ? stack.UnitIds : [stack.LeaderId];

    /// <summary>Несёт ли отряд этот экземпляр <c>IM</c> — инвентарь плюс надетое
    /// (<c>MidStack.ITEM_ID</c> и семь слотов). Та самая первая проверка движка «предмета нет в
    /// инвентаре держателя» (<c>0x005DE4A5</c> → <c>0x005DE66A</c>, индекс в инвентаре −1), которой
    /// правилам Ф6 взять было негде.</summary>
    public static bool Carried(MapStack stack, string instanceId) =>
        stack.Items.Concat(stack.Equipment)
             .Any(i => i.Equals(instanceId, StringComparison.OrdinalIgnoreCase));

    /// <summary>Надеть экземпляр в слот. Отказ оставляет слоты прежними и никому ничего не кладёт.
    /// <para>Занятый слот сначала освобождается <see cref="Unequip"/> — так это и делает движок:
    /// в журнале обмен вещей виден парой <c>item_unequip</c> + <c>item_equip</c> одного слота
    /// (например <c>seq 526783…526785</c> прогона <c>m8-mage</c>). Надеть в занятый слот — ошибка
    /// вызывающего, а не отказ движка: молча вытеснить вещь значит оставить её <c>MOD_EQUIP</c>
    /// висеть на юните.</para></summary>
    /// <param name="slots">Состояние семи слотов, если оно уже не то, что лежит в
    /// <paramref name="stack"/> — например после <see cref="Unequip"/> в том же действии. По
    /// умолчанию берётся из отряда.</param>
    public static EquipOutcome Equip(MapStack stack, Item item, string instanceId, int slot,
                                     IReadOnlySet<int> abilities, bool leaderAlive,
                                     IReadOnlyDictionary<string, Modifier>? modifiers = null,
                                     IReadOnlyList<string>? slots = null, LeaderEffects? has = null)
    {
        slots ??= Slots(stack);
        var refusal = Check(item, slot, abilities, leaderAlive, modifiers, has);
        if (refusal != EquipRefusal.Ok) return new EquipOutcome(refusal, slots, [], "");
        if (slots[slot].Length > 0)
            throw new InvalidOperationException(
                $"слот {slot} отряда {stack.Id} занят ({slots[slot]}) — сначала снять");
        var after = slots.ToList();
        after[slot] = instanceId;
        return new EquipOutcome(EquipRefusal.Ok, after, Bearers(item, stack), item.EquipModifierId);
    }

    /// <summary>Снять то, что лежит в слоте: слот пустеет, <c>MOD_EQUIP</c> уходит у тех же
    /// носителей (<c>unEquipItem 0x005E746D</c>). Своего гейта у снятия нет.
    /// <para>ponytail: запас хода на текущий день снятие не трогает, а движок трогает — ранг 1 на одну
    /// точку (отчёт B синтетических карт, ai-t05e, сохранения 035–037): снятые Boots of Speed
    /// (<c>g000um2503</c>, <c>TYPE 7</c>, +20 %) срезали текущий <c>MOVE</c> 24 → 20, а повторное
    /// надевание в тот же день запас не вернуло (20). В порте запас считается только в начале дня
    /// (<c>Movement.StartMovement</c> из <c>MapSim.StartDay</c>), надевания и снятия в
    /// <c>MapSim</c> нет вовсе; вид правила (пересчёт с зажимом или вычитание бонуса) одной точкой
    /// не различить. Снимет: снятие сапог при частично потраченном запасе.</para></summary>
    public static EquipOutcome Unequip(MapStack stack, int slot, Item item)
    {
        var slots = Slots(stack);
        if (slots[slot].Length == 0)
            throw new InvalidOperationException($"слот {slot} отряда {stack.Id} пуст — снимать нечего");
        var after = slots.ToList();
        after[slot] = "";
        return new EquipOutcome(EquipRefusal.Ok, after, Bearers(item, stack), item.EquipModifierId);
    }

    /// <summary>Семь слотов отряда. У отряда из <c>.sg</c> они есть всегда, у синтетического список
    /// пуст — тогда это семь пустых слотов, а не ошибка.</summary>
    public static IReadOnlyList<string> Slots(MapStack stack) =>
        stack.Equipment.Count == SlotCount ? stack.Equipment
        : stack.Equipment.Count == 0 ? Enumerable.Repeat("", SlotCount).ToList()
        : throw new InvalidDataException(
              $"отряд {stack.Id}: слотов снаряжения {stack.Equipment.Count}, а не {SlotCount}");
}
