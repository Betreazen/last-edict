using D2.Data.Content;
using D2.Data.Menu;
using D2.Data.Play;
using D2.Data.Play.Commands;
using D2.Rules.Map;

namespace D2.Data.Ui.Windows;

/// <summary>Портрет лорда игрока (<c>Lords.ff</c>): крупный <c>LORD{раса}L0{лицо}</c> (160×220) — <c>sprintf("LORD%s%s%02d", раса, "L"/"S", лицо + 1)</c>
/// (<c>0x5c5563</c>, строки <c>0x7b0314</c>…<c>0x7b0334</c>). Буквы рас — по <c>Grace.RACE_TYPE</c>: 0 HU, 1 UN, 2 HE, 3 DW, 5 EL; нейтралы (4) —
/// <c>NE</c> и всегда лицо 1 (<c>0x5c5615</c>: номер лица обнулён).</summary>
public static class LordFaces
{
    public static string Large(int raceType, int face) =>
        $"Lords:LORD{raceType switch { 1 => "UN", 2 => "HE", 3 => "DW", 4 => "NE", 5 => "EL", _ => "HU" }}L{(raceType == 4 ? 0 : Math.Clamp(face, 0, 3)) + 1:00}";

    /// <summary>Малый портрет лорда 70×85 (<c>LORD{раса}S0{лицо}</c>): лицо выбранного в панели дипломатии <c>IMG_LORD_FACE</c> (снимки 28.09 —
    /// разница 2,3), окна дани и цены.</summary>
    public static string Small(int raceType, int face) => Large(raceType, face).Replace("L0", "S0", StringComparison.Ordinal);

    /// <summary>Круглый портрет лорда 130×142 (<c>Faces</c>): <c>sprintf("%sLORD%d", RACE_ID, лицо + 1)</c> (<c>0x5c5424</c>) — слоты поля
    /// дипломатии (снимок 28.09 <c>CDiplomacyInterf</c>).</summary>
    public static string Round(string raceId, int face) => $"Faces:{raceId}LORD{Math.Clamp(face, 0, 3) + 1}";
}

/// <summary>Названия рас (строки имён) — для <c>%RACE%</c> в окнах дипломатии.</summary>
public static class RaceNames
{
    public static IReadOnlyDictionary<string, string> Load(string rulesDir) => Load(ContentSource.Open(rulesDir));

    public static IReadOnlyDictionary<string, string> Load(ContentSet content) =>
        content.Races.ToDictionary(r => r.Key, r => r.NameText ?? "", StringComparer.OrdinalIgnoreCase);
}

