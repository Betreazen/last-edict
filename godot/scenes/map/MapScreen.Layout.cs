using System;
using System.Collections.Generic;
using System.Linq;
using LastEdict.Data.Ui;
using Godot;

namespace LastEdict.Godot;

/// <summary>Экран карты на холсте формы окна (Э5, Р13; <c>ui/layout.json</c>): <c>DLG_ISO_VIEW</c> раскладывается якорями — место карты
/// <c>IMG_MAIN</c> во всё окно, места панелей <c>IMG_RESOURCE</c> и <c>IMG_PALETTE</c> у правого края. Подэкраны стоят в местах хозяина
/// (<see cref="UiScreenView.SlotAt"/>): окно сменило форму — каждый, кто стоял в месте, идёт за местом (экран поверх карты <c>DLG_ISO</c>
/// растягивается с ним, <see cref="UiScreenView.SlotSize"/>), окна и прочее — остаются по центру холста.</summary>
public partial class MapScreen
{
    /// <summary><c>DLG_ISO_VIEW</c> с политикой, до раскладки.</summary>
    private UiScreen _viewShaped = null!;

    /// <summary><c>DLG_ISO_VIEW</c>, разложенный на нынешнем холсте (<see cref="_host"/>).</summary>
    private UiScreen _layout = null!;

    /// <summary>Места хозяина, к которым привязаны подэкраны.</summary>
    private static readonly string[] HostSlots = ["IMG_MAIN", "IMG_RESOURCE", "IMG_PALETTE"];

    /// <summary>Разложить <c>DLG_ISO_VIEW</c> под окно (при открытии).</summary>
    private void LayoutView(UiScreen view)
    {
        _viewShaped = view;
        var size = GetViewport().GetVisibleRect().Size;
        var (w, h) = UiLayout.Canvas(view, size.X, size.Y);
        _layout = UiLayout.Place(view, w, h);
        _host = new Vector2(w, h);
    }

    /// <summary>Место хозяина на нынешнем холсте.</summary>
    private UiRect HostSlot(string id) => Slot(_layout, id);

    /// <summary>Окно сменило форму: новый холст, места и подэкраны в них. false — холст тот же.</summary>
    private bool Relayout()
    {
        var size = GetViewport().GetVisibleRect().Size;
        var (w, h) = UiLayout.Canvas(_viewShaped, size.X, size.Y);
        if (w == (int)_host.X && h == (int)_host.Y) return false;
        var old = HostSlots.ToDictionary(id => id, HostSlot);
        var oldHost = _host;
        var oldMain = new Vector2I(old["IMG_MAIN"].Width, old["IMG_MAIN"].Height);
        _layout = UiLayout.Place(_viewShaped, w, h);
        _host = new Vector2(w, h);
        var main = HostSlot("IMG_MAIN");
        _mapRect = new Rect2(main.X, main.Y, main.Width, main.Height);
        // Подэкран места стоит ровно в его углу (панели, поле дипломатии, полоса ресурсов); портрет и мини-карта — внутри места.
        Vector2 Moved(Vector2 at, bool inside, out string? slot, Vector2? size = null)
        {
            foreach (var (id, was) in old)
                if (inside ? at.X >= was.X && at.Y >= was.Y && at.X < was.Right && at.Y < was.Bottom : at.X == was.X && at.Y == was.Y)
                {
                    var now = HostSlot(id);
                    slot = id;
                    return at - new Vector2(was.X, was.Y) + new Vector2(now.X, now.Y);
                }
            slot = null;
            // Окно по центру холста — остаётся по центру, тем же округлением, что у открытого заново (MapScreen.Centered).
            var d = size ?? Vector2.Zero;
            return at + ((_host - d) / 2f).Floor() - ((oldHost - d) / 2f).Floor();
        }
        _faceSlot = new Rect2(Moved(_faceSlot.Position, inside: true, out _), _faceSlot.Size);
        _miniRect = new Rect2(Moved(_miniRect.Position, inside: true, out _), _miniRect.Size);
        foreach (var view in HostedViews())
        {
            if (view.SlotHost == oldHost) view.SlotHost = _host;
            if (view.SlotAt is not { } at) continue;
            var design = view.State.Screen.Design;
            view.SlotAt = Moved(at, inside: false, out _, new Vector2(design.Width, design.Height));
            // Холст подэкрана: весь экран карты или место карты — растут с ними (MapScreen.AddScreen).
            if (view.SlotSize is { } canvas)
                view.SlotSize = canvas == new Vector2I((int)oldHost.X, (int)oldHost.Y) ? new Vector2I(w, h)
                    : canvas == oldMain ? new Vector2I(main.Width, main.Height) : canvas;
            view.Fit();
        }
        return true;
    }

    /// <summary>Строка проверки раскладки: холст, место карты и вьюпорт карты, места панелей, мини-карта и портрет (в точках холста).</summary>
    private string LayoutLine()
    {
        static string R(UiRect r) => $"{r.X},{r.Y}:{r.Width}x{r.Height}";
        var viewport = _map.GetViewport() is SubViewport v ? $"{v.Size.X}x{v.Size.Y}" : "-";
        return $"layout холст={_host.X}x{_host.Y} карта={R(HostSlot("IMG_MAIN"))} вьюпорт={viewport} ресурсы={R(HostSlot("IMG_RESOURCE"))} " +
               $"панель={R(HostSlot("IMG_PALETTE"))} мини={_miniRect.Position.X},{_miniRect.Position.Y} портрет={_faceSlot.Position.X},{_faceSlot.Position.Y} " +
               $"панели={string.Join(";", HostedViews().Where(x => x.IsInsideTree() && x.SlotAt is not null).Select(x => $"{x.Name}@{x.SlotAt!.Value.X},{x.SlotAt.Value.Y}").Order())}";
    }

    /// <summary>Подэкраны карты (панели, окна, поле дипломатии…), кроме экранов внутри боя.</summary>
    private HashSet<UiScreenView> HostedViews()
    {
        // И снятые с дерева на время (панель команд под панелью столицы): вернутся — уже на новом месте.
        var found = new HashSet<UiScreenView>(_screens);
        if (_panelView is not null) found.Add(_panelView);
        if (_resourceView is not null) found.Add(_resourceView);
        var stack = new Stack<Node>(GetChildren());
        while (stack.Count > 0)
        {
            var node = stack.Pop();
            if (node is BattleScreen) continue;
            if (node is UiScreenView view) found.Add(view);
            foreach (var child in node.GetChildren()) stack.Push(child);
        }
        return found;
    }
}
