using LastEdict.Data.Content;
using LastEdict.Data.Menu;
using LastEdict.Data.Play;
using LastEdict.Data.Ui;
using LastEdict.Data.Ui.Windows;
using LastEdict.Rules.Map;
using LastEdict.Rules.Units;

namespace LastEdict.Data.Map;

/// <summary>Карточка объекта карты по правой кнопке: окно и его логика.</summary>
public sealed record MapCard(string Screen, string ObjectId, IScreenLogic Logic);

/// <summary>
/// Карточки правой кнопки на карте (<c>docs/ai-screen.md</c> §10; разбор <c>Discipl2.exe</c>, ранг 1 по листингу, игрой не проверено):
/// <c>CStratInterf::handleMouse</c> <c>0x48e3da</c> → <c>0x48e4d6</c>: клетка под туманом — окна нет; объект клетки (<c>0x48e659</c>) —
/// отряд, иначе первый объект; чужой невидимый отряд — нет. Раскладка по типу (<c>0x573d43</c>): отряд — <c>DLG_R_C_STACK</c> или
/// <c>DLG_R_C_STACK_NOSPY</c>, крепость — <c>DLG_R_C_CITY</c> или <c>DLG_R_C_MISC</c>, руина, мешок, площадка — <c>MISC</c>, жезл —
/// <c>ROD</c>, ориентир — <c>LANDMARK</c>, кристалл — <c>RES</c>, могила — <c>DLG_TOMB</c> (<c>CEncLayoutTomb</c>). Окно держится, пока
/// нажата кнопка (<c>StickyEncy</c> = 0).
/// </summary>
public static class MapCards
{
    /// <summary>Карточка объекта в клетке глазами <see cref="WindowContext.Viewer"/>; нечего показывать — null.</summary>
    public static MapCard? At(WindowContext c, IReadOnlyDictionary<string, string> raceNames, IReadOnlyDictionary<string, string> landmarkNames,
                              int x, int y, Func<int, int, bool> hidden)
    {
        ArgumentNullException.ThrowIfNull(c);
        ArgumentNullException.ThrowIfNull(hidden);
        var map = c.Map;
        if (!map.Contains(x, y) || hidden(x, y)) return null;
        var ids = map.Plan.TryGetValue((x, y), out var list) ? list : [];
        if (ids.Select(id => map.Stacks.GetValueOrDefault(id)).OfType<MapStack>().FirstOrDefault() is { } stack)
        {
            bool invisible = c.Session.Looks.Stacks.FirstOrDefault(s => s.Id.Equals(stack.Id, StringComparison.OrdinalIgnoreCase)) is { Invisible: true };
            if (invisible && !Same(stack.Owner, c.Viewer)) return null;
            return Stack(c, raceNames, stack);
        }
        foreach (string id in ids)
        {
            if (map.Sites.FirstOrDefault(s => s.Id.Equals(id, StringComparison.OrdinalIgnoreCase)) is { } site && Site(c, raceNames, site) is { } card)
                return card;
            if (map.Tombs.FirstOrDefault(t => Same(t.Id, id)) is { } tomb)
                return Card("DLG_TOMB", tomb.Id, ui => ui.SetText("TXT_INFO", TombText(c, raceNames, tomb)));
            if (map.Rods.GetValueOrDefault(id) is { } rod)
                return Card("DLG_R_C_ROD", rod.Id, ui => ui.SetText("TXT_INFO", Fill(c.Text("X005TA0658"), ("NAME", Player(c, rod.Owner)))));
        }
        if (c.Session.Looks.Landmarks.FirstOrDefault(l => Covers(map, l.Id, x, y)) is { } mark)
            return Card("DLG_R_C_LANDMARK", mark.Id,
                        ui => ui.SetText("TXT_INFO", Fill(c.Text("X005TA0877"), ("NAME", c.Text(landmarkNames.GetValueOrDefault(mark.Type, ""))))));
        return null;
    }

