using System;
using System.Collections.Generic;
using System.Linq;
using Void;
using Xunit;

namespace Void.Tests;

/// <summary>
/// Guards <see cref="ViewWindow"/>, the arithmetic deciding which chunks the
/// world viewer materialises (VOID-057).
///
/// <para>What breaks in the real game if these go red: the viewer either paints
/// fewer chunks than the camera can reach — so panning runs into empty cells
/// that look exactly like a generator producing nothing — or it asks
/// <see cref="TerrainMaterializer.MaterializeChunk"/> for a chunk outside the
/// world, which throws and leaves a half-painted window. Both failures point at
/// the generator rather than at the viewer, which is why the off-by-ones are
/// pulled out here where they can be checked directly.</para>
/// </summary>
public class ViewWindowTests
{
    // The shipped "small" preset. Used throughout so the numbers below are the
    // ones the viewer actually runs against, not an invented convenience size.
    private const int SmallWidth = 4200;
    private const int SmallHeight = 1200;

    /// <summary>
    /// The window covers at least what was asked for. Rounding a request *down*
    /// to whole chunks would leave the camera able to reach unpainted cells,
    /// which is the failure that reads as a broken generator.
    /// </summary>
    [Theory]
    [InlineData(1024, 320)]
    [InlineData(1, 1)]
    [InlineData(65, 63)]
    [InlineData(128, 128)]
    public void WindowIsAtLeastTheRequestedSize(int requestedWidth, int requestedHeight)
    {
        ViewWindow window = ViewWindow.Around(
            SmallWidth, SmallHeight, requestedWidth, requestedHeight, 2000, 400);

        Assert.True(window.TileWidth >= requestedWidth);
        Assert.True(window.TileHeight >= requestedHeight);
    }

    /// <summary>
    /// Snapping is outwards to whole chunks and no further. A window that grew
    /// by more than a chunk per axis would silently multiply materialisation
    /// cost — 80 chunks becoming 108 is a stall, not a rounding detail.
    /// </summary>
    [Fact]
    public void WindowSnapsOutwardsByAtMostOneChunk()
    {
        ViewWindow window = ViewWindow.Around(SmallWidth, SmallHeight, 1000, 300, 2000, 400);

        Assert.Equal(16, window.ChunkCountX);
        Assert.Equal(5, window.ChunkCountY);
        Assert.Equal(1024, window.TileWidth);
        Assert.Equal(320, window.TileHeight);
    }

    /// <summary>
    /// Every chunk the window names is inside the world's chunk grid. This is
    /// the one that stops <see cref="TerrainMaterializer.MaterializeChunk"/>
    /// throwing mid-paint: it refuses an out-of-range coordinate rather than
    /// returning air, deliberately.
    /// </summary>
    [Theory]
    [InlineData(0, 0)]
    [InlineData(4199, 1199)]
    [InlineData(-5000, -5000)]
    [InlineData(999999, 999999)]
    [InlineData(2100, 600)]
    public void EveryChunkIsInsideTheWorld(int centreX, int centreY)
    {
        // Chunk counts round up, exactly as WorldGenerator computes dimensions:
        // a world height that is not a whole number of chunks still has its
        // last, partly-used chunk row.
        int worldChunksX = (SmallWidth + Chunk.Width - 1) / Chunk.Width;
        int worldChunksY = (SmallHeight + Chunk.Height - 1) / Chunk.Height;

        ViewWindow window = ViewWindow.Around(
            SmallWidth, SmallHeight, 1024, 320, centreX, centreY);

        Assert.True(window.ChunkMinX >= 0);
        Assert.True(window.ChunkMinY >= 0);
        Assert.True(window.ChunkMaxX < worldChunksX);
        Assert.True(window.ChunkMaxY < worldChunksY);
    }

    /// <summary>
    /// A window near an edge slides inwards keeping its size, rather than being
    /// clipped. Clipping would mean the edges of the world showed less terrain
    /// than the middle — the viewer quietly telling you less exactly where
    /// generation is least tested.
    /// </summary>
    [Fact]
    public void WindowNearAnEdgeKeepsItsSize()
    {
        ViewWindow middle = ViewWindow.Around(SmallWidth, SmallHeight, 1024, 320, 2100, 400);
        ViewWindow left = ViewWindow.Around(SmallWidth, SmallHeight, 1024, 320, 0, 400);
        ViewWindow right = ViewWindow.Around(SmallWidth, SmallHeight, 1024, 320, 4199, 400);

        Assert.Equal(middle.ChunkCountX, left.ChunkCountX);
        Assert.Equal(middle.ChunkCountX, right.ChunkCountX);
        Assert.Equal(0, left.ChunkMinX);
        Assert.Equal((SmallWidth + Chunk.Width - 1) / Chunk.Width - 1, right.ChunkMaxX);
    }

