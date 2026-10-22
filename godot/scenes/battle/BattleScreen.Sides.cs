using System.Collections.Generic;
using System.Linq;
using D2.Data.Battle;
using D2.Rules.Battle;
using Godot;

namespace D2.Godot;

/// <summary>Проверка «--sides» (пометка 1 партии 14.10): стороны панелей отрядов и чьи крупные портреты слева и справа — на ходу своего
/// бойца, своего лекаря и врага, числами, без окна. Каждый ход человека — три строки: начало хода, мышь над первой целью, миг удара;
/// ход врага — миг его удара (показ зовёт <see cref="Numbers"/> с ходящим на поле, как <see cref="NextStep"/>).</summary>
public partial class BattleScreen
{
    private async System.Threading.Tasks.Task CheckSides()
    {
        var lines = new List<string>();
        var done = new HashSet<string>();
        for (int guard = 0; guard < 400 && !_session.Finished && done.Count < 3; guard++)
        {
            _session.StartTurn();   // начало хода, отложенное сессией до показа (BattleSession.HoldTurnStart)
            Refresh();   // поле с ходящим — как шаг показа (NextStep) и начало хода человека
            if (_session.Current is not { } mover) break;
            // Начало боя, мыши ещё не было: второй крупный портрет пуст (CBatBigFace 0x64ec10, setUnit 0x64f149 — Б41).
            if (guard == 0) lines.Add(SidesLine("начало", mover));
            if (!_session.WaitsPlayer)
            {
                var (records, numbers) = AiStep();
                numbers(null);
                RefreshPanels();   // миг удара: с окном Numbers пересобирает панели сам
                if (records.Count > 0 && done.Add("враг")) lines.Add(SidesLine("враг", mover));
                continue;
            }
            string kind = Attacks.TargetsAllies(mover.Attack.Class) ? "лекарь" : "боец";
            if (done.Contains(kind) || _session.Targets().FirstOrDefault() is not { } target)
            {
                _session.Defend();
                continue;
            }
            lines.Add(SidesLine(kind + "_ход", mover));
            Pointer(Center(target.Type.SizeSmall ? _scene.Slot(target.Attacker, target.Slot) : _scene.BigSlot(target.Attacker, target.Slot)));
            lines.Add(SidesLine(kind + "_мышь", mover));
            var before = StrikeNotes.Before(_session.State, mover);
            int allies = _session.Sim.AllyLog.Count;
            var struck = _session.Attack(target);
            Numbers(struck, before, [.. _session.Sim.AllyLog.Skip(allies)], []);
            RefreshPanels();
            lines.Add(SidesLine(kind + "_удар", mover));
            done.Add(kind);
        }
        Pointer(new Vector2(400, 20));
        for (int i = 0; i < 3; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        GD.Print($"battle sides {string.Join(" ", lines)}");
    }

    /// <summary>«что:ходящий свои=L|R враг=L|R лица=L=id,R=id»; ходящий — тот, чьё действие показано.</summary>
    private string SidesLine(string what, BattleUnit mover)
    {
        string Side(bool attacker) => _scene.PanelLeft(attacker) switch { true => "L", false => "R", _ => "-" };
        return $"{what}:{mover.Id}:{(_session.IsPlayers(mover) ? "свой" : "чужой")} свои={Side(PlayerIsAttacker)} враг={Side(!PlayerIsAttacker)} " +
               $"лица={_field.BigFaces.Replace(' ', ',')}";
    }
}
