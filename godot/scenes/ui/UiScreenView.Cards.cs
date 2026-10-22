using System;
using System.Collections.Generic;
using System.Linq;
using D2.Data.Ui;
using Godot;

namespace D2.Godot;

/// <summary>Карточки списков картинок (<see cref="UiScreenState.SetCards"/>): на каждую видимую ячейку списка — подложка
/// выбранного пункта, кадр и подпись. Ячейки — из разметки списка (<see cref="UiList.CellWidth"/>, зазоры); что в них
/// рисовать, решает логика экрана.</summary>
public partial class UiScreenView
{
    private sealed record CardSlot(UiRect Cell, ReferenceRect Mark, Sprite2D Image, Label Text, Sprite2D Overlay);

    private readonly Dictionary<string, List<CardSlot>> _cardSlots = new(StringComparer.Ordinal);

    /// <summary>Выбранная ячейка с текстом — контур в 1 точку по краю ячейки, чёрный (<c>CImage2Outline</c> <c>0x53a25f</c>, отрисовка
    /// ячейки <c>0x5c9d65</c>/<c>0x5c9fa4</c>); у ячеек без текста (сумки) рамки нет. Цвет списка задан логикой
    /// (<see cref="UiScreenState.MarkColor"/>, окна кражи — жёлтый) — рамка у выбранной всегда. ponytail: кадр выбора
    /// <c>UiList.Selection</c> (только <c>DLG_TRADE_ITEM</c>).</summary>
    private static readonly Color CardMark = Colors.Black;

    private void BuildCards(Node2D root, UiScreen screen)
    {
        Vector2I origin = Origin(screen);
        foreach (var e in screen.Elements.Where(e => e.Kind == UiKind.List && e.List is { CellWidth: > 0, CellHeight: > 0 }))
        {
            var spec = e.List!;
            int stepX = spec.CellWidth + spec.GapX, stepY = spec.CellHeight + spec.GapY;
            int columns = Math.Max(1, (e.Rect.Width + spec.GapX) / stepX);
            int rows = Math.Max(1, (e.Rect.Height + spec.GapY) / stepY);
            var slots = new List<CardSlot>();
            for (int r = 0; r < rows; r++)
                for (int c = 0; c < columns; c++)
                {
                    var cell = new UiRect(e.Rect.X + (c * stepX), e.Rect.Y + (r * stepY), spec.CellWidth, spec.CellHeight);
                    var at = new Vector2(cell.X - origin.X, cell.Y - origin.Y);
                    var mark = new ReferenceRect
                    {
                        Name = $"{e.Id}_mark{slots.Count}", Position = at, Size = new Vector2(cell.Width, cell.Height),
                        BorderColor = CardMark, BorderWidth = 1, EditorOnly = false, Visible = false,
                        MouseFilter = Control.MouseFilterEnum.Ignore,
                    };
                    var image = new Sprite2D { Name = $"{e.Id}_image{slots.Count}", Centered = false, Visible = false };
                    var text = NewLabel($"{e.Id}_text{slots.Count}", cell, origin);
                    text.Visible = false;
                    var overlay = new Sprite2D { Name = $"{e.Id}_over{slots.Count}", Centered = false, Visible = false };
                    root.AddChild(image);
                    root.AddChild(overlay);
                    root.AddChild(text);
                    root.AddChild(mark);
                    slots.Add(new CardSlot(cell, mark, image, text, overlay));
                }
            _cardSlots[e.Id] = slots;
        }
    }

