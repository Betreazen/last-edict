using System;
using System.Collections.Generic;
using System.Linq;
using D2.Data.Gfx;
using D2.Rules.Battle;
using Godot;

namespace D2.Godot;

/// <summary>Вспышки на портретах панели боя (<c>docs/battle-screen.md</c> §9): состояние портрета (<see cref="BattleFace"/>) — база по
/// состоянию юнита, поставленное до конца боя (опыт) или вспышка на время надписи.</summary>
public partial class BattleView
{
    /// <summary>Вспышки по юниту и их возраст, с: держится <see cref="NoteSeconds"/> (тот же счётчик, что у надписи: <c>+0x24</c> записи группы
    /// = <c>+0x44</c> группы, <c>0x649ddb</c>), потом портрет — снова база; «держащаяся» (<see cref="BattleFlash.Keep"/>) снимается первой сборкой
    /// панелей после своего срока — у движка её снимает пересборка конца шага (<c>0x637514</c> → <c>0x638183</c>).</summary>
    private readonly Dictionary<string, (BattleFlash Flash, double Age)> _flashes = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Поставленное состояние (<c>+0x40</c> записи группы, <c>0x64d2c3</c>) — вместо базы при каждой сборке (<c>0x64bc1b…0x64bc4e</c>):
    /// опыт в конце боя.</summary>
    private readonly Dictionary<string, BattleFace> _fixedFaces = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Спрайты портретов панели последней сборки — вспышка перекрашивает портрет сразу, без пересборки панелей.</summary>
    private readonly Dictionary<string, (Sprite2D Sprite, BattleSprite Portrait, BattleUnit Unit)> _faceSprites = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Точка цвета в середине портрета последней перекраски по юниту («R;G;B») — для проверок.</summary>
    public Dictionary<string, string> FaceCenters { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Вспышки, начатые за бой: «юнит:состояние» по порядку — для проверок.</summary>
    public List<string> FlashLog { get; } = [];

    /// <summary>Вспышки на виду за бой: юнит, состояние, источник и миг (мс часов движка Godot) — для проверок порядка показа.</summary>
    public List<(string Unit, BattleFace Face, string Src, ulong Ms)> FlashTimes { get; } = [];

    /// <summary>Состояние портрета юнита сейчас: вспышка, иначе поставленное, иначе база (<see cref="BattleFaces.Of"/>).</summary>
    public BattleFace FaceOf(BattleUnit unit)
    {
        ArgumentNullException.ThrowIfNull(unit);
        if (_flashes.TryGetValue(unit.Id, out var flash)) return flash.Flash.Face;
        return _fixedFaces.TryGetValue(unit.Id, out var face) ? face : BattleFaces.Of(unit);
    }

    /// <summary>
    /// Вспышка на портрете юнита (<c>CBatUnitGroup2</c> <c>0x64cee8</c>) — единственное место, где она начинается: удар, атака по своим,
    /// вампиризм, тик, начало хода, «Пас»/«Ждать»/«Защита», опыт. Как и надпись (<see cref="Note"/>), только в основной панели
    /// (<paramref name="anyPanel"/> — в любой: опыт в конце боя кладётся на все четыре группы, <c>0x636f06…0x636fb1</c>). Повторная вспышка
    /// того же юнита начинает отсчёт заново.
    /// </summary>
    /// <param name="src">Источник для строки зонда (как возврат <c>0x64cee8</c> у движка): <c>target</c> — итог удара по цели (<c>0x63bfad</c>),
    /// <c>actor</c> — надпись ходящего (<c>0x6371ab</c>: тики, «Пас»/«Ждать»/«Защита», откаты и изгнание).</param>
    public void Flash(BattleScene scene, BattleUnit unit, BattleFlash flash, bool anyPanel = false, string src = "target")
    {
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(unit);
        // Журнал зонда (схема 7.6, bat_flash): как у движка — и когда группы юнита не видно; прежняя вспышка — «cut».
        if (_flashes.ContainsKey(unit.Id)) TraceFlashEnd(unit.Id, "cut", null);
        TraceFlash(scene, unit, (int)flash.Face, flash.Keep, src: anyPanel ? "xp_up" : src);
        if (!anyPanel && (unit.Attacker != scene.MainShowsAttackers || !scene.PanelShown(unit.Attacker))) return;
        _flashes[unit.Id] = (flash, 0);
        FlashLog.Add($"{unit.Id}:{flash.Face}");
        FlashTimes.Add((unit.Id, flash.Face, src, Time.GetTicksMsec()));
        Repaint(unit.Id);
    }

    /// <summary>Портрет юнита в панели отражён: группа на правой стороне поля — зеркально, отступающий — наоборот: mirror = !group[+0x39]
    /// («нападающий» XOR f — группа слева), статус Retreat/Retreated меняет (0x64baeb…0x64bb7f; ранг 1, снимки 28.09 f0/f0t/f1). Одно правило
    /// для панели и полосы очереди ходов.</summary>
    public static bool PanelMirrored(BattleScene scene, BattleUnit unit)
    {
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(unit);
        return unit.Retreating ^ !scene.Layout.LeftSide(unit.Attacker);
    }

    /// <summary>Отражён ли портрет юнита в панели последней сборки; портрета в панели нет — null (для проверок).</summary>
    public bool? PanelFlipped(string unitId) =>
        _faceSprites.TryGetValue(unitId, out var shown) && IsInstanceValid(shown.Sprite) ? shown.Sprite.FlipH : null;

    /// <summary>Портрет юнита без подкраски (кадр <c>{базовый}FACE</c>, как у панели) — для полосы очереди ходов; нет в графике — null.</summary>
    public Texture2D? PlainFace(BattleUnit unit) =>
        _scene?.Portrait(unit) is { } portrait && Frames(portrait) is { Length: > 0 } frames ? frames[0] : null;

    /// <summary>Поставить состояние до конца боя (<c>0x64d2c3</c>): опыт — светло-жёлтым у получивших, без подкраски у выросших.</summary>
    public void FixFace(BattleUnit unit, BattleFace face)
    {
        ArgumentNullException.ThrowIfNull(unit);
        _fixedFaces[unit.Id] = face;
        TraceFlash(null, unit, (int)face, hold: true, mode: "set", src: face == BattleFace.Experience ? "xp" : "xp_up");   // bat_flash mode set
        Repaint(unit.Id);
    }

    /// <summary>Тик вспышек (66 мс, как у надписей): по сроку — назад к поставленному или базе.</summary>
    private void TickFlashes()
    {
        if (_flashes.Count == 0) return;
        foreach (var (id, (flash, age)) in _flashes.ToList())
        {
            double next = age + (FrameMs / 1000);
            if (next < NoteSeconds || flash.Keep)
            {
                _flashes[id] = (flash, next);
                continue;
            }
            _flashes.Remove(id);
            TraceFlashEnd(id, "end", _fixedFaces.TryGetValue(id, out var fixedFace) ? (int)fixedFace
                : _faceSprites.TryGetValue(id, out var shown) ? (int)BattleFaces.Of(shown.Unit) : null);
            Repaint(id);
        }
    }

    /// <summary>Сборка панелей: держащиеся вспышки после своего срока сняты (пересборка конца шага).</summary>
    private void DropKeptFlashes()
    {
        if (_flashes.Count == 0) return;
        foreach (var (id, (flash, age)) in _flashes.ToList())
            if (flash.Keep && age >= NoteSeconds && _flashes.Remove(id)) TraceFlashEnd(id, "end", null);
    }

    /// <summary>Перекрасить портрет юнита в панели последней сборки по его состоянию сейчас.</summary>
    private void Repaint(string unitId)
    {
        if (!_faceSprites.TryGetValue(unitId, out var shown) || !IsInstanceValid(shown.Sprite)) return;
        if (PanelFace(shown.Portrait, shown.Unit) is { } texture) shown.Sprite.Texture = texture;
    }
}
