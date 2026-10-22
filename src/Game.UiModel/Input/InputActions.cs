using System.Text.Json;
using System.Text.Json.Serialization;
using LastEdict.Data.Ui;

namespace LastEdict.Data.Input;

/// <summary>Группа экранов, у которых общие действия ввода (карта, бой, окна города…): подпись — ключ строки.</summary>
public sealed record InputGroup(string Id, string Label, IReadOnlyList<string> Screens);

/// <summary>Явное действие (<c>data/ui/input.json</c>): общие — на всех экранах (<see cref="Group"/> пусто): подтвердить, отмена, стрелки,
/// страницы; <see cref="Godot"/> — имя встроенного действия Godot (<c>ui_accept</c>…), его же ведут фокус окон на <c>Control</c> и
/// прокрутка карты. <see cref="Vk"/> — код клавиши разметки, который действие жмёт на экранах.</summary>
public sealed record InputActionDef(string Id, string Label)
{
    public string? Group { get; init; }
    public string? Godot { get; init; }
    public IReadOnlyList<int> Vk { get; init; } = [];
    public IReadOnlyList<string> Bindings { get; init; } = [];
}

/// <summary>Действие ввода: что жмёт (<see cref="Vk"/> — коды клавиш разметки экранов), где (<see cref="Screens"/>, пусто — везде), чем по
/// умолчанию (<see cref="Defaults"/>: <c>key:Space</c>, <c>pad:A</c>, <c>axis:LeftX-</c> — имена клавиш, кнопок и осей Godot).</summary>
public sealed record InputAction(string Id, string Label, string? Group, IReadOnlyList<string> Screens, IReadOnlyList<int> Vk,
                                 IReadOnlyList<string> Defaults, string? Godot)
{
    public bool Everywhere => Screens.Count == 0;

    /// <summary>Ключ строки подписи группы (<see cref="InputGroup.Label"/>); у общих — null.</summary>
    public string? GroupLabel { get; init; }
}

/// <summary>
/// Действия ввода Last Edict (Э5, Р12: «действия через InputMap, переназначение — сразу для клавиатуры и геймпада»). Разметка экранов
/// оригинала держит горячие клавиши кодами Windows (<see cref="UiElement.Hotkeys"/>), и одна буква значит разное на разных экранах (D —
/// «Защищаться» в бою, «Отпустить воина» в городе). Поэтому действия выводятся из разметки: у каждой группы экранов
/// (<see cref="InputGroup"/>) каждая горячая клавиша элемента, кроме общих (Enter, Esc, стрелки, страницы), — действие
/// <c>группа.ЭЛЕМЕНТ</c> с подписью-подсказкой элемента и клавишей оригинала по умолчанию. Общие действия, группы и кнопки геймпада по
/// умолчанию — свои данные <c>data/ui/input.json</c> (моды кладут свой — наложение по id). Новый экран мода с горячими клавишами в группе —
/// новые действия без кода.
/// </summary>
public sealed record InputConfig
{
    public IReadOnlyList<InputGroup> Groups { get; init; } = [];
    public IReadOnlyList<InputActionDef> Actions { get; init; } = [];

    /// <summary>Экраны, где крестовина — стрелки (прокрутка карты), а не шаг указателя по кнопкам (<see cref="UiNavigation"/>).</summary>
    public IReadOnlyList<string> ArrowScreens { get; init; } = [];

    /// <summary>Привязки геймпада по умолчанию у выведенных действий: id → <c>pad:…</c>/<c>axis:…</c>.</summary>
    public IReadOnlyDictionary<string, IReadOnlyList<string>> Pad { get; init; } = new Dictionary<string, IReadOnlyList<string>>();

    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNameCaseInsensitive = true, ReadCommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static InputConfig FromJson(string json) => JsonSerializer.Deserialize<InputConfig>(json, Json) ?? new InputConfig();

    public static InputConfig? Load(string path) => File.Exists(path) ? FromJson(File.ReadAllText(path)) : null;