    /// <summary>Имена ориентиров по типу.</summary>
    public static IReadOnlyDictionary<string, string> LandmarkNames(string rulesDir) => LandmarkNames(ContentSource.Open(rulesDir));

    public static IReadOnlyDictionary<string, string> LandmarkNames(ContentSet content) =>
        content.Landmarks.ToDictionary(l => l.Key, l => l.NameText ?? "", StringComparer.OrdinalIgnoreCase);

    /// <summary>Отряд (<c>CEncLayoutStack</c> <c>0x57b71a</c>): полная карточка, если хозяин — нейтрал, сам зритель, союзник или
    /// разведан (<c>SPY_N</c>); иначе «зашлите шпиона» (<c>X005TA0409</c>). Полная (<c>0x57becf</c>): имя лидера (<c>0x5e97dc</c>,
    /// <see cref="StackUnitName"/>), раса, «Сражений выиграно» (<c>X005TA0887</c>, <c>CMidStack+0xc4</c> = <c>NBBATTLE</c>), картина
    /// лидера во весь рост по центру места (<c>IMG_LEADER</c>, <see cref="UnitFaces.Picture"/>, флаги 9) и шесть мест группы.</summary>
    private static MapCard Stack(WindowContext c, IReadOnlyDictionary<string, string> races, MapStack stack)
    {
        var map = c.Map;
        string leaderType = map.Units.TryGetValue(stack.LeaderId, out var leader) ? leader.TypeId : "";
        string leaderName = StackUnitName(c, stack, leaderType);
        string race = RaceOf(c, races, stack.Owner);
        if (!Known(c, stack.Owner))
            return Card("DLG_R_C_STACK_NOSPY", stack.Id, ui =>
            {
                ui.SetText("TXT_INFO", Fill(c.Text("X005TA0409"), ("NAME", leaderName), ("RACE", race)));
                var (boost, lower) = SpellMarks(c, leader);
                ui.SetImage("IMG_BOOST", boost ? "Battle:FIISOSPELLBOOST" : null);
                ui.SetImage("IMG_LOWER", lower ? "Battle:FIISOSPELLLOWER" : null);
            });
        var places = Places(c, stack.UnitIds, stack.Slots);
        return Card("DLG_R_C_STACK", stack.Id, ui =>
        {
            ui.SetText("TXT_NAME", leaderName);
            ui.SetText("TXT_RACE", race);
            ui.SetText("TXT_BATTLES", Fill(c.Text("X005TA0887"), ("QTY", stack.BattlesWon)));
            ui.SetImage("IMG_LEADER", leaderType.Length > 0 ? c.Faces.Picture(leaderType) : null, UiImageAlign.Center);
            Group(c, ui, places, stack, StackImage, StackText, first: 1);
        }, screen => Widen(screen, places, StackImage, StackText, first: 1));
    }

    /// <summary>Значки карточки «зашлите шпиона» (<c>CEncLayoutStack</c>: <c>0x57b947</c> запоминает места <c>IMG_LOWER</c>/<c>IMG_BOOST</c> и
    /// снимает элементы; <c>0x57cac0</c> → <c>0x5ea500</c> по лидеру отряда, <c>CMidStack+0x28</c>): усиление — в цепочке лидера модификатор
    /// заклинания <c>L_BOOST</c> (кадр 13 <c>FIISOSPELLBOOST</c>, <c>0x57cc72</c>), ослабление — <c>L_LOWER</c> (кадр 14
    /// <c>FIISOSPELLLOWER</c>, <c>0x57cc5c</c>); рисуются от левого верхнего угла своего места (<c>0x57be6a</c>). Правило то же, что у
    /// значков ячейки воина (<see cref="UnitCellsView"/>).</summary>
    public static (bool Boost, bool Lower) SpellMarks(WindowContext c, MapUnit? leader)
    {
        ArgumentNullException.ThrowIfNull(c);
        if (leader is null) return (false, false);
        var marks = LastEdict.Data.Gfx.BattleMarkSets.Of(c.Session.RequireBook());
        return (leader.Modifiers.Any(marks.SpellBoost.Contains), leader.Modifiers.Any(marks.SpellLower.Contains));
    }

