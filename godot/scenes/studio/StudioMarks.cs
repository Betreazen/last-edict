using System.Collections.Generic;
using System.Linq;
using LastEdict.Authoring.Validation;
using Godot;

namespace LastEdict.Godot;

/// <summary>Ошибки проверки мода (<see cref="ModCheck"/>) у поля формы Студии: поле с находкой — красноватым, текст — подсказкой; у
/// заголовка формы — число находок сущности и первая из них.</summary>
public static class StudioMarks
{
    private static readonly Color Bad = new("#ff9a8a");

    public static void Mark(Control field, IReadOnlyList<CheckItem> problems)
    {
        field.Modulate = problems.Count > 0 ? Bad : Colors.White;
        // Без находок — прежняя подсказка поля (у выбора ссылки — id выбранного).
        field.TooltipText = problems.Count > 0 ? string.Join("\n", problems.Select(p => $"{p.Rule}: {p.Message}"))
            : field is StudioPicker picker ? picker.Selected ?? "" : "";
    }

    /// <summary>Строка заголовка о находках сущности; нет — пусто.</summary>
    public static string Head(IReadOnlyList<CheckItem> problems) =>
        problems.Count == 0 ? "" : $"  ⚠ {problems.Count}: {problems[0].Message}";
}
