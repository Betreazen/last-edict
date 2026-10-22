using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using LastEdict.Data.Gfx;
using LastEdict.Data.Play;
using LastEdict.Rules.Battle;
using LastEdict.Rules.Map;
using Godot;

namespace LastEdict.Godot;

/// <summary>Проверки «всё ли видно» экрана боя без окна (прогон 3 пользователя 11.10, пункты 4, 5, 7): бой с карты сценария порта
/// (<c>--scenario</c> + <c>--stacks</c>), видимый кадр каждого юнита на поле и портрет в панели — числами по точкам файлов картинок;
/// карточка по правой кнопке держится, пока кнопка нажата.</summary>
public partial class BattleScreen
{
    /// <summary>Бой двух групп карты сценария (<c>--stacks НАП,ЗАЩ</c>, как <c>mapbattle start</c> журнала зонда порта). <c>--squad</c> —
    /// подменить отряд нападающего: <c>ТИП[@УРОВЕНЬ]:КЛЕТКА,…</c> (уровень выше базового — порождённая реализация <c>G000UG…</c>, как у
    /// выросшего в партии юнита).</summary>
    private static BattleState? FromScenario(string[] args)
    {
        if (MapSource.Arg(args, "--stacks") is not { } stacks) return null;
        string[] ids = stacks.Split(',');
        if (ids.Length != 2) throw new FormatException($"--stacks: нужно «нападающий,защитник», есть «{stacks}»");
        var map = MapSource.Load(args).Map;
        var book = GameData.Book;
        var state = MapBattle.Build(map, ids[0], ids[1], book, difficulty: 1, attackerMove: 0);
        // rand() процесса для выбора призываемого из нескольких кандидатов (как бой с карты в игре — GameSession.Crt); зерно постоянное.
        state.ProcessRand ??= new LastEdict.Rules.Rng.CrtRand(1);
        if (MapSource.Arg(args, "--squad") is not { } squad) return state;
        state.Units.RemoveAll(u => u.Attacker);
        int n = 0;
        foreach (string piece in squad.Split(',', StringSplitOptions.RemoveEmptyEntries))
        {
            string[] parts = piece.Split(':');
            string[] typed = parts[0].Split('@');
            int level = typed.Length > 1 ? int.Parse(typed[1], CultureInfo.InvariantCulture) : 0;
            string type = typed[0].ToUpperInvariant();
            if (level == 0) level = book.Rules.Units.TryGetValue(type, out var known) ? known.Level : 1;
            var unit = new MapUnit { Id = $"CHECK{n++:X2}", TypeId = type, Level = level, Hp = 10000 };
            state.Units.Add(MapBattle.Unit(unit, int.Parse(parts[1], CultureInfo.InvariantCulture), book.Rules, isAttacker: true));
        }
        return state;
    }

    /// <summary>Строка проверки видимости: у каждого юнита на поле — кадр покоя с непрозрачными точками (тело, без тени), у каждого в
    /// панели показанной группы — портрет с непрозрачными точками. <c>нет_поля</c>/<c>нет_портрета</c> — id:тип непрошедших.
    /// <c>под_панелью</c> — юниты, у которых больше половины точек тела лежит под фоном показанной панели (<c>IMG_?UNITGROUPBG</c>):
    /// их на экране не видно, хотя кадр есть (пункт 7).</summary>
    private string VisibleLine()
    {
        var noField = new List<string>();
        var noFace = new List<string>();
        var covered = new List<string>();
        var panels = ShownPanelAreas();
        int onField = 0, inPanel = 0;
        foreach (var unit in _session.State.Units.Where(u => u.OnField && u.Alive))
        {
            onField++;
            var (dots, under) = BodyDots(unit, panels);
            if (dots == 0) noField.Add($"{unit.Id}:{unit.Type.Id}");
            else if (under * 2 > dots) covered.Add(FormattableString.Invariant($"{unit.Id}:{unit.Slot}:{100 * under / dots}%"));
            if (!_scene.PanelShown(unit.Attacker)) continue;
            inPanel++;
            int face = _scene.Portrait(unit) is { } portrait ? BattleView.FirstFramePaths(portrait).Take(1).Sum(p => Opaque(p).Count) : 0;
            if (face == 0 || !_field.PanelUnits.Contains(unit.Id)) noFace.Add($"{unit.Id}:{unit.Type.Id}");
        }
        static string List(List<string> items) => items.Count > 0 ? string.Join(",", items) : "-";
        return $"battle visible на_поле={onField} без_кадра={noField.Count} в_панели={inPanel} без_портрета={noFace.Count} " +
               $"нет_поля={List(noField)} нет_портрета={List(noFace)} под_панелью={List(covered)}";
    }