    /// <summary>Крепость (<c>0x579ebf</c>), руина, мешок, площадка, кристалл.</summary>
    private static MapCard? Site(WindowContext c, IReadOnlyDictionary<string, string> races, MapSite site)
    {
        var map = c.Map;
        switch (site.Kind)
        {
            case SiteKind.Capital or SiteKind.Village:
                string race = RaceOf(c, races, site.Owner);
                if (site.Kind == SiteKind.Capital && map.Players.TryGetValue(site.Owner, out var owner) && owner.CapturedBy.Length > 0)
                    return Card("DLG_R_C_MISC", site.Id, ui => ui.SetText("TXT_INFO", Fill(c.Text("X005TA0772"), ("NAME", site.Name), ("RACE", race))));
                if (!Known(c, site.Owner))
                    return Card("DLG_R_C_MISC", site.Id, ui => ui.SetText("TXT_INFO", Fill(c.Text("X005TA0433"), ("NAME", site.Name), ("RACE", race))));
                var guest = site.StackId.Length > 0 ? map.Stacks.GetValueOrDefault(site.StackId) : null;
                var guests = Places(c, guest?.UnitIds ?? [], guest?.Slots ?? []);
                var guards = Places(c, site.Guards, site.GuardSlots);
                return Card("DLG_R_C_CITY", site.Id, ui => City(c, ui, site, race, guest, guests, guards),
                            screen => Widen(Widen(screen, guests, GuestImage, GuestText, first: 0), guards, GuardImage, GuardText, first: 0));
            case SiteKind.Ruin when site.LooterId.Length == 0 && site.Visitors.Contains(c.Viewer, StringComparer.OrdinalIgnoreCase):
                // 0x5796c0: не разграблена и зритель среди посетивших (0x5fda67 — набор +0x84 руины; пополняют осмотр вором и бой у руины
                // нападавшего не нейтрала, CEffectVisitRuin → CVisitorRuinAddVisiter 0x613164) — охрана руины (0x579831, DLG_R_C_RUIN: имя
                // руины и шесть мест, как у отряда). Без игрока в параметре карточки (CEncParamIDPlayer, 0x573d82; не с карты) движок
                // показывает охрану всегда (0x579736), но с карты (0x48e5e4, 0x48e613) игрок — всегда зритель, а других открытий у порта нет.
                var ruinGuards = Places(c, site.Guards, site.GuardSlots);
                return Card("DLG_R_C_RUIN", site.Id, ui =>
                {
                    ui.SetText("TXT_NAME", site.Name);
                    Group(c, ui, ruinGuards, null, StackImage, StackText, first: 1);
                }, screen => Widen(screen, ruinGuards, StackImage, StackText, first: 1));
            case SiteKind.Ruin:
                // Разграблена — X005TA0186; не осмотрена зрителем — X005TA0187 «%RUINNAME%, еще не исследованы.» (0x5796c0).
                return Card("DLG_R_C_MISC", site.Id,
                            ui => ui.SetText("TXT_INFO", Fill(c.Text(site.LooterId.Length > 0 ? "X005TA0186" : "X005TA0187"), ("RUINNAME", site.Name))));
            case SiteKind.Bag when !site.Gone:
                return Card("DLG_R_C_MISC", site.Id, ui => ui.SetText("TXT_INFO", c.Text("X005TA0171")));
            case SiteKind.Merchant or SiteKind.Mage or SiteKind.Mercenaries or SiteKind.Trainer:
                string type = c.Text(site.Kind switch
                {
                    SiteKind.Merchant => "X005TA0873", SiteKind.Mage => "X005TA0874", SiteKind.Mercenaries => "X005TA0875", _ => "X005TA0876",
                });
                return Card("DLG_R_C_MISC", site.Id,
                            ui => ui.SetText("TXT_INFO", Fill(c.Text("X005TA0868"), ("NAME", site.Name), ("TYPE", type), ("DESC", site.Description))));
            case SiteKind.Crystal:
                bool free = site.Owner.Length == 0 || c.Session.RequireBook().Economy.Races.TryGetValue(map.Players.GetValueOrDefault(site.Owner)?.RaceId ?? "", out var r) && !r.Playable;
                return Card("DLG_R_C_RES", site.Id, ui => ui.SetText("TXT_INFO", Fill(c.Text("X005TA0869"),
                    ("TYPE", c.Text(free ? "X005TA0870" : "X005TA0871")), ("INCOME", ""),
                    ("TXT", c.Text(free ? "X005TA0342" : "X005TA0341")), ("TXT2", free ? c.Text("X005TA0343") : ""))));
            default:
                return null;
        }
    }

