using System;
using System.Collections.Generic;
using System.Linq;
using LastEdict.Data.Battle;
using LastEdict.Data.Gfx;
using LastEdict.Data.Sound;
using LastEdict.Rules.Battle;
using Godot;

namespace LastEdict.Godot;

/// <summary>Показ действий боя: модель решает ход сразу, а экран показывает действия по одному — замах ходящего, эффект у него, эффект на
/// цели, реакция целей (удар, смерть, эффекты <c>Battle.ff</c>), затем поле собирается заново и ходит следующий. Хронология — просмотр
/// боя движка (<c>docs/battle-render.md</c>, «Показ действий»). Без окна (проверки <c>--headless</c>) бой идёт разом, как при
/// <c>BattleSpeed=4</c> у движка (<c>0x62e26a</c>).</summary>
public partial class BattleScreen
{
    /// <summary>Кадр анимации боя — 66 мс, общий аниматор движка (<c>docs/battle-render.md</c> §2).</summary>
    private const double AnimFrameMs = 66;

    /// <summary>Кадр эффекта на цели <c>TUCH</c> (и тика яда), на котором цели реагируют: поле +8 контейнера — всегда 1 (<c>0x6483ec</c>,
    /// <c>0x64848c</c>, <c>0x647cc2</c>; ранг 1).</summary>
    private const int TouchReactFrame = 1;

    /// <summary>Без окна показывать нечего: бой идёт разом; <c>D2_BATTLE_PLAYBACK=1</c> — показ и без окна (проверка его порядка и длин).
    /// Скорость боя «Сразу» (<see cref="InstantSpeed"/>, сессия Б) — тоже разом.</summary>
    private bool Instant => InstantSpeed || DisplayServer.GetName() == "headless" && System.Environment.GetEnvironmentVariable("D2_BATTLE_PLAYBACK") != "1";

    /// <summary>Идёт показ действия: ход игрока и команды панели ждут.</summary>
    private bool _playing;

    /// <summary>Номер показа: <see cref="StopPlaying"/> его меняет, и отложенные шаги прежнего показа молчат.</summary>
    private int _playRun;

    /// <summary>Сколько тиков ядов (<see cref="BattleSim.DotLog"/>), сообщений боя (<see cref="BattleState.Notes"/>), атак по своим
    /// (<see cref="BattleSim.AllyLog"/>) и лечений вампиризмом (<see cref="BattleState.DrainHeals"/>) уже показано.</summary>
    private int _dotsShown, _notesShown, _alliesShown, _drainsShown;

    /// <summary>Классы с ударом по цели: у них цель играет <c>HHIT</c> (<c>0x635914</c>) — у яда, мороза и ожога только если удар убил.
    /// Лечение, усиления, ослабления, паралич, страх и прочее — без своей анимации цели, только <c>TUCH</c> и надпись (ранг 1).</summary>
    private static bool Strikes(AttackClass c) =>
        c is AttackClass.Damage or AttackClass.Drain or AttackClass.DrainOverflow or AttackClass.Poison or AttackClass.Frostbite or AttackClass.Blister;

    private static bool IsDot(AttackClass c) => c is AttackClass.Poison or AttackClass.Frostbite or AttackClass.Blister;

    /// <summary>Удар цели <c>HHIT</c> показывается: только при «дополнительной анимации боя» и не мгновенном бое — подготовка удара
    /// (<c>0x6517e1</c>) грузит его картинки лишь при <c>battleAnimations</c> (<c>+0xff4</c>) и без «Сразу» (<c>+0xff5</c>). Журналы оригинала:
    /// <c>BattleAnim</c> 0 (<c>20261016e-av</c>, <c>20261016g-port-synth</c>) — ни одной строки <c>HHIT</c> на 265 замахов, покой <c>STIL</c>;
    /// <c>BattleAnim</c> 1 (<c>20261017-port-synth</c>) — 42 <c>HHIT</c>. Без удара нет и <c>DEATH</c> (<see cref="DeathAfter"/>). По настройке, а не
    /// по показу: бой без окна (<see cref="Instant"/>) картинок не ставит, но план звуков и длины шагов считает как при показе.</summary>
    private bool HurtShown => BattleAnim && !InstantSpeed;

    /// <summary>Эффект <c>Battle.ff</c> реакции цели по классу атаки (<c>0x63bfcc</c>): воскрешение — код 2, превращение другого и себя — 3,
    /// двойник — 5, вытягивание уровня — 7 (<c>CBatUnitAnim::playEffect</c> <c>0x65255b</c>). Призыв (код 9) картинки не имеет.</summary>
    private static string? ReactionEffect(AttackClass c) => c switch
    {
        AttackClass.Revive => "REVIVEANIM",
        AttackClass.TransformOther or AttackClass.TransformSelf => "TRANSF",
        AttackClass.Doppelganger => "DOPPEL",
        AttackClass.DrainLevel => "DRAINLVL",
        _ => null,
    };

    /// <summary>Анимация тика (<c>0x635ecc</c>: яд, мороз, прочее — ожог).</summary>
    private static string TickEffect(AttackClass c) => c switch
    {
        AttackClass.Poison => "POISONANIM",
        AttackClass.Frostbite => "FROSTBITEANIM",
        _ => "BLISTERANIM",
    };

