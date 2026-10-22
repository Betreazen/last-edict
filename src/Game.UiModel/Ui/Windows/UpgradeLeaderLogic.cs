using D2.Data.Content;
using D2.Data.Play;
using D2.Rules.Units;
using D2.Data.Play.Commands;

namespace D2.Data.Ui.Windows;

/// <summary>Имя и описание навыка лидера (<c>GleaUpg.NAME_TXT</c>, <c>DESC_TXT</c>) по <c>MODIF_ID</c>: у одного модификатора у всех
/// лордов одни и те же строки (27 модификаторов, расхождений 0), поэтому ключ — только модификатор.</summary>
public sealed record SkillTexts(IReadOnlyDictionary<string, (string Name, string Desc)> Skills)
{
    public static readonly SkillTexts Empty = new(new Dictionary<string, (string, string)>());

    public static SkillTexts Load(string rulesDir) => Load(ContentSource.Open(rulesDir));

    public static SkillTexts Load(ContentSet c)
    {
        ArgumentNullException.ThrowIfNull(c);
        return new(c.LeaderSkills.GroupBy(s => c.KeyOf(s.Modifier), StringComparer.OrdinalIgnoreCase)
                                 .ToDictionary(g => g.Key, g => (g.First().NameText ?? "", g.First().DescriptionText ?? ""),
                                               StringComparer.OrdinalIgnoreCase));
    }
}

/// <summary>
/// Очередь окон выбора навыка в начале хода человека (<c>0x5edbf7</c>, её собирает фаза начала хода <c>0x4d9f71</c> и по одному
/// окну открывает <c>0x4da212</c>): отряды игрока в порядке объектов карты, каждый столько раз подряд, сколько у него отложенных
/// выборов (<c>MidStack.UPGCOUNT</c>, <c>+0x9a</c>). Лидер-вор (<c>IUsNoble</c>) и лидер чужой расы окна не получают.
/// </summary>
public static class UpgradeLeaderQueue
{
    public static IReadOnlyList<string> For(GameSession session, string playerId)
    {
        ArgumentNullException.ThrowIfNull(session);
        var book = session.RequireBook();
        var map = session.Map;
        if (!map.Players.TryGetValue(playerId, out var player)) return [];
        var queue = new List<string>();
        foreach (var stack in map.Stacks.Values)
        {
            if (stack.UpgradeCount <= 0 || !stack.Owner.Equals(playerId, StringComparison.OrdinalIgnoreCase)
                || !map.Units.TryGetValue(stack.LeaderId, out var leader)
                || book.CategoryOf(leader.TypeId) == D2.Rules.Battle.UnitCategory.Noble
                || !book.RaceOf(leader.TypeId).Equals(player.RaceId, StringComparison.OrdinalIgnoreCase)) continue;
            for (int i = 0; i < stack.UpgradeCount; i++) queue.Add(stack.Id);
        }
        return queue;
    }

    /// <summary>Окно выбора для отряда очереди. Очередь движка предложение не проверяет (<c>0x5edbf7</c>): пустой список — окно с пустым
    /// списком, «ОК» закрывает без выбора. Нет — только когда отряда или живого лидера уже нет.</summary>
    public static UiOpen? Window(WindowContext context, string stackId, SkillTexts texts)
    {
        ArgumentNullException.ThrowIfNull(context);
        var map = context.Map;
        return map.Stacks.TryGetValue(stackId, out var stack) && stack.LeaderAlive && map.Units.ContainsKey(stack.LeaderId)
            ? new UiOpen(UpgradeLeaderLogic.Screen, new UpgradeLeaderLogic(context, stackId, texts))
            : null;
    }

    /// <summary>Лидерство отряда не больше 6 (<c>0x5a3983</c>).</summary>
    public const int MaxLeadership = 6;

