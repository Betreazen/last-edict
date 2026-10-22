using System.Globalization;
using LastEdict.Data.Leaders;
using LastEdict.Data.Ui;
using LastEdict.Data.Ui.Windows;
using LastEdict.Rules.Battle;

namespace LastEdict.Data.Menu;

/// <summary>
/// Навыки героя из файла строками окна просмотра (<c>0x4cb1ec</c>): по способности файла на строку, в порядке файла и без пропусков,
/// и «Полёт» в конце, если у героя бонус местности. Значок, название (<c>0x61dd44</c>) и описание (<c>0x61df32</c>) — для строки и
/// карточки навыка (<see cref="AbilityCardLogic"/>).
/// </summary>
public static class LeaderAbilities
{
    /// <summary>Способность (<c>LLeadA</c>) → значок <c>Icons.ff</c>, название, описание. Критический удар значка не имеет.</summary>
    private static readonly Dictionary<int, (string Icon, string Name, string Desc)> Table = new()
    {
        [7] = ("ABIL0001", "X005TA0181", "X005TA0900"), [8] = ("ABIL0002", "X005TA0182", "X005TA0901"),
        [9] = ("ABIL0003", "X005TA0183", "X005TA0902"), [10] = ("ABIL0006", "X005TA0184", "X005TA0903"),
        [13] = ("ABIL0004", "X005TA0359", "X005TA0904"), [3] = ("ABIL0007", "X005TA0175", "X005TA0905"),
        [6] = ("ABIL0008", "X005TA0180", "X005TA0906"), [14] = ("ABIL0009", "X005TA0176", "X005TA0907"),
        [15] = ("ABIL0010", "X005TA0177", "X005TA0908"), [16] = ("ABIL0011", "X005TA0178", "X005TA0909"),
        [17] = ("", "X160TA0017", "X160TA0018"),
    };

    private static readonly (string Icon, string Name, string Desc) Flight = ("ABIL0005", "X005TA0256", "X005TA0910");

    public static IReadOnlyList<(string Icon, string Name, string Desc)> Rows(ExportedLeader leader)
    {
        ArgumentNullException.ThrowIfNull(leader);
        var rows = leader.Abilities.Where(Table.ContainsKey).Select(a => Table[a]).ToList();
        if (leader.TerrainBonus) rows.Add(Flight);
        return rows;
    }
}

/// <summary>Карточка навыка (<c>DLG_R_C_LEADERABIL</c>, <c>CEncLayoutLeaderAbil</c> <c>0x5742d6</c>): <c>X005TA0911</c> — название и
/// описание. Закрывается отпусканием кнопки.</summary>
internal sealed class AbilityCardLogic(Func<string, string> text, string name, string desc) : IWindowLogic
{
    public const string Screen = "DLG_R_C_LEADERABIL";
    private readonly UiRequests _requests = new();

    public void Open(UiScreenState ui)
    {
        ArgumentNullException.ThrowIfNull(ui);
        ui.SetText("TXT_INFO", UiTemplate.Fill(text("X005TA0911"), ("NAME", text(name)), ("DESC", text(desc))));
    }

    public void Handle(UiEvent e, UiScreenState ui)
    {
    }

    public void RightRelease(int x, int y, UiScreenState ui) => _requests.Add(new UiClose());

    public void Release(int x, int y, UiScreenState ui) => _requests.Add(new UiClose());

    public UiRequest? TakeRequest() => _requests.Take();
}

/// <summary>
/// Карточка героя из файла (<c>DLG_R_C_UNIT</c>, <c>CLeaderUnitDescriptor</c> <c>0x4c809f</c> — тот же строитель, что у карточки воина,
/// данные из записи файла): портрет базового типа, имя героя, описание базового типа; уровень, опыт «опыт / до уровня», здоровье
/// «N / N» (оба — здоровье файла), броня (база — броня типа, факт — файла), иммунитеты и защиты файла; атака и вторая атака —
/// названия и числа файла, альтернативной нет; лидерство, скорость и «Выиграно сражений» (<c>X005TA0809</c>); навыки. Контекста
/// карты нет — модификаторов укрепления нет. Закрывается отпусканием кнопки.
/// </summary>
internal sealed class LeaderFileCardLogic(MenuContext context, ExportedLeader leader) : IWindowLogic
{
    private readonly UiRequests _requests = new();

