using System;
using System.Collections.Generic;
using System.Linq;
using LastEdict.Authoring.Workspaces;
using LastEdict.Data.Content.Schema;
using LastEdict.Rules.Battle;
using LastEdict.Rules.Units;
using Godot;

namespace LastEdict.Godot;

/// <summary>
/// Бой юнита во вкладке «Юниты» (Студия 2, задача 3): атака (первая или вторая) — эффект из реестра, источник, дальность, числа, уровень,
/// «до конца боя», крит, обереги, призыв/превращение; иммунитеты к источникам и эффектам; ауры (модификатор и кому) и свой модификатор
/// (элемент и число). Каждая кнопка «Применить» — одна команда <see cref="UnitWorkspace"/>.
/// </summary>
public partial class StudioUnitCombat : VBoxContainer
{
    private static readonly string[] SourceTitles = ["Оружие", "Разум", "Жизнь", "Смерть", "Огонь", "Вода", "Земля", "Воздух"];
    private static readonly (ReachKind Kind, string Title)[] Reaches = [(ReachKind.Adjacent, "Соседний"), (ReachKind.Any, "Любой"), (ReachKind.All, "Все (площадная)")];
    private static readonly (AuraReach Kind, string Title)[] AuraReaches =
        [(AuraReach.Group, "свой отряд"), (AuraReach.Adjacent, "соседи по отряду"), (AuraReach.Enemies, "отряд врага"), (AuraReach.Self, "сам")];
    private static readonly string[] LevelTitles = ["нет", "один раз", "всегда"];
    private static readonly (ModifierElementType Type, string Title)[] ElementTypes =
    [
        (ModifierElementType.Armor, "Броня, +ед."), (ModifierElementType.QtyDamage, "Урон, %"), (ModifierElementType.Power, "Точность, %"),
        (ModifierElementType.Initiative, "Инициатива, %"), (ModifierElementType.Regeneration, "Регенерация, %"),
        (ModifierElementType.Leadership, "Лидерство, +ед."),
    ];

    private readonly UnitWorkspace _ws;
    private readonly OptionButton _which = new(), _source = new(), _reach = new();
    private readonly StudioPicker _effect = new(), _wardPick = new(), _summonPick = new();
    private readonly SpinBox _initiative = Spin(0, 200), _accuracy = Spin(0, 100), _damage = Spin(0, 9999), _heal = Spin(0, 9999), _level = Spin(0, 10);
    private readonly CheckBox _infinite = new() { Text = "до конца боя" }, _crit = new() { Text = "критический удар" };
    private readonly LineEdit _attackTitle = new() { PlaceholderText = "название атаки" };
    private readonly Label _wards = new() { AutowrapMode = TextServer.AutowrapMode.WordSmart }, _summons = new() { AutowrapMode = TextServer.AutowrapMode.WordSmart };
    private readonly OptionButton[] _sourceImmunity = [.. Enumerable.Range(0, 8).Select(_ => Levels())];
    private readonly Dictionary<AttackClass, OptionButton> _classImmunity = Enum.GetValues<AttackClass>().ToDictionary(c => c, _ => Levels());
    private readonly VBoxContainer _auraRows = new();
    private readonly OptionButton _elementType = new();
    private readonly SpinBox _elementValue = Spin(-999, 999);
    private readonly LineEdit _modifierTitle = new() { PlaceholderText = "название модификатора" };
    private List<PickItem> _modifierItems = [];
    private List<string> _wardIds = [], _summonIds = [];
    private string? _unit;

    private readonly Action _accepted;

