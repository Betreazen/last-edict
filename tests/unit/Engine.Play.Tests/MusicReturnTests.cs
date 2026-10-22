using LastEdict.Data.Sound;

namespace LastEdict.Data.Tests;

/// <summary>Место основной музыки под временной (бой, столица; <c>GameAudio</c>): новая временная во время затухания прежней не
/// подменяет запомненную основную дорожку боевой.</summary>
public class MusicReturnTests
{
    [Fact]
    public void RemembersMainTrackUnderTemporary()
    {
        var back = new MusicReturn();
        back.Begin("INGAME1", 30f);
        Assert.True(back.End());

        Assert.Equal("INGAME1", back.Track);
        Assert.Equal(30f, back.At);
    }

    [Fact]
    public void NewTemporaryDuringReturnKeepsMainTrack()
    {
        var back = new MusicReturn();
        back.Begin("INGAME1", 30f);
        back.End();                    // бой кончился, боевая дорожка затухает 700 мс
        back.Begin("BATTLE01", 12f);   // а уже открыта столица — играет ещё боевая

        Assert.True(back.End());
        Assert.Equal("INGAME1", back.Track);
        Assert.Equal(30f, back.At);
    }

    [Fact]
    public void AfterReturnNextTemporaryRemembersAgain()
    {
        var back = new MusicReturn();
        back.Begin("INGAME1", 30f);
        back.End();
        back.Resumed();
        back.Begin("INGAME2", 5f);

        Assert.Equal("INGAME2", back.Track);
        Assert.Equal(5f, back.At);
    }

    [Fact]
    public void EndWithoutTemporaryDoesNothing()
    {
        var back = new MusicReturn();
        Assert.False(back.End());
        back.Begin("INGAME1", 1f);
        back.Reset();   // новая основная (меню, карта)
        Assert.False(back.End());
    }
}