    private void RefreshCards()
    {
        _imageless.Clear();
        foreach (var (id, slots) in _cardSlots)
        {
            var cards = _state.Cards(id);
            int top = _state.ListTop(id), chosen = _state.ListSelected(id);
            var markColor = _state.MarkColor(id);
            Vector2I origin = Origin(_state.Screen);
            for (int i = 0; i < slots.Count; i++)
            {
                var slot = slots[i];
                int index = top + i;
                var card = index < cards.Count ? cards[index] : null;
                slot.Mark.Visible = index == chosen && (markColor is not null ? card is not null : card is { Text.Length: > 0 });
                slot.Mark.BorderColor = markColor is { } m ? Color.Color8(m.R, m.G, m.B) : CardMark;
                slot.Image.Visible = false;
                slot.Text.Visible = false;
                slot.Overlay.Visible = false;
                if (card is null) continue;
                if (card.Overlay is { Length: > 0 } over && FrameImage(over, out _) is { } overImage)
                {
                    slot.Overlay.Texture = Texture(overImage);
                    slot.Overlay.Position = new Vector2(slot.Cell.X + card.ImageAt.X - origin.X, slot.Cell.Y + card.ImageAt.Y - origin.Y);
                    // Наложение — по смешению своей картинки: отметка изученного заклинания ICNSY001 ложится сложением (opacity 300),
                    // копией её чёрный фон закрывал значок (замечание 23 прогона 11.10).
                    GfxBlending.Apply(slot.Overlay, UiFrameRef.BlendOf(over, GameData.Content.Gfx));
                }
                if (card.Image is { Length: > 0 } frame && FrameImage(frame, out _) is { } image)
                {
                    slot.Image.Texture = Texture(image);
                    slot.Image.SetMeta(FrameMeta, frame);
                    slot.Image.Position = new Vector2(slot.Cell.X + card.ImageAt.X - origin.X, slot.Cell.Y + card.ImageAt.Y - origin.Y);
                    slot.Image.Modulate = card.Dimmed ? new Color(127f / 255f, 127f / 255f, 127f / 255f) : Colors.White;
                    slot.Image.Visible = true;
                }
                else if (card.Image is { Length: > 0 } lost) _imageless.Add($"{id}:{lost}");
                if (card.Text is { Length: > 0 } markup)
                {
                    _boxes[slot.Text] = new Rect2(slot.Cell.X + card.TextAt.X - origin.X, slot.Cell.Y + card.TextAt.Y - origin.Y,
                                                  card.TextAt.Width, card.TextAt.Height);
                    Apply(slot.Text, UiTextStyle.Parse(markup));
                    slot.Text.Visible = true;
                }
            }
        }
    }

    private readonly List<string> _imageless = [];

    /// <summary>Карточки, чей кадр не нашёлся (видна одна подпись — «призрак» счётчика в ячейке), «список:кадр» — для строки проверки окна.</summary>
    public IReadOnlyList<string> ImagelessCards => _imageless;

    /// <summary>Сколько видимых наложений карточек ложится сложением (отметки изученных заклинаний) — для строки проверки окна.</summary>
    public int AdditiveOverlays => _cardSlots.Values.SelectMany(slots => slots)
        .Count(s => s.Overlay.Visible && s.Overlay.Material is CanvasItemMaterial { BlendMode: CanvasItemMaterial.BlendModeEnum.Add });

    /// <summary>Первая видимая карточка списка для строки проверки: «кадр@x,y текст@x,y:ШxВ=начало_текста» — места в координатах разметки,
    /// по узлам; карточек нет — «-».</summary>
    public string FirstCard(string id)
    {
        if (!_cardSlots.TryGetValue(id, out var slots) || slots.FirstOrDefault(s => s.Image.Visible || s.Text.Visible) is not { } slot) return "-";
        Vector2I origin = Origin(_state.Screen);
        string image = slot.Image.Visible
            ? $"{slot.Image.GetMeta(FrameMeta, "?")}@{(int)slot.Image.Position.X + origin.X},{(int)slot.Image.Position.Y + origin.Y}" : "-";
        string text = slot.Text.Visible && _boxes.TryGetValue(slot.Text, out var box)
            ? $"{(int)box.Position.X + origin.X},{(int)box.Position.Y + origin.Y}:{(int)box.Size.X}x{(int)box.Size.Y}="
              + new string([.. slot.Text.Text.Where(c => !char.IsWhiteSpace(c)).Take(24)])
            : "-";
        return $"{image} текст@{text}";
    }

    /// <summary>Сколько карточек видно сейчас (для строки проверки окна).</summary>
    public int CardsShown(string id) =>
        _cardSlots.TryGetValue(id, out var slots) ? slots.Count(s => s.Image.Visible || s.Text.Visible) : 0;
}