    /// <param name="accepted">Своя правка принята — вкладка не перезаполняет форму (набранное в других блоках не стирается).</param>
    public StudioUnitCombat(UnitWorkspace ws, Action accepted)
    {
        _ws = ws ?? throw new ArgumentNullException(nameof(ws));
        _accepted = accepted ?? throw new ArgumentNullException(nameof(accepted));
        AddThemeConstantOverride("separation", 6);
        _which.AddItem("первая атака");
        _which.AddItem("вторая атака");
        foreach (string s in SourceTitles) _source.AddItem(s);
        foreach (var (_, t) in Reaches) _reach.AddItem(t);
        foreach (var (_, t) in ElementTypes) _elementType.AddItem(t);
        _elementValue.Value = 20;

        AddChild(Title("Атака"));
        AddChild(Grid(("Какая", _which), ("Название", _attackTitle), ("Эффект", _effect), ("Источник", _source), ("Дальность и цели", _reach),
                      ("Инициатива", _initiative), ("Точность, %", _accuracy), ("Урон", _damage), ("Лечение", _heal), ("Уровень эффекта", _level),
                      ("", _infinite), ("", _crit)));
        AddChild(Grid(("Обереги цели", Row(_wards, _wardPick, Button("+", AddWard), Button("×", () => { _wardIds.Clear(); ShowLists(); }))),
                      ("Призыв / превращение", Row(_summons, _summonPick, Button("+", AddSummon), Button("×", () => { _summonIds.Clear(); ShowLists(); })))));
        AddChild(Button("Применить атаку", ApplyAttack, primary: true));

        AddChild(Title("Иммунитеты и защиты"));
        AddChild(Grid([.. SourceTitles.Select((t, i) => ($"к источнику «{t}»", (Control)_sourceImmunity[i]))]));
        AddChild(Grid([.. _classImmunity.Select(p => ($"к эффекту «{EffectTitle(p.Key)}»", (Control)p.Value))]));
        AddChild(Button("Применить иммунитеты", ApplyImmunities, primary: true));

        AddChild(Title("Ауры (пока юнит на поле боя)"));
        AddChild(_auraRows);
        AddChild(Row(Button("+ аура", () => AddAuraRow(null, AuraReach.Group)), Button("Применить ауры", ApplyAuras, primary: true)));
        AddChild(Row(new Label { Text = "Свой модификатор:", ThemeTypeVariation = SettingsTheme.HelpLabel }, _modifierTitle, _elementType, _elementValue,
                     Button("Создать", CreateModifier)));

        _which.ItemSelected += _ => ShowAttack();   // только поля атаки: строки аур и иммунитеты — как набраны
    }

    /// <summary>Имя эффекта класса атаки — строкой содержимого (эффект с этим поведением), иначе имя класса.</summary>
    private string EffectTitle(AttackClass cls)
    {
        string behavior = System.Text.Json.JsonNamingPolicy.CamelCase.ConvertName(cls.ToString());
        return _ws.Effects.FirstOrDefault(e => e.Behavior == behavior) is { } e && _ws.Session.NameOf(e.Id) is { } n && n != e.Id[(e.Id.LastIndexOf('/') + 1)..]
            ? n : cls.ToString();
    }

    private static SpinBox Spin(int min, int max) => new() { MinValue = min, MaxValue = max, SizeFlagsHorizontal = SizeFlags.ExpandFill };

    private static OptionButton Levels()
    {
        var pick = new OptionButton();
        foreach (string t in LevelTitles) pick.AddItem(t);
        return pick;
    }

    private static Label Title(string text) => new() { Text = text, ThemeTypeVariation = SettingsTheme.GroupLabel };

    private static Button Button(string text, Action pressed, bool primary = false)
    {
        var b = new Button { Text = text, ThemeTypeVariation = primary ? SettingsTheme.Primary : "" };
        b.Pressed += pressed;
        return b;
    }

    private static HBoxContainer Row(params Control[] items)
    {
        var row = new HBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        foreach (var c in items) row.AddChild(c);
        items[0].SizeFlagsHorizontal = SizeFlags.ExpandFill;
        return row;
    }

    private static GridContainer Grid(params (string Title, Control Field)[] fields)
    {
        var grid = new GridContainer { Columns = 4, SizeFlagsHorizontal = SizeFlags.ExpandFill };
        foreach (var (title, field) in fields)
        {
            grid.AddChild(new Label { Text = title, ThemeTypeVariation = SettingsTheme.HelpLabel });
            field.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            grid.AddChild(field);
        }
        return grid;
    }

