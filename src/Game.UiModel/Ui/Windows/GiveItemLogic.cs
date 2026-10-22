using LastEdict.Data.Play;
using LastEdict.Rules.Map;

namespace LastEdict.Data.Ui.Windows;

/// <summary>Одно окно полученного: тип предмета с числом штук или одно заклинание; <see cref="Library"/> — заклинания из библиотеки
/// взятой столицы (флаг сообщения 43 <c>+0x2c</c>).</summary>
public sealed record GivenEntry(bool Spell, string Id, int Count, bool Library = false);

/// <summary>
/// Сборщик полученных предметов и заклинаний (<see cref="TurnSightKind.ItemsGiven"/>/<see cref="TurnSightKind.SpellsGiven"/>, сообщения
/// 44/43): подписан на ход партии (<see cref="GameSession.Watch"/>), копит окна по тому, кто их увидит. За ИИ человек окон не видит
/// (сообщение уходит только в очередь адресата, <c>0x420464</c>). В «горячем стуле» все люди — на одном клиенте, и окно видит тот, кто за ПК
/// в миг сообщения (<paramref name="atPc"/>; место меняется только в начале хода человека, <c>0x4177bf</c>): предметы (44) — только если
/// это получатель (<c>0x48ab78</c> сверяет адресата с игроком клиента <c>0x404af9</c>), иначе сообщение пропускается совсем; заклинания (43)
/// — без проверки адресата (<c>0x48ac3e</c> → <c>CTaskGiveSpell</c>), тому, кто за ПК (ответ пользователя 28.09 — «как в игре»).
/// Нет <paramref name="atPc"/> — видит получатель. Каждое сообщение — свои окна подряд.
/// </summary>
public sealed class GivenWatch(Func<string, bool> human, Func<string?>? atPc = null) : ITurnObserver
{
    private readonly List<(TurnSight Sight, string Viewer)> _pending = [];

    public void PlayerTurn(string playerId)
    {
    }

    public void StackMoving(StackMove move)
    {
    }

    public IReadOnlyList<LastEdict.Rules.Battle.TurnRecord>? Battle(LastEdict.Rules.Battle.BattleState state) => null;

    public void Happened(TurnSight sight)
    {
        ArgumentNullException.ThrowIfNull(sight);
        if (sight.Kind is not (TurnSightKind.ItemsGiven or TurnSightKind.SpellsGiven) || !human(sight.PlayerId)) return;
        string viewer = atPc?.Invoke() is { Length: > 0 } seated ? seated : sight.PlayerId;
        if (sight.Kind == TurnSightKind.ItemsGiven && !Same(viewer, sight.PlayerId)) return;   // 44 не за ПК — пропущено (0x48ab78)
        lock (_pending) _pending.Add((sight, viewer));
    }

    /// <summary>Есть ли окна для того, кто за ПК.</summary>
    public bool Has(string viewer)
    {
        lock (_pending) return _pending.Any(p => Same(p.Viewer, viewer));
    }

    /// <summary>Забрать сообщения, которые видит <paramref name="viewer"/>, по порядку прихода.</summary>
    public IReadOnlyList<TurnSight> Take(string viewer)
    {
        lock (_pending)
        {
            var mine = _pending.Where(p => Same(p.Viewer, viewer)).Select(p => p.Sight).ToList();
            _pending.RemoveAll(p => Same(p.Viewer, viewer));
            return mine;
        }
    }

    /// <summary>Окна одного сообщения по порядку: предметы — по типу с числом штук (<c>CItemCount</c> <c>0x61f174</c>: запись на тип в
    /// порядке прихода, ранг 2), заклинания — по одному; пустой список (мешок без предметов) — одна запись null: «Ничего не найдено».</summary>
    public static IReadOnlyList<GivenEntry?> Entries(TurnSight sight, MapState map)
    {
        ArgumentNullException.ThrowIfNull(sight);
        ArgumentNullException.ThrowIfNull(map);
        if (sight.Ids.Count == 0) return sight.Kind == TurnSightKind.ItemsGiven ? [null] : [];
        if (sight.Kind == TurnSightKind.SpellsGiven) return [.. sight.Ids.Select(id => (GivenEntry?)new GivenEntry(true, id, 1, sight.Library))];
        return [.. sight.Ids.Select(id => map.ItemTypes.TryGetValue(id, out var type) ? type : id)
                            .GroupBy(t => t, StringComparer.OrdinalIgnoreCase)
                            .Select(g => (GivenEntry?)new GivenEntry(false, g.Key, g.Count()))];
    }

    private static bool Same(string a, string b) => a.Equals(b, StringComparison.OrdinalIgnoreCase);
}

