using System;
using System.Collections.Generic;
using System.Linq;
using LastEdict.Data.Battle;
using LastEdict.Data.Gfx;
using LastEdict.Rules.Battle;
using Godot;

namespace LastEdict.Godot;

/// <summary>Полоса очереди ходов (<see cref="BattleQueueStrip"/>, <c>docs/own-version-roadmap.md</c> §3.3): включение по настройке, обновление по
/// событию сессии, связь наведения с полем; проверка <c>--queue-check</c> — числами, без окна.</summary>
public partial class BattleScreen
{
    /// <summary>Показывать полосу очереди ходов (<see cref="LastEdict.Data.Map.MapSettings.QueueStrip"/>; задаёт хозяин боя по настройкам).</summary>
    public bool QueueStrip { get; set; } = new LastEdict.Data.Map.MapSettings().QueueStrip;

    private BattleQueueStrip? _strip;

    /// <summary>Своя настройка вида боя (полоса очереди — ключ <c>--queue-strip</c>, широкий бой — <c>--wide-battle</c>): включена настройкой,
    /// а под проверкой (<c>--check</c>, сценарий <c>--input</c>) — только с её ключом <paramref name="key"/>: прежние проверки идут без неё,
    /// их числа и снимки не меняются.</summary>
    private static bool Wanted(bool setting, string[] args, string key) =>
        setting && (args.Contains(key) || !(args.Contains("--check") || args.Contains("--input")));

    /// <summary>Широкий бой (<see cref="LastEdict.Data.Map.MapSettings.WideBattle"/>; задаёт хозяин боя по настройкам). Под проверкой — с
    /// ключом <c>--wide-battle</c>: прежние проверки идут на раскладке 800×600.</summary>
    public bool Wide { get; set; } = new LastEdict.Data.Map.MapSettings().WideBattle;

    /// <summary>Полоса над полем: портреты — те же кадры <c>Faces</c>, что у панели (<see cref="BattleView.PlainFace"/>); пересборка — по
    /// <see cref="BattleSession.QueueChanged"/>.</summary>
    private void AddQueueStrip()
    {
        _strip = new BattleQueueStrip
        {
            Name = "QueueStrip", ZIndex = BattleScene.PanelLayer + 20, ZAsRelative = false, Face = _field.PlainFace, IsPlayers = _session.IsPlayers,
            Mirrored = unit => BattleView.PanelMirrored(_scene, unit), Text = Text,
        };
        AddChild(_strip);
        _session.QueueChanged += FillStrip;
        FillStrip();
    }

    private void FillStrip() => _strip?.Fill(_session.TwoRounds);

    /// <summary>Отписка от сессии при снятии экрана.</summary>
    private void DropQueueStrip()
    {
        if (_strip is not null) _session.QueueChanged -= FillStrip;
    }

    /// <summary>Юнит под точкой холста 800×600: портрет полосы, иначе юнит на поле.</summary>
    private BattleUnit? HoveredAt(Vector2 at) => _strip?.UnitAt(at) ?? UnitAt(at);

    /// <summary>Мышь над точкой: метка под юнитом на поле (<see cref="BattleView.Hover"/>) — и когда мышь над его портретом в полосе, и рамка
    /// его портрета в полосе — и когда мышь над ним на поле.</summary>
    private void HoverAt(Vector2 at)
    {
        var over = HoveredAt(at);
        _field.Hover(over);
        _strip?.Hover(over);
    }