    private string ModifierName(string id) => _ws.Modifiers.FirstOrDefault(m => m.Id == id) is { } m
        ? $"{(m.Comment is { Length: > 0 } c ? c : id[(id.LastIndexOf('/') + 1)..])} ({string.Join(", ", m.Elements.Select(Element))})"
        : _ws.Session.NameOf(id);

    private static string Element(ModifierElementDef e) =>
        $"{e.Type}{(e.Number is { } n ? $" {n:+#;-#;0}" : "")}{(e.Percent is { } p ? $" {p:+#;-#;0}%" : "")}";

    /// <summary>Показать бой юнита <paramref name="unit"/> (null — пусто).</summary>
    public void ShowUnit(string? unit)
    {
        _unit = unit;
        Visible = unit is not null;
        if (unit is null) return;
        ShowAttack();
        _modifierItems = [.. _ws.Modifiers.Select(m => new PickItem(m.Id, ModifierName(m.Id)))];
        _wardPick.SetItems(_modifierItems, null);
        _summonPick.SetItems([.. _ws.Units().Select(u => new PickItem(u.Id, $"{u.Name} · {u.Id[(u.Id.LastIndexOf('/') + 1)..]}"))], null);
        ShowRest(unit);
    }

    /// <summary>Поля атаки (первой или второй) выбранного юнита.</summary>
    private void ShowAttack()
    {
        if (_unit is not { } unit) return;
        bool second = _which.Selected == 1;
        var a = _ws.AttackFormOf(unit, second);
        _effect.SetItems([.. _ws.Effects.Select(e => new PickItem(e.Id, $"{_ws.Session.NameOf(e.Id)} · {e.Behavior}"))], a.Effect);
        _source.Selected = (int)a.Source;
        _reach.Selected = Math.Max(0, Array.FindIndex(Reaches, r => r.Kind == a.Reach));
        (_initiative.Value, _accuracy.Value, _damage.Value, _heal.Value, _level.Value) = (a.Initiative, a.Accuracy, a.Damage ?? 0, a.Heal ?? 0, a.Level ?? 0);
        (_infinite.ButtonPressed, _crit.ButtonPressed, _attackTitle.Text) = (a.Infinite, a.CriticalHit, a.Title);
        (_wardIds, _summonIds) = ([.. a.Wards], [.. a.Units]);
        ShowLists();
    }

    /// <summary>Иммунитеты и ауры выбранного юнита.</summary>
    private void ShowRest(string unit)
    {
        var def = _ws.Session.Report.Content?.Find(unit) as UnitDef;
        for (int i = 0; i < 8; i++)
            _sourceImmunity[i].Selected = def?.Immunities.FirstOrDefault(x => (int)x.Source == i) is { } s ? (int)s.Level - 1 : 0;
        foreach (var (cls, pick) in _classImmunity)
            pick.Selected = def?.ClassImmunities.FirstOrDefault(x => x.AttackClass == cls) is { } c ? (int)c.Level - 1 : 0;
        foreach (var child in _auraRows.GetChildren()) { _auraRows.RemoveChild(child); child.QueueFree(); }
        foreach (var aura in def?.Auras ?? []) AddAuraRow(aura.Modifier, aura.Reach);
    }


    private void ShowLists()
    {
        _wards.Text = _wardIds.Count == 0 ? "—" : string.Join("; ", _wardIds.Select(ModifierName));
        _summons.Text = _summonIds.Count == 0 ? "—" : string.Join("; ", _summonIds.Select(id => _ws.Session.NameOf(id)));
    }

    private void AddWard()
    {
        if (_wardPick.Selected is { } id) _wardIds.Add(id);
        ShowLists();
    }

    private void AddSummon()
    {
        if (_summonPick.Selected is { } id) _summonIds.Add(id);
        ShowLists();
    }

    private void AddAuraRow(string? modifier, AuraReach reach)
    {
        var pick = new StudioPicker();
        pick.SetItems(_modifierItems, modifier);
        var to = new OptionButton();
        foreach (var (_, t) in AuraReaches) to.AddItem(t);
        to.Selected = Math.Max(0, Array.FindIndex(AuraReaches, r => r.Kind == reach));
        var row = Row(pick, to);
        row.AddChild(Button("×", () => { _auraRows.RemoveChild(row); row.QueueFree(); }));
        _auraRows.AddChild(row);
    }

    /// <summary>Форма атаки сейчас.</summary>
    public AttackForm CurrentAttack() => new()
    {
        Title = _attackTitle.Text.Trim(), Effect = _effect.Selected, Source = (AttackSource)Math.Max(0, _source.Selected),
        Reach = Reaches[Math.Max(0, _reach.Selected)].Kind, Initiative = (int)_initiative.Value, Accuracy = (int)_accuracy.Value,
        Damage = _damage.Value > 0 ? (int)_damage.Value : null, Heal = _heal.Value > 0 ? (int)_heal.Value : null,
        Level = _level.Value > 0 ? (int)_level.Value : null, Infinite = _infinite.ButtonPressed, CriticalHit = _crit.ButtonPressed,
        Wards = [.. _wardIds], Units = [.. _summonIds],
    };

    private void ApplyAttack()
    {
        if (_unit is { } id && _ws.EditAttack(id, CurrentAttack(), second: _which.Selected == 1)) _accepted();
    }

    private void ApplyImmunities()
    {
        if (_unit is not { } id) return;
        var sources = Enumerable.Range(0, 8).Where(i => _sourceImmunity[i].Selected > 0)
            .Select(i => new SourceImmunity((AttackSource)i, (ImmunityLevel)(_sourceImmunity[i].Selected + 1))).ToList();
        var classes = _classImmunity.Where(p => p.Value.Selected > 0)
            .Select(p => new ClassImmunity(p.Key, (ImmunityLevel)(p.Value.Selected + 1))).ToList();
        if (_ws.SetImmunities(id, sources, classes)) _accepted();
    }

    /// <summary>Ауры по строкам.</summary>
    public IReadOnlyList<UnitAuraDef> CurrentAuras() =>
        [.. _auraRows.GetChildren().OfType<HBoxContainer>()
            .Select(row => (Pick: row.GetChildren().OfType<StudioPicker>().FirstOrDefault(), To: row.GetChildren().OfType<OptionButton>().FirstOrDefault()))
            .Where(r => r.Pick?.Selected is not null && r.To is not null)
            .Select(r => new UnitAuraDef { Modifier = r.Pick!.Selected!, Reach = AuraReaches[Math.Max(0, r.To!.Selected)].Kind })];

    private void ApplyAuras()
    {
        if (_unit is { } id && _ws.SetAuras(id, CurrentAuras())) _accepted();
    }

    private void CreateModifier()
    {
        var type = ElementTypes[Math.Max(0, _elementType.Selected)].Type;
        string title = _modifierTitle.Text.Trim();
        if (_ws.CreateModifier(title.Length > 0 ? title : _elementType.Text, [UnitWorkspace.Element(type, (int)_elementValue.Value)]) is { } created)
        {
            _accepted();   // строки аур, набранные до создания, остаются
            _modifierItems = [.. _ws.Modifiers.Select(m => new PickItem(m.Id, ModifierName(m.Id)))];
            AddAuraRow(created, AuraReach.Group);   // новый — сразу строкой ауры (её можно убрать)
        }
    }

    /// <summary>Шаги проверки без окна.</summary>
    public void Check(string? effect, int? damage, string? aura, string? reach, string? immuneSource)
    {
        if (effect is not null) _effect.Select(effect);
        if (damage is { } d) _damage.Value = d;
        if (aura is not null)
            AddAuraRow(aura, reach is null ? AuraReach.Group : Enum.Parse<AuraReach>(reach, ignoreCase: true));
        if (immuneSource is not null) _sourceImmunity[(int)Enum.Parse<AttackSource>(immuneSource, ignoreCase: true)].Selected = 2;
    }

    public void PressAttack() => ApplyAttack();

    public void PressAuras() => ApplyAuras();

    public void PressImmunities() => ApplyImmunities();

    public string Line() => $"бой: атака_эффект={_effect.Selected ?? "-"} ауры={CurrentAuras().Count}";
}