    /// <summary>
    /// Показать записи хода. Сначала то, что случилось в начале хода (<see cref="Pending"/>: тики ядов, откат превращения), затем действия
    /// (записи подряд одного юнита) по хронологии просмотра движка (шаг 0, <c>0x634b78</c>, ранг 1):
    /// <list type="number">
    /// <item>замах <c>HMOV</c> ходящего с начала — кроме второй атаки, двойника и превращения себя (<c>0x634dd2</c>); кадр удара — <c>hitFrame</c>
    /// звуков юнита (<c>SNDINFO.DAT</c>), нет звуков — середина замаха (ранг 3);</item>
    /// <item>на кадре удара — эффект у бьющего <c>HEFF</c> (только основная атака, <c>0x635787</c>), его <c>effectFrame</c> (<c>0x648323</c>;
    /// нет записи звуков — 1) — миг эффекта на цели; <c>HEFF</c> нет — эффект на цели сразу;</item>
    /// <item><c>TUCH</c> (юнита или класса атаки) на целях без промаха; через <see cref="TouchReactFrame"/> кадр — реакция целей; <c>TUCH</c> нет — сразу;</item>
    /// <item>реакция: <c>HHIT</c> по классам с ударом (<see cref="Strikes"/>), смерть <c>DEATH_*</c> — по концу <c>HHIT</c>; эффект <c>Battle.ff</c>
    /// (<see cref="ReactionEffect"/>) на цели, у превращения себя и двойника — на самом ходящем; облик меняется на половине эффекта
    /// (<c>0x652bb2</c>).</item>
    /// </list>
    /// Звуки и надписи (<paramref name="numbers"/>) — с кадра замаха <c>attackFrame</c>, как было. Следующий шаг ждёт конца всех картинок
    /// (<c>CAnimCounter</c>, ранг 2). Возвращает длину показа, мс.
    /// </summary>
    private double Animate(IReadOnlyList<TurnRecord> records, Action<int?> numbers)
    {
        var actions = Actions(WithAllies(records)).ToList();
        var healed = TakeDrains();
        if (!Instant) _field.Prefetch(PendingSprites().Concat(StepSprites(actions)));   // все картинки шага — разом в потоках (замечание 35)
        // Начало хода (тики, откаты) показано до хода (NextStep, BattleSession.HoldTurnStart); здесь — только то, что не успело.
        double at = Pending(0);
        double from = at;
        bool heard = false;
        foreach (var action in actions)
        {
            if (_session.State.Unit(action[0].Unit) is not { } actor) continue;
            // Вторая атака — свой шаг просмотра после первой, без замаха (0x634dd2), со своими надписями и вспышками (Numbers, part).
            bool leading = true, own = !heard;   // надписи шага — с первым действием (прочие — атаки по своим того же шага)
            var parts = ByAttack(action).ToList();
            foreach (var part in parts)
            {
                int? attack = parts.Count > 1 ? part[0].Attack : null;
                at = Act(actor, part, at, own ? () => numbers(attack) : null, leading, healed);
                leading = false;
            }
            heard = true;
        }
        if (!heard)
        {
            // Действия без картинок (защита, ожидание) — надписи и звуки после начала хода; «Защита» ИИ — надпись на портрете, и следующий
            // шаг ждёт её снятия (счётчик +0x14d8), как у надписей удара.
            bool turnShown = _session.TurnShows.Skip(_turnsShown).Any(t => !t.AtTurnStart);
            Later(at, () => numbers(null));
            if (turnShown && !Instant) at += NoteMs;
        }
        if (_checks)
        {
            _actionEnds[_actionNo] = Now + at;   // показ этого действия кончится тут (часы показа)
            if (!Instant && actions.Count > 0)
                GD.Print(FormattableString.Invariant($"battle order действие#{_actionNo} {actions[0][0].Unit} с={Now + from:0} до={Now + at:0}"));
        }
        WarmNext();   // ход не кончился (первый удар двойной атаки) — тот же ходящий; кончился — греет NextStep после StartTurn
        return at;
    }

    /// <summary>Лечения вампиризмом (<see cref="BattleState.DrainHeals"/>), ещё не показанные, — их забирает показ этого шага.</summary>
    private List<DrainHeal> TakeDrains()
    {
        var drains = _session.State.DrainHeals;
        var healed = drains.Skip(_drainsShown).ToList();
        _drainsShown = drains.Count;
        return healed;
    }

    /// <summary>Режим проверки (<c>--synth</c>): только в нём копятся накопители порядка показа (<see cref="TickOrder"/>, <see cref="TouchLog"/>)
    /// и печатаются строки «battle order» на каждое действие.</summary>
    private bool _checks;

    /// <summary>Номер последнего действия сессии, вызванного экраном (<see cref="BeforeAction"/>); 0 — ещё ни одного.</summary>
    private int _actionNo;

    /// <summary>Сколько тиков было в журнале модели перед действием N (индекс N − 1): тик с номером не меньше — следствие действия N или
    /// позже (начало хода следующего юнита после него).</summary>
    private readonly List<int> _dotsBeforeAction = [];

    /// <summary>Конец показа действия N по часам показа, мс (без картинок — миг вызова).</summary>
    private readonly Dictionary<int, double> _actionEnds = [];

    /// <summary>Перед каждым действием сессии от экрана (удар, ход ИИ, предмет, кнопки): номер действия и сколько тиков было до него.</summary>
    private void BeforeAction()
    {
        if (!_checks) return;
        _dotsBeforeAction.Add(_session.Sim.DotLog.Count);
        _actionNo = _dotsBeforeAction.Count;
        _actionEnds[_actionNo] = Now;
    }

    /// <summary>Порядок показа тиков (проверка «--synth A1o»): номер тика в журнале модели, действие, после которого начался ход тикнувшего, и
    /// когда тик показан (часы показа, мс). Тик раньше конца показа своего действия (<see cref="ActionEnd"/>) — нарушение (партия 15.10: «яд
    /// сработает ещё до окончания анимации действия предыдущего юнита»).</summary>
    public List<(int Tick, int Action, double Shown)> TickOrder { get; } = [];

    /// <summary>Конец показа действия <paramref name="action"/> по часам показа, мс; нет такого — 0.</summary>
    public double ActionEnd(int action) => _actionEnds.GetValueOrDefault(action, 0);

    /// <summary>Записать тик <paramref name="index"/> журнала модели, показанный через <paramref name="at"/> мс.</summary>
    private void TickShown(int index, double at)
    {
        if (!_checks) return;
        int cause = _dotsBeforeAction.FindLastIndex(n => n <= index) + 1;
        TickOrder.Add((index, cause, Now + at));
        if (!Instant) GD.Print(FormattableString.Invariant($"battle order тик#{index} после_действия#{cause} показ={Now + at:0}"));
    }

    /// <summary>Картинки начала хода, ещё не показанные (<see cref="Pending"/>): тики ядов и смерть от них, откат превращения.</summary>
    private IEnumerable<BattleSprite> PendingSprites()
    {
        var dots = _session.Sim.DotLog;
        for (int i = _dotsShown; i < dots.Count; i++)
        {
            if (_session.State.Unit(dots[i].Unit) is not { } unit) continue;
            if (_scene.UnitEffect(TickEffect(dots[i].Class), unit, BattleScene.TouchSublayer) is { } tick) yield return tick;
            if (dots[i].HpAfter <= 0 && _scene.Death(unit) is { } death) yield return death;
        }
        var notes = _session.State.Notes;
        for (int i = _notesShown; i < notes.Count; i++)
            if (TurnStartEffect(notes[i].Kind) is { } effect && _session.State.Unit(notes[i].UnitId) is { } unit
                && _scene.UnitEffect(effect, unit) is { } undo)
                yield return undo;
    }