    /// <summary>
    /// Проверка «--queue-check» (с <c>--queue-strip</c>): бой идёт ход за ходом сессией (ИИ за всех, «Защищаться» за человека, один раз
    /// «Ждать»), пока не начнутся два новых раунда. На каждом ходу и после каждого действия — порядок портретов полосы против
    /// <see cref="BattleSession.TwoRounds"/> (строка «battle queue ход=…»). Затем: пометка «пропустит» (паралич ставится юниту в хвосте полосы,
    /// полоса пересобирается явно — правка состояния мимо сессии события не даёт), наведение на портрет и на юнита поля, свободно ли место
    /// (элементы разметки в полосе, верх зон мыши поля) — строка «battle queue итог».
    /// </summary>
    private async System.Threading.Tasks.Task CheckQueueStrip()
    {
        if (_strip is null)
        {
            GD.Print("battle queue итог полосы нет");
            return;
        }
        int events = 0, rebuildsFrom = _strip.Rebuilds;
        _session.QueueChanged += () => events++;
        int turns = 0, checks = 0, same = 0, newRounds = 0, most = 0, outside = 0, round = _session.Round;
        var waiters = new List<string>();
        var waitMarks = new List<string>();
        bool Compare()
        {
            checks++;
            var model = _session.TwoRounds.Take(BattleQueueStrip.MaxCells).ToList();
            bool ok = Marks(model) == Marks(_strip.Shown) && _strip.Cells.Count == model.Count;
            if (ok) same++;
            most = Math.Max(most, _strip.Cells.Count);
            outside += _strip.Cells.Count(c => !BattleQueueStrip.Area.Encloses(c));
            return ok;
        }
        // «Ждать» — дважды: в первый же ход человека, где можно (его ход в конце раунда сверяется), и после двух новых раундов — им проверка
        // и кончается (ждущий виден в полосе на снимке).
        for (int guard = 0; guard < 400 && !_session.Finished && waiters.Count < 2; guard++)
        {
            _session.StartTurn();
            if (_session.Finished || _session.Current is not { } mover) break;
            if (_session.Round != round) (newRounds, round) = (newRounds + 1, _session.Round);
            turns++;
            bool ok = Compare();
            GD.Print($"battle queue ход={turns} раунд={round} ходит={mover.Id} модель={Marks(_session.TwoRounds.Take(BattleQueueStrip.MaxCells))} " +
                     $"полоса={Marks(_strip.Shown)} совпало={(ok ? 1 : 0)}");
            if ((waiters.Count == 0 || newRounds >= 2) && _session.WaitsPlayer && _session.Allowed.Contains(BattleCommand.Wait))
            {
                _session.Wait();
                waiters.Add(mover.Id);
                waitMarks.Add(_strip.Shown.LastOrDefault(e => ReferenceEquals(e.Unit, mover) && !e.NextRound) is { } entry ? Code(entry.Mark) : "-");
            }
            else if (_session.WaitsPlayer) _session.Defend();
            else _session.AiStep();
            Compare();
        }
        _session.StartTurn();
        Compare();
        Refresh();   // поле и панели — по нынешнему ходу (сессию вели мимо показа)
        int rebuilds = _strip.Rebuilds - rebuildsFrom;   // до явной пересборки ниже: все — по событию
        // «Пропустит»: паралич юниту в хвосте полосы (не ходящему).
        string skip = "-";
        if (_session.Upcoming.Skip(1).LastOrDefault(e => e.Mark == QueueMark.Next) is { } tail)
        {
            tail.Unit.Paralyzed = true;
            FillStrip();
            skip = $"{tail.Unit.Id}:{(_strip.Shown.FirstOrDefault(e => ReferenceEquals(e.Unit, tail.Unit)) is { } shown ? Code(shown.Mark) : "-")}";
            Compare();
        }
        // Наведение на юнита поля (середина его места, с конца полосы — первый, кого там ловит мышь) — рамка его портрета в полосе; на портрет
        // — метка под юнитом на поле (последним, для снимка).
        string fieldHover = "-", stripHover = "-";
        foreach (var entry in _strip.Shown.Reverse())
        {
            var slot = entry.Unit.Type.SizeSmall ? _scene.Slot(entry.Unit.Attacker, entry.Unit.Slot) : _scene.BigSlot(entry.Unit.Attacker, entry.Unit.Slot);
            var middle = new Vector2(slot.X + (slot.Width / 2f), slot.Y + (slot.Height / 2f));
            if (!ReferenceEquals(UnitAt(middle), entry.Unit)) continue;
            Pointer(middle);
            fieldHover = $"{entry.Unit.Id}:{_strip.Hovered?.Id ?? "-"}";
            break;
        }
        if (_strip.Cells.Count > 1)
        {
            var cell = _strip.Cells[1];
            Pointer(cell.GetCenter());
            stripHover = $"{_strip.Shown[1].Unit.Id}:{_field.HoverMarks}:{_strip.Hovered?.Id ?? "-"}";
        }
        // Свободно ли место: элементы разметки экрана в полосе и верх зон мыши мест поля (места и 32 px под ними, BattleScene.HitSlot).
        var area = BattleQueueStrip.Area;
        int elements = _panel.State.Screen.Elements.Count(e => area.Intersects(new Rect2(e.Rect.X, e.Rect.Y, e.Rect.Width, e.Rect.Height)));
        int hitTop = ((bool[])[true, false]).SelectMany(side => Enumerable.Range(0, 6).Select(s => _scene.HitSlot(side, s).Y)).Min();
        int covered = _strip.BarsCovered();   // рамки ходящего и наведённого — снаружи полоски стороны
        // Отражение портретов полосы — как у их портретов в боковой панели (обе панели: SPOT показывает и вторую группу).
        var mirrors = new List<string>();
        int mirrorSame = 0, mirrorSeen = 0;
        foreach (bool spot in (bool[])[false, true])
        {
            _scene.SpotShown = spot;
            RefreshPanels();
            for (int i = 0; i < _strip.Shown.Count; i++)
                if (_field.PanelFlipped(_strip.Shown[i].Unit.Id) is { } panel && mirrors.Count(m => m.StartsWith(_strip.Shown[i].Unit.Id + ":", StringComparison.Ordinal)) == 0)
                {
                    mirrorSeen++;
                    if (panel == _strip.Flipped[i]) mirrorSame++;
                    mirrors.Add($"{_strip.Shown[i].Unit.Id}:{(_strip.Flipped[i] ? 1 : 0)}{(panel ? 1 : 0)}");
                }
        }
        _scene.SpotShown = false;
        RefreshPanels();
        for (int i = 0; i < 3; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        GD.Print($"battle queue итог ходов={turns} сверок={checks} совпало={same} новых_раундов={newRounds} ждал={string.Join(",", waiters.DefaultIfEmpty("-"))} пометка_ждёт={string.Join(",", waitMarks.DefaultIfEmpty("-"))} " +
                 $"пропуск={skip} портретов_макс={most} вне={outside} рамка={_strip.Bounds()} элементов_в_полосе={elements} зона_мыши_верх={hitTop} " +
                 $"событий={events} пересборок={rebuilds} наведение_поле={fieldHover} наведение_полоса={stripHover} полосок_под_рамкой={covered} " +
                 $"отражение={mirrorSame}/{mirrorSeen} отражённых={_strip.Flipped.Count(f => f)} портреты={string.Join(",", mirrors.DefaultIfEmpty("-"))}");
    }

    private static string Code(QueueMark mark) => mark switch { QueueMark.Current => "C", QueueMark.Skips => "S", QueueMark.Waits => "W", _ => "N" };

    /// <summary>«id:пометка» через запятую; пусто — «-».</summary>
    private static string Marks(IEnumerable<QueueEntry> entries) =>
        string.Join(",", entries.Select(e => $"{e.Unit.Id}:{Code(e.Mark)}")) is { Length: > 0 } line ? line : "-";
}
