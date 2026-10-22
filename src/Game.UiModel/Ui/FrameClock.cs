namespace D2.Data.Ui;

/// <summary>Время кадра в целых миллисекундах с переносом остатка: счёт автоповтора, подсказок и анимаций интерфейса не зависит от
/// частоты кадров (округление каждого кадра на 300 к/с теряло бы десятую часть времени).</summary>
public sealed class FrameClock
{
    private double _carry;

    /// <summary>Прошло <paramref name="seconds"/>: сколько целых миллисекунд отдать сейчас.</summary>
    public int Advance(double seconds)
    {
        _carry += seconds * 1000;
        int ms = (int)Math.Floor(_carry);
        _carry -= ms;
        return ms;
    }
}
