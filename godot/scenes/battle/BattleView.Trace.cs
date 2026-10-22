using System;
using System.Collections.Generic;
using LastEdict.Data.Gfx;
using LastEdict.Rules.Battle;
using Godot;

namespace LastEdict.Godot;

/// <summary>Строки зонда порта о портретах панели (схема 7.6, <c>docs/probe-av.md</c> §8) — те же, что пишет зонд оригинала
/// (<c>tools/d2probe/mss32/src/probeflash.cpp</c>). Зовут их точки вида портретов (<see cref="Flash"/>, <see cref="FixFace"/>, конец —
/// <c>TickFlashes</c>/<c>DropKeptFlashes</c>). Без журнала зонда — ничего.</summary>
public partial class BattleView
{
    /// <summary>Вспышки на виду — для строки <c>bat_flash_end</c>: состояние и миг начала.</summary>
    private readonly Dictionary<string, (int State, ulong Shown)> _tracedFlashes = new(StringComparer.OrdinalIgnoreCase);

    /// <summary><c>bat_flash</c>: портрет юнита <paramref name="unit"/> сменил состояние <paramref name="state"/> (номер состояния движка,
    /// <see cref="LastEdict.Data.Diagnostics.PortraitFlash"/>). <paramref name="mode"/> — <c>flash</c> (на срок надписи, тиков — как у неё:
    /// <c>+0x44</c> группы, 15 при скорости 1) или <c>set</c> (стойко); <paramref name="hold"/> — остаётся после конца вспышки;
    /// <paramref name="src"/> — откуда (<c>target</c>, <c>actor</c>, <c>heal</c>, <c>xp</c>, <c>xp_up</c>), как у движка;
    /// <paramref name="scene"/> null — поставленное до конца боя (видно в любой группе).</summary>
    public void TraceFlash(BattleScene? scene, BattleUnit unit, int state, bool hold, string mode = "flash", string src = "target")
    {
        ArgumentNullException.ThrowIfNull(unit);
        if (PortProbe.Av is not { } av) return;
        bool on = scene is null || (unit.Attacker == scene.MainShowsAttackers && scene.PanelShown(unit.Attacker));
        int ticks = mode == "flash" ? (int)Math.Round(NoteSeconds * 1000 / FrameMs) - 1 : -1;
        av.BattleFlash(unit.Id, state, mode, src, hold, on, !unit.Type.SizeSmall, ticks);
        if (mode == "flash") _tracedFlashes[unit.Id] = (state, Time.GetTicksMsec());
    }

    /// <summary><c>bat_flash_end</c>: вспышка юнита кончилась — <paramref name="how"/> <c>end</c> (срок), <c>cut</c> (новая поверх),
    /// <c>gone</c> (юнит ушёл из панели); <paramref name="after"/> — состояние портрета после неё (null — держит вспышку).</summary>
    public void TraceFlashEnd(string unitId, string how, int? after)
    {
        ArgumentNullException.ThrowIfNull(unitId);
        if (!_tracedFlashes.Remove(unitId, out var flash)) return;
        PortProbe.Av?.BattleFlashEnd(unitId, flash.State, how, (long)(Time.GetTicksMsec() - flash.Shown), after);
    }
}