/// <summary>
/// Окно полученного (<c>DLG_ITEM</c>, <c>CItemInterf</c>, конструктор <c>0x4be1e2</c>; задачи <c>CTaskGiveItem</c> <c>0x4d947d</c> и
/// <c>CTaskGiveSpell</c> <c>0x4d99ec</c>): заголовок <c>TXT_TITLE</c> — предмет <c>X005TA0283</c>, заклинание <c>X005TA0773</c> (из библиотеки
/// столицы — <c>X005TA0774</c>); <c>TXT_ITEM</c> — <c>X005TA0516</c> (имя и число штук; у заклинания 1); <c>IMG_ITEM</c> — значок
/// <c>IconItem</c>/<c>IconSpel</c>, правая кнопка на нём — карточка (<c>0x4be4ef</c>, ранг 2). «ОК» (Enter, Esc) закрывает; следующее окно
/// того же сообщения открывает хозяин (<c>0x4d9861</c>). Пустой мешок — окно сообщения <c>X005TA0401</c> «Ничего не найдено»
/// (<c>0x4d964b</c>, <c>CMidgardMsgBox</c>) той же логикой на <c>DLG_MESSAGE_BOX</c>. <see cref="First"/> — первое окно сообщения: звук
/// <c>TAKEBAG</c> движок даёт один раз на сообщение (<c>0x4d95f5</c>, <c>0x4d9aa9</c>) — ставит сессия Г.
/// </summary>
public sealed class GiveItemLogic(WindowContext context, GivenEntry? entry, bool first,
                                  IReadOnlyDictionary<string, SpellText> spells) : IWindowLogic
{
    public const string ItemScreen = "DLG_ITEM";
    private const string Icon = "IMG_ITEM";
    private readonly UiRequests _requests = new();
    private UiRect _icon;

    /// <summary>Экран окна: <see cref="ItemScreen"/> или (пустой мешок) окно сообщения.</summary>
    public string Screen => entry is null ? MessageBoxLogic.Screen : ItemScreen;

    public GivenEntry? Entry => entry;

    public bool First => first;

    public UiRequest? TakeRequest() => _requests.Take();

    public string Summary => entry is null ? "получено=ничего"
        : $"получено={(entry.Spell ? "заклинание" : "предмет")} {entry.Id} шт={entry.Count} библиотека={(entry.Library ? 1 : 0)} первое={(first ? 1 : 0)}";

    public UiScreen Shape(UiScreen screen)
    {
        ArgumentNullException.ThrowIfNull(screen);
        _icon = screen.Elements.FirstOrDefault(e => e.Id == Icon)?.Rect ?? default;
        return screen;
    }

    public bool Allows(string element, UiScreenState ui) => element is not ("BTN_OK" or "BTN_YES" or "BTN_NO");

    public void Open(UiScreenState ui)
    {
        ArgumentNullException.ThrowIfNull(ui);
        if (entry is null)
        {
            ui.SetText("TXT_INFO", context.Text("X005TA0401"));
            ui.SetHidden("BTN_YES", true);
            ui.SetHidden("BTN_NO", true);
            return;
        }
        ui.SetText("TXT_TITLE", context.Text(!entry.Spell ? "X005TA0283" : entry.Library ? "X005TA0774" : "X005TA0773"));
        ui.SetText("TXT_ITEM", UiTemplate.Fill(context.Text("X005TA0516"), ("ITEM", Name), ("QTY", entry.Count)));
        ui.SetImage(Icon, entry.Spell ? $"IconSpel:{ArtNames.Of(entry.Id)}" : ItemCells.IconFrame(entry.Id, context));
    }

    /// <summary>Имя: предмет — <c>GItem.NAME_TXT</c>, заклинание — <c>Gspells.NAME_TXT</c>; нет строки — id.</summary>
    public string Name => entry is null ? ""
        : entry.Spell ? spells.TryGetValue(entry.Id, out var s) ? context.Text(s.NameTxt) : entry.Id
        : context.ItemTexts?.Items.TryGetValue(entry.Id, out var txt) == true ? context.Text(txt!) : entry.Id;

    public void Handle(UiEvent e, UiScreenState ui)
    {
        if (e is UiClicked { Element: "BTN_OK" or "BTN_YES" or "BTN_NO" }) _requests.Add(new UiClose());
    }

    /// <summary>Правая кнопка на значке — карточка предмета или заклинания, пока кнопка держится.</summary>
    public void RightPress(int x, int y, UiScreenState ui)
    {
        if (entry is null || x < _icon.X || x >= _icon.Right || y < _icon.Y || y >= _icon.Bottom) return;
        var card = entry.Spell ? new UiOpen(SpellCardLogic.Screen, new SpellCardLogic(context, entry.Id, spells, hideCasting: true))
                               : ItemCards.For(context, entry.Id);
        if (card is not null) _requests.Add(card);
    }
}
