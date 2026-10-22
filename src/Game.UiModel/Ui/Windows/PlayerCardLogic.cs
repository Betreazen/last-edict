using LastEdict.Data.Menu;
using LastEdict.Rules.Map;

namespace LastEdict.Data.Ui.Windows;

/// <summary>
/// Карточка игрока (<c>DLG_R_C_PLAYER</c>, <c>CEncLayoutPlayer</c> <c>0x5778eb</c>, заполнение <c>0x577a0c</c>; параметр — <c>CEncParamIDPlayer</c>
/// <c>0x573b38</c>: игрок карточки и зритель), пока держится правая кнопка. Открывает её только окно дипломатии (<c>CDiplomacyInterf</c>,
/// обработчик мыши <c>0x48522d</c>, <c>WM_RBUTTONDOWN</c>): над лицом выбранного в панели (<c>IMG_LORD_FACE</c>) — выбранный игрок, над
/// портретом слота поля — игрок слота (журнал <c>20260921-ui</c>, seq 5114455, <c>ret 0x577991</c>).
/// <list type="bullet">
/// <item><c>IMG_FACE</c> — крупный портрет лорда игрока карточки (<c>0x577cd6</c> → <c>0x5b24ed</c> → <c>0x5c5563</c>: <c>LORD{раса}L{лицо+1:00}</c>,
/// у нейтралов <c>NE</c> и лицо 1), от левого верхнего угла места (<c>0x530db8</c> с точкой 0,0).</item>
/// <item><c>TXT_PLAYER</c>: сам зритель — <c>X005TA0350</c>; иначе <c>X005TA0351</c>, два перевода строки и отношение рас (<c>0x5f788f</c>): война
/// (<c>0x5f7d94</c>) — <c>X005TA0352</c>, мир (<c>0x5f7e10</c>) — <c>X005TA0353</c> и союз (<c>0x5f78fc</c>) <c>X005TA0355</c> или его нет
/// <c>X005TA0354</c>, между ними — ничего. <c>%PLAYER%</c> — имя игрока, <c>%TYPE%</c> — вид лорда (<c>0x577d28</c>): маг <c>X005TA0066</c>,
/// воин <c>X005TA0065</c>, прочие <c>X005TA0067</c>.</item>
/// </list>
/// </summary>
public sealed class PlayerCardLogic(WindowContext context, string playerId) : IWindowLogic
{
    public const string Screen = "DLG_R_C_PLAYER";

    private readonly UiRequests _requests = new();

    public UiRequest? TakeRequest() => _requests.Take();

    private string _face = "-", _text = "";

    /// <summary>Для проверок: игрок, кадр портрета и текст одной строкой (пробелы — «_», перевод строки — «/»).</summary>
    public string Summary => $"игрок={playerId} лицо={_face} текст={_text.Replace(' ', '_').Replace(@"\n", "/", StringComparison.Ordinal).Replace('\n', '/')}";

    public void Open(UiScreenState ui)
    {
        ArgumentNullException.ThrowIfNull(ui);
        if (!context.Map.Players.TryGetValue(playerId, out var player)) return;
        _face = LordFaces.Large(context.Session.RequireBook().LookOf(player.RaceId), player.Face);
        _text = Text(player);
        ui.SetImage("IMG_FACE", _face);
        ui.SetText("TXT_PLAYER", _text);
    }

    /// <summary>Текст карточки (<c>0x577a0c</c>).</summary>
    public string Text(MapPlayer player)
    {
        ArgumentNullException.ThrowIfNull(player);
        string text;
        if (player.Id.Equals(context.Viewer, StringComparison.OrdinalIgnoreCase)) text = context.Text("X005TA0350");
        else
        {
            var book = context.Session.RequireBook();
            int me = context.Map.Players.TryGetValue(context.Viewer, out var viewer) ? Party(viewer) : -1, them = Party(player);   // ключ отношений — сторона
            int current = Diplomacy.Current(context.Map.Relations, me, them);
            string relation = Diplomacy.IsAtWar(book.Diplomacy, current) ? context.Text("X005TA0352")
                : Diplomacy.IsPeaceful(book.Diplomacy, current)
                    ? context.Text("X005TA0353") + context.Text(Diplomacy.AreAllies(context.Map.Relations, me, them) ? "X005TA0355" : "X005TA0354")
                    : "";
            text = context.Text("X005TA0351") + "\n\n" + relation;
        }
        return UiTemplate.Fill(text, ("PLAYER", player.Name), ("TYPE", context.Text(LordType(player))));
    }

    /// <summary>Вид лорда (<c>0x577d28</c>): маг (<c>[0x837760]</c>) — <c>X005TA0066</c>, воин (<c>[0x837750]</c>) — <c>X005TA0065</c>, прочие —
    /// <c>X005TA0067</c>.</summary>
    private string LordType(MapPlayer player) =>
        (LordCategory)context.Session.RequireBook().LordCategory(player.LordId) switch
        {
            LordCategory.Mage => "X005TA0066",
            LordCategory.Warrior => "X005TA0065",
            _ => "X005TA0067",
        };

    private int Party(MapPlayer player) => Diplomacy.PartyOf(player, context.Session.RequireBook().Economy.Races) ?? -1;

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
