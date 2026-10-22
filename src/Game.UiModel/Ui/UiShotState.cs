using System.Text.Json;

namespace LastEdict.Data.Ui;

/// <summary>
/// Состояние экрана на снимке оригинала — для попиксельной сверки (<c>tools/pixel-compare.py</c>, <c>docs/ui-windows.md</c> §10).
/// Порт без окна рисует экран в начальном состоянии разметки, а на снимке окно уже наполнено логикой движка: отмечены флажки,
/// стоят счётчики, спрятаны лишние кнопки, свой фон расы. Файл <c>tools/pixel-states/&lt;снимок&gt;.json</c> говорит порту то же
/// состояние, что видно на снимке; поля <c>note</c> и <c>mask</c> (маска содержимого, которое порт в этой сверке не рисует, с
/// объяснением) читает сам скрипт сверки.
///
/// <code>{ "underlay": "DLG_CHOOSE_LORD_HUMAN_BG", "checked": ["TOG_AUTOSAVE"], "hidden": ["BTN_NO"],
///   "disabled": ["BTN_SAVE"], "spins": { "SPIN_FX_VOLUME": [4, 0] }, "groups": { "RAD_LORD": 1 },
///   "images": { "IMG_CITY": "City:ALD0" }, "centered": ["IMG_CITY"], "lists": { "LBOX_LOGS": [2, 0] } }</code>
/// <c>centered</c> — картинки из <c>images</c>, которые движок кладёт по центру места (<see cref="UiImageAlign.Center"/>); <c>sprites</c> —
/// кадр кнопки во всех состояниях вместо кадров разметки (логика ставит его <see cref="UiScreenState.SetSprites"/>: лицо лорда
/// <c>BTN_FACE</c> — <c>{ "sprites": { "BTN_FACE": "FACE_HU_0" } }</c>).
/// </summary>
public sealed record UiShotState(
    string? Background = null,
    IReadOnlyList<string>? Checked = null,
    IReadOnlyList<string>? Unchecked = null,
    IReadOnlyList<string>? Disabled = null,
    IReadOnlyList<string>? Hidden = null,
    IReadOnlyDictionary<string, string>? Images = null,
    IReadOnlyDictionary<string, int[]>? Spins = null,
    IReadOnlyDictionary<string, int>? Groups = null,
    string? Underlay = null,
    IReadOnlyDictionary<string, int[]>? Lists = null,
    IReadOnlyList<string>? Centered = null,
    IReadOnlyDictionary<string, string>? Sprites = null)
{
    private static readonly JsonSerializerOptions Options = new() { PropertyNameCaseInsensitive = true };

    /// <exception cref="FormatException">Файл пуст, счётчик задан не парой «сколько значений, какое выбрано», список — не парой
    /// «сколько пунктов, какой выбран» с выбором внутри.</exception>
    public static UiShotState FromJson(string json)
    {
        var state = JsonSerializer.Deserialize<UiShotState>(json, Options) ?? throw new FormatException("пустое состояние снимка");
        if (state.Spins?.FirstOrDefault(p => p.Value is not { Length: 2 }) is { Key: { } bad })
            throw new FormatException($"счётчик {bad}: нужна пара [сколько значений, какое выбрано]");
        if (state.Lists?.FirstOrDefault(p => p.Value is not [> 0 and var count, var selected] || selected < -1 || selected >= count)
            is { Key: { } badList })
            throw new FormatException($"список {badList}: нужна пара [сколько пунктов, какой выбран (-1 — никакой)]");
        return state;
    }

    public static UiShotState Load(string path) => FromJson(File.ReadAllText(path));

    /// <summary>Наложить на экран. Элемента, которого нет на экране, — ошибка (<see cref="KeyNotFoundException"/>): опечатка в файле
    /// не должна молча дать «в пределах».</summary>
    public void ApplyTo(UiScreenState ui)
    {
        ArgumentNullException.ThrowIfNull(ui);
        if (Background is not null) ui.SetBackground(Background);
        if (Underlay is not null) ui.SetUnderlay(Underlay);
        foreach (var id in Checked ?? []) ui.SetChecked(id, true);
        foreach (var id in Unchecked ?? []) ui.SetChecked(id, false);
        foreach (var id in Disabled ?? []) ui.SetEnabled(id, false);
        foreach (var id in Hidden ?? []) ui.SetHidden(id, true);
        foreach (var (id, frame) in Images ?? new Dictionary<string, string>())
            ui.SetImage(id, frame, Centered?.Contains(id) == true ? UiImageAlign.Center : UiImageAlign.Fit);
        foreach (var (id, frame) in Sprites ?? new Dictionary<string, string>()) ui.SetSprites(id, new UiSprites(frame, frame, frame, frame));
        foreach (var (id, spin) in Spins ?? new Dictionary<string, int[]>())
        {
            if (spin is not { Length: 2 }) throw new FormatException($"счётчик {id}: нужна пара [сколько значений, какое выбрано]");
            ui.SetSpin(id, spin[0], spin[1]);
        }
        foreach (var (id, index) in Groups ?? new Dictionary<string, int>()) ui.SetGroupIndex(id, index);
        foreach (var (id, list) in Lists ?? new Dictionary<string, int[]>())
        {
            // Подписи пунктов в сверку не входят (маска подписей) — важны число пунктов и выбор: от них стрелки у края.
            ui.SetItems(id, [.. Enumerable.Repeat("-", list[0])]);
            if (list[1] >= 0) ui.Select(id, list[1]);
        }
    }
}
