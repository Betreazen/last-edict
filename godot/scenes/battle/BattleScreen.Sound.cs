using System;
using System.Collections.Generic;
using System.Linq;
using D2.Data.Gfx;
using D2.Data.Sound;
using D2.Rules.Battle;
using Godot;

namespace D2.Godot;

/// <summary>Звуки действий боя (<c>docs/ai-screen.md</c> §12): план действия — <see cref="BattleSoundPlan"/>; действия идут очередью, у
/// каждого свой отрезок времени (ходы ИИ модель решает разом, а звучать им — по одному).</summary>
public partial class BattleScreen
{
    /// <summary>Отрезок действия: не короче <see cref="ActionSeconds"/> и не короче его звуков с хвостом (ранг 3 — темп анимаций поля).</summary>
    private const double ActionSeconds = 0.8, ActionTail = 0.3;

    private readonly Random _soundRandom = new();

    /// <summary>Когда (время движка, с) может начаться звук следующего действия.</summary>
    private double _soundsFreeAt;

    /// <summary>Поставить звуки записей хода в очередь: сначала тики ядов (они — в начале хода), потом действия; записи разных
    /// юнитов — разные действия. <paramref name="actorType"/> — чьи звуки у действия (боевой предмет — юнит предмета),
    /// <paramref name="itemStep"/> — шаг предмета (<see cref="BattleSoundPlan.Of"/>: удар сразу, без звука эффекта).</summary>
    private void Sounds(IReadOnlyList<TurnRecord> records, string? actorType = null, bool itemStep = false)
    {
        var dots = _session.Sim.DotLog;
        if (_dotsHeard < dots.Count)
        {
            Queue(dots[_dotsHeard].Unit, BattleSoundPlan.OfTicks(dots.Skip(_dotsHeard)));
            _dotsHeard = dots.Count;
        }
        // Сообщения без удара (откат долгого превращения, снятие паралича) — в начале хода юнита, до его действия.
        var notes = _session.State.Notes;
        for (; NotesHeard < notes.Count; NotesHeard++)
            Queue(notes[NotesHeard].UnitId, [new SoundCue(0, BattleNoteSounds.Archive, BattleNoteSounds.Of(notes[NotesHeard].Kind, -1))]);
        foreach (var action in Actions(records))
        {
            // Звук эффекта (SNDINFO+0x30) движок ставит в стадии HEFF (0x63a30f в 0x639f2b): нет картинки HEFF у ходящего — нет и звука.
            // Боевой предмет — звуки юнита предмета; шаг предмета звука эффекта не имеет вовсе (itemStep, BattleSoundPlan).
            bool heff = actorType is not null || _session.State.Unit(action[0].Unit) is not { } actor
                        || _scene.CasterEffects(actor.Type.GlobalId, TargetsAttackers(actor, action)).Count > 0;
            // У шага предмета удар цели начинается через кадр после TUCH (TouchReactFrame), а звук удара — сразу: DEATH — на этот кадр позже.
            Func<string, double?> death = itemStep ? id => DeathAfter(id) + (TouchReactFrame * AnimFrameMs / 1000.0) : DeathAfter;
            Queue(action[0].Unit, BattleSoundPlan.Of(action, _session.State.Unit, GameSound.Units, _soundRandom.Next, actorType, heff, itemStep,
                                                     death));
        }
    }

    /// <summary>Через сколько секунд после удара звучит <c>DEATH</c> павшего <paramref name="target"/>: в конце его удара <c>HHIT</c>
    /// (<c>0x6533fd</c> → <c>0x65363e</c>); без «дополнительной анимации боя» и в бою без показа удара цели нет — нет и звука (<c>0x6517e1</c>).</summary>
    private double? DeathAfter(string target)
    {
        if (!HurtShown || _session.State.Unit(target) is not { } unit) return null;
        return _field.Length(_scene.UnitSprites(unit, BattleAction.Hurt, unit.Type.SizeSmall)) * AnimFrameMs / 1000.0;
    }

    /// <summary>Сколько тиков журнала уже прозвучало.</summary>
    private int _dotsHeard;

    /// <summary>Сколько сообщений боя (<see cref="BattleState.Notes"/>) уже прозвучало; конец боя модели (уход призванных, откат
    /// превращений, рост) дописывает их после экрана — их играет хозяин (<c>MapView.PlayBattleNotes</c>).</summary>
    public int NotesHeard { get; private set; }

    private void Queue(string who, IReadOnlyList<SoundCue> cues)
    {
        if (cues.Count == 0) return;
        var audio = GameAudio.Of(this);
        GameLog.Verbose(FormattableString.Invariant($"battle sounds {who} {string.Join(",", cues.Select(c => FormattableString.Invariant($"{c.Name}@{c.At:0.###}")))}"));
        double now = Time.GetTicksMsec() / 1000.0, start = Math.Max(now, _soundsFreeAt);
        _soundsFreeAt = start + Math.Max(ActionSeconds, cues.Max(c => c.At) + ActionTail);
        foreach (var cue in cues)
        {
            double delay = start - now + cue.At;
            if (delay <= 0) audio.PlayFx(cue.Archive, cue.Name, cue.Volume);
            else GetTree().CreateTimer(delay).Timeout += () =>
            {
                if (IsInstanceValid(this) && IsInsideTree()) audio.PlayFx(cue.Archive, cue.Name, cue.Volume);   // бой закрыт — хвост молчит
            };
        }
    }

    /// <summary>Записи подряд одного юнита — одно действие.</summary>
    private static IEnumerable<IReadOnlyList<TurnRecord>> Actions(IReadOnlyList<TurnRecord> records)
    {
        var action = new List<TurnRecord>();
        foreach (var record in records)
        {
            if (action.Count > 0 && !string.Equals(action[0].Unit, record.Unit, StringComparison.OrdinalIgnoreCase))
            {
                yield return action;
                action = new List<TurnRecord>();
            }
            action.Add(record);
        }
        if (action.Count > 0) yield return action;
    }
}
