using LastEdict.Data.Menu;
using LastEdict.Data.Ui.Windows;
using LastEdict.Data.Ui;
using LastEdict.Rules.Map;

namespace LastEdict.Data.Map;

/// <summary>
/// Переключатели палитры карты (<c>DLG_ISO_PAL</c> / <c>DLG_ISO_LAND</c>, разбор <c>Discipl2.exe</c> 11.10, ранг 1 по листингу):
/// <list type="bullet">
/// <item><c>TOG_RESPOPUP</c> (<c>0x489093</c>) — полоса ресурсов <c>DLG_ISO_RESPOPUP</c> поверх карты: включена — окно создаётся,
/// выключена — снимается. Состояние — глобал <c>0x835a7c</c>. По листингу при начале партии он обнуляется (<c>0x488f96</c>), но
/// пользователь, знающий оригинал, видит полосу всегда с начала партии (ответ 11.10) — у порта она по умолчанию включена;</item>
/// <item><c>BTN_RES_PAL</c> (<c>0x48905a</c>) — смена палитры: глобал <c>0x835a78</c> 0 ↔ 1 и пересборка (<c>0x4885c1</c>): 0 — мини-карта
/// <c>DLG_ISO_PAL</c>, 1 — владения <c>DLG_ISO_LAND</c>; при начале партии — 0;</item>
/// <item><c>TOG_BANNERS</c> (<c>0x489080</c>) — знамёна отрядов: байт 0 настроек <c>0x838650</c> (в начале процесса 0, <c>0x5b32db</c>);
/// картинка знамени обёрнута (<c>0x5bbf96</c> → <c>0x5b3312</c>) и рисуется, только если байт 0 или байт 1 не ноль (<c>0x5b3369</c>);
/// байт 1 — зажатый Shift (<c>0x490a0f</c>: <c>GetKeyState(VK_SHIFT)</c>), то есть знамёна видны и пока держишь Shift. У порта
/// переключатель сохраняется между запусками (<see cref="MapSettings.Banners"/>, ответ пользователя 11.10).</item>
/// </list>
/// </summary>
public sealed class MapPalette
{
    /// <summary>Знамёна включены переключателем — на весь процесс, как байт настроек движка; при старте карты — из настроек порта
    /// (<see cref="MapSettings.Banners"/>).</summary>
    public static bool Banners { get; set; }

    /// <summary>Полоса ресурсов показана; в начале партии — да (ответ пользователя 11.10).</summary>
    public bool Resources { get; set; } = true;

    /// <summary>Вместо мини-карты — владения (<c>DLG_ISO_LAND</c>).</summary>
    public bool Land { get; set; }

    public const string Pal = "DLG_ISO_PAL", LandScreen = "DLG_ISO_LAND";

    /// <summary>Экран палитры сейчас.</summary>
    public string Screen => Land ? LandScreen : Pal;
}

/// <summary>Доля равнины расы игрока для <c>DLG_ISO_LAND</c>: тип расы (<c>LRaceCategory</c>) и проценты.</summary>
public readonly record struct LandShare(int RaceType, int Percent)
{
    /// <summary>Значок расы — <c>Icons.ff</c> <c>LOGO{HU,UD,HE,DW,EL}</c> (<c>0x5b2989</c>: архив реестра <c>+0x5c</c>).</summary>
    public string Logo => $"Icons:LOGO{RaceType switch { 0 => "HU", 1 => "UD", 2 => "HE", 3 => "DW", _ => "EL" }}";

    /// <summary>Игроки играбельных рас по порядку карты (<c>0x488b9c</c>, <c>0x488ccd</c>: неиграбельная раса пропускается,
    /// <c>0x5e53a6</c>) и их доля равнины (<c>0x5e5d50</c>: <c>100 · своих / всех</c>, целым делением —
    /// <see cref="AiDiplomacy.PlainLandShare"/>).</summary>
    public static IReadOnlyList<LandShare> Of(MapState map, EconomyRules economy)
    {
        ArgumentNullException.ThrowIfNull(map);
        ArgumentNullException.ThrowIfNull(economy);
        var shares = new List<LandShare>();
        foreach (var player in map.Players.Values)
        {
            if (!economy.Races.TryGetValue(player.RaceId, out var race) || !race.Playable) continue;
            shares.Add(new LandShare(race.RaceType, AiDiplomacy.PlainLandShare(map, race.RaceType, economy)));
        }
        return shares;
    }
}

/// <summary>Логика палитры (<see cref="MapPalette"/>): ставит переключатели по состоянию, у <c>DLG_ISO_LAND</c> — значки рас
/// (<c>IMG_RACE_n</c>, по центру места, флаги 9) и доли (<c>TXT_RACE_n</c>, <c>X005TA0707</c> «%QTY%%»); щелчки меняют состояние и
/// зовут <see cref="Changed"/> — хозяин прячет или показывает полосу ресурсов, знамёна и меняет палитру.</summary>
public sealed class MapPaletteLogic(MapPalette palette, Func<IReadOnlyList<LandShare>> lands, Func<string, string> text) : IScreenLogic
{
    private const int RaceSlots = 5;

    /// <summary>Состояние палитры сменилось.</summary>
    public event Action? Changed;

    public MapPalette Palette => palette;

    public void Open(UiScreenState ui)
    {
        ArgumentNullException.ThrowIfNull(ui);
        ui.SetChecked("TOG_RESPOPUP", palette.Resources);
        ui.SetChecked("TOG_BANNERS", MapPalette.Banners);
        if (ui.Screen.Id != MapPalette.LandScreen) return;
        var shares = lands();
        for (int i = 0; i < RaceSlots; i++)
        {
            string n = (i + 1).ToString(System.Globalization.CultureInfo.InvariantCulture);
            bool shown = i < shares.Count;
            ui.SetImage($"IMG_RACE_{n}", shown ? shares[i].Logo : null, UiImageAlign.Center);
            ui.SetText($"TXT_RACE_{n}", shown ? UiTemplate.Fill(text("X005TA0707"), ("QTY", shares[i].Percent)) : "");
        }
    }

    public void Handle(UiEvent e, UiScreenState ui)
    {
        switch (e)
        {
            case UiToggled { Element: "TOG_RESPOPUP" } t: palette.Resources = t.Checked; break;
            case UiToggled { Element: "TOG_BANNERS" } t: MapPalette.Banners = t.Checked; break;
            case UiClicked { Element: "BTN_RES_PAL" }: palette.Land = !palette.Land; break;
            default: return;
        }
        Changed?.Invoke();
    }
}
