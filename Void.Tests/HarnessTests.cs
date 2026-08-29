namespace Void.Tests;

/// <summary>
/// Proves the test harness can load and exercise the game assembly, including
/// its internals. Delete once real tests cover src/.
/// </summary>
public class HarnessTests
{
    [Fact]
    public void GameAssemblyIsReferenced()
    {
        Assert.Equal("Void", typeof(BuildInfo).Assembly.GetName().Name);
    }

    [Fact]
    public void InternalsAreVisible()
    {
        // InternalsVisibleTo in Void.csproj is what makes this compile.
        Assert.NotNull(typeof(BuildInfo).Assembly.GetType("Void.BuildInfo"));
    }
}