    /// <summary>Картинки, которые покажет шаг: замах и эффект ходящего, на целях — эффект удара, удар, смерть, эффект Battle.ff (как в
    /// <see cref="Act"/>/<see cref="React"/>, с запасом: удар — у всех целей другой стороны).</summary>
    private IEnumerable<BattleSprite> StepSprites(IReadOnlyList<IReadOnlyList<TurnRecord>> actions)
    {
        foreach (var action in actions)
        {
            if (_session.State.Unit(action[0].Unit) is not { } actor) continue;
            foreach (var sprite in _scene.UnitSprites(actor, BattleAction.Hit, actor.Type.SizeSmall)) yield return sprite;
            foreach (var sprite in _scene.CasterEffects(actor.Type.Id, TargetsAttackers(actor, action))) yield return sprite;
            foreach (var record in action.DistinctBy(r => (r.Target.ToUpperInvariant(), r.Attack)))
            {
                if (_session.State.Unit(record.Target) is not { } target) continue;
                var cls = ClassOf(actor, record);
                if (_scene.Touch(actor.Type.Id, cls, record.Attack != 2, target) is { } touch) yield return touch;
                if (target.Attacker != actor.Attacker)
                    foreach (var sprite in _scene.UnitSprites(target, BattleAction.Hurt, target.Type.SizeSmall)) yield return sprite;
                if (!target.Alive && _scene.Death(target) is { } death) yield return death;
                var holder = cls is AttackClass.TransformSelf or AttackClass.Doppelganger ? actor : target;
                if (cls is { } c1 && ReactionEffect(c1) is { } name && _scene.UnitEffect(name, holder) is { } effect) yield return effect;
            }
        }
    }

    /// <summary>Пока идёт показ шага (или игрок думает), в фоне разобрать картинки следующего хода: замах и эффект ходящего, у хода
    /// человека — ещё удар и эффект на его годных целях (замечание 35: прежде их PNG разбирались в главном потоке в миг удара, 0,1–0,6 с).</summary>
    private void WarmNext()
    {
        if (Instant || _session.Finished || _session.Current is not { } next) return;
        var sprites = new List<BattleSprite>();
        sprites.AddRange(_scene.UnitSprites(next, BattleAction.Hit, next.Type.SizeSmall));
        IReadOnlyList<BattleUnit> targets = _session.WaitsPlayer ? _session.Targets() : [];
        sprites.AddRange(_scene.CasterEffects(next.Type.Id, targets.FirstOrDefault()?.Attacker ?? !next.Attacker));
        foreach (var target in targets)
        {
            if (_scene.Effect(next.Type.Id, target) is { } touch) sprites.Add(touch);
            if (target.Attacker != next.Attacker) sprites.AddRange(_scene.UnitSprites(target, BattleAction.Hurt, target.Type.SizeSmall));
        }
        _field.Warm(sprites);
    }

    /// <summary>Группа целей действия — у первой цели записи (<c>0x639f2b</c>: эффект ходящего <c>HEFF</c> рисуется над ней); цели нет на
    /// поле — противник ходящего.</summary>
    private bool TargetsAttackers(BattleUnit actor, IReadOnlyList<TurnRecord> action) =>
        _session.State.Unit(action[0].Target)?.Attacker ?? !actor.Attacker;

    /// <summary>Записи действия подряд одной атаки (первая, затем вторая).</summary>
    private static IEnumerable<IReadOnlyList<TurnRecord>> ByAttack(IReadOnlyList<TurnRecord> action)
    {
        var part = new List<TurnRecord>();
        foreach (var record in action)
        {
            if (part.Count > 0 && part[0].Attack != record.Attack)
            {
                yield return part;
                part = new List<TurnRecord>();
            }
            part.Add(record);
        }
        if (part.Count > 0) yield return part;
    }

    /// <summary>Одно действие с момента <paramref name="start"/>; возвращает его конец, мс.</summary>
    /// <param name="leading">Первая часть действия: замах и эффект ходящего — и у второй атаки, если первая записи не дала (Патриарх по
    /// павшему: лечение его не трогает, поднимает вторая атака — ход всё равно начинается замахом).</param>
    /// <param name="healed">Непоказанные лечения вампиризмом шага: их картинки ставит часть действия, чья атака — вампиризм (и вторая, если
    /// вампиризм у второй атаки), и забирает свои записи — второй раз в шаге они не показываются.</param>
    private double Act(BattleUnit actor, IReadOnlyList<TurnRecord> action, double start, Action? numbers, bool leading = false,
                       List<DrainHeal>? healed = null)
    {
        var first = action[0];
        var used = ClassOf(actor, first);
        bool main = first.Attack != 2;
        bool swing = (main || leading) && used is not (AttackClass.TransformSelf or AttackClass.Doppelganger);
        int swingFrames = swing ? _field.Length(_scene.UnitSprites(actor, BattleAction.Hit, actor.Type.SizeSmall)) : 0;
        var info = GameSound.Units.Of(actor.Type.Id);
        int hitFrame = info is { HitFrame: > 0 } ? info.HitFrame : swingFrames / 2;
        double hitAt = start + (swing ? hitFrame * AnimFrameMs : 0), end = start + (swingFrames * AnimFrameMs);
        if (swing) Later(start, () => _field.Show(actor, BattleAction.Hit));
        // Надписи — с кадра замаха attackFrame; у второй атаки (шаг без замаха) — с начала её шага (журнал 20261020-faces: bat_show attack 2 →
        // bat_text через 16 мс).
        double notesAt = start + (main || leading ? (info?.AttackFrame ?? 0) * AnimFrameMs : 0);
        if (numbers is not null) Later(notesAt, numbers);
        // Следующее действие ждёт и снятия надписей на портретах (счётчик анимаций +0x14d8, 0x637473; сессия Б).
        if (numbers is not null) end = Math.Max(end, notesAt + NoteMs);
        var cast = main || leading ? _scene.CasterEffects(actor.Type.Id, TargetsAttackers(actor, action)) : [];
        double touchAt = hitAt;
        if (cast.Count > 0)
        {
            end = Math.Max(end, hitAt + (_field.Length(cast) * AnimFrameMs));
            touchAt = hitAt + (Math.Max(1, info?.EffectFrame ?? 1) * AnimFrameMs);
            if (!Instant)
                GD.Print(FormattableString.Invariant($"battle cast {actor.Id} {string.Join(",", cast.Select(s => s.Name))} над={cast[0].Source} ")
                         + FormattableString.Invariant($"кадр=({cast[0].X},{cast[0].Y}) слои={string.Join(",", cast.Select(s => s.Layer))} @{hitAt - start:0}"));
            Later(hitAt, () =>
            {
                foreach (var sprite in cast) _field.PlayOnce(sprite, "HEFF", actor);
            });
        }
        foreach (var group in action.GroupBy(r => r.Target, StringComparer.OrdinalIgnoreCase))
        {
            if (_session.State.Unit(group.Key) is not { } target) continue;
            end = Math.Max(end, React(actor, target, [.. group], touchAt, main, start, cast.Count > 0 ? cast[0].Name : "-"));
        }
        if (used is AttackClass.Drain or AttackClass.DrainOverflow && healed is { Count: > 0 })
        {
            bool Mine(DrainHeal h) => h.Unit.Equals(actor.Id, StringComparison.OrdinalIgnoreCase);
            end = Math.Max(end, DrainTouches(actor, healed.Where(Mine), touchAt, start));
            healed.RemoveAll(Mine);
        }
        return end;
    }