    /// <summary><c>DLG_R_C_CITY</c>: имя, бунт, уровень, хозяин, восстановление ОЗ (<c>X005TA0429</c>); гость (<c>X005TA0431</c> или
    /// <c>X005TA0619</c>) и гарнизон (<c>X005TA0432</c>) портретами.</summary>
    private static void City(WindowContext c, UiScreenState ui, MapSite site, string race, MapStack? guest,
                             Place?[] guests, Place?[] guards)
    {
        var map = c.Map;
        var book = c.Session.RequireBook();
        bool riot = site.RiotTurn is { } t && t > map.Turn;
        string level = site.Kind == SiteKind.Capital ? c.Text("X005TA0095")
            : Fill(c.Text("X005TA0663"), ("LEVEL", (site.Size ?? 1).ToString(System.Globalization.CultureInfo.InvariantCulture)));
        string raceId = map.Players.GetValueOrDefault(site.Owner)?.RaceId ?? "";
        int hp;
        try
        {
            hp = site.Kind == SiteKind.Capital
                ? book.Economy.Races.TryGetValue(raceId, out var capitalRace) ? Regeneration.CapitalPercent(capitalRace) : 0
                : Regeneration.VillagePercent(book.Economy, raceId, site.Size ?? 1, site.RegenBonus ?? 0, riot);
        }
        catch (ArgumentException)
        {
            hp = 0;   // нет строки GcityInf расы хозяина
        }
        ui.SetText("TXT_INFO", Fill(c.Text("X005TA0429"), ("NAME", site.Name), ("RIOT", riot ? c.Text("X005TA0690") : ""), ("LEVEL", level),
                                    ("RACE", race), ("HP", hp.ToString(System.Globalization.CultureInfo.InvariantCulture))));
        // Имя гостя — как у карточки отряда (0x57acd3 → 0x5e97dc): у героя — его собственное.
        string guestName = guest is not null && map.Units.TryGetValue(guest.LeaderId, out var gl) ? StackUnitName(c, guest, gl.TypeId) : "";
        ui.SetText("TXT_STACK_NAME", guest is null ? c.Text("X005TA0619") : Fill(c.Text("X005TA0431"), ("LEADER", guestName)));
        ui.SetText("TXT_REINF", c.Text("X005TA0432"));
        Group(c, ui, guests, guest, GuestImage, GuestText, first: 0);
        Group(c, ui, guards, null, GuardImage, GuardText, first: 0);
    }

    private const string StackImage = "IMG_UNIT_{0}", StackText = "TXT_UNIT_{0}", GuestImage = "IMG_STACK_{0}", GuestText = "TXT_STACK_{0}",
                         GuardImage = "IMG_DEFENSE_{0}", GuardText = "TXT_DEFENSE_{0}";

    /// <summary>Рамки портретов карточек (<c>Icons.ff</c>, таблица <c>0x7ae938</c>): малый воин — 84×99, большой — 164×99.</summary>
    public const string SmallBorder = "Icons:BORDERUNITSMALL", LargeBorder = "Icons:BORDERUNITLARGE";

    /// <summary>Юнит на месте группы: большой стоит на месте и на соседнем по ряду (<see cref="Formation.Pair"/>).</summary>
    private readonly record struct Place(MapUnit Unit, bool Big);