/// <summary>
/// Предложение союза от ИИ человеку (ответ пользователя 23.09: ИИ предлагает в свой ход, окно появляется у игрока в начале
/// его хода; предложение ждёт в <see cref="GameState.Offers"/>). Окно — <c>DLG_DIPLO_BOX</c> (граф: после конца хода
/// <c>DLG_STRATEGIC.BTN_END_TURN</c> → <c>DLG_DIPLO_BOX</c>, <c>BTN_OK</c> → <c>DLG_CHAT_AI</c>): портрет лорда
/// предлагающего, <c>X005TA0289</c> «%PLAYER%, Повелитель %RACE%, предлагает заключить союз. Принять предложение?»,
/// «Принять» (<c>BTN_OK</c>) и «Отказ» (<c>BTN_CANCEL</c>); одиночная «ОК» (<c>BTN_OK2</c>) спрятана. Ответ —
/// <see cref="GameSession.AnswerAllianceOffer"/>; реплика предлагавшего ИИ показывается в <c>DLG_CHAT_AI</c>
/// (<see cref="ChatLogic"/>). Окно <c>DLG_OFFER</c> движка — только для сделок предметом и заклинанием (<c>COfferInterf</c>
/// <c>0x4b6126</c>), а сделок в порте нет (<c>docs/ai-diplomacy.md</c>).
/// Тем же окном человек отвечает на обмен картами от другого человека (горячий стул, ответ пользователя 28.09: «окно как у
/// союза»): <c>X160TA0008</c> «%PLAYER%, повелитель %RACE%, предлагает обменяться видом карты. Вы принимаете предложение?»
/// (в русской строке игры в начале лишний «|» — берётся как есть), ответ — <see cref="GameSession.AnswerExchangeOffer"/>,
/// реплики нет (предлагает человек).
/// </summary>
public sealed class AllianceOfferLogic(WindowContext context, AllianceOffer offer, IReadOnlyDictionary<string, string> raceNames)
    : IWindowLogic
{
    public const string Screen = "DLG_DIPLO_BOX";
    private readonly UiRequests _requests = new();

    public AllianceOffer Offer => offer;

    public string? LastRefusal { get; private set; }

    /// <summary>Строка вопроса: союз — <c>X005TA0289</c>, обмен картами — <c>X160TA0008</c>.</summary>
    public string Question => offer.Exchange ? "X160TA0008" : "X005TA0289";

    public UiRequest? TakeRequest() => _requests.Take();

    public string Summary =>
        $"{(offer.Exchange ? "обмен_картами" : "союз")} предлагает={offer.From} кому={offer.To} отказ={(LastRefusal ?? "-").Replace(' ', '_')}";

    /// <summary>Лицо предлагающего игрока — для этого окна и реплики после ответа.</summary>
    public string? Face
    {
        get
        {
            var map = context.Map;
            if (!map.Players.TryGetValue(offer.From, out var from)) return null;
            int type = context.Session.RequireBook().Economy.Races.TryGetValue(from.RaceId, out var race) ? race.RaceType : 0;
            return LordFaces.Large(type, from.Face);
        }
    }

    public void Open(UiScreenState ui)
    {
        ArgumentNullException.ThrowIfNull(ui);
        var from = context.Map.Players[offer.From];
        string race = raceNames.TryGetValue(from.RaceId, out var txt) ? context.Text(txt) : from.RaceId;
        ui.SetText("TXT_MESSAGE", UiTemplate.Fill(context.Text(Question), ("PLAYER", from.Name), ("RACE", race)));
        ui.SetImage("IMG_FACE", Face);
        ui.SetHidden("BTN_OK2", true);
    }

    /// <summary>Ответ отдаёт сама логика: переход графа от «Принять» ведёт в окно реплики, которое открывается с текстом.</summary>
    public bool Allows(string element, UiScreenState ui) => element is not ("BTN_OK" or "BTN_CANCEL" or "BTN_OK2");

    public void Handle(UiEvent e, UiScreenState ui)
    {
        if (e is not UiClicked { Element: "BTN_OK" or "BTN_CANCEL" } click) return;
        var report = new List<string>();
        bool accept = click.Element == "BTN_OK";
        // Командой (docs/game-commands.md): ответ в ход ИИ — решение, которое журнал команд пишет к ждущему ходу.
        var result = context.Session.Execute(offer.Exchange ? new AnswerExchangeCommand(offer.From, offer.To, accept)
                                                            : new AnswerAllianceCommand(offer.From, offer.To, accept));
        report.AddRange(result.Report);
        LastRefusal = (string?)result.Value;
        // Ответ (сообщения 18/61) движок шлёт предложившему (0x4d5429, 0x4d59fa): ИИ-предложивший беззвучен, человек «горячего стула»
        // слышит его сразу после ответа — обработчики без фильтра игрока (0x48a7c0, разбор 28.09, docs/ai-screen.md §11).
        if (LastRefusal is null && context.Session.State.Info.Humans.Contains(offer.From, StringComparer.OrdinalIgnoreCase))
            context.Announce(offer.Exchange ? accept ? TurnSightKind.Traded : TurnSightKind.TradeRefused
                                            : accept ? TurnSightKind.AllianceMade : TurnSightKind.AllianceRefused, offer.Exchange ? "" : offer.From);
        _requests.Add(new UiClose());
        if (LastRefusal is not null) _requests.Add(new UiTell(LastRefusal));
        // Реплику предлагавшего ИИ (GameSession.Chat → TurnSightKind.AiChat) показывает сборщик окон сцены (NoticeWatch → ChatLogic.Of),
        // как и прочие сообщения ИИ: окно DLG_CHAT_AI за этим окном.
        if (LastRefusal is null && context.Session.State.Info.Humans.Contains(offer.From, StringComparer.OrdinalIgnoreCase)
                 && context.Map.Players.TryGetValue(offer.To, out var to))
        {
            // Предложил человек («горячий стул»): итог — ему сразу (сообщения 18/61 только предложившему, 0x4d5429): союз
            // X005TA0290/X005TA0300, обмен картами X160TA0009/X160TA0010 — от имени адресата. ponytail: у движка это DLG_DIPLO_BOX с одной
            // BTN_OK2, у порта — окно сообщения, как у ответа ИИ.
            string id = offer.Exchange ? accept ? "X160TA0009" : "X160TA0010" : accept ? "X005TA0290" : "X005TA0300";
            string race = raceNames.TryGetValue(to.RaceId, out var txt) ? context.Text(txt) : to.RaceId;
            _requests.Add(new UiTell(UiTemplate.Fill(context.Text(id), ("PLAYER", to.Name), ("RACE", race))));
        }
    }
}