    /// <summary>
    /// Вампиризм (<c>0x6354f4…0x635736</c>, ранг 1): на группе бьющего у каждого, на ком лечение вампиризмом (<see cref="DrainHeal"/>: сам
    /// бьющий — всегда, союзники — полеченные излишком), <c>TUCH</c> класса «лечение» (<c>0x64845d</c> с <c>0x837490</c>) на его месте — в тот
    /// же шаг, что и <c>TUCH</c> целей. Прежде у порта лечение вампира ничем не показывалось (партия 15.10, пометки 1 и 2: «у вампира не
    /// восстановилось здоровье»). Возвращает конец этих картинок, мс.
    /// </summary>
    private double DrainTouches(BattleUnit actor, IEnumerable<DrainHeal> healed, double touchAt, double start)
    {
        double end = touchAt;
        foreach (string id in healed.Select(h => h.Target).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (_session.State.Unit(id) is not { OnField: true } unit || _scene.ClassEffect(AttackClass.Heal, unit) is not { } touch) continue;
            end = Math.Max(end, touchAt + (_field.Length([touch]) * AnimFrameMs));
            if (!Instant) GD.Print(FormattableString.Invariant($"battle drain {actor.Id} → {unit.Id} на_нём={touch.Name} @{touchAt - start:0}"));
            Later(touchAt, () => _field.PlayOnce(touch, "HEAL", actor));
            DrainShown++;
        }
        return end;
    }

    /// <summary>Картинки на целях по порядку показа: «ходящий:класс:номер атаки:TUCH» («-» — картинки нет) — для проверок.</summary>
    public List<string> TouchLog { get; } = [];

    /// <summary>Показы призыва по порядку: «призванный:клетка:TUCH на ней:пауза, кадров:кадр появления» — для проверок.</summary>
    public List<string> SummonShows { get; } = [];

    /// <summary>Сколько картинок лечения вампиризмом поставлено за бой — для проверок.</summary>
    public int DrainShown { get; private set; }

    /// <summary>Эффект на цели и реакция одной цели действия; возвращает конец её картинок, мс. <paramref name="item"/> — шаг боевого предмета
    /// или зелья (<see cref="BattleItemUse"/>): класс — атаки показа предмета, <c>TUCH</c> — юнита предмета (<c>0x634e0b</c> с признаком
    /// «предмет»: <c>0x6498f9</c> → <c>0x6483b1</c>), иммунитет и оберег — без него.</summary>
    private double React(BattleUnit actor, BattleUnit target, IReadOnlyList<TurnRecord> hits, double touchAt, bool main, double start, string caster,
                         BattleItemUse? item = null)
    {
        var cls = item is null ? ClassOf(actor, hits[0]) : item.Shown?.Class;
        bool miss = hits.All(r => r.Miss) || item is { Immune: true };
        bool killed = !target.Alive && hits.Any(r => !r.Miss && r.TargetHpAfter <= 0);
        // TUCH (0x63513a): у основной атаки — свой у юнита, у второй — только по классу (BattleScene.Touch, ранг 1). Промах и иммунитет —
        // без него (ранг 2).
        var touch = miss ? null
            : item is null ? _scene.Touch(actor.Type.Id, cls, main, target)
            : item.Item.UnitId is { Length: > 0 } itemUnit ? _scene.Effect(itemUnit, target) : null;
        double reactAt = touchAt + (touch is null ? 0 : TouchReactFrame * AnimFrameMs);
        bool hurt = HurtShown && !miss && cls is { } c && Strikes(c) && (killed || !IsDot(c)) && target.Attacker != actor.Attacker;
        int hurtFrames = hurt ? _field.Length(_scene.UnitSprites(target, BattleAction.Hurt, target.Type.SizeSmall)) : 0;
        var death = killed ? _scene.Death(target) : null;
        double deathAt = reactAt + (hurtFrames * AnimFrameMs);
        // Превращение себя и двойник — облик меняет сам ходящий (0x63c71f; у двойника ранг 3).
        var holder = cls is AttackClass.TransformSelf or AttackClass.Doppelganger ? actor : target;
        var effect = !miss && cls is { } c1 && ReactionEffect(c1) is { } name ? _scene.UnitEffect(name, holder) : null;
        // Призыв (цель показа — призванный, ShowOrder.SummonTargets): код 9 без картинки — пауза в кадры TUCH его места (0x63c902 берёт их
        // у места, +0x13f4, 0x63ca4b → 0x652299 → playEffect 0x65255b; 0x6529c3: код 9 — пустая анимация 0x654d40), призванный встаёт на её
        // половине (0x653219 на кадре count/2), шаг ждёт её конца. Журналы оригинала: пауза 23 = кадры G000UU0153TUCH, STIL призванного
        // через 11 кадров после начала паузы (20261016g-port-synth seq 519049…: +734 → +1094 мс при 33 мс на кадр).
        bool summoned = cls is AttackClass.Summon && !ReferenceEquals(holder, actor);
        int pause = summoned && touch is not null ? _field.Length([touch]) : 0;
        int effectFrames = effect is null ? pause : _field.Length([effect]);
        if (_checks && summoned) SummonShows.Add($"{target.Id}:{target.Slot}:{touch?.Name ?? "-"}:{pause}:{effectFrames / 2}");
        double end = Math.Max(touchAt + ((touch is null ? 0 : _field.Length([touch])) * AnimFrameMs),
                              Math.Max(deathAt + ((death is null ? 0 : _field.Length([death])) * AnimFrameMs),
                                       reactAt + (effectFrames * AnimFrameMs)));
        if (_checks) TouchLog.Add($"{actor.Id}:{cls?.ToString() ?? "-"}:{(main ? 1 : 2)}:{touch?.Name ?? "-"}");
        if (!Instant)
            GD.Print(FormattableString.Invariant($"battle show {actor.Id} → {target.Id} класс={cls?.ToString() ?? "-"} удар@{touchAt - start:0} ")
                     + FormattableString.Invariant($"у_бьющего={caster} на_цели={touch?.Name ?? "-"} реакция@{reactAt - start:0} цель={(hurt ? "HHIT" : "-")} ")
                     + FormattableString.Invariant($"эффект={effect?.Name ?? (pause > 0 ? $"пауза{pause}" : "-")} смерть={death?.Name ?? "-"} до={end - start:0}"));
        if (touch is not null) Later(touchAt, () => _field.PlayOnce(touch, "TUCH", actor));
        Later(reactAt, () =>
        {
            if (hurt) _field.Show(target, BattleAction.Hurt);
            if (effect is not null) _field.PlayOnce(effect, "EFFECT", holder);
            else if (pause > 0) PortProbe.Av?.BattleAnim(holder.Id, holder.Type.Id, "EFFECT", pause);   // как bat_anim EFFECT паузы призыва
        });
        // Облик (новый тип, воскрешённый на поле, призванный) — на половине эффекта или паузы призыва; без них — сразу (0x6529ab). Призыв без
        // призванного (цель — сам ходящий) замах не обрывает.
        if (effect is not null || cls is AttackClass.Revive || summoned)
            Later(reactAt + (effectFrames / 2 * AnimFrameMs), () => _field.Show(holder, BattleAction.Idle));
        if (killed)
            Later(deathAt, () =>
            {
                _field.Show(target, BattleAction.Idle);   // павший снимается с поля, дальше — его смерть
                if (death is not null) _field.PlayOnce(death, "EFFECT", target);
            });
        return end;
    }

