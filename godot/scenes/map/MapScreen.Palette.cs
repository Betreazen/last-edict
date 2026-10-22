using System.Linq;
using D2.Data.Map;
using D2.Data.Ui;
using Godot;

namespace D2.Godot;

/// <summary>Палитра карты (<see cref="MapPalette"/>): полоса ресурсов по <c>TOG_RESPOPUP</c> (в начале партии — видна), смена мини-карты и
/// владений <c>DLG_ISO_LAND</c> по <c>BTN_RES_PAL</c>, знамёна отрядов по <c>TOG_BANNERS</c> (сохраняется в настройках порта) или пока зажат Shift. Кнопки панели,
/// заходящие на окно мини-карты, мини-карта не перехватывает (<see cref="MiniMapView.Covered"/>).</summary>
public partial class MapScreen
{
    private readonly MapPalette _palette = new();
    private MapPaletteLogic? _paletteLogic;
    private bool _screensVisible = true;

    private MapPaletteLogic PaletteLogic
    {
        get
        {
            if (_paletteLogic is not null) return _paletteLogic;
            _paletteLogic = new MapPaletteLogic(_palette, () => _map.Session.Book is { } book ? LandShare.Of(_map.Map, book.Economy) : [], Text);
            _paletteLogic.Changed += OnPaletteChanged;
            return _paletteLogic;
        }
    }

    /// <summary>Панель палитры сейчас: мини-карта или владения (под панелью постройки столицы её нет в списке).</summary>
    private UiScreenView? PaletteView => _screens.FirstOrDefault(v => v.Hosted is MapPalette.Pal or MapPalette.LandScreen);

    private void OnPaletteChanged()
    {
        ApplyResources();
        UpdateBanners();
        SaveBanners();
        if (PaletteView is { } view && view.Hosted != _palette.Screen) Callable.From(SwapPalette).CallDeferred();
    }

    /// <summary>Полоса ресурсов видна, только если включена и экраны карты показаны.</summary>
    private void ApplyResources()
    {
        if (_resourceView is not null) _resourceView.Visible = _screensVisible && _palette.Resources;
    }

    /// <summary>Знамёна: переключатель или зажатый Shift, пока карта принимает ввод (<c>0x4909e4</c>).</summary>
    private void UpdateBanners()
    {
        if (_map is null) return;
        bool shift = Input.IsKeyPressed(Key.Shift) && _map.PointerEnabled;
        bool want = MapPalette.Banners || shift;
        if (_map.BannersShown != want) _map.BannersShown = want;
    }

    /// <summary>Сменить панель палитры (<c>0x48905a</c> → пересборка <c>0x4885c1</c>): новый экран на месте прежнего, мини-карта — только
    /// при <c>DLG_ISO_PAL</c>; корень стека окон — новый экран (переходы его кнопок — из графа экранов).</summary>
    private void SwapPalette()
    {
        if (PaletteView is not { } old || old.Hosted == _palette.Screen) return;
        int at = _screens.IndexOf(old), child = old.GetIndex();
        var view = new UiScreenView
        {
            Hosted = _palette.Screen, Name = _palette.Screen, SlotAt = old.SlotAt, SlotHost = _host, Logic = PaletteLogic,
        };
        RemoveChild(old);
        old.QueueFree();
        _screens[at] = view;
        AddChild(view);
        MoveChild(view, child);
        view.Visible = _screensVisible;
        view.SetProcessUnhandledInput(old.IsProcessingUnhandledInput());
        view.Fired += events => OnWindowEvents(view, events);
        if (_dialogs is null || _dialogs.Stack.Count <= 1)
            _dialogs = new UiNavigator(GameData.Content.Flow, _palette.Screen, UiPhase.Game);
        if (_mini is not null) _mini.Visible = _screensVisible && !_palette.Land;
        CoverPalette();
        GD.Print($"palette {_palette.Screen} {PaletteLine()}");
    }

    /// <summary>Кнопка панели палитры над точкой окна мини-карты (координаты окна <c>IMG_PALMAP</c>).</summary>
    private bool PaletteCovers(Vector2 local)
    {
        if (PaletteView is not { Hosted: MapPalette.Pal } view) return false;
        var window = view.State.Screen.Elements.FirstOrDefault(e => e.Id == "IMG_PALMAP")?.Rect;
        if (window is not { } r) return false;
        return view.State.ElementAt(r.X + (int)local.X, r.Y + (int)local.Y) is not null;
    }

    /// <summary>Строка проверки палитры: экран, полоса ресурсов, знамёна (узлов видно / всего), мини-карта.</summary>
    private string PaletteLine()
    {
        var (all, shown) = _map.BannerCount();
        return $"палитра={PaletteView?.Hosted ?? "-"} ресурсы={(_resourceView is { Visible: true } ? 1 : 0)} " +
               $"знамёна={shown}/{all} мини={(_mini is { Visible: true } ? 1 : 0)}";
    }
}