/// <summary>
/// Сообщение ИИ человеку (<c>DLG_CHAT_AI</c>, <c>CAIChatInterf</c> <c>0x4b9747</c>, открывает <c>CCmdChatMsg</c> — <c>0x48c3d1</c>):
/// <c>TXT_MESSAGE</c> — <c>X005TA0606</c> «%NAME%, %RACE%: "%MSG%"» (имя полужирным; <c>0x4b986d</c>): имя отправителя
/// (<c>0x5f1c1d</c>), название его расы (<c>0x5e5056</c> → <c>0x582003</c>), строка <c>TAiMsg</c> (<c>0x478ade</c>); <c>IMG_FACE</c> — крупный
/// портрет лорда отправителя по его лицу (<c>+0x20</c>, <c>0x5b24ed</c>); звук <c>AICHAT</c> (<c>0x5b03ce(0x29)</c>, <c>0x4b99cc</c>).
/// «Закрыть» (<c>BTN_OK</c>, <c>X100TA0020</c>) закрывает окно.
/// </summary>
public sealed class ChatLogic(string text, string? face, string message = "") : IWindowLogic
{
    public const string Screen = "DLG_CHAT_AI";

    /// <summary>Строка окна (<c>0x4b986d</c>).</summary>
    public const string TextId = "X005TA0606";

    private readonly UiRequests _requests = new();

    /// <summary>Текст окна целиком.</summary>
    public string Text => text;

    /// <summary>Портрет отправителя (<c>IMG_FACE</c>).</summary>
    public string? Face => face;

    /// <summary>Само сообщение (<c>%MSG%</c>).</summary>
    public string Message => message.Length > 0 ? message : text;

    /// <summary>Окно сообщения <paramref name="sight"/> (<see cref="TurnSightKind.AiChat"/>): отправитель — <see cref="TurnSight.Id"/>.</summary>
    public static ChatLogic Of(TurnSight sight, MapState map, GameBook book, IReadOnlyDictionary<string, string> raceNames, Func<string, string> text)
    {
        ArgumentNullException.ThrowIfNull(sight);
        ArgumentNullException.ThrowIfNull(map);
        ArgumentNullException.ThrowIfNull(book);
        ArgumentNullException.ThrowIfNull(raceNames);
        ArgumentNullException.ThrowIfNull(text);
        var from = map.Players.GetValueOrDefault(sight.Id);
        string race = from is not null && raceNames.TryGetValue(from.RaceId, out var txt) ? text(txt) : "";
        int type = from is not null && book.Economy.Races.TryGetValue(from.RaceId, out var r) ? r.RaceType : 0;
        string body = UiTemplate.Fill(text(TextId), ("NAME", from?.Name.Trim() ?? ""), ("RACE", race), ("MSG", sight.Text));
        return new ChatLogic(body, from is null ? null : LordFaces.Large(type, from.Face), sight.Text);
    }

    public UiRequest? TakeRequest() => _requests.Take();

    public string Summary => $"реплика={Message.Replace(' ', '_')}";

    public void Open(UiScreenState ui)
    {
        ArgumentNullException.ThrowIfNull(ui);
        ui.SetText("TXT_MESSAGE", text);
        ui.SetImage("IMG_FACE", face);
    }

    public bool Allows(string element, UiScreenState ui) => element != "BTN_OK";

    public void Handle(UiEvent e, UiScreenState ui)
    {
        if (e is UiClicked { Element: "BTN_OK" }) _requests.Add(new UiClose());
    }
}
