using LastEdict.Data.Gfx;
using LastEdict.Data.Ui.Windows;
using LastEdict.Rules.Map;

namespace LastEdict.Data.Map;

/// <summary>Значок поверх портрета панели: кадр <c>Battle.ff</c> и место от левого верхнего угла портрета; <see cref="Centered"/> —
/// по середине ширины портрета (<c>X</c> тогда не смотрится).</summary>
public readonly record struct PanelFaceIcon(string Name, int X, int Y, bool Centered = false);

/// <summary>
/// Портрет лидера выбранного отряда в панели команд <c>DLG_STRATEGIC</c> (<c>IMG_LEADER</c>; панель отряда <c>0x48d8f3</c>, разбор 17.10):
/// <list type="bullet">
/// <item>картинка — <c>CFaceImgImpl</c> от <c>0x5b1e6a</c> (<c>{база}FACES</c>, нет — <c>FACE</c>), <b>отражена</b>: <c>setLeftSide(0)</c>
/// (<c>0x48da79</c>, слот <c>+0x30</c>; так же гость панели столицы <c>0x486a6a</c>). Снимок оригинала (ScreenShot009, Nigel G000UU0019):
/// средняя разница с отражённым портретом 4,9 на канал;</item>
/// <item>лидер жив (<c>0x5e98ad</c>: здоровье &gt; 0) — доля здоровья <c>hp·100/max</c> (<c>0x48da91…0x48daae</c>, max — <c>hitPoints</c>
/// солдата <c>0x40a3f9</c>, с модификаторами) и состояние 0: нижняя потерянная доля смешана с красным (<c>0x5b1af4</c>); павший —
/// состояние 10 (<c>0x48dac5</c>): маска-череп <c>MASKDEAD{S|L}</c> поверх всего лица, <c>S</c> — у картинки шириной 70 (<c>0x5b16f9</c>,
/// <c>[0x6ebedc]</c> = 70), красной доли нет. Текст <c>TXT_LEADER_STATS</c> у павшего тот же («Скорость: 0 / 20» — так и у движка);</item>
/// <item>значки поверх (составная картинка <c>0x5b4f54</c>, слой <c>0x5b507d(картинка, x, y)</c>; лицо — по центру, −999): по цепочке
/// модификаторов лидера (<c>0x5ea500</c>) усиление заклинанием — кадр 13 <c>FIISOSPELLBOOST</c> в (0, 0), ослабление — 14
/// <c>FIISOSPELLLOWER</c> в (0, 56), зелье-усиление — 15 <c>FIPOTIONBOOST</c> в (0, 28); высокий уровень (<c>0x61ea4c</c>) — кадр
/// 23…25 <c>FIHIGHLEVEL1..3</c> в (середина ширины − половина значка, 0). Жив ли лидер, для значков движок не смотрит.</item>
/// </list>
/// Карточка отряда по ПКМ картину лидера (<c>0x57c0a9</c>) по смерти не меняет — павшего видно по ячейке (<see cref="MapCards"/>), значок
/// на карте — <c>ICNDEADL</c> (<see cref="MapScene"/>).
/// </summary>
/// <remarks>Равенство записи по списку <see cref="Icons"/> — ссылочное; сравнивать портреты — по <see cref="Key"/>.</remarks>
public sealed record PanelFace(string Face, bool Dead, string? Mask, int Percent, IReadOnlyList<PanelFaceIcon> Icons)
{
    /// <summary>Портрет панели отражён всегда (<c>setLeftSide(0)</c>).</summary>
    public const bool Mirrored = true;

    /// <summary>Ширина малого портрета: маска <c>MASKDEADS</c> — у неё, иначе <c>MASKDEADL</c>.</summary>
    public const int SmallWidth = 70;

    /// <summary>Места значков (<c>0x48db23…0x48db8d</c>).</summary>
    public const int BoostY = 0, PotionY = 28, LowerY = 56;

    /// <summary>Ключ картинки: одинаковый — та же картинка (кэш текстуры в сцене).</summary>
    public string Key => $"{Face}|{Mask}|{Percent}|{string.Join(",", Icons.Select(i => i.Name))}";

    /// <summary>Строка проверки: «отражено=1 павший=1 маска=MASKDEADS доля=100 значки=FIISOSPELLBOOST,…».</summary>
    public string Line => $"отражено={(Mirrored ? 1 : 0)} павший={(Dead ? 1 : 0)} маска={Mask ?? "-"} доля={Percent} " +
                          $"значки={(Icons.Count == 0 ? "-" : string.Join(",", Icons.Select(i => i.Name)))}";

    /// <summary>Портрет лидера <paramref name="leader"/> картинкой <paramref name="face"/> шириной <paramref name="faceWidth"/>.</summary>
    public static PanelFace Of(WindowContext c, MapUnit leader, string face, int faceWidth)
    {
        ArgumentNullException.ThrowIfNull(c);
        ArgumentNullException.ThrowIfNull(leader);
        ArgumentNullException.ThrowIfNull(face);
        bool dead = leader.Hp <= 0;
        int max = c.Session.HpMax(leader);
        int percent = dead || max <= 0 ? 100 : Math.Clamp(leader.Hp * 100 / max, 0, 100);
        string? mask = dead ? faceWidth == SmallWidth ? "MASKDEADS" : "MASKDEADL" : null;
        return new PanelFace(face, dead, mask, percent, IconsOf(c, leader));
    }

    /// <summary>Значки по порядку слоёв движка: усиление, ослабление, зелье, высокий уровень.</summary>
    public static IReadOnlyList<PanelFaceIcon> IconsOf(WindowContext c, MapUnit leader)
    {
        ArgumentNullException.ThrowIfNull(c);
        ArgumentNullException.ThrowIfNull(leader);
        var icons = new List<PanelFaceIcon>();
        if (c.Session.Book is { } book)
        {
            var marks = BattleMarkSets.Of(book);
            if (leader.Modifiers.Any(marks.SpellBoost.Contains)) icons.Add(new("FIISOSPELLBOOST", 0, BoostY));
            if (leader.Modifiers.Any(marks.SpellLower.Contains)) icons.Add(new("FIISOSPELLLOWER", 0, LowerY));
            if (leader.Modifiers.Any(marks.Potion.Contains)) icons.Add(new("FIPOTIONBOOST", 0, PotionY));
        }
        int high = c.Session.Book is null ? 0 : UnitCellsView.HighLevel(c, leader);
        if (high > 0) icons.Add(new($"FIHIGHLEVEL{high}", 0, 0, Centered: true));
        return icons;
    }

    /// <summary>Сколько верхних строк портрета высотой <paramref name="height"/> остаются без красного (высота·доля/100, как <see cref="BattlePanel.KeptRows"/>).</summary>
    public int KeptRows(int height) => height * Percent / 100;
}
