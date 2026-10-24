using LastEdict.Authoring.Documents;

namespace LastEdict.Authoring.Commands;

/// <summary>
/// История правок документа: команда даёт новый документ, отмена возвращает прежний, повтор — снова применённый. Отказ команды
/// (исключение) историю и документ не трогает. <see cref="Revision"/> растёт с каждой переменой — окно Студии по нему перерисовывается.
/// <c>UndoRedo</c> Godot не используется: история — в ядре, её проверяют тесты и <c>d2mod apply --undo-check</c>.
/// </summary>
public sealed class EditHistory(ModDocument start)
{
    private readonly Stack<(IEditCommand Command, ModDocument Before)> _undo = new();
    private readonly Stack<(IEditCommand Command, ModDocument After)> _redo = new();

    public ModDocument Current { get; private set; } = start ?? throw new ArgumentNullException(nameof(start));

    public int Revision { get; private set; }

    /// <summary>Документ на момент последней записи (для пометки «не сохранено»).</summary>
    public ModDocument Saved { get; private set; } = start;

    public bool Dirty => !ReferenceEquals(Current, Saved);

    public bool CanUndo => _undo.Count > 0;

    public bool CanRedo => _redo.Count > 0;

    public string? UndoName => _undo.Count > 0 ? _undo.Peek().Command.Name : null;

    public string? RedoName => _redo.Count > 0 ? _redo.Peek().Command.Name : null;

    /// <summary>Применить команду; документ не изменился (та же правка) — в историю не идёт. Отказ — исключение, всё по-прежнему.</summary>
    public void Do(IEditCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);
        var next = command.Apply(Current);
        if (ReferenceEquals(next, Current)) return;
        _undo.Push((command, Current));
        _redo.Clear();
        Current = next;
        Revision++;
    }

    public bool Undo()
    {
        if (_undo.Count == 0) return false;
        var (command, before) = _undo.Pop();
        _redo.Push((command, Current));
        Current = before;
        Revision++;
        return true;
    }

    public bool Redo()
    {
        if (_redo.Count == 0) return false;
        var (command, after) = _redo.Pop();
        _undo.Push((command, Current));
        Current = after;
        Revision++;
        return true;
    }

    /// <summary>Документ записан — он теперь «сохранённый».</summary>
    public void MarkSaved() => Saved = Current;

    /// <summary>Документ перечитан с диска (файл поменяли снаружи): история с ним больше не сходится — начинается заново.</summary>
    public void Reset(ModDocument document)
    {
        Current = Saved = document ?? throw new ArgumentNullException(nameof(document));
        _undo.Clear();
        _redo.Clear();
        Revision++;
    }
}