    /// <summary>
    /// Что случилось в начале хода юнита, до его действия, с момента <paramref name="at"/> (мс); возвращает конец. Тик яда, мороза, ожога
    /// (шаги 6–8, <c>0x635ecc</c>, ранг 1): <c>POISONANIM</c>/<c>FROSTBITEANIM</c>/<c>BLISTERANIM</c> на юните, подслой 7, со звуком тика и
    /// надписью «Яд(−N)» / «Мороз (−N)» / «Пузырь (−N)» на портрете (<see cref="StrikeNotes.TickText"/>), панель — с новым здоровьем; урона
    /// при наложении нет (<c>Attacks.ApplyDot</c>) — картинка только на тике, в начале хода отравленного (решение пользователя 28.09: эффект
    /// не сразу — со следующего хода юнита); убил — <c>HHIT</c> и смерть (<c>0x6361c0</c>). Сообщения начала хода — со звуком: откат долгого
    /// превращения (шаг 13, код 4) — <c>UTRANSF</c>; снятие паралича и окаменения — надписи «Паралич прошел!» / «Оживает!»
    /// (<see cref="StrikeNotes.NoteText"/>), картинок нет (<c>UNPARALYZ</c>/<c>UNPETRIFY</c> не запрашиваются, <c>0x647a12…</c>). Начало хода
    /// показывается после показа прежнего действия (<see cref="NextStep"/>, <see cref="BattleSession.HoldTurnStart"/>).
    /// </summary>
    private double Pending(double at)
    {
        var dots = _session.Sim.DotLog;
        for (; _dotsShown < dots.Count; _dotsShown++)
        {
            var tick = dots[_dotsShown];
            if (_session.State.Unit(tick.Unit) is not { } unit) continue;
            var sprite = _scene.UnitEffect(TickEffect(tick.Class), unit, BattleScene.TouchSublayer);
            bool killed = tick.HpAfter <= 0;
            var death = killed ? _scene.Death(unit) : null;
            double reactAt = at + (TouchReactFrame * AnimFrameMs);
            double end = Math.Max(at + ((sprite is null ? 0 : _field.Length([sprite])) * AnimFrameMs),
                                  reactAt + ((death is null ? 0 : _field.Length([death])) * AnimFrameMs));
            end = Math.Max(end, at + (Instant ? 0 : NoteMs));   // следующий шаг ждёт и снятия надписи на портрете (счётчик +0x14d8)
            if (!Instant)
                GD.Print(FormattableString.Invariant($"battle tick {unit.Id} {tick.Class} эффект={sprite?.Name ?? "-"} урон={tick.Damage} смерть={death?.Name ?? "-"} до={end - at:0}"));
            string? note = StrikeNotes.TickText(tick, Text);
            TickShown(_dotsShown, at);
            Later(at, () =>
            {
                if (sprite is not null) _field.PlayOnce(sprite, "TICK", unit);
                Queue(unit.Id, BattleSoundPlan.OfTicks([tick]));
                if (note is not null) Note(unit, note);
                if (note is not null) _field.Flash(_scene, unit, BattleFaces.Tick(tick.Class), src: "actor");   // 0x6370e5 после надписи: тот же цвет, что у базы
            });
            if (killed)
                Later(reactAt, () =>
                {
                    _field.Show(unit, BattleAction.Idle);
                    if (death is not null) _field.PlayOnce(death, "EFFECT", unit);
                });
            at = end;
        }
        _dotsHeard = Math.Max(_dotsHeard, _dotsShown);   // звук тика — здесь, а не со следующим действием (BattleScreen.Sound.cs)
        var notes = _session.State.Notes;
        // Только сообщения начала хода: уход призванных, рост и откаты конца боя показывает PlayEnd (с NotesHeard) — на первом из них стоп.
        for (; _notesShown < notes.Count && TurnStartNote(notes[_notesShown].Kind); _notesShown++)
        {
            var note = notes[_notesShown];
            var cue = new SoundCue(0, BattleNoteSounds.Archive, BattleNoteSounds.Of(note.Kind, -1));
            if (_session.State.Unit(note.UnitId) is not { } unit)
            {
                Later(at, () => Queue(note.UnitId, [cue]));   // юнита нет — звук всё равно (NotesHeard уже за ним)
                continue;
            }
            var sprite = TurnStartEffect(note.Kind) is { } effect ? _scene.UnitEffect(effect, unit) : null;
            var (text, flash) = TurnStartFace(note.Kind, unit, sprite is not null);
            int frames = sprite is null ? 0 : _field.Length([sprite]);
            if (!Instant) GD.Print(FormattableString.Invariant($"battle note {unit.Id} {note.Kind} эффект={sprite?.Name ?? "-"} до={frames * AnimFrameMs:0}"));
            Later(at, () =>
            {
                Queue(unit.Id, [cue]);
                if (sprite is not null) _field.PlayOnce(sprite, "EFFECT", unit);
                if (text is not null && unit.Alive) Note(unit, text);
                if (flash is { } f && unit.Alive) _field.Flash(_scene, unit, f, src: "actor");
            });
            if (sprite is not null && note.Kind == BattleNoteKind.Untransformed)
                Later(at + (frames / 2 * AnimFrameMs), () => _field.Show(unit, BattleAction.Idle));   // прежний облик — на половине
            at += Math.Max(frames * AnimFrameMs, text is null || Instant ? 0 : NoteMs);
        }
        NotesHeard = Math.Max(NotesHeard, _notesShown);
        return ShowTurns(at, turnStartOnly: true);   // «Пас» парализованного и «Защита» окаменевшего — ход занят в начале хода (BattleSession.Advance)
    }

