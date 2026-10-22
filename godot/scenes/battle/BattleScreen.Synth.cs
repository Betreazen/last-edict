using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using D2.Data.Battle;
using D2.Data.Gfx;
using D2.Rules.Battle;
using Godot;

namespace D2.Godot;

/// <summary>Проверка «--synth A1|A1o|A2|A3|A4|A5|I2» на полигонах карты <c>port-synth-1-battle</c> (<c>--scenario port-synth-1-battle --stacks …</c>):
/// ходы человека по памятке через те же методы, что щелчок мыши, и числа видимого — партии пользователя 15.10 (пометки 2–12) и 15.10 вечер
/// (вампиризм, обереги Лесной Девы, предметы при тащении, порядок показа тиков).</summary>
public partial class BattleScreen
{
    private async Task CheckSynth(string zone)
    {
        async Task Settle()
        {
            for (int i = 0; i < 3; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            while (_playing) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        }
        // Ход человека до юнита, которого выбирает pick; остальные свои защищаются, чужие ходят ИИ.
        async Task<bool> Until(Func<BattleUnit, bool> pick)
        {
            for (int i = 0; i < 400 && !_session.Finished; i++)
            {
                while (!_session.Finished && !_session.WaitsPlayer) _session.AiStep();
                if (_session.Current is { } c && pick(c)) { Refresh(); await Settle(); return true; }
                if (!_session.Finished) _session.Defend();
            }
            return false;
        }
        BattleUnit? Of(string type) => _session.State.Units.FirstOrDefault(u => u.Attacker && u.Type.Id.Equals(type, StringComparison.OrdinalIgnoreCase));
        Refresh();
        await Settle();
        var dead = _session.State.Units.Where(u => !u.Alive && !u.Retreated).Select(u => u.Id).ToList();
        GD.Print($"battle synth {zone} start павших={dead.Count} костей_на_поле={_field.CorpsesShown} " +
                 $"в_панели_павших={_field.PanelUnits.Count(p => p.EndsWith(":павший", StringComparison.Ordinal))} значков_на_поле={_field.FieldIcons}");
        switch (zone)
        {
            case "A1": await SynthCure(Until, Of); break;
            case "A1o": await SynthOrder(); break;
            case "A2":
                await SynthSummonRevive(Until, Of, Settle);
                await SynthDrain(Until);
                break;
            case "A3": await SynthDoppelWolf(Until, Of); break;
            case "A4": await SynthDryad(Until, Of); break;
            case "A5": await SynthFlag(Settle); break;
            case "I2": await SynthItems(Until, Settle); break;
            case "B1": await SynthBigSummon(Until, Settle); break;
            case "F": await SynthFaces(); break;
            case "K": await SynthButtons(); break;
        }
    }

    /// <summary>K — бой полигона с показом (<c>D2_BATTLE_PLAYBACK=1</c>) кнопками панели, для зонда порта (<c>D2_PORT_PROBE=1</c>, строки
    /// <c>ui_enable</c>/<c>ui_click</c>, Зс55): ход человека — через 120 мс после начала (кнопки зажглись), щелчком по панели: где можно ждать — через ход
    /// «Ждать» и «Защищаться», иначе удар по первому врагу (нет целей — «Защищаться»); переключатель групп — раз в начале. Печать — сколько
    /// щелчков прошло и сколько раз кнопки гасли и зажигались по кадрам.</summary>
    private async Task SynthButtons()
    {
        _synthHp = _session.State.Units.ToDictionary(u => u.Id, u => u.Hp, StringComparer.OrdinalIgnoreCase);   // конец боя — как у F
        var begun = DateTime.UtcNow;
        bool Late() => (DateTime.UtcNow - begun).TotalMinutes > 5;
        async Task Frames(int n)
        {
            for (int i = 0; i < n; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        }
        // Кнопки зажигаются кадром (SyncButtons в _Process); щелчок — не раньше чем через 120 мс, как у человека: смены ближе 50 мс сверка
        // сворачивает в дрожание (AvJournal.EnableSettleMs), а смены за 100 мс после щелчка относит к нему (EnableJoinMs).
        async Task Pause()
        {
            var at = DateTime.UtcNow;
            while ((DateTime.UtcNow - at).TotalMilliseconds < 120) await Frames(1);
        }
        int clicks = 0, refused = 0, strikes = 0, silent = 0, dimmed = 0, lit = 0, turns = 0;
        bool toggled = false, wasOn = _panel.State.IsEnabled(BattlePanelButtons.Defend);
        for (int guard = 0; guard < 400 && !_session.Finished && !Late(); guard++)
        {
            while (_playing && !Late())
            {
                await Frames(1);
                Count();
            }
            if (_session.Finished) break;
            if (!_session.WaitsPlayer || _session.Current is not { } unit)
            {
                Continue();
                await Frames(1);
                Count();
                continue;
            }
            await Pause();
            Count();
            turns++;
            if (!toggled)
            {
                toggled = _panel.Step($"click {BattlePanelButtons.Groups}", out _);
                await Pause();
            }
            // «Защищаться» — только когда можно и ждать: так нажимал пользователь (37 из 37 щелчков в 20261020-faces гасят все четыре); без
            // «Ждать» щелчок гасил бы три — набор, которого в журналах движка нет, хотя правило то же.
            var allowed = _session.Allowed;
            string? button = !allowed.Contains(BattleCommand.Wait) ? null
                : turns % 2 == 0 && allowed.Contains(BattleCommand.Defend) ? BattlePanelButtons.Defend : BattlePanelButtons.Wait;
            if (button is null && _session.Targets() is { Count: > 0 } targets)
            {
                TryTarget(targets.FirstOrDefault(t => t.Attacker != unit.Attacker) ?? targets[0]);
                strikes++;
            }
            else if (button is null)
            {
                // Целей нет и ждать нельзя — защита без щелчка (как ход по клавише модели): кнопки гаснут тем же правилом, ключа нажатия нет.
                if (_session.Defend()) BeforeAction();
                ContinueAfterTurnShow();
                silent++;
            }
            else
            {
                int before = _session.State.Actions.Count;
                bool on = _panel.State.IsEnabled(button);
                _panel.Step($"click {button}", out _);
                // «Мимо» — щелчок по погашенной кнопке или прошедший щелчок без действия хода.
                if (!on || _session.State.Actions.Count == before && ReferenceEquals(_session.Current, unit)) refused++;
                else clicks++;
            }
            await Frames(1);
            Count();
        }
        var started = DateTime.UtcNow;
        while ((_playing || !_endShown) && (DateTime.UtcNow - started).TotalSeconds < 30) await Frames(1);
        await Frames(5);
        Count();
        GD.Print($"battle synth K buttons ходов={turns} щелчков={clicks} мимо={refused} ударов={strikes} защит_без_щелчка={silent} погасли={dimmed} зажглись={lit} " +
                 $"конец={(_endShown ? 1 : 0)} переключатель_в_конце={(_panel.State.IsEnabled(BattlePanelButtons.Groups) ? 1 : 0)} " +
                 $"защита_в_конце={(_panel.State.IsEnabled(BattlePanelButtons.Defend) ? 1 : 0)}");

        void Count()
        {
            bool on = _panel.State.IsEnabled(BattlePanelButtons.Defend);
            if (on == wasOn) return;
            if (on) lit++;
            else dimmed++;
            wasOn = on;
        }
    }

    /// <summary>F — бой полигона целиком с показом (<c>D2_BATTLE_PLAYBACK=1</c>), для журнала зонда порта (<c>D2_PORT_PROBE=1</c>) против партии
    /// оригинала на тех же полигонах (<c>corpus/20261020-faces</c>, <c>AvFaceHarnessTests</c>): ход человека — призыв на первую клетку, превращение
    /// себя — по себе, прочее — по первому врагу в целях (нет — по первой цели), без целей — защита; ИИ ходит показом. Конец боя — как у хозяина
    /// боя с карты (<see cref="PlayEnd"/>, затем <see cref="ShowEnd"/>): откаты, изгнание и опыт. Опыт — накоплением по убитым (<see
    /// cref="D2.Data.Play.BattleAftermath.Kills"/>, без надбавок): сверка берёт шаблон надписи, а не число.</summary>
    private async Task SynthFaces()
    {
        _synthHp = _session.State.Units.ToDictionary(u => u.Id, u => u.Hp, StringComparer.OrdinalIgnoreCase);
        var begun = DateTime.UtcNow;
        bool Late() => (DateTime.UtcNow - begun).TotalMinutes > 5;   // показ залип — не висеть вечно
        for (int guard = 0; guard < 400 && !_session.Finished && !Late(); guard++)
        {
            while (_playing && !Late()) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            if (_session.Finished) break;
            if (_session.WaitsPlayer && _session.Current is { } unit)
            {
                int summonItem = Enumerable.Range(0, unit.BattleItems.Count).FirstOrDefault(i => _session.ItemSummonSlots(i).Count > 0, -1);
                if (summonItem >= 0)
                {
                    // Сфера призыва (I1, у пользователя — Сфера Сквайра): вспышка призыва — на призванном (журнал 20261020-faces seq 557992).
                    StartItemDrag(summonItem);
                    DropItemAt(_session.ItemSummonSlots(summonItem)[0]);
                    _itemSummons++;
                }
                else if (_session.SummonSlots() is { Count: > 0 } cells) Attack(unit, cells[0]);
                else if (_session.Targets() is { Count: > 0 } targets)
                    TryTarget(unit.Attack.Class == AttackClass.TransformSelf && targets.Contains(unit) ? unit
                              : targets.FirstOrDefault(t => t.Attacker != unit.Attacker) ?? targets[0]);
                else
                {
                    BeforeAction();
                    _session.Defend();
                    Continue();
                }
            }
            else Continue();
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            _seenSummoned.UnionWith(_session.State.Units.Where(u => u.Summoned).Select(u => u.Id));
        }
        var started = DateTime.UtcNow;
        while ((_playing || !_endShown) && (DateTime.UtcNow - started).TotalSeconds < 30) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        // Опыт до закрытия: надпись и жёлтый ещё на месте (у движка — до закрытия боя, 0x64d23e).
        for (int i = 0; i < 40; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        GD.Print($"battle synth F faces вспышек={_field.FlashTimes.Count} конец={(_endShown ? 1 : 0)} победа={_session.Winner ?? "-"} " +
                 $"стойких_надписей={_field.SteadyNotes} сфер_призыва={_itemSummons} призыв_на_призванном={SummonFlashes()} " +
                 $"{SecondAttackFaces().Replace("battle synth A1o second ", "", StringComparison.Ordinal)}");
    }

    private int _itemSummons;

    /// <summary>Призванные за бой проверки F (в конце боя модель их уводит из состава).</summary>
    private readonly HashSet<string> _seenSummoned = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Вспышки призыва (28) на виду: «на призванном/всего» — у движка вспышка призыва ложится на призванного (<c>0x63bf8e</c>).</summary>
    private string SummonFlashes()
    {
        var summons = _field.FlashTimes.Where(f => f.Face == BattleFace.Summoned).ToList();
        int onSummoned = summons.Count(f => _seenSummoned.Contains(f.Unit));
        return $"{onSummoned}/{summons.Count}";
    }

    /// <summary>Здоровье юнитов в начале боя проверки F — для опыта по убитым в её конце (<see cref="SynthEnd"/>).</summary>
    private Dictionary<string, int>? _synthHp;

    /// <summary>Конец боя проверки F (без хозяина): как <c>MapScreen.EndBattleOnScreen</c> — снимок, конец боя модели, опыт выжившим, показ
    /// конца боя и опыт на портретах.</summary>
    private void SynthEnd()
    {
        var before = BeforeEnd();
        var kills = D2.Data.Play.BattleAftermath.Kills(_session.State, _synthHp ?? [], _session.Log, GameData.Book);
        foreach (var (id, xp) in D2.Rules.Units.Experience.Accumulate(kills))
            if (_session.State.Unit(id) is { Alive: true } unit) unit.Xp += xp;
        _session.Sim.AfterBattle();
        PlayEnd(before, _ => 0, ShowEnd);
    }

    /// <summary>B1, партия 17.10 (бой у города орков и карта 8): призыватель человека рядом с большим (<c>--squad G000UU6007:0,G000UU5016:2</c> —
    /// Лиф и тролль). Обе клетки тролля — не цели призыва (<see cref="D2.Rules.Battle.BattleState.SummonCells"/>), метки — на всех целях и
    /// при открытии боя, когда призыватель ходит первым; щелчок по месту в панели призывает (<c>0x64c393</c>); пара свободна — встаёт
    /// большой (Гигантский чёрный паук, <c>0x6635dc</c>) на чётную клетку; у тролля в панели один широкий портрет, и его вторая половина —
    /// тот же тролль.</summary>
    private async Task SynthBigSummon(Func<Func<BattleUnit, bool>, Task<bool>> until, Func<Task> settle)
    {
        var troll = _session.State.Units.FirstOrDefault(u => u.Attacker && u.OnField && !u.Type.SizeSmall);
        if (troll is null || !await until(c => c.Attack.Class == AttackClass.Summon) || _session.Current is not { } summoner)
        {
            GD.Print("battle synth B1 summon нет_хода");
            return;
        }
        static Vector2 Mid(D2.Data.Ui.UiRect r) => new(r.X + (r.Width / 2f), r.Y + (r.Height / 2f));
        var cells = _session.SummonSlots();
        int fieldMarks = _field.EmptyMarks;
        int panelFrames = _field.PanelFrames.Count(f => f.StartsWith("EMPTYSEL", StringComparison.OrdinalIgnoreCase));
        int trollFaces = _field.PanelUnits.Count(id => id == troll.Id);
        var half = UnitAt(Mid(_scene.PanelSlot(troll.Attacker, D2.Rules.Units.Formation.Pair(troll.Slot))));
        int? onTroll = SummonCellAt(Mid(_scene.PanelSlot(troll.Attacker, D2.Rules.Units.Formation.Pair(troll.Slot))), cells);
        int cell = cells.FirstOrDefault(c => cells.Contains(c ^ 1), -1);
        int? picked = cell < 0 ? null : SummonCellAt(Mid(_scene.PanelSlot(summoner.Attacker, cell)), cells);
        int before = _session.State.Units.Count(u => u.Summoned);
        if (picked is { } place) Attack(summoner, place);
        await settle();
        var placed = _session.State.Units.LastOrDefault(u => u.Summoned);
        GD.Print($"battle synth B1 summon клетки={string.Concat(cells)} клетка_тролля={(cells.Any(troll.Covers) ? 1 : 0)} меток_поля={fieldMarks} " +
                 $"рамок_панели={panelFrames} портретов_тролля={trollFaces} половина_тролля={(ReferenceEquals(half, troll) ? 1 : 0)} " +
                 $"по_троллю={(onTroll?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "-")} " +
                 $"панель={(picked?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "-")} призвано={_session.State.Units.Count(u => u.Summoned) - before} " +
                 $"встал={(placed?.Slot.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "-")} большой={(placed is { Type.SizeSmall: false } ? 1 : 0)} " +
                 $"тип={placed?.Type.Id ?? "-"} портретов_призванного={(placed is null ? 0 : _field.PanelUnits.Count(id => id == placed.Id))}");
    }

    /// <summary>Показ призыва человека (<see cref="SummonShows"/> с номера <paramref name="from"/>): цель показа — призванный <paramref name="placed"/>,
    /// <c>TUCH</c> призывателя на его клетке, пауза кода 9 в кадры этого <c>TUCH</c>, призванный встаёт на её половине (у движка Повелитель
    /// стихий <c>G000UU0153</c>: <c>TUCH</c> на клетке призванного, пауза 23, <c>STIL</c> призванного через 11 кадров — <c>0x63c902</c>, журнал
    /// 20261016g-port-synth с seq 519049).</summary>
    private string SummonShowLine(int from, BattleUnit? placed)
    {
        string[] shown = SummonShows.Count > from ? SummonShows[from].Split(':') : [];
        if (shown.Length != 5) return $"цель_призванный=0 показов={SummonShows.Count - from}";
        bool onSummoned = placed is not null && shown[0].Equals(placed.Id, StringComparison.OrdinalIgnoreCase);
        return $"цель_призванный={(onSummoned ? 1 : 0)} клетка={shown[1]} касание={shown[2]} пауза={shown[3]} встаёт_на={shown[4]}";
    }

    /// <summary>A1, пометка 2: Аббатиса по отравленному — значок яда и жёлто-зелёная подкраска портрета сняты.</summary>
    private async Task SynthCure(Func<Func<BattleUnit, bool>, Task<bool>> until, Func<string, BattleUnit?> of)
    {
        var knight = of("g000uu0019");
        bool got = await until(c => c.Type.Id.Equals("g000uu0017", StringComparison.OrdinalIgnoreCase) && knight is { Alive: true }
                                    && knight.Dots.Any(d => d.Class == AttackClass.Poison));
        if (!got || knight is null || _session.Current is not { } abbess) { GD.Print("battle synth A1 cure нет_хода"); return; }
        string before = string.Join("+", BattlePanel.Icons(knight, !knight.Type.SizeSmall, _field.Marks, upgrade: false, highLevel: 0).Select(i => i.Name));
        var tintBefore = BattlePanel.Tint(knight).Whole;
        _session.Attack(knight);   // ход Аббатисы — значки сразу после него, до ходов врага
        string after = string.Join("+", BattlePanel.Icons(knight, !knight.Type.SizeSmall, _field.Marks, upgrade: false, highLevel: 0).Select(i => i.Name));
        GD.Print($"battle synth A1 cure {abbess.Id}→{knight.Id} значки_до={before} значки_после={(after.Length > 0 ? after : "-")} " +
                 $"яд_до={(before.Contains("FIPOISON", StringComparison.Ordinal) ? 1 : 0)} яд_после={(after.Contains("FIPOISON", StringComparison.Ordinal) ? 1 : 0)} " +
                 $"подкраска_до={tintBefore} подкраска_после={BattlePanel.Tint(knight).Whole} hp={knight.Hp}");
        Refresh();
    }

    /// <summary>A2, пометки 3–5, 12: павший сквайр костями на своей клетке; выбор клетки призыва (метки на пустых и на клетке с костями);
    /// воскрешение Патриархом; вампир бьёт упырей; клетка с костями под призванным — в панели один портрет и одна надпись «ОЗ».</summary>
    private async Task SynthSummonRevive(Func<Func<BattleUnit, bool>, Task<bool>> until, Func<string, BattleUnit?> of, Func<Task> settle)
    {
        var squire = of("g000uu0001");
        // Вампир — первым: его ход раньше, чем упыри его парализуют.
        var vampire = of("g000uu0175");
        GD.Print($"battle synth A2 vampire целей={(vampire is null ? 0 : D2.Rules.Battle.BattleTargets.Possible(_session.State, vampire, vampire.Attack, human: true).Count)} " +
                 $"у_ИИ={(vampire is null ? 0 : D2.Rules.Battle.BattleTargets.Possible(_session.State, vampire, vampire.Attack).Count)}");
        if (await until(c => c.Attack.Class == AttackClass.Summon) && _session.Current is { } summoner)
        {
            var cells = _session.SummonSlots();
            int marks = _field.EmptyMarks;
            int cell = cells.FirstOrDefault(s => !_session.State.Units.Any(u => u.Attacker == summoner.Attacker && u.Slot == s));
            var r = _scene.HitSlot(summoner.Attacker, cell);
            var center = new Vector2(r.X + (r.Width / 2f), r.Y + (r.Height / 2f));
            bool hit = EmptySlotAt(center) == cell;
            int before = _session.State.Units.Count(u => u.Summoned), shows = SummonShows.Count;
            Attack(summoner, cell);
            await settle();
            var placed = _session.State.Units.LastOrDefault(u => u.Summoned);
            GD.Print($"battle synth A2 summon клеток={cells.Count} клетки={string.Join("", cells)} с_костями={(squire is not null && cells.Contains(squire.Slot) ? 1 : 0)} " +
                     $"меток_поля={marks} щелчок_по_клетке={(hit ? 1 : 0)} выбрана={cell} встал={(placed?.Slot.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "-")} " +
                     $"призвано={_session.State.Units.Count(u => u.Summoned) - before}");
            GD.Print($"battle synth A2 summon_show {SummonShowLine(shows, placed)}");
            // Клетка с костями под призванным (призыв на место павшего): в панели — один портрет на клетку, одна надпись «ОЗ».
            if (placed is not null)
            {
                var fallen = new BattleUnit
                {
                    Id = "SYNTHFALLEN", Type = placed.Type, Attack = placed.Attack, Attacker = placed.Attacker, Slot = placed.Slot, HpMax = 10, Hp = 0,
                };
                _session.State.Units.Insert(0, fallen);
                _scene.SpotShown = true;   // обе панели
                RefreshPanels();
                int faces = _field.PanelUnits.Count(p => p.StartsWith(placed.Id, StringComparison.OrdinalIgnoreCase)
                                                        || p.StartsWith("SYNTHFALLEN", StringComparison.OrdinalIgnoreCase));
                GD.Print($"battle synth A2 covered клетка={placed.Slot} портретов={faces}");
                _session.State.Units.Remove(fallen);
                _scene.SpotShown = false;
                RefreshPanels();
            }
        }
        else GD.Print("battle synth A2 summon нет_хода");
        if (squire is not null && await until(c => c.Attack2?.Class == AttackClass.Revive && _session.Targets().Contains(squire)))
        {
            int corpses = _field.CorpsesShown;
            TryTarget(squire);
            while (_playing) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            Refresh();
            await settle();
            GD.Print($"battle synth A2 revive {squire.Id} hp={squire.Hp}/{squire.HpMax} воскрешён={(squire.Revived ? 1 : 0)} костей_до={corpses} " +
                     $"костей_после={_field.CorpsesShown}");
        }
        else GD.Print("battle synth A2 revive нет_хода");
    }

    /// <summary>A3, пометка 9: двойник — цели в обеих группах и метки под всеми (как у движка, 0x661a7a/0x6372e7); Повелитель волков — себя в
    /// целях, щелчок по себе превращает; удар по врагу — запасной атакой по всем врагам, у каждой цели один удар (HHIT) и без эффекта
    /// превращения.</summary>
    private async Task SynthDoppelWolf(Func<Func<BattleUnit, bool>, Task<bool>> until, Func<string, BattleUnit?> of)
    {
        if (await until(c => c.Attack.Class == AttackClass.Doppelganger) && _session.Current is { } dop)
        {
            var targets = _session.Targets();
            GD.Print($"battle synth A3 doppel целей={targets.Count} своих={targets.Count(t => t.Attacker == dop.Attacker)} " +
                     $"чужих={targets.Count(t => t.Attacker != dop.Attacker)} меток_всех={_field.FieldMarks}");
        }
        else GD.Print("battle synth A3 doppel нет_хода");
        var wolf = of("g000uu0164");
        if (wolf is not null && await until(c => ReferenceEquals(c, wolf)))
        {
            var targets = _session.Targets();
            var enemy = targets.FirstOrDefault(t => t.Attacker != wolf.Attacker);
            int from = _session.State.Actions.Count;
            if (enemy is not null) TryTarget(enemy);
            while (_playing) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            var struck = _session.State.Actions.Skip(from).Where(a => a.Unit == wolf.Id).ToList();
            int repeats = struck.GroupBy(a => (a.Target, a.Attack)).Select(g => g.Count()).DefaultIfEmpty(0).Max();
            GD.Print($"battle synth A3 wolf себя_в_целях={(targets.Contains(wolf) ? 1 : 0)} по_врагу целей={struck.Count} " +
                     $"классы={string.Join(",", struck.Select(a => a.Class).Distinct())} ударов_на_цель={repeats} превращён={(wolf.TransformedSelf ? 1 : 0)}");
            if (await until(c => ReferenceEquals(c, wolf)) && _session.Targets().Contains(wolf))
            {
                TryTarget(wolf);
                while (_playing) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                GD.Print($"battle synth A3 wolf self превращён={(wolf.TransformedSelf ? 1 : 0)} тип={wolf.Type.Id}");
            }
            else GD.Print("battle synth A3 wolf self нет_хода");
        }
        else GD.Print("battle synth A3 wolf нет_хода");
    }

    /// <summary>A2, пометки 1–2 вечерней партии 15.10: вампир (<c>DrainOverflow</c>) бьёт — на нём самом лечение по классу (<c>HEALTUCH</c>,
    /// <c>0x63570a</c>), запись бьющего в журнале вампиризма — с прибавкой здоровья.</summary>
    private async Task SynthDrain(Func<Func<BattleUnit, bool>, Task<bool>> until)
    {
        if (!await until(c => c.Attack.Class == AttackClass.DrainOverflow) || _session.Current is not { } vampire
            || _session.Targets().FirstOrDefault(t => t.Attacker != vampire.Attacker) is not { } enemy)
        {
            GD.Print("battle synth A2 drain нет_хода");
            return;
        }
        vampire.Hp = Math.Max(1, vampire.HpMax / 2);   // ранен — вампиризму есть куда лечь
        int hp = vampire.Hp, shown = DrainShown, from = _session.State.DrainHeals.Count;
        TryTarget(enemy);
        while (_playing) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        var heals = _session.State.DrainHeals.Skip(from).Where(h => h.Unit == vampire.Id).ToList();
        GD.Print($"battle synth A2 drain {vampire.Id} касаний={DrainShown - shown} себе={heals.Where(h => h.Target == vampire.Id).Sum(h => h.Amount)} " +
                 $"hp_до={hp} hp_после={vampire.Hp} записей={heals.Count}");
    }

    /// <summary>A4, пометки 3 и 5: Лесная Дева — обереги основной атакой (свой <c>TUCH</c> девы) показываются раньше лечения второй атакой, а у
    /// лечения второй атакой эльфа картинки на цели нет (<c>0x635321…0x6353da</c>) — прежде первым шло красное <c>HEALTUCH</c>.</summary>
    private async Task SynthDryad(Func<Func<BattleUnit, bool>, Task<bool>> until, Func<string, BattleUnit?> of)
    {
        if (!await until(c => c.Type.Id.Equals("g000uu8033", StringComparison.OrdinalIgnoreCase)) || _session.Current is not { } dryad
            || _session.Targets().FirstOrDefault(t => !ReferenceEquals(t, dryad)) is not { } ally)
        {
            GD.Print("battle synth A4 dryad нет_хода");
            return;
        }
        ally.Hp = Math.Min(ally.Hp, ally.HpMax - 30);   // ранен — у второй атаки (лечения) есть запись
        int from = TouchLog.Count;
        TryTarget(ally);
        while (_playing) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        var touches = TouchLog.Skip(from).Where(t => t.StartsWith(dryad.Id + ":", StringComparison.OrdinalIgnoreCase))
                              .Select(t => t[(dryad.Id.Length + 1)..]);
        GD.Print($"battle synth A4 dryad касания={string.Join(",", touches)}");
        // Партия 16.10: оберег у движка — модификатор в цепочке юнита, тот же, что у заклинания «Защита от …», — значок FIISOSPELLBOOST
        // слева вверху портрета (0x5ea6c1, место 0x64abb2). Значки панели — после хода Девы, до ходов врага.
        RefreshPanels();
        string shown = string.Join("+", _field.PanelIconsShown.Where(s => s.StartsWith(ally.Id + ":", StringComparison.OrdinalIgnoreCase))
                                                         .Select(s => s[(ally.Id.Length + 1)..]));
        GD.Print($"battle synth A4 ward {ally.Id} оберегов={ally.Wards.Count} значки_панели={(shown.Length > 0 ? shown : "-")}");
    }

    /// <summary>I2, пометки 6–7: тащение зелья — метка под живым юнитом под курсором (как у движка, <c>0x632b35</c>), под костями — нет
    /// (<c>0x632e88</c>: павших обход пропускает); зелье жизни, брошенное на портрет павшего в панели, применяется (цель броска — и в панели,
    /// <c>0x63911f</c>).</summary>
    private async Task SynthItems(Func<Func<BattleUnit, bool>, Task<bool>> until, Func<Task> settle)
    {
        int heal = -1, revive = -1;
        bool Ready(BattleUnit c)
        {
            heal = c.BattleItems.FindIndex(i => i.Category == D2.Rules.Magic.ItemCategory.PotionHeal);
            revive = c.BattleItems.FindIndex(i => i.Category == D2.Rules.Magic.ItemCategory.PotionRevive);
            return heal >= 0 && revive >= 0 && _session.ItemTargets(heal).Count > 0 && _session.ItemTargets(revive).Count > 0;
        }
        if (!await until(Ready) || _session.Current is not { } leader)
        {
            GD.Print("battle synth I2 items нет_хода");
            return;
        }
        var wounded = _session.ItemTargets(heal)[0];
        var fallen = _session.ItemTargets(revive)[0];
        Vector2 Feet(BattleUnit u)
        {
            var r = u.Type.SizeSmall ? _scene.Slot(u.Attacker, u.Slot) : _scene.BigSlot(u.Attacker, u.Slot);
            return new Vector2(r.X + (r.Width / 2f), r.Y + r.Height - 4);
        }
        StartItemDrag(heal);
        DragOver(Feet(wounded));
        int underLiving = _field.HoverMarks;
        DragOver(Feet(fallen));
        int underBones = _field.HoverMarks;
        CancelItemDrag();
        var face = _scene.PanelFace(fallen, fallen.Type.SizeSmall ? 70 : BattlePanel.BigWidth);
        var center = new Vector2(face.X + (face.Width / 2f), face.Y + (face.Height / 2f));
        var picked = UnitAt(center, withFallen: true);
        StartItemDrag(revive);
        DropItem(picked);
        await settle();
        GD.Print($"battle synth I2 items метка_под_живым={underLiving} метка_под_костями={underBones} " +
                 $"портрет_павшего={(ReferenceEquals(picked, fallen) ? 1 : 0)} применён={(fallen.Alive ? 1 : 0)} hp={fallen.Hp}");
    }

    /// <summary>A1o — порядок показа (с <c>D2_BATTLE_PLAYBACK=1</c>, по часам показа): бой ведётся экраном — удар бойца, прочие защищаются,
    /// ИИ ходит показом; каждый тик яда показан не раньше конца показа действия, после которого начался ход отравленного (у движка: действие →
    /// его анимации и надписи → начало хода следующего → его ход). Прежде тик следующего юнита шёл в начале показа удара прежнего.</summary>
    private async Task SynthOrder()
    {
        for (int guard = 0; guard < 80 && !_session.Finished && TickOrder.Count < 3; guard++)
        {
            while (_playing) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            if (_session.Finished) break;
            if (_session.WaitsPlayer && _session.Current is { } unit)
            {
                if (unit.Attack.Class == AttackClass.Damage && _session.Targets().FirstOrDefault(t => t.Attacker != unit.Attacker) is { } enemy)
                    TryTarget(enemy);
                else
                {
                    BeforeAction();
                    _session.Defend();
                    Continue();
                }
            }
            else Continue();
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        }
        while (_playing) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        int early = TickOrder.Count(t => t.Shown < ActionEnd(t.Action) - 1);
        foreach (var (tick, action, shown) in TickOrder)
            GD.Print(FormattableString.Invariant($"battle synth A1o tick #{tick} после_действия#{action} показ={shown:0} конец_действия={ActionEnd(action):0}"));
        GD.Print($"battle synth A1o order действий={_actionNo} тиков={TickOrder.Count} раньше_конца_действия={early}");
        GD.Print(SecondAttackFaces());
    }

    /// <summary>A1o — вторая атака своим шагом (журнал 20261020-faces: у Головореза «−N» и красный, затем шаг второй атаки — «Яд» и оливковый
    /// через срок надписи): по вспышкам удара на виду — сколько раз за красным того же юнита шёл яд, мороз или ожог отдельно (не раньше
    /// 80 % срока надписи) и сколько — разом (до 100 мс: вторая гасила первую, как у порта до 20.10).</summary>
    private string SecondAttackFaces()
    {
        int apart = 0, together = 0;
        var hits = _field.FlashTimes.Where(f => f.Src == "target").ToList();
        for (int i = 1; i < hits.Count; i++)
        {
            var (unit, face, _, ms) = hits[i];
            if (face is not (BattleFace.Poisoned or BattleFace.Frostbitten or BattleFace.Blistered)) continue;
            int prev = hits.FindLastIndex(i - 1, f => f.Unit == unit);
            if (prev < 0 || hits[prev].Face != BattleFace.Hit) continue;
            double gap = ms - hits[prev].Ms;
            if (gap < 100) together++;
            else if (gap >= _field.NoteSeconds * 800) apart++;
        }
        return $"battle synth A1o second вторая_отдельно={apart} разом={together}";
    }

    /// <summary>A5, пометка 10: отступающий (страх) — флаг только в панели у его портрета, значков на поле нет.</summary>
    private async Task SynthFlag(Func<Task> settle)
    {
        var victim = _session.State.Units.First(u => !u.Attacker && u.OnField);
        victim.Retreating = true;
        Refresh();
        await settle();
        _scene.SpotShown = true;   // обе панели — флаг виден в панели защитника
        RefreshPanels();
        GD.Print($"battle synth A5 flag {victim.Id} значков_на_поле={_field.FieldIcons} значков_в_панели={_field.PanelIcons}");
        _scene.SpotShown = false;
        victim.Retreating = false;
        Refresh();
    }
}
