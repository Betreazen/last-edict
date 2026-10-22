using System.Collections.Generic;
using System.Linq;
using D2.Data.Map;
using Godot;

namespace D2.Godot;

/// <summary>Карточка объекта карты по правой кнопке (<see cref="MapCards"/>, <c>docs/ai-screen.md</c> §10): пока кнопка нажата над
/// объектом — окно <c>DLG_R_C_*</c> по середине экрана, отпущена — окна нет (как у движка при <c>StickyEncy</c> = 0). Не во время хода
/// ИИ, боя, окон, прицела и мешка.</summary>
public partial class MapScreen
{
    private UiScreenView? _card;
    private IReadOnlyDictionary<string, string>? _landmarkNames;

    /// <summary>Подписка на правую кнопку вида карты (из <see cref="_Ready"/>).</summary>
    private void WireCards() => _map.RightButton += cell =>
    {
        CloseCard();
        if (cell is { } c) OpenCard(c);
    };

    /// <summary>Открыть карточку объекта в клетке; нечего показывать — false.</summary>
    private bool OpenCard((int X, int Y) cell)
    {
        if (_map.AiRunning || Aiming || _dropStack is not null || _dialogViews.Count > 0 || _overlay is not null || _battle is not null
            || Windows is not { } context)
            return false;
        _landmarkNames ??= MapCards.LandmarkNames(GameData.Rules);
        var card = MapCards.At(context, _raceNames ??= D2.Data.Ui.Windows.RaceNames.Load(GameData.Rules), _landmarkNames,
                               cell.X, cell.Y, (x, y) => _map.Scene.Hidden(x, y));
        if (card is null) return false;
        _card = new UiScreenView { Hosted = card.Screen, Name = "Card", SlotHost = _host, Logic = card.Logic };
        if (Centered(card.Screen) is { } at) _card.SlotAt = at;
        AddChild(_card);
        _card.SetProcessUnhandledInput(false);
        GD.Print($"card {cell.X},{cell.Y} окно={card.Screen} объект={card.ObjectId}");
        return true;
    }

    /// <summary>Правая кнопка отпущена где угодно (над панелью, вне карты) — карточка закрывается: событие вида карты приходит только
    /// над картой.</summary>
    public override void _Input(InputEvent e)
    {
        if (_card is not null && e is InputEventMouseButton { ButtonIndex: MouseButton.Right, Pressed: false }) CloseCard();
    }

    private void CloseCard()
    {
        if (_card is not { } card) return;
        _card = null;
        RemoveChild(card);
        card.QueueFree();
    }

    /// <summary>Строка проверки открытой карточки: окно и первые знаки текста.</summary>
    private string CardLine()
    {
        if (_card is not { } card) return "карточка=-";
        // У окна отряда нет TXT_INFO, у прочих — TXT_NAME: берётся тот, что есть в разметке (TextOf бросает на чужом элементе).
        string id = card.State.Screen.Elements.Any(e => e.Id == "TXT_INFO") ? "TXT_INFO" : "TXT_NAME";
        string text = card.State.TextOf(id) ?? "";
        text = text.Replace('\n', ' ').Replace(' ', '_');
        string leader = card.State.Screen.Elements.Any(e => e.Id == "IMG_LEADER") ? card.State.Frame("IMG_LEADER") ?? "-" : "-";
        string missing = card.MissingFrames.Count > 0 ? string.Join(",", card.MissingFrames) : "-";
        // Портреты групп в рамке (кадр из слоёв «рамка&портрет», MapCards.Framed): число, рамки и размеры собранных картинок.
        var framed = card.State.Screen.Elements.Where(e => e.Kind == D2.Data.Ui.UiKind.Image).Select(e => card.State.Frame(e.Id))
                         .OfType<string>().Select(f => (Frame: f, Layers: D2.Data.Ui.UiFrameRef.Layers(f))).Where(p => p.Layers is not null).ToList();
        int portraits = card.State.Screen.Elements.Count(e => e.Kind == D2.Data.Ui.UiKind.Image && card.State.Frame(e.Id) is { } f
                                                              && f.Contains("Faces:", System.StringComparison.Ordinal));
        string borders = framed.Count == 0 ? "-"
            : string.Join(",", framed.GroupBy(p => $"{p.Layers!.Value.Under.Split(':')[^1]}:{card.FrameSize(p.Frame)}").Select(g => $"{g.Key}×{g.Count()}"));
        return $"карточка={card.Hosted} текст={(text.Length > 60 ? text[..60] : text)} лидер={leader} нет_кадров={missing} " +
               $"портреты={portraits} в_рамке={framed.Count} рамки={borders}";
    }
}