    /// <summary>Сколько ходов без удара (<see cref="BattleSession.TurnShows"/>) уже показано.</summary>
    private int _turnsShown;

    /// <summary>Ходы без удара, ещё не показанные (шаги просмотра 1, 9, 10: <c>0x635a9c</c>, <c>0x6364fb</c>, <c>0x63656c</c> → <c>0x6370be</c>):
    /// на портрете ходящего надпись «Пас» <c>X008TA0010</c> / «Ждать» <c>X008TA0020</c> / «Защита» <c>X008TA0021</c> и вспышка (<see cref="BattleFace.Pass"/>
    /// — ни подкраски, ни ран; <see cref="BattleFace.Waited"/>, <see cref="BattleFace.Defending"/> — только раны), с момента <paramref name="at"/>; возвращает
    /// конец — следующий шаг ждёт снятия надписи. <paramref name="turnStartOnly"/> — только занятые в начале хода (до первой команды
    /// «Ждать»/«Защищаться»: её показывает шаг действия, <see cref="Numbers"/>, вместе со звуком).</summary>
    private double ShowTurns(double at = 0, bool turnStartOnly = false)
    {
        var shows = _session.TurnShows;
        for (; _turnsShown < shows.Count; _turnsShown++)
        {
            var show = shows[_turnsShown];
            if (turnStartOnly && !show.AtTurnStart) break;
            if (_session.State.Unit(show.UnitId) is not { } unit) continue;
            var (id, face) = show.Kind switch
            {
                BattleTurnShow.Pass => ("X008TA0010", BattleFace.Pass),
                BattleTurnShow.Wait => ("X008TA0020", BattleFace.Waited),
                _ => ("X008TA0021", BattleFace.Defending),
            };
            string text = Text(id);
            Later(at, () =>
            {
                Note(unit, text);
                _field.Flash(_scene, unit, new BattleFlash(face, false), src: "actor");
            });
            if (!Instant) at += NoteMs;
        }
        return at;
    }

    /// <summary>Надпись и вспышка сообщения начала хода (шаги 4, 5, 13, 14 просмотра): снятие паралича и окаменения — «Паралич прошел!» /
    /// «Оживает!» и <see cref="BattleFace.Normal"/> (<c>0x635ba5</c>, <c>0x635c51</c>); откат превращения — «Прежний облик!» <c>X008TA0023</c> и
    /// чёрным целиком, только если картинка отката сыграла (<c>0x63679b</c>: <c>0x63c7e1</c> → <c>0x65255b</c>); изгнание призванного — «Изгнать»
    /// <c>X008TA0030</c> и чёрным целиком, если он жив (<c>0x6368c3</c>: юнит по id в карте объектов боя <c>0x623cc0</c> и <c>0x5e98ad</c>,
    /// здоровье &gt; 0) — только посреди боя: в конце боя (шаг 14 <see cref="PlayEnd"/>) призванного в карте объектов уже нет, и движок
    /// показывает только картинку ухода (журнал 20261020-faces: два изгнания в конце боя — <c>bat_show step 14</c> и <c>EFFECT</c>, ни
    /// <c>bat_text</c>, ни <c>bat_flash</c> 29; у порта до 20.10 — «Изгнать» и чёрный портрет).</summary>
    private (string? Text, BattleFlash? Flash) TurnStartFace(BattleNoteKind kind, BattleUnit unit, bool played) => kind switch
    {
        BattleNoteKind.Unparalyzed or BattleNoteKind.Unpetrified => (StrikeNotes.NoteText(kind, Text), new BattleFlash(BattleFace.Normal, false)),
        BattleNoteKind.Untransformed when played => (Text("X008TA0023"), new BattleFlash(BattleFace.Transformed, true)),
        BattleNoteKind.UnsummonedInBattle when unit.Hp > 0 => (Text("X008TA0030"), new BattleFlash(BattleFace.Banished, true)),
        _ => (null, null),
    };

    /// <summary>Сообщение начала хода (<see cref="BattleSim.BeginTurn"/>): снятие паралича и окаменения, откат долгого превращения.</summary>
    private static bool TurnStartNote(BattleNoteKind kind) =>
        kind is BattleNoteKind.Unparalyzed or BattleNoteKind.Unpetrified or BattleNoteKind.Untransformed or BattleNoteKind.UnsummonedInBattle;

    /// <summary>Эффект сообщения начала хода: откат превращения — <c>UTRANSF</c>, изгнание призванного посреди боя (призыватель пал или
    /// ушёл, <c>0x628c00</c>) — <c>USUMMONANIM</c>, как у изгнания в конце боя (<see cref="PlayEnd"/>).</summary>
    private static string? TurnStartEffect(BattleNoteKind kind) => kind switch
    {
        BattleNoteKind.Untransformed => "UTRANSF",
        BattleNoteKind.UnsummonedInBattle => "USUMMONANIM",
        _ => null,
    };

    /// <summary>Надпись начала хода на портрете юнита: его группа — в основной панели (у движка надпись и состояние кладутся на группу
    /// ходящего, <c>0x6370e5</c>), здоровье в панели — уже после тика.</summary>
    private void Note(BattleUnit unit, string markup)
    {
        bool moved = _scene.MainShowsAttackers != unit.Attacker;
        _scene.MainShowsAttackers = unit.Attacker;
        if (!Instant || moved) RefreshPanels();
        _field.Note(_scene, unit, markup, src: "actor");   // и журнал зонда: надпись ходящего, как у движка (0x6370e5) — с разметкой
    }

    /// <summary>Класс атаки записи в миг удара: у атак по своим — из <see cref="BattleSim.AllyLog"/> (у превращения себя атака после удара
    /// уже другая), иначе — атака ходящего по номеру записи.</summary>
    private AttackClass? ClassOf(BattleUnit actor, TurnRecord record)
    {
        // Класс удара на деле — из применений модели (BattleState.Actions): у превращения себя по врагу это запасная атака (удар по всем
        // врагам с HHIT), а не «превращение» цели — прежде цели играли эффект превращения на каждом ходу Повелителя волков (пометка 9).
        var actions = _session.State.Actions;
        for (int i = actions.Count - 1; i >= 0; i--)
            if (actions[i].Round == record.Round && actions[i].Attack == record.Attack
                && actions[i].Unit.Equals(record.Unit, StringComparison.OrdinalIgnoreCase)
                && actions[i].Target.Equals(record.Target, StringComparison.OrdinalIgnoreCase))
                return actions[i].Class;
        var allies = _session.Sim.AllyLog;
        for (int i = allies.Count - 1; i >= 0; i--)
            if (allies[i].Round == record.Round && allies[i].Unit.Equals(record.Unit, StringComparison.OrdinalIgnoreCase)
                && allies[i].Target.Equals(record.Target, StringComparison.OrdinalIgnoreCase))
                return allies[i].Class;
        return (record.Attack == 2 ? actor.Attack2 : actor.Attack)?.Class;
    }

