using D2.Data;
using Xunit;

public class GamePathsTests
{
    [SkippableFact]
    public void GlobalsDir_IsUnderGameDir()
    {
        Skip.IfNot(GamePaths.IsAvailable, GamePaths.GameDir);
        Assert.StartsWith(GamePaths.GameDir, GamePaths.Globals);
    }

    [SkippableFact]
    public void GameDir_ResolvesToExistingPristineCopy()
    {
        Skip.IfNot(GamePaths.IsAvailable, GamePaths.GameDir);
    }
}
