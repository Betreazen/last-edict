using LastEdict.Data.Play;
using LastEdict.Rules.Magic;
using LastEdict.Rules.Map;

namespace LastEdict.Data.Ui.Windows;

/// <summary>Картинка прицела на клетке карты: кадр <c>IsoCmon</c> в точке клетки.</summary>
public sealed record AimMark(int X, int Y, string Frame);

/// <summary>Что показать прицелом над клеткой: курсор (<c>IsoCursr</c>), квадрат области, лежит ли он над туманом, отметки целей.</summary>
public sealed record AimView(bool CanCast, string Cursor, IReadOnlyList<AimMark> Area, bool AreaOverFog, IReadOnlyList<AimMark> Targets);

/// <summary>
/// Прицел заклинанием на карте (<c>CTaskSpell</c> <c>0x4d2745</c>) — чистая часть, без сцены:
/// <list type="bullet">
/// <item>курсор (<c>getCursorAtPoint</c> <c>0x4d342f</c>): бросить можно (<see cref="GameSession.SpellRefusal"/> — null) — <c>SPELLVAL</c>,
/// нельзя — <c>SPELLINV</c>;</item>
/// <item>область (<c>onMouseMove</c> <c>0x4d2c8b</c>): у площади 1 и «всей карты» квадрата нет; туман (<c>L_FOG</c>, <c>0x4d2f58</c>) —
/// по клеткам квадрата <c>±(AREA &gt;&gt; 1)</c> в карте, целиком (у «Тумана Смерти» — 81 клетка; углы снимает только эффект, <c>0x5d4dfc</c>):
/// в обзоре чужих (<see cref="GameSession.ForeignSight"/>) — <c>SPLAREAFOGN</c>, прочие — <c>SPLAREAFOG</c>; остальные — одна картинка
/// <c>SPLAREA{сторона}</c> на клетке. Над туманом карты лежат только туман и снятие тумана (слой <c>SYM_SPELL_FOG</c>), прочее — под ним;</item>
/// <item>цели (<c>0x62078e</c>, <c>0x620a18</c> заполняет все четыре, <c>0x620975</c> рисует каждую — поправка 28.09, десятый круг: раньше
/// считалась только первая): клетки курсора и сдвигов (0,1), (1,0), (1,1), что на карте, открыты игроку и несут отряд вне укрепления
/// (невидимый — только свой), укрепление с отрядом (его угловая клетка), мешок, кристалл, жезл или гробницу; у призыва, тумана и снятия
/// тумана отметок нет. Кадр — <c>SELECT_NO</c>, если на эту клетку бросить можно, иначе <c>SELECT_YES</c> (имена у движка обратны смыслу,
/// <c>0x4d2c0f</c>).</item>
/// </list>
/// </summary>
public static class SpellAimView
{
    /// <summary>Площадь «вся карта».</summary>
    private const int WholeMap = 999;

    private static readonly (int Dx, int Dy)[] TargetCells = [(0, 0), (0, 1), (1, 0), (1, 1)];

    /// <param name="canCast">Своя проверка «бросить можно» (посох или свиток — <see cref="GameSession.ItemCastRefusal"/>); нет — заклинание
    /// (<see cref="GameSession.SpellRefusal"/>).</param>
    public static AimView At(GameSession session, string viewer, string spellId, int x, int y, Func<int, int, bool>? canCast = null)
    {
        ArgumentNullException.ThrowIfNull(session);
        var map = session.Map;
        Func<int, int, bool> can0 = canCast ?? ((cx, cy) => CanCast(session, viewer, spellId, cx, cy));
        bool can = map.Contains(x, y) && can0(x, y);
        string cursor = can ? "SPELLVAL" : "SPELLINV";
        if (session.Book?.Spells.TryGetValue(spellId, out var spell) != true || spell is null || !map.Contains(x, y))
            return new AimView(can, cursor, [], false, []);
        return new AimView(can, cursor, Area(session, viewer, spell, x, y), spell.Category is SpellCategory.Fog or SpellCategory.Unfog,
                           Target(session, viewer, spell, x, y, can0));
    }

    private static bool CanCast(GameSession session, string viewer, string spellId, int x, int y) =>
        session.Map.Contains(x, y) && session.SpellRefusal(viewer, spellId, x, y, useSpellBank: false) is null;

