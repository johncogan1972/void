namespace Void.Tests;

/// <summary>
/// Proves the test harness can load and exercise the game assembly, including
/// its internals. Delete once real tests cover src/.
/// </summary>
public class HarnessTests
{
    /// <summary>
    /// Proves the test project actually reaches the game assembly rather than a
    /// stale copy: if the project reference breaks, every other C# test would pass
    /// vacuously against nothing.
    /// </summary>
    [Fact]
    public void GameAssemblyIsReferenced()
    {
        Assert.Equal("Void", typeof(BuildInfo).Assembly.GetName().Name);
    }

    /// <summary>
    /// Guards the InternalsVisibleTo entry in Void.csproj. Much of the codebase is
    /// internal by design (RNG internals, registry construction), so losing this
    /// would silently make those paths untestable.
    /// </summary>
    [Fact]
    public void InternalsAreVisible()
    {
        // InternalsVisibleTo in Void.csproj is what makes this compile.
        Assert.NotNull(typeof(BuildInfo).Assembly.GetType("Void.BuildInfo"));
    }
}