    /// <summary>Слой мода поверх: группы и действия — по id целиком, кнопки геймпада — по id действия, экраны стрелок — добавляются.</summary>
    public static InputConfig Merge(InputConfig under, InputConfig over)
    {
        ArgumentNullException.ThrowIfNull(under);
        ArgumentNullException.ThrowIfNull(over);
        var groups = under.Groups.Where(g => over.Groups.All(o => o.Id != g.Id)).Concat(over.Groups).ToList();
        var actions = under.Actions.Where(a => over.Actions.All(o => o.Id != a.Id)).Concat(over.Actions).ToList();
        var pad = new Dictionary<string, IReadOnlyList<string>>(under.Pad);
        foreach (var (id, buttons) in over.Pad) pad[id] = buttons;
        return new InputConfig
        {
            Groups = groups, Actions = actions, Pad = pad,
            ArrowScreens = [.. under.ArrowScreens.Concat(over.ArrowScreens).Distinct(StringComparer.OrdinalIgnoreCase)],
        };
    }
}

public static class InputActions
{
    /// <summary>Действия по разметке экранов и конфигурации: сначала явные (общие и групповые), затем выведенные по группам.</summary>
    public static IReadOnlyList<InputAction> Build(InputConfig config, IReadOnlyList<UiScreen> screens)
    {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(screens);
        var groups = config.Groups.ToDictionary(g => g.Id, StringComparer.Ordinal);
        var result = new List<InputAction>();
        foreach (var def in config.Actions)
        {
            var group = def.Group is { } g ? groups.GetValueOrDefault(g) : null;
            result.Add(new InputAction(def.Id, def.Label, def.Group, group?.Screens ?? [], def.Vk, def.Bindings, def.Godot)
            {
                GroupLabel = group?.Label,
            });
        }
        // Коды клавиш общих действий (Enter, Esc, стрелки, страницы) — их жмут общие действия на любом экране.
        var general = config.Actions.Where(a => a.Group is null).SelectMany(a => a.Vk).ToHashSet();
        var byScreen = screens.ToDictionary(s => s.Id, StringComparer.OrdinalIgnoreCase);
        var taken = result.Select(a => a.Id).ToHashSet(StringComparer.Ordinal);
        foreach (var group in config.Groups)
        {
            // В порядке экранов группы и элементов разметки — так строки и стоят во вкладке «Управление».
            var found = new List<(UiElement Element, List<int> Vk)>();
            foreach (string screen in group.Screens)
            {
                if (!byScreen.TryGetValue(screen, out var s)) continue;
                foreach (var e in s.Elements)
                    foreach (int vk in e.Hotkeys.Where(v => !general.Contains(v)))
                    {
                        int at = found.FindIndex(f => f.Element.Id == e.Id);
                        if (at < 0) found.Add((e, [vk]));
                        else if (!found[at].Vk.Contains(vk)) found[at].Vk.Add(vk);
                    }
            }
            foreach (var (e, vks) in found)
            {
                string id = $"{group.Id}.{e.Id}";
                if (!taken.Add(id)) continue;
                string label = e.Tooltip ?? e.TextId ?? e.Id;
                var defaults = vks.Select(InputNames.KeyOfVk).OfType<string>().Select(k => $"key:{k}")
                                  .Concat(config.Pad.TryGetValue(id, out var pad) ? pad : []).ToList();
                result.Add(new InputAction(id, label, group.Id, group.Screens, vks, defaults, null) { GroupLabel = group.Label });
            }
        }
        return result;
    }

    /// <summary>Действия, которые работают на экране <paramref name="screen"/>: общие и групп, куда экран входит.</summary>
    public static IEnumerable<InputAction> On(IEnumerable<InputAction> actions, string screen) =>
        actions.Where(a => a.Everywhere || a.Screens.Contains(screen, StringComparer.OrdinalIgnoreCase));

    /// <summary>Пересекаются ли действия по экранам (общее — со всеми): одна привязка у двоих таких — конфликт.</summary>
    public static bool Overlap(InputAction a, InputAction b)
    {
        ArgumentNullException.ThrowIfNull(a);
        ArgumentNullException.ThrowIfNull(b);
        return a.Everywhere || b.Everywhere || a.Screens.Intersect(b.Screens, StringComparer.OrdinalIgnoreCase).Any();
    }

    /// <summary>Привязки действия: переопределённые пользователем (<paramref name="overrides"/>), иначе по умолчанию.</summary>
    public static IReadOnlyList<string> BindingsOf(InputAction action, IReadOnlyDictionary<string, IReadOnlyList<string>>? overrides)
    {
        ArgumentNullException.ThrowIfNull(action);
        return overrides is not null && overrides.TryGetValue(action.Id, out var own) ? own : action.Defaults;
    }