    private static IReadOnlyList<AimMark> Area(GameSession session, string viewer, Spell spell, int x, int y)
    {
        if (spell.Area <= 1 || spell.Area >= WholeMap) return [];
        if (spell.Category != SpellCategory.Fog) return [new AimMark(x, y, $"SPLAREA{spell.Area}")];
        // Квадрат ±(AREA >> 1) целиком, без снятых углов (0x4d2f58 → 0x4d32e0; эффект тумана углы снимает — 0x5d4dfc), в границах карты.
        int r = spell.Area / 2, size = session.Map.Size;
        var seen = session.ForeignSight(viewer, x, y, r);
        return [.. from cy in Enumerable.Range(y - r, 2 * r + 1)
                   from cx in Enumerable.Range(x - r, 2 * r + 1)
                   where cx >= 0 && cy >= 0 && cx < size && cy < size
                   select new AimMark(cx, cy, seen[cx, cy] ? "SPLAREAFOGN" : "SPLAREAFOG")];
    }

    private static IReadOnlyList<AimMark> Target(GameSession session, string viewer, Spell spell, int x, int y, Func<int, int, bool> canCast) =>
        spell.Category is SpellCategory.Summon or SpellCategory.Fog or SpellCategory.Unfog ? Array.Empty<AimMark>() : TargetMarks(session, viewer, x, y, canCast);

    /// <summary>Отметки целей над клеткой курсора (<c>0x62078e</c>, <c>0x620a18</c>, рисует <c>0x620975</c>) — общие у прицела заклинания и у
    /// режимов карты <c>CTaskStrat</c> (<c>0x4d410e</c>: обмен отрядами, постановка жезла): каждая клетка блока 2×2 от курсора, что на карте,
    /// открыта игроку и несёт то, что отмечается; кадр — <c>SELECT_NO</c>, если клетка годна (<paramref name="can"/>), иначе <c>SELECT_YES</c>.</summary>
    public static IReadOnlyList<AimMark> TargetMarks(GameSession session, string viewer, int x, int y, Func<int, int, bool> can)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(can);
        var map = session.Map;
        bool[,]? fog = map.Players.TryGetValue(viewer, out var player) && map.Fog.TryGetValue(player.FogId, out var f) ? f : null;
        var marks = new List<AimMark>();
        foreach (var (dx, dy) in TargetCells)
        {
            int cx = x + dx, cy = y + dy;
            if (!map.Contains(cx, cy) || (fog is not null && fog[cx, cy]) || !Selectable(session, viewer, cx, cy)) continue;
            marks.Add(new AimMark(cx, cy, can(cx, cy) ? "SELECT_NO" : "SELECT_YES"));
        }
        return marks;
    }

    /// <summary>Годность клетки у режимов карты по умолчанию (<c>CTaskStrat</c> <c>ICanSelect</c> <c>0x4d4116</c> → <c>0x4d412b</c>, ранг 1): в клетке
    /// свой отряд или крепость со своим отрядом — обмен и постановка жезла её не переопределяют.</summary>
    public static bool OwnStackAt(GameSession session, string viewer, int x, int y)
    {
        ArgumentNullException.ThrowIfNull(session);
        var map = session.Map;
        bool Own(string stackId) => map.Stacks.TryGetValue(stackId, out var s) && s.Owner.Equals(viewer, StringComparison.OrdinalIgnoreCase);
        return map.Stacks.Values.Any(s => s.X == x && s.Y == y && s.Inside.Length == 0 && s.Owner.Equals(viewer, StringComparison.OrdinalIgnoreCase))
               || map.Sites.Any(s => s.Kind is SiteKind.Capital or SiteKind.Village && s.X == x && s.Y == y && s.StackId.Length > 0 && Own(s.StackId));
    }

    /// <summary>Есть ли на клетке то, что прицел отмечает (<c>0x620ae0</c>).</summary>
    private static bool Selectable(GameSession session, string viewer, int x, int y)
    {
        var map = session.Map;
        var looks = session.Looks;
        if (map.Stacks.Values.Any(s => s.X == x && s.Y == y && s.Inside.Length == 0
                                       && (s.Owner.Equals(viewer, StringComparison.OrdinalIgnoreCase)
                                           || looks.Stacks.FirstOrDefault(l => l.Id.Equals(s.Id, StringComparison.OrdinalIgnoreCase)) is not { Invisible: true })))
            return true;
        if (map.Sites.Any(s => s.Kind is SiteKind.Capital or SiteKind.Village && s.StackId.Length > 0 && s.X == x && s.Y == y))
            return true;
        return looks.Bags.Any(b => b.X == x && b.Y == y && map.Sites.Any(s => s.Id.Equals(b.Id, StringComparison.OrdinalIgnoreCase) && !s.Gone))
               || looks.Crystals.Any(c => c.X == x && c.Y == y) || looks.Rods.Any(r => r.X == x && r.Y == y)
               || looks.Tombs.Any(t => t.X == x && t.Y == y);
    }
}
