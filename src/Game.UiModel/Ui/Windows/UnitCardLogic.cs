using System.Globalization;
using D2.Data.Menu;
using D2.Rules.Battle;
using D2.Rules.Battle.Effects;
using D2.Rules.Map;
using D2.Rules.Units;

namespace D2.Data.Ui.Windows;

/// <summary>
/// Карточка юнита в окнах карты (<c>DLG_R_C_UNIT</c>, <c>CEncyclopediaPopup</c> <c>0x4c5fca</c>, строитель <c>CEncLayoutUnit</c>
/// <c>0x5744f1</c> — общий с экраном боя): открывается нажатием правой кнопки на воине, закрывается её отпусканием (или левой,
/// <c>0x4c6298</c>) — ответ пользователя 25.09 «пока держишь кнопку». Поля (<c>0x574706</c>, <c>0x574e40</c>):
/// <list type="bullet">
/// <item>портрет, имя <c>X005TA0567</c>, описание типа <c>X005TA0422</c>;</item>
/// <item><c>X005TA0423</c>: уровень, опыт (<c>X005TA0649</c> «опыт / до уровня», последний уровень — <c>X005TA0648</c>), здоровье,
/// защита (база типа и цветная разница с фактом по цепочке модификаторов, <c>X005TA0486</c>/<c>0487</c>/<c>0488</c>), иммунитеты и
/// стойкости (сначала источники, потом классы атак, через «, », пусто — <c>X005TA0469</c>; <c>0x61d332</c>/<c>0x61d9d3</c>);</item>
/// <item>атака <c>X005TA0424</c> = <c>X005TA0787</c> (оружие, вторая атака, «(2x)», альтернативная, точность) +
/// <c>X005TA0788</c> (действие по классу, урон с пределом <c>(Max)</c>, источник, инициатива, досягаемость, цели; <c>0x575b80</c>);</item>
/// <item>лидер: <c>X005TA0809</c> (лидерство <c>X005TA0583</c>), навыки героя в <c>MLBOX_ITEM_ICON</c> (<c>X005TA0891</c>, заголовок
/// <c>X005TA0185</c>) — у героя играбельной расы; не лидер: «нужна постройка» (<c>0x575675</c>) — <c>X005TA0513</c> с постройками
/// наследников через <c>X005TA0459</c> и значок <c>FIUPGRADE</c>, чужая раса — <c>X005TA0822</c>, остановлен — <c>X005TA0941</c>.</item>
/// </list>
/// Герой: «выиграно сражений» <c>X005TA0737</c> по <c>NBBATTLE</c> отряда. Усиление и ослабление в бою — не здесь.
/// </summary>
/// <param name="shown">Воин не с карты — тип без экземпляра (<see cref="OfType"/>); null — воин <paramref name="unitId"/> карты.</param>
public sealed class UnitCardLogic(WindowContext context, string unitId, MapUnit? shown = null) : IWindowLogic
{
    public const string Screen = "DLG_R_C_UNIT";

    /// <summary>Карточка типа воина без экземпляра — воин постройки в окне построек (<c>X005TA0382</c> «Для деталей — правый клик»):
    /// уровень типа, полное здоровье, опыта нет, модификаторов нет.</summary>
    public static UnitCardLogic OfType(WindowContext context, string typeId)
    {
        ArgumentNullException.ThrowIfNull(context);
        var book = context.Session.RequireBook();
        int level = book.Rules.Units.TryGetValue(typeId, out var type) ? type.Level : 1;
        var unit = new MapUnit { Id = "", TypeId = typeId, Level = level };
        return new UnitCardLogic(context, "", unit with { Hp = context.Session.HpMax(unit) });
    }