    /// <summary>Фоны показанных сейчас панелей (<c>IMG_LUNITGROUPBG</c>/<c>IMG_RUNITGROUPBG</c>) в координатах поля.</summary>
    private List<Rect2I> ShownPanelAreas()
    {
        var screen = GameData.Content.Screens.First(s => s.Id == Screen);
        var sides = _session.State.Units.Select(u => u.Attacker).Distinct().Select(_scene.PanelLeft).OfType<bool>().Distinct();
        return [.. sides.Select(left => screen.Elements.FirstOrDefault(e => e.Id == $"IMG_{(left ? 'L' : 'R')}UNITGROUPBG")?.Rect)
                        .OfType<LastEdict.Data.Ui.UiRect>().Select(r => new Rect2I(r.X, r.Y, r.Width, r.Height))];
    }

    /// <summary>Непрозрачных точек тела юнита в покое (первый кадр, без тени) и сколько из них под прямоугольниками <paramref name="panels"/>.</summary>
    private (int Dots, int Under) BodyDots(BattleUnit unit, List<Rect2I> panels)
    {
        int dots = 0, under = 0;
        foreach (var sprite in _scene.UnitSprites(unit, BattleAction.Idle, unit.Type.SizeSmall)
                     .Where(s => s.Archive == "BatUnits" && s.Name.Length > 4 && s.Name[^5] != 'S'))   // тень — «…S1A00»
            foreach (string path in BattleView.FirstFramePaths(sprite).Take(1))
            {
                var (count, points) = Opaque(path);
                dots += count;
                foreach (var point in points)
                    if (panels.Any(r => r.HasPoint(point + new Vector2I(sprite.X, sprite.Y)))) under++;
            }
        return (dots, under);
    }

    /// <summary>Непрозрачные точки картинки в координатах холста (файл сборки с обрезкой — по углу из описи, <see cref="GfxTextures.Canvas"/>);
    /// нет файла — пусто.</summary>
    private static (int Count, List<Vector2I> Points) Opaque(string path)
    {
        var points = new List<Vector2I>();
        if (!File.Exists(path)) return (0, points);
        using var image = GfxTextures.Canvas(path);
        if (image is null) return (0, points);
        image.Convert(Image.Format.Rgba8);
        byte[] data = image.GetData();
        int w = image.GetWidth();
        for (int i = 3; i < data.Length; i += 4)
            if (data[i] != 0) points.Add(new Vector2I((i / 4) % w, i / 4 / w));
        return (points.Count, points);
    }

    /// <summary>Проверка «--rmb»: правая кнопка над юнитом открывает карточку, отпускание — закрывает (как на карте, «держится, пока
    /// нажата»). Нажатие и отпускание идут настоящим путём — через вьюпорт.</summary>
    private async System.Threading.Tasks.Task CheckHeldCard()
    {
        if (_session.State.Units.FirstOrDefault(u => u.OnField && u.Alive) is not { } unit) return;
        var slot = unit.Type.SizeSmall ? _scene.Slot(unit.Attacker, unit.Slot) : _scene.BigSlot(unit.Attacker, unit.Slot);
        var size = GetViewport().GetVisibleRect().Size;
        float k = Mathf.Min(size.X / _host.X, size.Y / _host.Y);
        var at = ((size - (_host * k)) / 2f) + (new Vector2(slot.X + (slot.Width / 2f), slot.Y + (slot.Height / 2f)) * k);
        GetViewport().PushInput(new InputEventMouseButton { Position = at, GlobalPosition = at, ButtonIndex = MouseButton.Right, Pressed = true }, true);
        for (int i = 0; i < 3; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        bool opened = CardShown;
        GetViewport().PushInput(new InputEventMouseButton { Position = at, GlobalPosition = at, ButtonIndex = MouseButton.Right, Pressed = false }, true);
        for (int i = 0; i < 3; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        GD.Print($"battle rmb юнит={unit.Id} нажата_карточка={(opened ? 1 : 0)} отпущена_карточка={(CardShown ? 1 : 0)}");
    }
}