    /// <summary>Шесть мест группы: юнит, стоящий на месте (<c>Slots</c> — номер юнита по месту, большой — на двух, портрет на первом);
    /// второе место большого и пустые — null.</summary>
    private static Place?[] Places(WindowContext c, IReadOnlyList<string> units, IReadOnlyList<int> slots)
    {
        var places = new Place?[Formation.Cells];
        var seen = new HashSet<int>();
        for (int place = 0; place < places.Length; place++)
        {
            int index = place < slots.Count ? slots[place] : -1;
            if (index < 0 || index >= units.Count || !seen.Add(index) || !c.Map.Units.TryGetValue(units[index], out var unit)) continue;
            int pair = Formation.Pair(place);
            places[place] = new Place(unit, pair < slots.Count && slots[pair] == index);
        }
        return places;
    }

    /// <summary>Место большого юнита — оба места ряда (<c>0x57c4ef</c>…<c>0x57c687</c>: у большого движок двигает картинку и подпись
    /// первого места на середину пары, а элементы второго сжимает в ничто): картинка — во весь ряд от левого края левой до правого края
    /// правой (портрет по центру, <see cref="UiImageAlign.Center"/>), подпись своей ширины — по середине ряда подписей.</summary>
    private static UiScreen Widen(UiScreen screen, Place?[] places, string image, string text, int first)
    {
        if (!places.Any(p => p is { Big: true })) return screen;
        var rects = screen.Elements.ToDictionary(e => e.Id, e => e.Rect, StringComparer.Ordinal);
        var moved = new Dictionary<string, UiRect>(StringComparer.Ordinal);
        string Id(string format, int p) => string.Format(System.Globalization.CultureInfo.InvariantCulture, format, p + first);
        for (int place = 0; place < places.Length; place++)
        {
            if (places[place] is not { Big: true }) continue;
            int pair = Formation.Pair(place);
            if (rects.TryGetValue(Id(image, place), out var a) && rects.TryGetValue(Id(image, pair), out var b))
                moved[Id(image, place)] = Union(a, b);
            if (rects.TryGetValue(Id(text, place), out var ta) && rects.TryGetValue(Id(text, pair), out var tb))
            {
                var row = Union(ta, tb);
                moved[Id(text, place)] = ta with { X = row.X + ((row.Width - ta.Width) / 2) };
            }
        }
        return screen with { Elements = [.. screen.Elements.Select(e => moved.TryGetValue(e.Id, out var r) ? e with { Rect = r } : e)] };
    }