    /// <summary>Новые переопределения: у <paramref name="action"/> привязки устройства (<c>key</c> или <c>pad</c>: кнопки и оси) — одна
    /// <paramref name="binding"/> (null — снять); у пересекающихся по экранам действий та же привязка снимается (конфликт). Совпавшие с
    /// умолчанием (без учёта порядка) переопределения не хранятся.</summary>
    public static Dictionary<string, IReadOnlyList<string>> Bind(IReadOnlyList<InputAction> actions,
                                                                 IReadOnlyDictionary<string, IReadOnlyList<string>>? overrides,
                                                                 InputAction action, string device, string? binding)
    {
        ArgumentNullException.ThrowIfNull(actions);
        ArgumentNullException.ThrowIfNull(action);
        var result = overrides is null ? new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal)
            : new Dictionary<string, IReadOnlyList<string>>(overrides, StringComparer.Ordinal);
        var kept = BindingsOf(action, overrides).Where(b => DeviceOf(b) != device);
        result[action.Id] = [.. binding is null ? kept : kept.Append(binding)];
        foreach (var other in actions.Where(a => binding is not null && a.Id != action.Id && Overlap(a, action)))
        {
            var mine = BindingsOf(other, result);
            if (mine.Contains(binding!)) result[other.Id] = [.. mine.Where(b => b != binding)];
        }
        foreach (var a in actions)
            if (result.TryGetValue(a.Id, out var own) && own.ToHashSet(StringComparer.Ordinal).SetEquals(a.Defaults)) result.Remove(a.Id);
        return result;
    }

    /// <summary>Подсказки кнопок геймпада для экранов, что сейчас принимают ввод (полоса внизу, когда играют геймпадом): щелчок указателем,
    /// «Отмена», затем действия групп этих экранов с кнопкой геймпада — по первой кнопке на действие, не больше <paramref name="limit"/>. Оси
    /// (стики, курки), крестовина и движение указателя не подсказываются — их назначение одно на всю игру.</summary>
    public static IReadOnlyList<(InputAction Action, string Binding)> Hints(IReadOnlyList<InputAction> actions,
                                                                             IReadOnlyDictionary<string, IReadOnlyList<string>>? overrides,
                                                                             IReadOnlyCollection<string> screens, int limit = 7)
    {
        ArgumentNullException.ThrowIfNull(actions);
        ArgumentNullException.ThrowIfNull(screens);
        static bool Button(string b) => b.StartsWith("pad:", StringComparison.Ordinal) && !b.StartsWith("pad:Dpad", StringComparison.Ordinal);
        var on = actions.Where(a => a.Everywhere || a.Screens.Intersect(screens, StringComparer.OrdinalIgnoreCase).Any()).ToList();
        var first = on.Where(a => a.Id == "pointer.click").Concat(on.Where(a => a.Id == "cancel"));
        var own = on.Where(a => a.Group is not null);
        return [.. first.Concat(own)
            .Select(a => (Action: a, Binding: BindingsOf(a, overrides).FirstOrDefault(Button)))
            .Where(h => h.Binding is not null)
            .Select(h => (h.Action, h.Binding!))
            .Take(limit)];
    }

    /// <summary>Устройство привязки: <c>key</c> — клавиатура, <c>pad</c> — кнопки и оси геймпада.</summary>
    public static string DeviceOf(string binding) => binding.StartsWith("key:", StringComparison.Ordinal) ? "key" : "pad";
}

/// <summary>Надписи на кнопках геймпада: Xbox (и прочие XInput), PlayStation, Steam Deck.</summary>
public enum PadStyle
{
    Xbox,
    PlayStation,
    SteamDeck,
}

/// <summary>Имена клавиш Godot (<c>Key</c>) для кодов клавиш Windows разметки (ASCII-буквы и цифры совпадают, F1…F12 подряд, прочие — таблица).</summary>
public static class InputNames
{
    private static readonly Dictionary<int, string> Special = new()
    {
        [8] = "Backspace", [9] = "Tab", [13] = "Enter", [27] = "Escape", [32] = "Space", [33] = "Pageup", [34] = "Pagedown", [35] = "End",
        [36] = "Home", [37] = "Left", [38] = "Up", [39] = "Right", [40] = "Down", [45] = "Insert", [46] = "Delete",
    };

