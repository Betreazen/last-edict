using LastEdict.Data.Content;
using LastEdict.Data.Play;
using LastEdict.Rules.Map;

namespace LastEdict.Data.Ui.Windows;

/// <summary>Портреты юнитов в графике игры (<c>Faces.ff</c>): малый <c>{база}FACE</c> (70×85, у большого 150×85) и
/// крупный <c>{база}FACEB</c> (130×142). База — <c>Gunits.BASE_UNIT</c> (выросший тип рисуется картинкой исходного),
/// пусто — сам тип; так же берёт портреты экран боя.</summary>
public sealed class UnitFaces(IReadOnlyDictionary<string, string> baseUnits)
{
    public const string Archive = "Faces";

    /// <summary>Есть ли в <c>Faces.ff</c> картинка с таким именем (без архива); не задано — нет ни одной <c>FACES</c>
    /// (<see cref="PanelName"/> берёт <c>FACE</c>).</summary>
    public Func<string, bool>? Has { get; init; }

    /// <summary>Портреты по <c>Gunits</c>; <paramref name="has"/> — проверка картинки <c>Faces.ff</c> (<see cref="Has"/>).</summary>
    public static UnitFaces Load(string rulesDir, Func<string, bool>? has) => Load(ContentSource.Open(rulesDir), has);

    public static UnitFaces Load(ContentSet content, Func<string, bool>? has)
    {
        ArgumentNullException.ThrowIfNull(content);
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var unit in content.Units)
            if (unit.BaseUnit is not null) map[unit.Key] = content.KeyOf(unit.BaseUnit);
        return new UnitFaces(map) { Has = has };
    }

    public static UnitFaces Load(string rulesDir) => Load(rulesDir, null);

    /// <summary>Имя графики базового типа (<see cref="ArtNames"/>: id оригинала заглавными).</summary>
    public string Base(string type) => ArtNames.Of(baseUnits.TryGetValue(type, out var b) ? b : type);

    /// <summary>Имя малого портрета в <c>Faces.ff</c> без архива — <c>{база}FACE</c> (панели берут <see cref="PanelName"/>). Движок берёт картинку у <c>0x5b1e6a</c> (вызов из <c>0x48da55</c> панели отряда <c>0x48d8f3</c>): у типа —
    /// базовый (<c>vtable+0x2c</c>), пустой базовый (<c>[0x8372f4]</c>) — сам тип. Лидер, выданный сценарием, бывает обычным воином с
    /// <c>BASE_UNIT</c> (ассасин <c>G000UU5354</c> → <c>G000UU0154</c>), своего <c>FACE</c> у него нет (пометка 1 прогона 14.10).</summary>
    public string SmallName(string type) => $"{Base(type)}FACE";

    /// <summary>Портрет панелей — команд карты (<c>IMG_LEADER</c>, <c>0x48da55</c>), столицы (гость, <c>0x486a46</c>) и окна имени героя
    /// (<c>0x4aff40</c>): <c>0x5b1e6a</c> → <c>0x5c6718</c> грузит <c>{база}FACES</c> (<c>[0x7b0400]</c> + <c>[0x7b040c]</c>) — у большого
    /// юнита это малый портрет 70×85 вместо широкого <c>FACE</c> 150×85; нет такой картинки — <c>{база}FACE</c> (<c>0x5c680d</c> →
    /// <c>0x5c65eb</c>). Пометка 3 партии 15.10: призванный большой лидер (<c>G000UU0102</c>) на панели был сжатым широким портретом.</summary>
    public string PanelName(string type)
    {
        string face = SmallName(type);
        return Has?.Invoke(face + "S") == true ? face + "S" : face;
    }

    /// <summary>Портрет панелей кадром <c>Faces:…</c> (<see cref="PanelName"/>).</summary>
    public string Panel(string type) => $"{Archive}:{PanelName(type)}";

    /// <summary>Малый портрет — кадр вида <c>Faces:{база}FACE</c> (<see cref="UiCard.Image"/>).</summary>
    public string Small(string type) => $"{Archive}:{SmallName(type)}";

    /// <summary>Крупный портрет — <c>Faces:{база}FACEB</c>.</summary>
    public string Large(string type) => $"{Archive}:{Base(type)}FACEB";

    /// <summary>Картина юнита во весь рост — кадр <c>Events</c> базового типа (350×450): её ставят карточка воина
    /// <c>DLG_R_C_UNIT</c> (<c>IMG_UNIT_ICON</c>, <c>0x574feb</c> → <c>0x576d51</c>) и карточка отряда <c>DLG_R_C_STACK</c>
    /// (<c>IMG_LEADER</c>, <c>0x57c0a9</c>): обе берут имя у <c>0x5b891e</c> (тип, выросший — базовый) и грузят из архива реестра
    /// <c>+0x48</c> (<c>0x5ae087</c>; порядок загрузки <c>0x5adab0</c>: <c>Events.ff</c>) без отражения, кладут по центру места
    /// (флаги 9 <c>setImage</c> <c>0x530e29</c>, <see cref="UiImageAlign.Center"/>).</summary>
    public string Picture(string type) => $"gfx:Events/{Base(type)}";
}