    /// <summary>Карточка воина в бою с карты по правой кнопке (<c>0x6399cf</c>; строитель тот же, <c>CEncLayoutUnit</c>): воин карты
    /// <paramref name="unitId"/> со здоровьем и опытом из боя; тип и уровень — из боя, если бой его подменил (понижение уровня,
    /// превращение) и такой тип есть в правилах. Воина нет на карте (призванный) — null: карточку строит экран боя сам.</summary>
    public static UnitCardLogic? InBattle(WindowContext context, string unitId, string battleTypeId, int battleLevel, int hp, int xp)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (!context.Map.Units.TryGetValue(unitId, out var unit)) return null;
        bool retyped = !battleTypeId.Equals(unit.TypeId, StringComparison.OrdinalIgnoreCase)
                       && context.Session.RequireBook().Rules.Units.ContainsKey(battleTypeId);
        var shown = unit with { Hp = hp, Xp = xp };
        return new UnitCardLogic(context, unitId, retyped ? shown with { TypeId = battleTypeId, Level = battleLevel } : shown);
    }

    /// <summary>Тип воина карточки (у карточки типа — он сам).</summary>
    public string TypeId => shown?.TypeId ?? (context.Map.Units.TryGetValue(unitId, out var u) ? u.TypeId : "");

    /// <summary><c>Gattacks.REACH</c> 3 — <c>L_ADJACENT</c>: только он пишется «Ближайшие цели», остальные (и <c>L_ANY</c>) — «Все поле
    /// боя» (<c>0x575b80</c>).</summary>
    private const int ReachAdjacent = 3;

    private readonly UiRequests _requests = new();

    /// <summary>Классы атак без броска точности — «100%» (<c>0x576c34</c>).</summary>
    private static readonly AttackClass[] AlwaysHit =
        [AttackClass.Heal, AttackClass.BoostDamage, AttackClass.Cure, AttackClass.Summon, AttackClass.Doppelganger,
         AttackClass.TransformSelf, AttackClass.GiveAttack];

    /// <summary>Источник атаки → строка (<c>0x61cd1b</c>, по имени категории: у земли и воздуха строки переставлены).</summary>
    private static readonly string[] SourceTexts =
        ["X005TA0145", "X005TA0146", "X005TA0147", "X005TA0148", "X005TA0149", "X005TA0150", "X005TA0152", "X005TA0151"];

    /// <summary>Класс атаки → строка (<c>0x61d5ed</c>); нет в таблице — пусто.</summary>
    public string UnitId => unitId;

    public UiRequest? TakeRequest() => _requests.Take();

    public string Summary => $"карточка={(unitId.Length > 0 ? unitId : TypeId)}";

    public void Open(UiScreenState ui)
    {
        ArgumentNullException.ThrowIfNull(ui);
        if ((shown ?? context.Map.Units.GetValueOrDefault(unitId)) is not { } unit) return;
        var book = context.Session.RequireBook();
        var type = book.Grown(unit.TypeId, unit.Level)?.Type;
        var rules = context.Rules;
        ui.SetImage("IMG_UNIT_ICON", context.Faces.Picture(unit.TypeId), UiImageAlign.Center);   // картина во весь рост, по центру (0x576d51)
        ui.SetText("TXT_UNIT_NAME", UiTemplate.Fill(Text("X005TA0567"), ("NAME", context.UnitName(unit.TypeId))));
        ui.SetText("TXT_UNIT_INFO", UiTemplate.Fill(Text("X005TA0422"),
            ("DESC", rules?.UnitDescs.TryGetValue(unit.TypeId, out var desc) == true ? Text(desc) : "")));
        if (type is null) return;
        var chain = unit.Modifiers.Select(m => book.Rules.Modifiers?.GetValueOrDefault(m)).OfType<Modifier>().ToList();
        var stats = EffectiveStats.Of(type, chain);
        ui.SetText("TXT_STATS", UiTemplate.Fill(Text("X005TA0423"), ("LEVEL", unit.Level), ("XP", Experience(unit)),
            ("HP1", Math.Max(0, unit.Hp)), ("HP2", context.Session.HpMax(unit)),
            ("ARMOR", Armor(unit, type, chain)),
            ("IMMU", Immunities(type, stats, ImmunityLevel.Always)), ("WARD", Immunities(type, stats, ImmunityLevel.Once))));
        ui.SetText("TXT_ATTACK_INFO", AttackInfo(type, chain));
        var leads = unitId.Length == 0 ? null
            : context.Map.Stacks.Values.FirstOrDefault(s => s.LeaderId.Equals(unitId, StringComparison.OrdinalIgnoreCase));
        if (type.IsLeader) Leader(ui, leads);
        else NeedUpgrade(ui, unit);
    }

    /// <summary>
    /// Защита (<c>0x574e40</c>): база — броня типа воина с «редакторскими» модификаторами (id <c>G000UM9xxx</c>, <c>0x576ffa</c>), итог —
    /// броня воина со всей цепочкой и защитой укрепления, где он стоит гостем или в гарнизоне (<c>0x5f4e29</c>: деревня —
    /// <c>GCityInf.PROTECTION</c> расы владельца по уровню и <c>PROTECT_B</c>, столица — <c>Grace.PROTECTION</c>); обе — 0…90.
    /// </summary>
    private string Armor(MapUnit unit, UnitType grown, IReadOnlyList<Modifier> chain)
    {
        var book = context.Session.RequireBook();
        var type = book.Rules.Units.GetValueOrDefault(unit.TypeId) ?? grown;
        var editor = chain.Where(m => m.Id.Length == 10 && int.TryParse(m.Id[6..], System.Globalization.NumberStyles.HexNumber,
                                                                         CultureInfo.InvariantCulture, out int n) && n is >= 0x9000 and <= 0x9999).ToList();
        int baseArmor = Math.Clamp(EffectiveStats.Of(type, editor).Armor, 0, EffectiveStats.ArmorMax);
        var fort = FortModifiers(unit).Select(m => book.Rules.Modifiers?.GetValueOrDefault(m)).OfType<Modifier>();
        int actual = Math.Clamp(EffectiveStats.Of(grown, [.. chain, .. fort]).Armor, 0, EffectiveStats.ArmorMax);
        return Num(baseArmor) + Difference(baseArmor, actual);
    }

    /// <summary>Модификаторы защиты укрепления воина; не в укреплении — пусто.</summary>
    private IEnumerable<string> FortModifiers(MapUnit unit)
    {
        var map = context.Map;
        var economy = context.Session.RequireBook().Economy;
        string fortId = map.Stacks.Values.FirstOrDefault(s => s.UnitIds.Contains(unit.Id, StringComparer.OrdinalIgnoreCase))?.Inside ?? "";
        var fort = map.Sites.FirstOrDefault(s => fortId.Length > 0 ? s.Id.Equals(fortId, StringComparison.OrdinalIgnoreCase)
                                                                   : s.Guards.Contains(unit.Id, StringComparer.OrdinalIgnoreCase));
        if (fort is not { Kind: SiteKind.Capital or SiteKind.Village }) yield break;
        string race = map.Players.TryGetValue(fort.Owner, out var owner) ? owner.RaceId
            : economy.Races.Values.FirstOrDefault(r => r.Neutral)?.Id ?? "";   // ничей — нейтральная раса (0x580cf0)
        if (fort.Kind == SiteKind.Capital)
        {
            if (economy.Races.TryGetValue(race, out var info)) yield return info.ProtectionId;
            yield break;
        }
        if (economy.Cities.FirstOrDefault(c => c.RaceId.Equals(race, StringComparison.OrdinalIgnoreCase) && c.Size == (fort.Size ?? 1)) is { } city)
            yield return city.ProtectionId;
        if (fort.ProtectBonus.Length > 0) yield return fort.ProtectBonus;
    }

    private string Experience(MapUnit unit)
    {
        int next = context.Session.RequireBook().XpNextOf(unit.TypeId, unit.Level);
        return next <= 0 ? Text("X005TA0648") : UiTemplate.Fill(Text("X005TA0649"), ("XP1", unit.Xp), ("XP2", next));
    }

    /// <summary>Разница базы и факта: «+ N» зелёным, «- N» красным и сброс цвета (<c>0x5759f5</c>); равны — пусто.</summary>
    private string Difference(int baseValue, int actual) => actual == baseValue ? ""
        : UiTemplate.Fill(Text(actual > baseValue ? "X005TA0486" : "X005TA0487"), ("NUMBER", Math.Abs(actual - baseValue))) + Text("X005TA0488");

    /// <summary>Иммунитеты или стойкости: источники типа и цепочки, затем классы атак типа.</summary>
    private string Immunities(UnitType type, EffectiveStats stats, ImmunityLevel level)
    {
        var sources = (type.ImmunitiesBySource ?? []).Concat(stats.Immunities).Where(g => g.Level == level)
                                                     .Select(g => g.Category).Distinct()
                                                     .Where(s => s is >= 0 and < 8).Select(s => Text(SourceTexts[s]));
        var classes = (type.ImmunitiesByClass ?? []).Concat(stats.ClassImmunities).Where(g => g.Level == level)
                                                     .Select(g => g.Category).Distinct()
                                                     .Select(c => ClassTextOf((AttackClass)c) is { Length: > 0 } t ? Text(t) : "")
                                                     .Where(t => t.Length > 0);
        var all = sources.Concat(classes).ToList();
        return all.Count == 0 ? Text("X005TA0469") : string.Join(", ", all);
    }

    private string AttackInfo(UnitType type, IReadOnlyList<Modifier> chain)
    {
        var attacks = context.Session.RequireBook().Rules.Attacks;
        if (!attacks.TryGetValue(type.AttackId, out var raw)) return "";
        var attack = EffectiveStats.OfAttack(raw, chain).Attack;
        var second = type.Attack2Id.Length > 0 && attacks.TryGetValue(type.Attack2Id, out var a2) ? a2 : null;
        var alt = attack.HasAltAttack && attacks.TryGetValue(attack.AltAttackId, out var a3) ? a3 : null;
        string part1 = UiTemplate.Fill(Text("X005TA0787"),
            ("TWICE", type.AttackTwice ? UiTemplate.Fill(Text("X005TA0786"), ("BLANK", "")) : ""),
            ("ALTATTACK", alt is null ? "" : UiTemplate.Fill(Text("X005TA0829"), ("ATTACK", AttackName(alt.Id)), ("BLANK", ""))),
            ("ATTACK", AttackName(raw.Id)),
            ("SECOND", second is null ? "" : UiTemplate.Fill(Text("X005TA0785"), ("ATTACK", AttackName(second.Id)))),
            ("HIT", $"{Num(Accuracy(raw))}%" + Difference(Accuracy(raw), Accuracy(attack))),
            ("HIT2", second is null ? "" : UiTemplate.Fill(Text("X005TA0881"), ("POWER", $"{Accuracy(second)}%"))));
        string part2 = UiTemplate.Fill(Text("X005TA0788"), ("EFFECT", Text(Effect(raw.Class))), ("DAMAGE", Damage(type, raw, attack, second)),
            ("SOURCE", Source(raw.Source)), ("SOURCE2", second is null ? "" : UiTemplate.Fill(Text("X005TA0816"), ("SOURCE", Source(second.Source)))),
            ("INIT", Num(raw.Initiative) + Difference(raw.Initiative, attack.Initiative)),
            ("REACH", Text(raw.Reach == ReachAdjacent ? "X005TA0201" : "X005TA0200")),
            ("TARGETS", Text(raw.Reach == AttackType.ReachAll ? "X005TA0674" : "X005TA0675")));
        return UiTemplate.Fill(Text("X005TA0424"), ("PART1", part1), ("PART2", part2));
    }

    private static int Accuracy(AttackType a) => AlwaysHit.Contains(a.Class) ? 100 : Math.Min(100, a.Power);

    /// <summary>Классы без броска точности — для карточки героя из файла (<c>D2.Data.Menu.LeaderFileCardLogic</c>).</summary>
    internal static IReadOnlyCollection<AttackClass> AlwaysHitClasses => AlwaysHit;

    /// <summary>Строка действия атаки по классу.</summary>
    internal static string EffectText(AttackClass c) => Effect(c);

    /// <summary>Строка источника атаки (0…7).</summary>
    internal static string SourceTextOf(int source) => SourceTexts[source];

    /// <summary>Строка класса атаки — имя эффекта (<see cref="EffectLooks.NameText"/>); нет — пусто.</summary>
    internal static string ClassTextOf(AttackClass c) => AttackEffects.Of(c).Looks.NameText ?? "";

    /// <summary>Подпись действия — облик эффекта (<see cref="EffectLooks.ActionText"/>): лечение и обереги «Лечить» (<c>X005TA0504</c>),
    /// усиление, ослабление, замедление — свои; прочее «Урон» (<c>X005TA0503</c>).</summary>
    private static string Effect(AttackClass c) => AttackEffects.Of(c).Looks.ActionText ?? "X005TA0503";

    /// <summary>Урон с пределом типа (<see cref="Formulas.DamageMax"/>, «(Max)» — <c>X005TA0811</c>); сила и ослабление — процент уровня
    /// атаки; лечение — число лечения. Вторая атака (<c>0x5764e1</c>): яд, волдырь или мороз — «/ урон» с тем же пределом
    /// (<c>0x5765a1</c>); лечение при первой «даровать защиту» — строка целиком заменяется лечением второй (<c>0x576551</c>:
    /// <c>L_HEAL</c> <c>0x837498</c> у второй и <c>L_BESTOW_WARDS</c> <c>0x837478</c> у первой, <c>QTY_HEAL</c> — слот <c>+0x24</c>).
    /// Прогон 15.10 (пометка 1): у Лесной Девы было «Лечить: 0».</summary>
    private string Damage(UnitType type, AttackType raw, AttackType attack, AttackType? second)
    {
        var constants = context.Session.RequireBook().Rules.Constants;
        switch (raw.Class)
        {
            case AttackClass.BoostDamage:
                return UiTemplate.Fill(Text("X005TA0535"), ("BOOST", constants.BoostDamagePercent(raw.Level)));
            case AttackClass.LowerDamage:
                return UiTemplate.Fill(Text("X005TA0550"), ("LOWER", constants.LowerDamagePercent(raw.Level)));
            case AttackClass.Heal:
                return Num(raw.QtyHeal);
        }
        int max = type.GlobalId.Length == 10 && type.GlobalId[4..6].Equals("uu", StringComparison.OrdinalIgnoreCase)
            ? Formulas.DamageMax(constants, type.GlobalId) : constants.MaxDamage;
        int baseDamage = Math.Min(raw.QtyDamage, max), actual = Math.Min(attack.QtyDamage, max);
        string text = Num(baseDamage) + Difference(baseDamage, actual);
        if (actual == max) text = UiTemplate.Fill(Text("X005TA0811"), ("DMG", text));
        if (second is { Class: AttackClass.Poison or AttackClass.Blister or AttackClass.Frostbite })
            return text + $" / {Num(Math.Min(second.QtyDamage, max))}";
        return raw.Class == AttackClass.BestowWards && second is { Class: AttackClass.Heal } ? Num(second.QtyHeal) : text;
    }

    private string Source(int source) => source is >= 0 and < 8 ? Text(SourceTexts[source]) : "";

    private string AttackName(string attackId) =>
        context.Rules?.AttackNames.TryGetValue(attackId, out var name) == true ? Text(name) : attackId;

    /// <summary>Герой: лидерство и навыки (у героя играбельной расы); «нужна постройка» убирается.</summary>
    private void Leader(UiScreenState ui, MapStack? stack)
    {
        foreach (string hidden in (string[])["IMG_UPGRADE", "TXT_NEED_UPGRADE"]) ui.SetHidden(hidden, true);
        string lead = stack is null ? "" : UiTemplate.Fill(Text("X005TA0583"), ("MAX", context.Session.Leadership(stack) - 1));
        ui.SetText("TXT_LEADER_INFO", UiTemplate.Fill(Text("X005TA0809"), ("LDRSHP", lead), ("AP", ""),
            ("NBBAT", stack is null ? "" : UiTemplate.Fill(Text("X005TA0737"), ("VALUE", stack.BattlesWon)))));
        var rows = stack is not null && context.Map.Players.TryGetValue(stack.Owner, out var owner) && owner.RaceId.Length > 0
            ? HeroSkills.Rows(context, stack) : [];
        ui.SetText("TXT_LABILITIES", rows.Count > 0 ? Text("X005TA0185") : "");
        ui.SetCards("MLBOX_ITEM_ICON", [.. rows.Select(r => new UiCard($"Icons:{r.Icon}", new UiPoint(0, 0),
            UiTemplate.Fill(Text("X005TA0891"), ("ABIL", Text(r.Text))), new UiRect(36, 0, 125 - 36, 36)))]);
    }

    /// <summary>Не герой: навыков нет; готов к повышению — какие постройки нужны или какая раса их строит; остановлен — так и
    /// сказано.</summary>
    private void NeedUpgrade(UiScreenState ui, MapUnit unit)
    {
        ui.SetHidden("MLBOX_ITEM_ICON", true);
        var owner = context.Map.Stacks.Values.FirstOrDefault(s => s.UnitIds.Contains(unitId, StringComparer.OrdinalIgnoreCase))?.Owner
                    ?? context.Map.Sites.FirstOrDefault(s => s.Guards.Contains(unitId, StringComparer.OrdinalIgnoreCase))?.Owner ?? "";
        string text = "";
        string? icon = null;
        var rules = context.Rules;
        if (UnitCellsView.ReadyToUpgrade(context, unit, owner))
        {
            string race = rules?.UnitRaces.GetValueOrDefault(unit.TypeId, "") ?? "";
            bool ours = context.Map.Players.TryGetValue(owner, out var player) && player.RaceId.Equals(race, StringComparison.OrdinalIgnoreCase);
            if (ours)
            {
                var book = context.Session.RequireBook();
                var names = UnitUpgrade.Successors(book.Rules.Units, unit.TypeId, unit.Level)
                    .Select(s => book.UpgradeBuilding.GetValueOrDefault(s.Id, "")).Where(b => b.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase)
                    .Select(b => rules?.BuildingNames.TryGetValue(b, out var n) == true ? Text(n) : b);
                text = UiTemplate.Fill(Text("X005TA0513"), ("BUILDING", string.Join(Text("X005TA0459"), names)));
                icon = "Battle:FIUPGRADE";
            }
            else
            {
                text = UiTemplate.Fill(Text("X005TA0822"), ("RACE", rules?.RaceNames.TryGetValue(race, out var r) == true ? Text(r) : race));
            }
        }
        else if (unit.DynLevel)
        {
            text = Text("X005TA0941");
        }
        ui.SetText("TXT_NEED_UPGRADE", text);
        ui.SetImage("IMG_UPGRADE", icon);
        ui.SetText("TXT_LEADER_INFO", "");
        ui.SetText("TXT_LABILITIES", "");
    }

    private string Text(string id) => context.Text(id);

    private static string Num(int value) => value.ToString(CultureInfo.InvariantCulture);

    public void Handle(UiEvent e, UiScreenState ui)
    {
    }

    public void RightRelease(int x, int y, UiScreenState ui) => Close();

    public void Release(int x, int y, UiScreenState ui) => Close();

    private void Close()
    {
        if (_requests.Count == 0) _requests.Add(new UiClose());
    }
}

/// <summary>Открыть карточку воина под мышью — общее для окон с ячейками групп.</summary>
public static class UnitCards
{
    /// <summary>Воин в позиции группы <paramref name="groupId"/> — просьба открыть его карточку; пусто — null.</summary>
    public static UiOpen? For(WindowContext context, string? groupId, int? position)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (groupId is null || position is not { } p || context.Session.GroupCells(groupId) is not UnitCells cells
            || cells.Cells[p].Length == 0) return null;
        return new UiOpen(UnitCardLogic.Screen, new UnitCardLogic(context, cells.Cells[p]));
    }

    public static string Num(int value) => value.ToString(CultureInfo.InvariantCulture);
}