    /// <summary>Записи хода и атаки по своим без записи (превращение себя, двойник, усиление — <see cref="BattleSim.AllyLog"/>), которых ещё не
    /// показывали: у них тоже замах и эффект.</summary>
    private List<TurnRecord> WithAllies(IReadOnlyList<TurnRecord> records)
    {
        var allies = _session.Sim.AllyLog;
        // Атака без записи — на своём месте по номеру атаки (ShowOrder.WithAllies): основная раньше второй.
        var all = ShowOrder.WithAllies(records, allies.Skip(_alliesShown), id => _session.State.Unit(id)?.Hp ?? 0);
        _alliesShown = allies.Count;
        return all;
    }

    /// <summary>Что нужно показу конца боя, снятое до конца боя модели (<see cref="GameSession.FinishBattle"/> снимает превращения и уводит
    /// призванных из состава): юниты и какой откат у превращённых — вытягивание уровня <c>UDRAINLVL</c>, двойник <c>UDOPPEL</c>, прочее
    /// <c>UTRANSF</c> (стойкий код эффекта юнита решает имя, <c>0x6525d7</c>, ранг 1).</summary>
    public sealed record EndSnapshot(IReadOnlyDictionary<string, BattleUnit> Units, IReadOnlyDictionary<string, string> Undo, int NotesFrom);

