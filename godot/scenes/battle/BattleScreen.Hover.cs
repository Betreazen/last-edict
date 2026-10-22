using System.Collections.Generic;
using System.Linq;
using D2.Data.Battle;
using D2.Data.Gfx;
using D2.Rules.Battle;
using Godot;

namespace D2.Godot;

/// <summary>Проверка «--hover» (слова пользователя 15.10): зона мыши юнита на поле и анимация метки под ним — числами, без окна.
/// Зона — место и 32 px под ним (<see cref="BattleScene.HitSlot"/>, <c>0x6326b9</c>): строка «battle hoverzone» — кто ловится у точек
/// под ногами, у ног, в теле и над головой. Анимация — строка «battle hoveranim»: мышь ходит внутри зоны одного юнита, кадр метки растёт
/// без сброса (<c>0x633012</c>), переход на другого — его метка с первого кадра, мимо всех — меток нет.</summary>
public partial class BattleScreen
{
    private void CheckHover()
    {
        while (!_session.Finished && !_session.WaitsPlayer) _session.AiStep();
        Refresh();
        bool side = _session.Current?.Attacker ?? true;
        var alive = _session.State.Units.Where(u => u.OnField && u.Alive).ToList();
        if (alive.FirstOrDefault(u => u.Attacker != side && u.Type.SizeSmall) is not { } target
            || alive.FirstOrDefault(u => !ReferenceEquals(u, target)) is not { } other)
        {
            GD.Print("battle hoverzone нет юнитов");
            return;
        }
        var slot = _scene.Slot(target.Attacker, target.Slot);
        var hit = _scene.HitSlot(target.Attacker, target.Slot);
        int cx = slot.X + (slot.Width / 2);
        string At(int dy) => UnitAt(new Vector2(cx, slot.Bottom + dy))?.Id ?? "-";
        GD.Print($"battle hoverzone юнит={target.Id} место={slot.X},{slot.Y},{slot.Width},{slot.Height} зона={hit.X},{hit.Y},{hit.Width},{hit.Height} " +
                 $"под_ногами+16={At(16)} край+31={At(31)} за_краем+32={At(32)} у_ног-4={At(-4)} тело-60={At(-60)} голова-130={At(-130)}");

        // Мышь ходит внутри зоны цели (по метке под ногами и выше), кадр анимации — шагом экрана (66 мс).
        var frames = new List<int>();
        Pointer(new Vector2(cx, slot.Bottom + 16));
        for (int i = 0; i < 20; i++)
        {
            Pointer(new Vector2(cx + (i % 5) - 2, slot.Bottom + 16 - (i * 3)));
            _field._Process(0.066);
            frames.Add(_field.HoverFrame);
        }
        int length = _scene.Mark(target, "MRK", small: true) is { } mark && GameData.Content.Gfx.Anim(mark.Archive, mark.Name) is { } anim ? anim.Frames.Count : 0;
        int resets = frames.Zip(frames.Skip(1)).Count(p => length > 0 && p.Second != (p.First + 1) % length);
        var otherSlot = other.Type.SizeSmall ? _scene.Slot(other.Attacker, other.Slot) : _scene.BigSlot(other.Attacker, other.Slot);
        Pointer(new Vector2(otherSlot.X + (otherSlot.Width / 2), otherSlot.Bottom + 8));
        string switched = $"{(_field.HoverMarks > 0 ? UnitAt(new Vector2(otherSlot.X + (otherSlot.Width / 2), otherSlot.Bottom + 8))?.Id ?? "-" : "-")}:{_field.HoverFrame}";
        Pointer(new Vector2(-10, -10));
        GD.Print($"battle hoveranim кадров={length} кадры={string.Join(",", frames)} сбросов={resets} другой={switched} мимо_меток={_field.HoverMarks}");
        OverlapLine(target);
    }

    /// <summary>Полоса перекрытия зон мест 2 и 3 левой группы (позиции 1 и 2): кто ловится, если юниты боя идут в списке позицией 2
    /// раньше позиции 1 (движок обходит отсортированный список, <see cref="BattleScene.FieldUnitAt"/>). В бою-образце двух юнитов на этих
    /// местах нет — берутся двое пробных того же типа.</summary>
    private void OverlapLine(BattleUnit like)
    {
        bool left = _scene.Layout.AttackersLeft;
        BattleUnit At(int slot) => new()
        {
            Id = $"поз{slot}", Attacker = left, Slot = slot, HpMax = like.HpMax, Hp = like.Hp, Type = like.Type, Attack = like.Attack,
        };
        var a = _scene.HitSlot(left, 1);
        var b = _scene.HitSlot(left, 2);
        int x = System.Math.Max(a.X, b.X) + 5, y = System.Math.Max(a.Y, b.Y) + 5;
        string caught = _scene.FieldUnitAt([At(2), At(1)], x, y)?.Id ?? "-";
        GD.Print($"battle hoveroverlap точка={x},{y} полоса_y={System.Math.Max(a.Y, b.Y)}-{System.Math.Min(a.Bottom, b.Bottom)} ловится={caught}");
    }
}