    private static UiRect Union(UiRect a, UiRect b) =>
        UiRect.FromCorners(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y), Math.Max(a.X + a.Width, b.X + b.Width), Math.Max(a.Y + a.Height, b.Y + b.Height));

    /// <summary>Шесть мест группы (<c>0x57c1e1</c>: место <c>n</c> — элемент <c>n + first</c>): портрет в рамке и имя юнита, стоящего на
    /// месте. Портрет — как в ячейках окон (<see cref="UnitCellsView.Portrait"/>: раненый — красная нижняя доля, павший — маска; <c>0x57c3ec</c>:
    /// жив — доля здоровья, нет — режим 10) поверх рамки <see cref="SmallBorder"/>, у большого — <see cref="LargeBorder"/> (<c>0x57c443</c>),
    /// по центру места. Полужирным (<c>X005TA0784</c>) — юнит с реализацией лидера (<c>0x57c2fa</c>: <c>IUsStackLeader</c>; у нейтрального
    /// отряда во главе с воином — никто), прочие — <c>X005TA0783</c>; имя — <c>0x5e97dc</c> (<see cref="StackUnitName"/>).</summary>
    private static void Group(WindowContext c, UiScreenState ui, Place?[] places, MapStack? stack, string image, string text, int first)
    {
        for (int place = 0; place < places.Length; place++)
        {
            string img = string.Format(System.Globalization.CultureInfo.InvariantCulture, image, place + first);
            string txt = string.Format(System.Globalization.CultureInfo.InvariantCulture, text, place + first);
            if (places[place] is not { } at)
            {
                // Пустое место — пустая малая рамка (пользователь 11.10: у пустых мест карточки рамки видны); второе место большого юнита
                // закрыто его большой рамкой — пусто.
                bool coveredByBig = places[Formation.Pair(place)] is { Big: true };
                ui.SetImage(img, coveredByBig ? null : SmallBorder, UiImageAlign.Center);
                ui.SetText(txt, "");
                continue;
            }
            var unit = at.Unit;
            ui.SetImage(img, Framed(c, unit, at.Big), UiImageAlign.Center);
            bool isLeader = stack is not null && unit.Id.Equals(stack.LeaderId, StringComparison.OrdinalIgnoreCase);
            string name = isLeader ? StackUnitName(c, stack!, unit.TypeId) : UnitName(c, unit.TypeId);
            ui.SetText(txt, Fill(c.Text(LeaderImpl(c, unit.TypeId) ? "X005TA0784" : "X005TA0783"), ("NAME", name)));
        }
    }

    /// <summary>Портрет юнита в рамке карточки (кадр из слоёв <see cref="UiFrameRef.Layered"/>).</summary>
    public static string Framed(WindowContext c, MapUnit unit, bool big)
    {
        ArgumentNullException.ThrowIfNull(c);
        ArgumentNullException.ThrowIfNull(unit);
        return UiFrameRef.Layered(big ? LargeBorder : SmallBorder, UnitCellsView.Portrait(c, unit, c.Session.HpMax(unit), big));
    }

    /// <summary>Имя лидера отряда (<c>CMidUnit::getName</c> <c>0x5e97dc</c>): у реализации лидера (<c>IUsStackLeader</c>), кроме вора
    /// (<c>IUsNoble</c>) и призванного (<c>IUsSummon</c>), — собственное имя юнита (<c>NAME_TXT</c> сохранения,
    /// <see cref="StackLook.LeaderName"/>), у прочих — имя типа. Так нейтральный отряд во главе с воином зовётся по типу («Копейщик»),
    /// а не английским именем, которое сценарий пишет каждому юниту («Spearman»).</summary>
    private static string StackUnitName(WindowContext c, MapStack stack, string type)
    {
        string own = c.Session.Looks.Stacks.FirstOrDefault(s => s.Id.Equals(stack.Id, StringComparison.OrdinalIgnoreCase))?.LeaderName ?? "";
        if (own.Length == 0 || !LeaderImpl(c, type) || c.Session.Book is not { } book) return UnitName(c, type);
        int category = book.TryGlobal(type, out var id, out _) && book.Rules.Units.TryGetValue(id, out var unit) ? unit.Category : -1;
        bool nameless = book.LeaderCategoryOf(type) == ThiefCategory
                        || category is LastEdict.Rules.Battle.UnitCategory.Noble or LastEdict.Rules.Battle.UnitCategory.Summon;
        return nameless ? UnitName(c, type) : own;
    }

    /// <summary><c>LleadC</c> вора — реализация <c>IUsNoble</c>.</summary>
    private const int ThiefCategory = 5;

    /// <summary>Реализация лидера (<c>IUsStackLeader</c>) — у типа задан <c>Gunits.LEADER_CAT</c> (воры тоже).</summary>
    private static bool LeaderImpl(WindowContext c, string type) => c.Session.Book?.LeaderCategoryOf(type) > 0;

    /// <summary>Полные сведения о хозяине: нейтрал или не задан, сам зритель, союзник (<c>0x5f78fc</c>) или разведан (<c>0x5e46c2</c>).</summary>
    private static bool Known(WindowContext c, string owner)
    {
        var map = c.Map;
        if (owner.Length == 0 || Same(owner, c.Viewer) || !map.Players.TryGetValue(owner, out var other)) return true;
        var book = c.Session.RequireBook();
        if (!(book.Economy.Races.TryGetValue(other.RaceId, out var otherRace) && otherRace.Playable)) return true;
        if (!map.Players.TryGetValue(c.Viewer, out var me)) return false;
        if (me.Spied.Contains(owner, StringComparer.OrdinalIgnoreCase)) return true;
        return Diplomacy.PartyOf(me, book.Economy.Races) is { } a && Diplomacy.PartyOf(other, book.Economy.Races) is { } b && a != b
               && Diplomacy.AreAllies(map.Relations, a, b);
    }

    /// <summary>Эпитафии (<c>CEncLayoutTomb</c> <c>0x578e1b</c>): есть записи — «Здесь покоится:» (<c>X005TA0097</c>), затем по записи
    /// от старой к новой <c>X005TA0098</c>: имя лидера и, если убийца есть, «, победитель — …» с переводом строки (<c>X005TA0099</c>).
    /// Без убийцы перевода строки нет — следующая запись идёт в ту же строку, как у движка. День и владелец не выводятся.</summary>
    public static string TombText(WindowContext c, IReadOnlyDictionary<string, string> races, MapTomb tomb)
    {
        ArgumentNullException.ThrowIfNull(c);
        ArgumentNullException.ThrowIfNull(tomb);
        if (tomb.Entries.Count == 0) return "";
        var text = new System.Text.StringBuilder(c.Text("X005TA0097"));
        foreach (var entry in tomb.Entries)
        {
            string defeated = entry.Killer.Length == 0 ? "" : Fill(c.Text("X005TA0099"), ("KILLER", KillerName(c, races, entry.Killer)));
            text.Append(Fill(c.Text("X005TA0098"), ("NAME", entry.StackName), ("DEFEATED", defeated)));
        }
        return text.ToString();
    }

    /// <summary>Убийца в эпитафии: игрок нейтральной (неиграбельной) расы — имя расы, прочий игрок — его имя, иначе (тип лидера
    /// победителя) — имя типа воина.</summary>
    private static string KillerName(WindowContext c, IReadOnlyDictionary<string, string> races, string killer)
    {
        if (!c.Map.Players.TryGetValue(killer, out var player)) return UnitName(c, killer);
        bool playable = c.Session.Book is { } book && book.Economy.Races.TryGetValue(player.RaceId, out var race) && race.Playable;
        return playable ? player.Name : RaceOf(c, races, killer);
    }

    private static string RaceOf(WindowContext c, IReadOnlyDictionary<string, string> races, string owner) =>
        c.Map.Players.TryGetValue(owner, out var p) && races.TryGetValue(p.RaceId, out var txt) ? c.Text(txt) : "";

    private static string Player(WindowContext c, string owner) =>
        c.Map.Players.TryGetValue(owner, out var p) ? p.Name : "";

    private static string UnitName(WindowContext c, string type) => c.Text(c.Session.Art.Unit(type)?.NameTxt ?? "");

    private static bool Covers(MapState map, string id, int x, int y) =>
        map.Plan.TryGetValue((x, y), out var ids) && ids.Contains(id, StringComparer.OrdinalIgnoreCase);

    private static bool Same(string a, string? b) => b is not null && a.Equals(b, StringComparison.OrdinalIgnoreCase);

    private static string Fill(string template, params (string Key, object Value)[] values) => UiTemplate.Fill(template, values);

    private static MapCard Card(string screen, string id, Action<UiScreenState> fill, Func<UiScreen, UiScreen>? shape = null) =>
        new(screen, id, new CardLogic(fill, shape));

    /// <summary>Карточка без кнопок: заполнить при открытии; <c>shape</c> — раскладка по содержимому (место большого юнита).</summary>
    private sealed class CardLogic(Action<UiScreenState> fill, Func<UiScreen, UiScreen>? shape) : IScreenLogic
    {
        public UiScreen Shape(UiScreen screen) => shape?.Invoke(screen) ?? screen;

        public void Open(UiScreenState ui) => fill(ui);

        public void Handle(UiEvent e, UiScreenState ui)
        {
        }
    }
}