    /// <summary>Снимок для <see cref="PlayEnd"/> — звать до конца боя модели.</summary>
    public EndSnapshot BeforeEnd()
    {
        var units = _session.State.Units.ToDictionary(u => u.Id, u => u, StringComparer.OrdinalIgnoreCase);
        var undo = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var u in _session.State.Units)
            if (u.LevelDrained) undo[u.Id] = "UDRAINLVL";
            else if (u.TransformedDoppelganger) undo[u.Id] = "UDOPPEL";
            else if (u.OrigType is not null) undo[u.Id] = "UTRANSF";
        return new EndSnapshot(units, undo, _session.State.Notes.Count);
    }

    /// <summary>
    /// Конец боя на поле (шаги 13–16 просмотра боя, <c>0x630a7b</c>): сообщения, которые модель дописала концом боя, по очереди — уход
    /// призванного <c>USUMMONANIM</c> (шаг 14, код 10), откат превращения <c>UTRANSF</c>/<c>UDOPPEL</c>/<c>UDRAINLVL</c> (шаги 13 и 16, код 4),
    /// рост <c>UPGRADE{раса}</c> (шаг 15, код 0; раса — владельца группы, <paramref name="raceTypeOf"/>) на месте юнита, подслой 12; облик
    /// меняется на половине; звуки — <see cref="LastEdict.Data.Sound.BattleNoteSounds"/> (у отката ещё <c>UTRANSF</c>, <c>0x5b4cb0</c> — файла нет).
    /// Потом <paramref name="done"/> — хозяин снимает экран (решение пользователя 28.09: экран ждёт анимаций конца боя). Без окна — сразу.
    /// </summary>
    public void PlayEnd(EndSnapshot before, Func<string, int> raceTypeOf, Action done)
    {
        ArgumentNullException.ThrowIfNull(before);
        ArgumentNullException.ThrowIfNull(raceTypeOf);
        ArgumentNullException.ThrowIfNull(done);
        _playRun++;   // прежний показ (если шёл) больше не трогает поле
        _endDone = done;
        var notes = _session.State.Notes;
        double at = 0;
        for (int i = Math.Max(before.NotesFrom, NotesHeard); i < notes.Count; i++)
        {
            var note = notes[i];
            if (!before.Units.TryGetValue(note.UnitId, out var unit)) continue;
            string? name = note.Kind switch
            {
                BattleNoteKind.Unsummoned => "USUMMONANIM",
                BattleNoteKind.Untransformed => before.Undo.GetValueOrDefault(note.UnitId, "UTRANSF"),
                BattleNoteKind.LevelUp => LastEdict.Data.Sound.BattleNoteSounds.Of(note.Kind, raceTypeOf(note.UnitId)),
                _ => null,
            };
            var sprite = name is null || !unit.OnField && note.Kind != BattleNoteKind.Unsummoned ? null : _scene.UnitEffect(name, unit);
            int frames = sprite is null ? 0 : _field.Length([sprite]);
            string sound = LastEdict.Data.Sound.BattleNoteSounds.Of(note.Kind, raceTypeOf(note.UnitId));
            // Надпись и вспышка шагов 13, 14, 16 (0x63679b, 0x6368c3): откат понижения уровня — «Восстанавливает силы» X008TA0029 и пурпурным
            // (шаг 16, 0x6367e7: 0x1b − 4), прочий откат — «Прежний облик!»; изгнание в конце боя — без надписи (TurnStartFace). Рост — опыт (ShowEnd, XpNotes).
            var (text, flash) = note.Kind == BattleNoteKind.Untransformed && sprite is not null && name == "UDRAINLVL"
                ? (Text("X008TA0029"), new BattleFlash(BattleFace.DrainLevel, true))
                : TurnStartFace(note.Kind, unit, sprite is not null);
            GD.Print(FormattableString.Invariant($"battle end {unit.Id} {note.Kind} эффект={sprite?.Name ?? "-"} звук={sound} с={at:0} до={frames * AnimFrameMs:0} надпись={(text is null ? "-" : LastEdict.Data.Ui.UiTextStyle.Parse(text).Text.Replace(' ', '_'))}"));
            Later(at, () =>
            {
                if (sprite is not null) _field.PlayOnce(sprite, "EFFECT", unit);
                Queue(unit.Id, [new LastEdict.Data.Sound.SoundCue(0, LastEdict.Data.Sound.BattleNoteSounds.Archive, sound)]);
                if (text is not null) Note(unit, text);   // 0x6370be: на группу стороны юнита шага, как начало хода
                if (flash is { } f) _field.Flash(_scene, unit, f, src: "actor");
            });
            // Облик на половине: призванный уходит с поля, превращённый — прежний (модель уже откатила тип), выросший — нового типа
            // (BattleAftermath.Award); портрет в панели — тоже (замечание пользователя 14.10).
            Later(at + (frames / 2 * AnimFrameMs), () =>
            {
                _field.Show(unit, BattleAction.Idle);
                RefreshPanels();
            });
            at += Math.Max(frames * AnimFrameMs, text is null || Instant ? 1 : NoteMs);
        }
        NotesHeard = notes.Count;
        Later(at, EndDone);
    }

    /// <summary>Продолжение хозяина после конца боя (<see cref="PlayEnd"/>): зовётся один раз — по концу показа или при исключении в нём.</summary>
    private Action? _endDone;

    private void EndDone()
    {
        if (_endDone is not { } done) return;
        _endDone = null;
        done();
    }

    /// <summary>Шаг показа ходов ИИ: начало хода следующего юнита (отложенное сессией до конца показа прежнего действия —
    /// <see cref="BattleSession.HoldTurnStart"/>: тики, откаты, снятие паралича) со своими картинками и надписями, поле по нынешнему
    /// состоянию (метка под ходящим), одно действие, его показ — и следующий шаг. Дошли до хода игрока — ждать.</summary>
    private void NextStep(int limit)
    {
        double late = _stepDue is { } due ? Now - due : 0;
        _stepDue = null;
        double started = Now;
        if (_session.TurnStartPending)
        {
            Refresh();   // поле — по итогу прежнего действия, тики ещё не прошли
            _session.StartTurn();
            // Картинки тиков — разом в потоках, а не по одной в кадре показа (замечание 35); пока идут тики (или думает человек) — в фоне
            // картинки хода нового ходящего (WarmNext: до StartTurn ходящего ещё нет).
            if (!Instant) _field.Prefetch(PendingSprites());
            WarmNext();
            double ticks = Pending(0);
            if (ticks > 0 && !Instant)
            {
                _stepDue = Now + ticks;
                Later(ticks, () => NextStep(limit));
                return;
            }
        }
        Refresh();
        double field = Now - started;
        if (_session.Finished || _session.WaitsPlayer || limit <= 0)
        {
            _playing = false;
            return;
        }
        int uses = _session.ItemUses.Count;
        var (records, numbers) = AiStep();
        // Предмет ИИ (сфера, талисман, зелье) — тот же шаг предмета, что у человека (BattleScreen.Items.cs): записей хода у него нет.
        double length = _session.ItemUses.Count > uses ? ItemStep(_session.ItemUses[^1], Pending(0)) : Animate(records, numbers);
        Timing(records.Count > 0 ? records[0].Unit : "-", field, Now - started, length, late);
        _stepDue = Now + length;
        Later(length, () => NextStep(limit - 1));
    }

    /// <summary>Начать отложенный ход (<see cref="BattleSession.StartTurn"/>) — для проверок, что ведут бой сессией напрямую; всегда истина.</summary>
    private bool StartedTurn()
    {
        _session.StartTurn();
        return true;
    }

    /// <summary>Часы показа, мс (замеры шагов, замечание 35).</summary>
    private static readonly System.Diagnostics.Stopwatch Clock = System.Diagnostics.Stopwatch.StartNew();

    private static double Now => Clock.Elapsed.TotalMilliseconds;

    /// <summary>Когда должен начаться следующий шаг показа (мс <see cref="Clock"/>); null — шаг не ждут.</summary>
    private double? _stepDue;

    /// <summary>Замер шага показа: сборка поля и панелей, вся синхронная часть шага (поле, модель, подготовка картинок), длина показа и
    /// опоздание начала шага против плана (кадр, зависший на разборе картинок, сдвигает все таймеры). Без окна — молчит.</summary>
    private void Timing(string unit, double field, double sync, double length, double late)
    {
        StepsShown++;
        SyncMsMax = Math.Max(SyncMsMax, sync);
        if (Instant) return;
        GD.Print(FormattableString.Invariant($"battle step {unit} поле={field:0.0}мс синхронно={sync:0.0}мс показ={length:0}мс опоздание={late:0.0}мс"));
    }

    /// <summary>Шагов показа и наибольшая синхронная часть шага, мс — для проверок.</summary>
    public int StepsShown { get; private set; }

    public double SyncMsMax { get; private set; }

    /// <summary>Сделать через <paramref name="ms"/> мс, если показ не оборван и экран ещё открыт; 0 и без окна — сразу. Исключение в шаге
    /// показа не оставляет экран навсегда «играющим»: показ обрывается, продолжение хозяина (<see cref="PlayEnd"/>) всё равно зовётся.
    /// Срок — по часам от мига вызова (<see cref="Clock"/>), проверка — каждый кадр (<see cref="_Process"/>). Таймер дерева
    /// (<c>CreateTimer</c>) прежде отсчитывал от начала кадра: после долгого кадра (разбор картинок) срабатывал раньше на всю его длину —
    /// следующий шаг пересобирал поле посреди анимаций (замер: на 0,02–0,15 с раньше плана; замечание 13).</summary>
    private void Later(double ms, Action act)
    {
        if (ms <= 0 || Instant)
        {
            Guarded(act);
            return;
        }
        _later.Add((Now + ms, _laterOrder++, _playRun, act));
    }

    /// <summary>Отложенные шаги показа: срок (мс <see cref="Clock"/>), порядок постановки, номер показа, действие.</summary>
    private readonly List<(double Due, long Order, int Run, Action Act)> _later = new();

    private long _laterOrder;

    public override void _Process(double delta)
    {
        if (_later.Count > 0)
        {
            double now = Now;
            while (Due(now) is { } next)
            {
                var (_, _, run, act) = _later[next];
                _later.RemoveAt(next);
                if (run == _playRun && IsInsideTree()) Guarded(act);
            }
        }
        StepRemote();   // бой людей по сети: решение другой стороны (BattleScreen.Net.cs)
        StepScript();   // драйвер двух машин: свои решения сам
        if (SyncButtons()) _panel.Sync();   // конец показа (шаг выше) и начало хода человека — кнопки панели (BattleScreen.Buttons.cs)
    }

    /// <summary>Самый ранний наступивший срок (при равных — поставленный раньше); нет — null.</summary>
    private int? Due(double now)
    {
        int? best = null;
        for (int i = 0; i < _later.Count; i++)
            if (_later[i].Due <= now && (best is not { } b || _later[i].Due < _later[b].Due
                                                          || _later[i].Due == _later[b].Due && _later[i].Order < _later[b].Order))
                best = i;
        return best;
    }

    private void Guarded(Action act)
    {
        try
        {
            act();
        }
        catch (Exception e)
        {
            Failures.Report(this, "BattleScreen.Later", e);
            StopPlaying();
            EndDone();
        }
    }

    /// <summary>Оборвать показ (быстрый бой, автоматическое завершение, закрытие экрана).</summary>
    private void StopPlaying()
    {
        _playRun++;
        _playing = false;
        _later.Clear();
        EndDone();   // оборванный показ конца боя — хозяин всё равно получает продолжение («Закрыть», ход ИИ)
    }
}