    /// <summary>
    /// Список окна (<c>0x4a6fea</c>): предложение <see cref="GameSession.SkillOffer"/> (<c>0x5d8b84</c>, общее с ИИ) и фильтры только окна
    /// человека (ИИ, <c>0x44f16b</c>, и проверка выбора, <c>0x60ea56</c>, их не применяют). Цикл <c>0x4a70a4</c>: навык со способностью
    /// лидера (<c>L_LEADER_ABILITY</c>), которая у лидера уже есть по всей цепочке — врождённые, навыки и надетое
    /// (<c>CUmStack::canApplyToUnit</c> <c>0x5a364c</c>), — прочь; навык лидерства (&gt; 0) при лидерстве 6 — прочь (<c>0x5a3688</c>, лидерство
    /// с цепочкой, зажато в 1…6; отложенных выборов окна у порта нет — выбор сразу). <c>0x4a732f</c>: из навыков лидерства — только
    /// первый по таблице. <c>0x4a7417</c>: навык движения по местности (<c>L_MOVE_ABILITY</c>, в ванили — Forestwalk) — прочь, если тип
    /// лидера без модификаторов ходит по равнине, лесу и воде (<c>0x61e2fb</c>, <c>GMAbi</c> {0, 1, 3}).
    /// </summary>
    public static IReadOnlyList<LeaderUpgrade> Offer(GameSession session, string stackId)
    {
        ArgumentNullException.ThrowIfNull(session);
        var offer = session.SkillOffer(stackId);
        if (offer.Count == 0 || !session.Map.Stacks.TryGetValue(stackId, out var stack)) return offer;
        var modifiers = session.RequireBook().Rules.Modifiers ?? new Dictionary<string, Modifier>();
        var abilities = session.LeaderAbilitiesOf(stack);
        bool fullLeadership = Math.Clamp(session.Leadership(stack), 1, MaxLeadership) >= MaxLeadership;
        bool flies = session.MoveOptionsOf(stack) is { PlainsBonus: true, ForestBonus: true, WaterBonus: true };
        bool leadershipTaken = false;
        var list = new List<LeaderUpgrade>();
        foreach (var upgrade in offer)
        {
            var elements = modifiers.TryGetValue(upgrade.ModifId, out var m) ? m.Elements : [];
            if (elements.Any(e => e.Type == ModifierElementType.LeaderAbility && e.Ability is int a && abilities.Contains(a))) continue;
            bool leadership = elements.Any(e => e.Type == ModifierElementType.Leadership);
            if (fullLeadership && elements.Any(e => e.Type == ModifierElementType.Leadership && e.Number > 0)) continue;
            if (leadership && leadershipTaken) continue;
            if (flies && elements.Any(e => e.Type == ModifierElementType.MoveAbility)) continue;
            leadershipTaken |= leadership;
            list.Add(upgrade);
        }
        return list;
    }
}

/// <summary>
/// Выбор навыка лидера за уровень (<c>DLG_UPGRADE_LEADER</c>, <c>CUpgradeLdrInterf</c>, конструктор <c>0x4a6bfd</c>): портрет лидера
/// отражён (<c>IMG_LEADER</c>, <c>0x4a6d4f</c>), правая кнопка на нём — карточка лидера (<c>0x4a7939</c>, <c>WM_RBUTTONDOWN</c>);
/// заголовок <c>TXT_INSTRUCT</c> — <c>X005TA0476</c> с именем лидера (<c>%NAME%</c>, <c>0x4a6dca</c>); список <c>TLBOX_BONUS_LIST</c> —
/// имена навыков предложения (<see cref="UpgradeLeaderQueue.Offer"/>, <c>0x4a6fea</c> → <c>0x5d8b84</c>; строка — <c>GleaUpg.NAME_TXT</c>,
/// <c>0x4a78dd</c>), выбран первый; <c>TXT_INFO</c> — описание выбранного (<c>DESC_TXT</c>, <c>0x4a77c6</c>). «ОК» (<c>0x4a778b</c>) —
/// выбор навыка (<see cref="GameSession.ChooseSkill"/>) и окно закрывается; следующее окно очереди открывает хозяин.
/// </summary>
public sealed class UpgradeLeaderLogic(WindowContext context, string stackId, SkillTexts texts) : IWindowLogic
{
    public const string Screen = "DLG_UPGRADE_LEADER";
    private const string List = "TLBOX_BONUS_LIST", Leader = "IMG_LEADER";

    private readonly UiRequests _requests = new();
    private IReadOnlyList<LeaderUpgrade> _offer = [];
    private UiRect _leaderRect;
    private int _selected;

    /// <summary>Выбранный навык (<c>MODIF_ID</c>) после «ОК»; до — null.</summary>
    public string? Chosen { get; private set; }

