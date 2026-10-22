namespace D2.Godot;

/// <summary>Смена зрителя карты — «горячий стул»: карта глазами игрока, чей ход начинается (туман, свои отряды). Выбор
/// отряда и путь сбрасываются.</summary>
public partial class MapView
{
    public void ChangeViewer(string? viewer)
    {
        if (string.Equals(Viewer, viewer, System.StringComparison.OrdinalIgnoreCase)) return;
        Viewer = viewer;
        ResetAnimations();   // другой зритель — его туман сразу (смена тумана и прокрутка прежнего не доигрываются)
        _selected = null;
        _selectionHeld = false;
        SelectedFort = null;
        _preview = null;
        _target = null;
        RefreshGround();
        Reload();
        Marks();
        Changed?.Invoke();
    }
}