    public void Open(UiScreenState ui)
    {
        ArgumentNullException.ThrowIfNull(ui);
        string type = Ui.ArtNames.Of(leader.UnitType);
        var book = context.Book;
        ui.SetImage("IMG_UNIT_ICON", $"gfx:Events/{type}", UiImageAlign.Center);   // картина во весь рост, как у карточки воина (0x576d51)
        ui.SetText("TXT_UNIT_NAME", UiTemplate.Fill(Text("X005TA0567"), ("NAME", leader.Name)));
        ui.SetText("TXT_UNIT_INFO", UiTemplate.Fill(Text("X005TA0422"),
            ("DESC", context.Rules?.UnitDescs.TryGetValue(type, out var desc) == true ? Text(desc) : "")));
        int baseArmor = book?.Rules.Units.TryGetValue(type, out var unit) == true ? unit.Armor : leader.Armor;
        ui.SetText("TXT_STATS", UiTemplate.Fill(Text("X005TA0423"), ("LEVEL", leader.Level),
            ("XP", UiTemplate.Fill(Text("X005TA0649"), ("XP1", leader.Xp), ("XP2", leader.XpNext))),
            ("HP1", leader.HitPoints), ("HP2", leader.HitPoints), ("ARMOR", Num(baseArmor) + Difference(baseArmor, leader.Armor)),
            ("IMMU", Immunities(leader.ImmuneSources, leader.ImmuneClasses)), ("WARD", Immunities(leader.WardSources, leader.WardClasses))));
        ui.SetText("TXT_ATTACK_INFO", AttackInfo(book));
        foreach (string hidden in (string[])["IMG_UPGRADE", "TXT_NEED_UPGRADE"]) ui.SetHidden(hidden, true);
        ui.SetText("TXT_LEADER_INFO", UiTemplate.Fill(Text("X005TA0809"),
            ("LDRSHP", UiTemplate.Fill(Text("X005TA0583"), ("MAX", leader.Leadership))),
            ("AP", UiTemplate.Fill(Text("X005TA0650"), ("AP", leader.Move))),
            ("NBBAT", UiTemplate.Fill(Text("X005TA0737"), ("VALUE", leader.BattlesWon)))));
        var rows = LeaderAbilities.Rows(leader);
        ui.SetText("TXT_LABILITIES", rows.Count > 0 ? Text("X005TA0185") : "");
        ui.SetCards("MLBOX_ITEM_ICON", [.. rows.Select(r => new UiCard(r.Icon.Length > 0 ? $"Icons:{r.Icon}" : null, new UiPoint(0, 0),
            UiTemplate.Fill(Text("X005TA0891"), ("ABIL", Text(r.Name))), new UiRect(36, 0, 125 - 36, 36)))]);
    }

    private string AttackInfo(LastEdict.Data.Play.GameBook? book)
    {
        var a = leader.Attack;
        var attacks = book?.Rules.Attacks;
        var raw = attacks?.GetValueOrDefault(a.Id);
        var cls = (AttackClass)a.Class;
        int accuracy = UnitCardLogic.AlwaysHitClasses.Contains(cls) ? 100 : Math.Min(100, a.Power);
        int baseAccuracy = raw is null ? accuracy : UnitCardLogic.AlwaysHitClasses.Contains(raw.Class) ? 100 : Math.Min(100, raw.Power);
        var second = leader.Attack2.Length > 0 ? attacks?.GetValueOrDefault(leader.Attack2) : null;
        string part1 = UiTemplate.Fill(Text("X005TA0787"),
            ("TWICE", leader.AttacksTwice ? UiTemplate.Fill(Text("X005TA0786"), ("BLANK", "")) : ""),
            ("ALTATTACK", ""), ("ATTACK", a.Name),
            ("SECOND", leader.Attack2.Length == 0 ? "" : UiTemplate.Fill(Text("X005TA0785"), ("ATTACK", leader.Attack2Name))),
            ("HIT", $"{Num(baseAccuracy)}%" + Difference(baseAccuracy, accuracy)),
            ("HIT2", leader.Attack2.Length == 0 ? "" : UiTemplate.Fill(Text("X005TA0881"), ("POWER", $"{leader.Attack2Power}%"))));
        string damage = cls switch
        {
            AttackClass.BoostDamage when book is not null => UiTemplate.Fill(Text("X005TA0535"), ("BOOST", book.Rules.Constants.BoostDamagePercent(a.Level))),
            AttackClass.LowerDamage when book is not null => UiTemplate.Fill(Text("X005TA0550"), ("LOWER", book.Rules.Constants.LowerDamagePercent(a.Level))),
            _ => Num(a.Damage),
        };
        int baseInitiative = raw?.Initiative ?? a.Initiative;
        string part2 = UiTemplate.Fill(Text("X005TA0788"), ("EFFECT", Text(UnitCardLogic.EffectText(cls))), ("DAMAGE", damage),
            ("SOURCE", Source(a.Source)),
            ("SOURCE2", second is null ? "" : UiTemplate.Fill(Text("X005TA0816"), ("SOURCE", Source(second.Source)))),
            ("INIT", Num(baseInitiative) + Difference(baseInitiative, a.Initiative)),
            ("REACH", Text(a.Reach == 3 ? "X005TA0201" : "X005TA0200")),
            ("TARGETS", Text(a.Reach == AttackType.ReachAll ? "X005TA0674" : "X005TA0675")));
        return UiTemplate.Fill(Text("X005TA0424"), ("PART1", part1), ("PART2", part2));
    }

    private string Immunities(IReadOnlyList<int> sources, IReadOnlyList<int> classes)
    {
        var all = sources.Where(s => s is >= 0 and < 8).Select(Source)
            .Concat(classes.Select(c => UnitCardLogic.ClassTextOf((AttackClass)c)).Where(t => t.Length > 0).Select(Text)).ToList();
        return all.Count == 0 ? Text("X005TA0469") : string.Join(", ", all);
    }

    private string Source(int source) => source is >= 0 and < 8 ? Text(UnitCardLogic.SourceTextOf(source)) : "";

    private string Difference(int baseValue, int actual) => actual == baseValue ? ""
        : UiTemplate.Fill(Text(actual > baseValue ? "X005TA0486" : "X005TA0487"), ("NUMBER", Math.Abs(actual - baseValue))) + Text("X005TA0488");

    private string Text(string id) => context.Text(id);

    private static string Num(int value) => value.ToString(CultureInfo.InvariantCulture);

    public void Handle(UiEvent e, UiScreenState ui)
    {
    }

    public void RightRelease(int x, int y, UiScreenState ui) => _requests.Add(new UiClose());

    public void Release(int x, int y, UiScreenState ui) => _requests.Add(new UiClose());

    public UiRequest? TakeRequest() => _requests.Take();
}
