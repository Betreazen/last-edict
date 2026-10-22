using System;
using System.Collections.Generic;
using System.Linq;
using LastEdict.Data.Gfx;
using Godot;

namespace LastEdict.Godot;

/// <summary>Проверка вспышек портретов (<c>--check --flash</c>, <c>docs/battle-screen.md</c> §9): числами, без окна.</summary>
public partial class BattleScreen
{
    /// <summary>Вспышки, которые проверка ставит по очереди на один портрет основной панели.</summary>
    private static readonly BattleFace[] CheckedFaces =
    [
        BattleFace.Hit, BattleFace.Missed, BattleFace.Shielded, BattleFace.Healed, BattleFace.Boosted, BattleFace.LowerDamage,
        BattleFace.Poisoned, BattleFace.Shattered, BattleFace.Experience, BattleFace.Pass, BattleFace.Transformed,
    ];

    /// <summary>Каждая вспышка — точка середины портрета до и во время; через срок надписи — назад к базе; «Понижение уровня» на малом
    /// портрете — строк и сколько шире места; держащаяся вспышка (превращение) снимается сборкой панелей.</summary>
    private async System.Threading.Tasks.Task CheckFlashes()
    {
        var unit = _session.State.Units.FirstOrDefault(u => u.OnField && u.Type.SizeSmall && _scene.PanelShown(u.Attacker));
        if (unit is null)
        {
            GD.Print("battle flash юнита нет");
            return;
        }
        _scene.MainShowsAttackers = unit.Attacker;
        RefreshPanels();
        await Frames(2);
        string plain = _field.FaceCenters.GetValueOrDefault(unit.Id, "-");
        var line = new System.Text.StringBuilder($"battle flash {unit.Id} база={_field.FaceOf(unit)}@{plain}");
        foreach (var face in CheckedFaces)
        {
            _field.Flash(_scene, unit, new BattleFlash(face, face == BattleFace.Transformed));
            line.Append(FormattableString.Invariant($" {face}={_field.FaceCenters.GetValueOrDefault(unit.Id, "-")}"));
        }
        // Срок: обычная вспышка уходит сама, держащаяся (последняя — превращение) — только сборкой панелей.
        _field.Flash(_scene, unit, new BattleFlash(BattleFace.Healed, false));
        var started = DateTime.UtcNow;
        while (_field.FaceOf(unit) == BattleFace.Healed && (DateTime.UtcNow - started).TotalSeconds < 5) await Frames(1);
        double gone = (DateTime.UtcNow - started).TotalSeconds;
        line.Append(FormattableString.Invariant($" срок={gone:0.0}с после={_field.FaceOf(unit)}@{_field.FaceCenters.GetValueOrDefault(unit.Id, "-")}"));
        _field.Flash(_scene, unit, new BattleFlash(BattleFace.Transformed, true));
        started = DateTime.UtcNow;
        while ((DateTime.UtcNow - started).TotalSeconds < _field.NoteSeconds + 0.3) await Frames(1);
        string kept = _field.FaceOf(unit).ToString();
        RefreshPanels();
        line.Append(System.Globalization.CultureInfo.InvariantCulture, $" держится={kept} после_сборки={_field.FaceOf(unit)}");
        // Длинная надпись на малом портрете: перенос по словам в ширину 70.
        _field.NoteWraps.Clear();
        _field.Note(_scene, unit, Text("X008TA0018"));
        line.Append(System.Globalization.CultureInfo.InvariantCulture, $" понижение_строк={string.Join(",", _field.NoteWraps)}");
        GD.Print(line.ToString());
    }

    private async System.Threading.Tasks.Task Frames(int count)
    {
        for (int i = 0; i < count; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
    }
}