    /// <summary>
    /// A window at least as large as the world is the whole world, on both axes
    /// independently. The small preset is only 19 chunks tall, so a request for
    /// a tall window hits this every time — the mixed case is the realistic one.
    /// </summary>
    [Fact]
    public void WindowLargerThanTheWorldBecomesTheWholeWorld()
    {
        ViewWindow window = ViewWindow.Around(
            SmallWidth, SmallHeight, 999999, 999999, 2100, 400);

        Assert.Equal(0, window.ChunkMinX);
        Assert.Equal(0, window.ChunkMinY);
        Assert.Equal((SmallWidth + Chunk.Width - 1) / Chunk.Width - 1, window.ChunkMaxX);
        Assert.Equal((SmallHeight + Chunk.Height - 1) / Chunk.Height - 1, window.ChunkMaxY);
    }

    /// <summary>
    /// The requested centre is inside the resulting window whenever the window
    /// is smaller than the world. If it were not, pressing "recentre" would move
    /// the view somewhere other than where you were looking.
    /// </summary>
    [Theory]
    [InlineData(0)]
    [InlineData(31)]
    [InlineData(2100)]
    [InlineData(4199)]
    public void CentreColumnIsInsideTheWindow(int centreX)
    {
        ViewWindow window = ViewWindow.Around(SmallWidth, SmallHeight, 1024, 320, centreX, 400);

        Assert.InRange(centreX, window.TileMinX, window.TileMinX + window.TileWidth - 1);
    }

    /// <summary>
    /// <see cref="ViewWindow.Chunks"/> yields exactly the rectangle, once each,
    /// in row-major order. The viewer paints straight from this, so a duplicate
    /// is wasted materialisation and a gap is an unpainted stripe.
    /// </summary>
    [Fact]
    public void ChunksEnumeratesTheRectangleRowMajor()
    {
        ViewWindow window = ViewWindow.Around(SmallWidth, SmallHeight, 128, 128, 2100, 400);

        List<(int ChunkX, int ChunkY)> chunks = window.Chunks().ToList();

        Assert.Equal(window.ChunkCount, chunks.Count);
        Assert.Equal(chunks.Count, chunks.Distinct().Count());
        Assert.All(chunks, c => Assert.True(window.Contains(c.ChunkX, c.ChunkY)));

        // Row-major: the row index never decreases, and it is the slower axis.
        Assert.Equal(chunks.OrderBy(c => c.ChunkY).ThenBy(c => c.ChunkX).ToList(), chunks);
    }

    /// <summary>
    /// <see cref="ViewWindow.Contains"/> agrees with the window's own bounds.
    /// The viewer uses it to decide a repaint can be skipped, so a false positive
    /// leaves stale terrain on screen.
    /// </summary>
    [Fact]
    public void ContainsMatchesTheBounds()
    {
        ViewWindow window = ViewWindow.Around(SmallWidth, SmallHeight, 1024, 320, 2100, 400);

        Assert.True(window.Contains(window.ChunkMinX, window.ChunkMinY));
        Assert.True(window.Contains(window.ChunkMaxX, window.ChunkMaxY));
        Assert.False(window.Contains(window.ChunkMinX - 1, window.ChunkMinY));
        Assert.False(window.Contains(window.ChunkMaxX + 1, window.ChunkMaxY));
        Assert.False(window.Contains(window.ChunkMinX, window.ChunkMinY - 1));
        Assert.False(window.Contains(window.ChunkMinX, window.ChunkMaxY + 1));
    }

    /// <summary>
    /// The same inputs give an equal window. The viewer compares windows to
    /// decide whether to repaint, and value equality is what makes that
    /// comparison mean "the same region" rather than "the same object".
    /// </summary>
    [Fact]
    public void SameInputsGiveAnEqualWindow()
    {
        ViewWindow a = ViewWindow.Around(SmallWidth, SmallHeight, 1024, 320, 2100, 400);
        ViewWindow b = ViewWindow.Around(SmallWidth, SmallHeight, 1024, 320, 2100, 400);

        Assert.Equal(a, b);
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
    }

    /// <summary>
    /// A non-positive extent throws rather than yielding a window covering
    /// nothing. A viewer showing a blank screen with no error is the worst
    /// outcome here: it looks exactly like a generator that produced an empty
    /// world.
    /// </summary>
    [Theory]
    [InlineData(0, SmallHeight, 1024, 320)]
    [InlineData(SmallWidth, 0, 1024, 320)]
    [InlineData(SmallWidth, SmallHeight, 0, 320)]
    [InlineData(SmallWidth, SmallHeight, 1024, -1)]
    public void NonPositiveExtentThrows(int worldWidth, int worldHeight, int windowWidth, int windowHeight)
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => ViewWindow.Around(worldWidth, worldHeight, windowWidth, windowHeight, 0, 0));
    }
}