/// <summary>Что нужно окнам партии: партия, чьими глазами, строки языка и портреты.</summary>
public sealed record WindowContext(GameSession Session, string Viewer, IReadOnlyDictionary<string, string> Strings, UnitFaces Faces)
{
    /// <summary>Строка по id; нет строки — сам id, чтобы пропуск был виден.</summary>
    public string Text(string id) => Strings.TryGetValue(id, out var s) ? s : id;

    /// <summary>Имя типа юнита (<c>Gunits.NAME_TXT</c> → строка); нет — сам тип.</summary>
    public string UnitName(string type) =>
        Session.Art.Unit(type) is { NameTxt.Length: > 0 } look && Strings.TryGetValue(look.NameTxt, out var s) ? s : type;

    /// <summary>Тексты таблиц правил (описания воинов, расы, постройки, атаки); нет — окна обходятся без них.</summary>
    public RulesTexts? Rules { get; init; }

    /// <summary>Имена и описания предметов (<c>GItem</c>) для карточки предмета; нет — карточка без имени.</summary>
    public StackTexts? ItemTexts { get; init; }

    /// <summary>Имена героев по умолчанию на языке интерфейса; нет — имена из правил (<c>Tleader</c> игры).</summary>
    public LeaderNameList? LeaderNames { get; init; }

    /// <summary>Что окна помнят между открытиями за партию (ветка окна построек — глобал движка <c>0x835a8c</c>).</summary>
    public WindowMemory Memory { get; init; } = new();

    public MapState Map => Session.Map;

    public MapPlayer Player => Map.Players[Viewer];

    /// <summary>Уведомление показа о действии человека в окне (<see cref="TurnSightKind.PotionDrunk"/> и дальше): звук ставит показ
    /// (сессия Г) через наблюдателя хода; модель не меняется. Наблюдателя нет — ничего.</summary>
    public void Announce(TurnSightKind kind, string objectId) =>
        Session.Observer?.Happened(new TurnSight(kind, Viewer, 0, 0, objectId));
}

/// <summary>Состояние окон, которое живёт дольше одного открытия.</summary>
public sealed class WindowMemory
{
    /// <summary>Последняя открытая ветка окна построек; пусто — первая («Воины»).</summary>
    public string BuildBranch { get; set; } = "";

    /// <summary>Последний уровень окна исследования (<c>[0x835a88]</c>), в начале — первый.</summary>
    public int ResearchLevel { get; set; } = 1;

    /// <summary>Последний уровень книги заклинаний (<c>[0x79b7fc]</c>); 0 — первый уровень, где что-то изучено.</summary>
    public int CastLevel { get; set; }
}
