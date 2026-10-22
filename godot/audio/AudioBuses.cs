using Godot;

namespace LastEdict.Godot;

/// <summary>
/// Шины звука порта (<c>AudioServer</c>, заводятся кодом при первом звуке — так одинаково в окне, без окна и в экспорте, файла раскладки
/// шин нет): <c>Master</c> ← <c>Music</c>, <c>Master</c> ← <c>Fx</c> ← <c>Voice</c>, <c>Fx</c> ← <c>Ambient</c> ← <c>Region0…3</c> (фоны мест,
/// <see cref="GameAudio"/>). Громкость — на шине, проигрыватели звучат своей долей: музыка — 0 дБ (затухания), звук — доля звука
/// (удар 0,7), голос — 0 дБ, фон места — его громкость по расстоянию. Голоса и фоны мест — под шиной звуков: у движка их громкость —
/// доля <c>FxVolume</c> (<c>0x51fff7</c>: <c>громкость% × FxVolume / 100</c>; голос — <c>Acknoledgements</c> × <c>FxVolume</c>), так
/// прежние значения настроек дают ту же громкость, что при громкости на проигрывателе.
/// </summary>
public static class AudioBuses
{
    public const string Master = "Master", Music = "Music", Fx = "Fx", Voice = "Voice", Ambient = "Ambient";

    /// <summary>Тишина, дБ (как у затуханий <see cref="GameAudio"/>).</summary>
    public const float Silence = -80f;

    /// <summary>Завести шины, если их ещё нет (повторный вызов ничего не меняет). Шина может слать только в шину левее себя — порядок
    /// заведения: сначала те, в которые шлют.</summary>
    public static void Ensure()
    {
        Add(Music, Master);
        Add(Fx, Master);
        Add(Voice, Fx);
        Add(Ambient, Fx);
    }

    /// <summary>Номер шины <paramref name="name"/> (нет — заводится в конце), выход — в <paramref name="send"/>.</summary>
    public static int Add(string name, string send)
    {
        int index = AudioServer.GetBusIndex(name);
        if (index < 0)
        {
            AudioServer.AddBus();
            index = AudioServer.BusCount - 1;
            AudioServer.SetBusName(index, name);
        }
        AudioServer.SetBusSend(index, send);
        return index;
    }

    /// <summary>Громкость шины долей 0…1.</summary>
    public static void Set(string bus, float share)
    {
        int index = AudioServer.GetBusIndex(bus);
        if (index >= 0) AudioServer.SetBusVolumeDb(index, Db(share));
    }

    /// <summary>Доля громкости → дБ; почти ноль — тишина.</summary>
    public static float Db(float linear) => linear <= 0.0001f ? Silence : Mathf.LinearToDb(linear);
}
