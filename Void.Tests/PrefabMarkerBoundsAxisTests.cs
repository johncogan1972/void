using System;
using System.IO;
using Void;

namespace Void.Tests;

/// <summary>
/// VOID-024 follow-up: proves the marker bounds check compares X against the
/// prefab's <b>width</b> and Y against its <b>height</b>, not the other way
/// round.
///
/// <c>PrefabDefinitionTests.MarkerOutsideBoundsIsFatal</c> uses a 2x2 prefab, so
/// swapping the two axes in <c>PrefabRegistryLoader.CheckMarkers</c> leaves it
/// green. On a non-square prefab the swap lets a marker sit outside the
/// footprint along the long axis: a chest authored at x=3 in a 2-wide, 4-tall
/// ruin would be stamped into whatever the generator already placed beside the
/// structure, and nothing downstream re-checks.
/// </summary>
public class PrefabMarkerBoundsAxisTests : IDisposable
{
    /// <summary>Throwaway content root, one per test.</summary>
    private readonly string _root;

    /// <summary>Creates the throwaway content root.</summary>
    public PrefabMarkerBoundsAxisTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "void-prefab-axis-" + Path.GetRandomFileName());
        Directory.CreateDirectory(_root);
    }

    /// <summary>Removes the throwaway content root.</summary>
    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }

        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// A 2-wide, 4-tall prefab with one marker at the given tile-local
    /// coordinates. Deliberately non-square: that is the whole point of this
    /// file.
    /// </summary>
    private static string TallPrefabJson(int markerX, int markerY) =>
        $$"""
        {
          "prefab_id": "test:tower",
          "category": "ruin",
          "dimensions": { "width": 2, "height": 4 },
          "block_ids": [2, 2, 2, 2, 2, 2, 2, 2],
          "wall_ids": [2, 2, 2, 2, 2, 2, 2, 2],
          "markers": [{ "type": "chest", "x": {{markerX}}, "y": {{markerY}} }],
          "weight": 1.0
        }
        """;

    /// <summary>Loads the throwaway root against the shipped blocks and walls.</summary>
    private Registry<PrefabDefinition> Load() =>
        PrefabRegistryLoader.Load(
            new DirectoryContentSource(_root), ContentPaths.Blocks(), ContentPaths.Walls());

    /// <summary>
    /// x=3 is inside the height (4) but outside the width (2), so only a check
    /// that uses width for X rejects it. If this goes red the axes have been
    /// swapped and markers can escape the footprint horizontally.
    /// </summary>
    [Fact]
    public void MarkerBeyondWidthIsFatalEvenWhenItWouldFitTheHeight()
    {
        File.WriteAllText(Path.Combine(_root, "tower.json"), TallPrefabJson(3, 0));

        ContentLoadException error = Assert.Throws<ContentLoadException>(Load);

        Assert.Contains("(3,0)", error.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// The mirror case: y=3 is outside the width (2) but a legal row of a
    /// 4-tall prefab, so a swapped check would wrongly reject valid content and
    /// fail the boot on a correctly authored prefab.
    /// </summary>
    [Fact]
    public void MarkerWithinHeightButBeyondWidthValueIsAccepted()
    {
        File.WriteAllText(Path.Combine(_root, "tower.json"), TallPrefabJson(1, 3));

        PrefabDefinition tower = Load()["test:tower"];

        Assert.Equal(1, tower.Markers[0].X);
        Assert.Equal(3, tower.Markers[0].Y);
    }
}