    public static string? KeyOfVk(int vk) => vk switch
    {
        >= 'A' and <= 'Z' => ((char)vk).ToString(),
        >= '0' and <= '9' => $"Key{(char)vk}",
        >= 112 and <= 123 => $"F{vk - 111}",
        _ => Special.GetValueOrDefault(vk),
    };

    /// <summary>Короткие подписи кнопок и осей геймпада Xbox: имя <c>JoyButton</c>/<c>JoyAxis</c> Godot → подпись; чего нет в таблице — само
    /// имя. У PlayStation и Steam Deck свои надписи поверх (<see cref="Styled"/>).</summary>
    private static readonly Dictionary<string, string> Pad = new(StringComparer.OrdinalIgnoreCase)
    {
        ["pad:LeftShoulder"] = "LB", ["pad:RightShoulder"] = "RB", ["pad:Back"] = "View", ["pad:Start"] = "Menu", ["pad:Guide"] = "Home",
        ["pad:LeftStick"] = "LS", ["pad:RightStick"] = "RS", ["pad:DpadUp"] = "D-pad ↑", ["pad:DpadDown"] = "D-pad ↓",
        ["pad:DpadLeft"] = "D-pad ←", ["pad:DpadRight"] = "D-pad →",
        ["axis:LeftX-"] = "LS ←", ["axis:LeftX+"] = "LS →", ["axis:LeftY-"] = "LS ↑", ["axis:LeftY+"] = "LS ↓",
        ["axis:RightX-"] = "RS ←", ["axis:RightX+"] = "RS →", ["axis:RightY-"] = "RS ↑", ["axis:RightY+"] = "RS ↓",
        ["axis:TriggerLeft+"] = "LT", ["axis:TriggerRight+"] = "RT",
    };

    private static readonly Dictionary<PadStyle, Dictionary<string, string>> Styled = new()
    {
        [PadStyle.PlayStation] = new(StringComparer.OrdinalIgnoreCase)
        {
            ["pad:A"] = "✕", ["pad:B"] = "○", ["pad:X"] = "□", ["pad:Y"] = "△", ["pad:LeftShoulder"] = "L1", ["pad:RightShoulder"] = "R1",
            ["axis:TriggerLeft+"] = "L2", ["axis:TriggerRight+"] = "R2", ["pad:Back"] = "Create", ["pad:Start"] = "Options",
            ["pad:LeftStick"] = "L3", ["pad:RightStick"] = "R3", ["pad:Guide"] = "PS",
        },
        [PadStyle.SteamDeck] = new(StringComparer.OrdinalIgnoreCase)
        {
            ["pad:LeftShoulder"] = "L1", ["pad:RightShoulder"] = "R1", ["axis:TriggerLeft+"] = "L2", ["axis:TriggerRight+"] = "R2",
            ["pad:LeftStick"] = "L3", ["pad:RightStick"] = "R3", ["pad:Guide"] = "Steam",
        },
    };

    /// <summary>Подпись привязки геймпада в надписях устройства (<c>pad:LeftShoulder</c> → «LB» у Xbox, «L1» у PlayStation и Steam Deck;
    /// <c>pad:A</c> → «A» или «✕»).</summary>
    public static string PadLabel(string binding, PadStyle style = PadStyle.Xbox)
    {
        ArgumentNullException.ThrowIfNull(binding);
        if (Styled.TryGetValue(style, out var own) && own.TryGetValue(binding, out string? styled)) return styled;
        return Pad.TryGetValue(binding, out string? label) ? label : binding[(binding.IndexOf(':', StringComparison.Ordinal) + 1)..];
    }

    /// <summary>Надписи кнопок по имени устройства (<c>Input.GetJoyName</c>): Steam Deck, PlayStation (DualShock, DualSense), иначе Xbox.</summary>
    public static PadStyle StyleOf(string? joyName)
    {
        string name = joyName ?? "";
        if (name.Contains("Steam Deck", StringComparison.OrdinalIgnoreCase)) return PadStyle.SteamDeck;
        string[] sony = ["PS3", "PS4", "PS5", "DualShock", "DualSense", "Sony", "PlayStation"];
        return sony.Any(s => name.Contains(s, StringComparison.OrdinalIgnoreCase)) ? PadStyle.PlayStation : PadStyle.Xbox;
    }
}
