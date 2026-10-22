namespace LastEdict.Data.Sound;

/// <summary>Место основной музыки под временной (бой, столица; <c>CMidMusic</c>, <c>0x5aa30e</c>): начало временной запоминает, что играло и с
/// какого места; конец — возврат к нему. Пока идёт возврат (затухание временной), новая временная место не перезаписывает — иначе
/// основной стала бы дорожка боя.</summary>
public sealed class MusicReturn
{
    private bool _temporary, _returning;

    /// <summary>Основная дорожка, к которой вернуться.</summary>
    public string Track { get; private set; } = "";

    /// <summary>Место в ней, секунд.</summary>
    public float At { get; private set; }

    /// <summary>Началась временная; сейчас играет <paramref name="nowTrack"/> с места <paramref name="at"/>.</summary>
    public void Begin(string nowTrack, float at)
    {
        if (!_temporary && !_returning)
        {
            Track = nowTrack;
            At = at;
        }
        _temporary = true;
    }

    /// <summary>Временная кончилась: true — пора возвращаться к <see cref="Track"/>; временной не было — false.</summary>
    public bool End()
    {
        if (!_temporary) return false;
        _temporary = false;
        _returning = true;
        return true;
    }

    /// <summary>Основная снова играет — возврат кончился.</summary>
    public void Resumed() => _returning = false;

    /// <summary>Новая основная (меню, карта): временной и возврата больше нет.</summary>
    public void Reset() => _temporary = _returning = false;
}