    /// <summary>Отказ ядра при выборе (для проверок); нет — <see cref="SkillRefusal.None"/>.</summary>
    public SkillRefusal Refusal { get; private set; }

    public string StackId => stackId;

    /// <summary>Навыки списка в порядке окна.</summary>
    public IReadOnlyList<LeaderUpgrade> Offer => _offer;

    public UiRequest? TakeRequest() => _requests.Take();

    public string Summary =>
        $"навык отряд={stackId} навыков={_offer.Count} выбран={(_selected < _offer.Count ? _offer[_selected].ModifId : "-")} взят={Chosen ?? "-"}";

    public UiScreen Shape(UiScreen screen)
    {
        ArgumentNullException.ThrowIfNull(screen);
        _leaderRect = screen.Elements.FirstOrDefault(e => e.Id == Leader)?.Rect ?? default;
        return screen;
    }

    // «ОК» закрывает окно сам (UiClose), а не переходом графа: у BTN_OK в графе переходы в DLG_EVENT_POPUP и в себя.
    public bool Allows(string element, UiScreenState ui) => element is not ("BTN_OK" or List);

    public void Open(UiScreenState ui)
    {
        ArgumentNullException.ThrowIfNull(ui);
        _offer = UpgradeLeaderQueue.Offer(context.Session, stackId);
        var map = context.Map;
        if (map.Stacks.TryGetValue(stackId, out var stack) && map.Units.TryGetValue(stack.LeaderId, out var leader))
        {
            var look = context.Session.Looks.Stacks.FirstOrDefault(s => s.Id.Equals(stackId, StringComparison.OrdinalIgnoreCase));
            string name = look is { LeaderName.Length: > 0 } ? look.LeaderName : context.UnitName(leader.TypeId);
            ui.SetImage(Leader, UiFrameRef.Mirrored(context.Faces.Small(leader.TypeId)));
            ui.SetText("TXT_INSTRUCT", UiTemplate.Fill(context.Text("X005TA0476"), ("NAME", name)));
        }
        ui.SetItems(List, [.. _offer.Select(u => SkillText(u.ModifId, desc: false))]);
        _selected = 0;
        if (_offer.Count > 0) ui.Select(List, 0);
        Describe(ui);
    }

    private string SkillText(string modifId, bool desc) =>
        texts.Skills.TryGetValue(modifId, out var t) ? context.Text(desc ? t.Desc : t.Name) : desc ? "" : modifId;

    private void Describe(UiScreenState ui) =>
        ui.SetText("TXT_INFO", _selected < _offer.Count ? SkillText(_offer[_selected].ModifId, desc: true) : "");

    public void Handle(UiEvent e, UiScreenState ui)
    {
        ArgumentNullException.ThrowIfNull(ui);
        switch (e)
        {
            case UiListSelected { Element: List } s:
                _selected = s.Index;
                Describe(ui);
                break;
            case UiListConfirmed { Element: List } c:
                _selected = c.Index;
                Choose();
                break;
            case UiClicked { Element: "BTN_OK" }:
                Choose();
                break;
        }
    }

    /// <summary>Правая кнопка на портрете — карточка лидера, пока кнопка держится.</summary>
    public void RightPress(int x, int y, UiScreenState ui)
    {
        var r = _leaderRect;
        if (x < r.X || x >= r.Right || y < r.Y || y >= r.Bottom || !context.Map.Stacks.TryGetValue(stackId, out var stack)
            || !context.Map.Units.ContainsKey(stack.LeaderId)) return;
        _requests.Add(new UiOpen(UnitCardLogic.Screen, new UnitCardLogic(context, stack.LeaderId)));
    }

    /// <summary>«ОК» (и подтверждение списка — щелчок «ОК» даёт оба события, берётся первое): навык выбранной строки; пустой список —
    /// окно просто закрывается, выбор остаётся отложенным (<c>0x4a7739</c> даёт пустой id, <c>0x4da212</c> его не пишет).</summary>
    private void Choose()
    {
        if (Chosen is not null || _requests.Count > 0) return;
        if (_selected < _offer.Count)
        {
            string modif = _offer[_selected].ModifId;
            Refusal = context.Session.Run<SkillRefusal>(new ChooseSkillCommand(stackId, modif));
            if (Refusal == SkillRefusal.None) Chosen = modif;
        }
        _requests.Add(new UiClose());
    }
}
