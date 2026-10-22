using System;
using System.Linq;
using LastEdict.Data.Battle;
using LastEdict.Data.Gfx;
using LastEdict.Rules.Battle;
using Godot;

namespace LastEdict.Godot;

/// <summary>Показ применения боевого предмета или зелья — шаг 12 просмотра движка (<c>showAttackEffect</c> → <c>0x6365f2</c>,
/// <see cref="BattleItemUse"/>).</summary>
public partial class BattleScreen
{
    /// <summary>Предмет игрока применён: шаг предмета (<see cref="ItemStep"/>), следующий ход — после конца его картинок.</summary>
    private void ShowItem(BattleItemUse use)
    {
        double length = ItemStep(use, 0);
        if (length <= 0)
        {
            Continue();
            return;
        }
        _playing = true;
        Later(length, () =>
        {
            _playing = false;
            Continue();
        });
    }

    /// <summary>
    /// Шаг предмета, с начала шага (<c>0x6365f2</c>, ранг 1 по листингу и журналам <c>20261017-port-synth</c> BI2 и <c>20261016e-av</c>):
    /// <list type="bullet">
    /// <item>замаха носителя нет; на его месте — анимация применения <c>BatItems.ff</c> (<see cref="BattleScene.ItemEffect"/>, <c>0x651c9c</c>);</item>
    /// <item>в тот же миг на цели — <c>TUCH</c> юнита предмета (<c>GItem.UNIT_ID</c>: у Сферы грома — молния <c>G000UU0008TUCH</c>; прежде порт
    /// его не рисовал — партия 17.10, BI2: «не было анимации на гноме»), через кадр — удар цели <c>HHIT</c> по классу атаки, смерть, надпись;</item>
    /// <item>звуки — замах и <c>TUCH</c> юнита предмета сразу (<c>mage1</c> + <c>unit08a</c>), без звука эффекта.</item>
    /// </list>
    /// Длина шага — по концу всех картинок и снятию надписи (у движка — по счётчику анимаций, 985 мс у <c>ITEM</c> 30 кадров). Шаг начинается
    /// через <paramref name="start"/> мс (у хода ИИ — после непоказанного начала хода, как у <see cref="Animate"/>). Возвращает конец, мс.
    /// </summary>
    private double ItemStep(BattleItemUse use, double start)
    {
        var bearer = _session.State.Unit(use.Bearer);
        var target = _session.State.Unit(use.Target);
        // Вампиризм предмета (сфера с атакой Drain) показ предмета не рисует; его записи — не следующему действию (Animate), иначе HEALTUCH
        // всплыл бы чужим шагом.
        _drainsShown = _session.State.DrainHeals.Count;
        var record = use.Record;
        if (use.Item.UnitId is { Length: > 0 } itemUnit) Later(start, () => Sounds([record], itemUnit, itemStep: true));
        var effect = bearer is null ? null : _scene.ItemEffect(use.Item.Category, bearer);
        int frames = effect is null || Instant ? 0 : _field.Length([effect]);
        if (effect is not null && frames > 0) Later(start, () => _field.PlayOnce(effect, "ITEM", bearer));
        double notesAt = start + (TouchReactFrame * AnimFrameMs);
        // Следующий шаг ждёт и снятия надписи на портрете (счётчик анимаций +0x14d8, как у Act).
        double end = Math.Max(start + (frames * AnimFrameMs), notesAt + (Instant ? 0 : NoteMs));
        // Призыв предметом — цель сам носитель, картинок на цели нет (призванный встаёт сам).
        if (bearer is not null && target is not null && use.Shown?.Class != AttackClass.Summon)
            end = Math.Max(end, React(bearer, target, [record], start, main: true, start, effect?.Name ?? "-", use));
        // Призыв предметом: вспышка призыва (28, чёрным целиком, держится) — на призванном, и основная панель — на его группе, как у движка
        // (журнал 20261020-faces seq 557992: Сфера Сквайра у S143UN0031 — bat_flash 28 у призванного S143UN0048, on = true); прежде порт клал её
        // на носителя (цель записи модели) — чернел портрет того, кто применил сферу.
        if (use.Shown?.Class == AttackClass.Summon)
        {
            var summoned = _session.State.Units.LastOrDefault(u => u.Summoned && use.Bearer.Equals(u.SummonerId, StringComparison.OrdinalIgnoreCase));
            Later(notesAt, () => ShowNotes(summoned?.Id ?? use.Target, [],
                summoned is null ? [] : [(summoned.Id, new BattleFlash(BattleFace.Summoned, true))]));
        }
        else Later(notesAt, () => ShowNotes(use.Target, use.Notes.After([record], _session.State, Text), use.Notes.Faces([record], _session.State)));
        GD.Print(FormattableString.Invariant(
            $"battle item show {effect?.Name ?? "-"} кадров={frames} юнит={use.Item.UnitId} цель={use.Target} класс={use.Shown?.Class.ToString() ?? "-"} до={end:0}"));
        return end;
    }
}
