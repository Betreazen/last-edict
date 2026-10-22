using System;
using System.Collections.Generic;
using System.Linq;
using D2.Data.Battle;
using D2.Data.Gfx;
using D2.Rules.Battle;
using Godot;

namespace D2.Godot;

/// <summary>Проверка интерфейса боя («--d1», замечания прогона 11.10, пункты 14, 16, 17, 24, 27, 30) — числами, без окна: подсказки подложек,
/// строки подписи крупного портрета, предметы только у ходящего лидера, выбор цели портретом панели, место окна сообщения и его слой,
/// слои места боя.</summary>
public partial class BattleScreen
{
    private async System.Threading.Tasks.Task CheckInterface()
    {
        while (!_session.Finished && !_session.WaitsPlayer) _session.AiStep();
        Refresh();
        for (int i = 0; i < 3; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        if (_session.Current is not { } mover) return;

        // 14: у картинок разметки подсказок нет (движок снимает их с диалога), у кнопок — есть.
        string Tip(string id)
        {
            var r = _panel.State.Screen.Elements.First(e => e.Id == id).Rect;
            return _panel.State.TooltipAt(r.X + (r.Width / 2), r.Y + (r.Height / 2))?.Text ?? "-";
        }
        string tips = string.Join(",", ((string[])["IMG_LITEMSBG", "IMG_RITEMSBG", "IMG_BIGFACESBG", "IMG_LUNITGROUPBG", "IMG_RUNITGROUPBG", "TOG_RIGHTUNITS"])
            .Select(id => $"{id}:{Tip(id)}"));

        // 16: подпись крупного портрета — строки своими начертаниями.
        string caption = _field.BigTextLines.FirstOrDefault() ?? "-";

        // 24: предметы — только у ходящего лидера: у другого юнита (лидера с зельем) при ходе не-лидера слоты пусты.
        var potion = D2.Data.Play.GameBook.Load(GameData.Rules).Items.Values.First(i => i.Category == D2.Rules.Magic.ItemCategory.PotionHeal);
        var moverType = mover.Type;
        var leader = _session.State.Units.Where(u => u.OnField && !ReferenceEquals(u, mover)).OrderBy(u => u.Attacker == mover.Attacker ? 0 : 1).FirstOrDefault();
        int foreign = -1;
        if (leader is not null)
        {
            var leaderType = leader.Type;
            mover.Type = moverType with { IsLeader = false };
            leader.Type = leaderType with { IsLeader = true };
            leader.BattleItems.Add(potion);
            Refresh();
            foreign = _field.ItemIcons + (ItemAt(Center(_scene.ItemSlot(mover.Attacker, 0))) is null ? 0 : 100);
            leader.BattleItems.Remove(potion);
            leader.Type = leaderType;
        }
        mover.Type = moverType with { IsLeader = true };
        mover.BattleItems.Add(potion);
        Refresh();
        int own = _field.ItemIcons + (ItemAt(Center(_scene.ItemSlot(mover.Attacker, 0))) is null ? 0 : 100);
        // Пометка 4 прогона 15.10: выпитое зелье уходит из слота сразу (0x649939 → 0x622fee), второе остаётся. Строка — значков до и после,
        // ловят ли мышь слоты 1 и 2 после.
        mover.BattleItems.Add(potion);
        Refresh();
        int before = _field.ItemIcons;
        mover.AppliedItems.Add(0);   // то, что делает питьё (BattleSim.DrinkPotion → MarkUsed)
        Refresh();
        string drunk = $"{before}>{_field.ItemIcons}:" + string.Concat(Enumerable.Range(0, 2)
            .Select(i => ItemAt(Center(_scene.ItemSlot(mover.Attacker, i))) is null ? "0" : "1"));
        mover.AppliedItems.Remove(0);
        mover.BattleItems.Remove(potion);
        mover.BattleItems.Remove(potion);
        mover.Type = moverType;
        int ai = ForeignLeaderItems(potion);

        // 17: цель — щелчком по портрету в панели: цель в основной панели; своя группа после TOG_RIGHTUNITS; своя — над SPOT.
        string Pick(BattleUnit? unit)
        {
            if (unit is null || !_scene.PanelShown(unit.Attacker)) return "-";
            var face = _scene.PanelFace(unit, unit.Type.SizeSmall ? 70 : BattlePanel.BigWidth);
            return $"{unit.Id}={UnitAt(Center(face))?.Id ?? "-"}";
        }
        Refresh();
        string target = Pick(_session.Targets().FirstOrDefault(t => _scene.PanelShown(t.Attacker)));
        bool main = _scene.MainShowsAttackers;
        _scene.MainShowsAttackers = mover.Attacker;
        Refresh();
        string self = Pick(mover);
        _scene.MainShowsAttackers = main;
        _scene.SpotShown = true;
        Refresh();
        string spot = Pick(mover);
        _scene.SpotShown = false;
        Refresh();
        // Обе панели сразу (ответ пользователя 11.10): без наведения на SPOT — группы по разные стороны, портрет своей ловится.
        string Side(bool attacker) => _scene.PanelLeft(attacker) switch { true => "L", false => "R", _ => "-" };
        string panels = $"{Side(true)}{Side(false)}";
        string second = Pick(_session.State.Units.FirstOrDefault(u => u.OnField && u.Attacker != _scene.MainShowsAttackers));

        // 30: окно сообщения — по середине экрана боя, на слое окон над портретами панелей.
        Message(ResolveAsk);
        for (int i = 0; i < 3; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        string window = _message is { } box
            ? $"{box.SlotAt?.X ?? -1},{box.SlotAt?.Y ?? -1} слой={(box.GetParent() as CanvasLayer)?.Layer ?? 0}" : "-";
        if (MapSource.Arg(OS.GetCmdlineUserArgs(), "--message-shot") is { } messageShot) GetViewport().GetTexture().GetImage().SavePng(messageShot);   // только с окном
        _message?.Step("click BTN_OK", out _);
        for (int i = 0; i < 3; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

        // 27: слои места боя — фон, тень, передний план.
        string place = string.Join(",", _scene.PlaceLayers("UNDEAD", 0).Concat(_scene.PlaceLayers("NEUTRAL", 0)).Select(s => $"{s.Name}:{s.Layer}"));

        string healer = HealerButtons();
        string wait = WaitToRoundEnd();

        GD.Print($"battle d1 подсказки={tips} подпись={caption.Replace(' ', '_')} имена={Names(_session.State)} предметы_не_лидер={foreign} предметы_лидер={own} выпито={drunk} " +
                 $"панель_цель={target} панель_свой={self} spot={spot} окно={window} закрыто={(_message is null ? 1 : 0)} место={place} " +
                 $"панели={panels} вторая={second} предметы_ИИ={ai} лекарь={healer} ждать={wait}");
    }

    /// <summary>Пункт 24 (ответ 11.10): лидер ИИ с зельем на своём ходу — значков предметов нет. Поле строится с ходящим юнитом чужой стороны
    /// (<see cref="BattleView.ActiveHuman"/> — по <see cref="BattleSession.IsPlayers"/>); число — значки плюс 100, если слот ловит мышь.</summary>
    private int ForeignLeaderItems(D2.Rules.Magic.Item potion)
    {
        if (_session.State.Units.FirstOrDefault(u => u.OnField && !_session.IsPlayers(u)) is not { } foe) return -1;
        var type = foe.Type;
        foe.Type = type with { IsLeader = true };
        foe.BattleItems.Add(potion);
        _field.Active = foe;
        _field.ActiveHuman = _session.IsPlayers(foe);
        _field.Build(_scene, _session.State, _place);
        _field.BuildPanels(_overlay, _scene, _session.State);
        int icons = _field.ItemIcons + (BattleScene.ShowsItems(foe, _field.ActiveHuman) ? 100 : 0);
        foe.BattleItems.Remove(potion);
        foe.Type = type;
        Refresh();
        return icons;
    }

    /// <summary>Пункт 18 (ответ 11.10): лекарь в ходу после победы — только лечение. Отдельный бой: боец добивает врага, у победителя раненый;
    /// строка — какие кнопки разрешены и не скрыты (1/0) и цели лечения.</summary>
    private static string HealerButtons()
    {
        var rules = D2.Data.Battle.RulesLoader.Load(GameData.Rules);
        BattleUnit Unit(string id, bool attacker, int slot, int hp, int hpMax, AttackType attack) => new()
        {
            Id = id, Attacker = attacker, Slot = slot, Hp = hp, HpMax = hpMax, Attack = attack,
            Type = new UnitType("g000uu0019", 1, hpMax, 0, attack.Id, "g000000000", false, true, 10, 100),
        };
        var fighter = Unit("F", true, 0, 50, 100, new AttackType("g000aa0019", 90, AttackClass.Damage, 100, 2, 100, 0, false, 1));
        var healer = Unit("H", true, 1, 60, 60, new AttackType("g000aa0011", 10, AttackClass.Heal, 100, 2, 0, 20, false, 1));
        var enemy = Unit("E", false, 0, 10, 10, new AttackType("g000aa0020", 50, AttackClass.Damage, 100, 2, 1, 0, false, 1));
        var state = new BattleState { Units = [fighter, healer, enemy], AttackerGroup = "A", DefenderGroup = "D" };
        var session = new D2.Data.Battle.BattleSession(state, new BattleSim(state, new D2.Rules.Rng.EngineRng(), rules.Constants), rules.Constants);
        for (int guard = 0; guard < 100 && session.Winner is null; guard++)
        {
            if (!session.WaitsPlayer) session.AiStep();
            else if (ReferenceEquals(session.Current, fighter)) session.Attack(enemy);
            else session.Defend();
        }
        if (!session.AfterBattle || !ReferenceEquals(session.Current, healer)) return "-";
        var buttons = new BattleButtons(() => session);
        bool hidden = session.Finished || session.AfterBattle;   // как PanelBackgrounds
        string Shown(string id) => $"{id}:{(buttons.Allows(id, null!) && !(hidden && id is "BTN_DEFEND" or "BTN_WAIT" or "BTN_RETREAT") ? 1 : 0)}";
        return string.Join(",", ((string[])["BTN_DEFEND", "BTN_WAIT", "BTN_RETREAT"]).Select(Shown))
               + $",лечит={string.Join("+", session.Targets().Select(t => t.Id))}";
    }

    /// <summary>Находка D2 (а): «Ждать» на ходу человека — юнит ходит ещё раз последним в этом же раунде. Строка: ждал ли, ходов после ожидания
    /// до конца раунда, последний ли из них ждавший (1/0), и можно ли ему на этом ходу ждать ещё раз — кнопка «Ждать» включена (1/0; пометка 16
    /// партии 15.10: у движка нельзя — бит 4 <c>waited</c>, <c>0x623ef6</c> → <c>0x622eaf</c>, снимает только новый раунд).</summary>
    private string WaitToRoundEnd()
    {
        while (!_session.Finished && !(_session.WaitsPlayer && _session.Allowed.Contains(BattleCommand.Wait))) _session.AiStep();
        if (_session.Current is not { } waiter) return "-";
        int round = _session.Round;
        if (!_session.Wait()) return "отказ";
        var after = new List<string>();
        int again = -1;
        while (StartedTurn() && !_session.Finished && _session.Round == round && _session.Current is { } next)
        {
            after.Add(next.Id);
            if (ReferenceEquals(next, waiter))
            {
                Refresh();
                again = _panel.Logic is BattleButtons buttons && buttons.Allows("BTN_WAIT", _panel.State) ? 1 : 0;
            }
            if (_session.WaitsPlayer) _session.Defend();
            else _session.AiStep();
        }
        Refresh();
        return $"{waiter.Id}:ходов={after.Count}:последний={(after.Count > 0 && after[^1] == waiter.Id ? 1 : 0)}:повтор={again}";
    }

    private static Vector2 Center(D2.Data.Ui.UiRect r) => new(r.X + (r.Width / 2f), r.Y + (r.Height / 2f));
}
