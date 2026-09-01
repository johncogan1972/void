using System;
using System.Collections.Generic;

namespace Void;

/// <summary>
/// The rectangle of chunks a viewer materialises around a point of interest
/// (VOID-057).
///
/// <para>Rendering a whole world is not viable — the smallest size preset is
/// 4200x1200, about 5 million tiles — so the static world viewer materialises a
/// bounded window and pans within it. This type is the arithmetic that decides
/// which chunks that window covers, kept separate from the viewer node so it can
/// be tested under plain <c>dotnet test</c>: no Godot types anywhere here.</para>
///
/// <para><b>The window is snapped outwards to whole chunks.</b>
/// <see cref="TerrainMaterializer.MaterializeChunk"/> is the only way to get
/// tiles and it produces one whole chunk at a time, so a window that ended
/// mid-chunk would still have to materialise that chunk. Expressing the window
/// in chunks makes the covered tile rect exactly what was materialised, rather
/// than a sub-rect a caller has to remember to clip against.</para>
///
/// <para><b>Clamping is silent and deliberate.</b> A window near an edge slides
/// inwards to stay inside the world rather than hanging over it, and a window
/// larger than the world collapses to the whole world. Both are ordinary — the
/// viewer's centre column is a human poking at the world, not a computed
/// invariant — so neither throws.</para>
/// </summary>
public readonly record struct ViewWindow
{
    /// <summary>
    /// Private so the only way to build one is <see cref="Around"/>, which is
    /// where the clamping rules live. A directly-constructed window could name
    /// chunks outside the world, which is the one thing this type prevents.
    /// </summary>
    private ViewWindow(int chunkMinX, int chunkMinY, int chunkMaxX, int chunkMaxY)
    {
        ChunkMinX = chunkMinX;
        ChunkMinY = chunkMinY;
        ChunkMaxX = chunkMaxX;
        ChunkMaxY = chunkMaxY;
    }

    /// <summary>Leftmost chunk column, inclusive. Never negative.</summary>
    public int ChunkMinX { get; }

    /// <summary>Topmost chunk row, inclusive. Never negative.</summary>
    public int ChunkMinY { get; }

    /// <summary>Rightmost chunk column, <b>inclusive</b> — not one past the end.</summary>
    public int ChunkMaxX { get; }

    /// <summary>Bottommost chunk row, <b>inclusive</b> — not one past the end.</summary>
    public int ChunkMaxY { get; }

    /// <summary>Chunk columns covered. Always at least 1.</summary>
    public int ChunkCountX => ChunkMaxX - ChunkMinX + 1;

    /// <summary>Chunk rows covered. Always at least 1.</summary>
    public int ChunkCountY => ChunkMaxY - ChunkMinY + 1;

    /// <summary>Total chunks to materialise — the cost of this window, in chunks.</summary>
    public int ChunkCount => ChunkCountX * ChunkCountY;

    /// <summary>World tile column of the window's left edge.</summary>
    public int TileMinX => ChunkMinX * Chunk.Width;

    /// <summary>World tile row of the window's top edge.</summary>
    public int TileMinY => ChunkMinY * Chunk.Height;

    /// <summary>
    /// Window width in tiles. This is the snapped-outwards width, so it is at
    /// least the requested width and at most one chunk wider on each side.
    /// </summary>
    public int TileWidth => ChunkCountX * Chunk.Width;

    /// <summary>Window height in tiles, snapped outwards like <see cref="TileWidth"/>.</summary>
    public int TileHeight => ChunkCountY * Chunk.Height;

    /// <summary>
    /// The window of at least <paramref name="windowWidthTiles"/> x
    /// <paramref name="windowHeightTiles"/> centred as near as possible on
    /// (<paramref name="centreX"/>, <paramref name="centreY"/>).
    /// </summary>
    /// <param name="worldWidthTiles">World width; from the size preset, not the chunk grid.</param>
    /// <param name="worldHeightTiles">World height; from the size preset.</param>
    /// <param name="windowWidthTiles">Requested width. Rounded up to whole chunks.</param>
    /// <param name="windowHeightTiles">Requested height. Rounded up to whole chunks.</param>
    /// <param name="centreX">Tile column to centre on. Clamped into the world.</param>
    /// <param name="centreY">Tile row to centre on. Clamped into the world.</param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// If any extent is not positive. A zero-width world or window is a caller
    /// bug rather than an edge case: it would produce a window covering nothing
    /// and a viewer showing an empty screen with no indication why.
    /// </exception>
    public static ViewWindow Around(
        int worldWidthTiles,
        int worldHeightTiles,
        int windowWidthTiles,
        int windowHeightTiles,
        int centreX,
        int centreY)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(worldWidthTiles);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(worldHeightTiles);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(windowWidthTiles);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(windowHeightTiles);

        // The world's chunk grid, rounded up exactly as WorldGenerator does: a
        // world whose height is not a whole number of chunks still has its last,
        // partly-used chunk row. Computed here rather than taken from
        // WorldDimensions so this stays usable from a test with two integers.
        int worldChunksX = CeilDiv(worldWidthTiles, Chunk.Width);
        int worldChunksY = CeilDiv(worldHeightTiles, Chunk.Height);

        (int minX, int maxX) = Span(worldChunksX, windowWidthTiles, centreX, Chunk.Width);
        (int minY, int maxY) = Span(worldChunksY, windowHeightTiles, centreY, Chunk.Height);

        return new ViewWindow(minX, minY, maxX, maxY);
    }

    /// <summary>
    /// One axis of <see cref="Around"/>: the inclusive chunk span of a window of
    /// <paramref name="windowTiles"/> centred on <paramref name="centreTile"/>.
    /// </summary>
    /// <remarks>
    /// Split out because the two axes are the same rule with different extents,
    /// and a copy-pasted second axis is where a viewer bug that only shows up
    /// near the bottom of the world would come from.
    ///
    /// <para>Order matters: the span is sized first and slid second. Clamping
    /// the centre before sizing would shrink the window near an edge, so a world
    /// edge would show less terrain rather than the same amount shifted
    /// inwards.</para>
    /// </remarks>
    private static (int Min, int Max) Span(
        int worldChunks, int windowTiles, int centreTile, int chunkTiles)
    {
        int windowChunks = CeilDiv(windowTiles, chunkTiles);

        // A window at least as large as the world is the whole world. Handled
        // before the slide because the arithmetic below would otherwise produce
        // a span wider than the grid and clamp it to a lopsided one.
        if (windowChunks >= worldChunks)
        {
            return (0, worldChunks - 1);
        }

        int centreChunk = Math.Clamp(centreTile, 0, (worldChunks * chunkTiles) - 1) / chunkTiles;

        // Slide the whole span inwards so it stays inside the grid, keeping its
        // size. The left bias on odd widths is arbitrary but fixed, so the same
        // centre always yields the same window.
        int min = Math.Clamp(centreChunk - (windowChunks / 2), 0, worldChunks - windowChunks);

        return (min, min + windowChunks - 1);
    }

    /// <summary>True if the chunk is inside this window.</summary>
    /// <remarks>
    /// Lets a caller skip re-materialising chunks a moved window still covers.
    /// </remarks>
    public bool Contains(int chunkX, int chunkY) =>
        chunkX >= ChunkMinX && chunkX <= ChunkMaxX &&
        chunkY >= ChunkMinY && chunkY <= ChunkMaxY;

    /// <summary>
    /// Every chunk coordinate in the window, in row-major order.
    /// </summary>
    /// <remarks>
    /// The order is fixed and explicit so a materialisation pass is reproducible
    /// and so a partial pass fails in the same place twice. Nothing about the
    /// tiles produced depends on it — <see cref="TerrainMaterializer.MaterializeChunk"/>
    /// is a pure function of the chunk coordinate.
    /// </remarks>
    public IEnumerable<(int ChunkX, int ChunkY)> Chunks()
    {
        for (int chunkY = ChunkMinY; chunkY <= ChunkMaxY; chunkY++)
        {
            for (int chunkX = ChunkMinX; chunkX <= ChunkMaxX; chunkX++)
            {
                yield return (chunkX, chunkY);
            }
        }
    }

    /// <summary>Integer division rounding up. Both arguments are positive here.</summary>
    private static int CeilDiv(int value, int divisor) => (value + divisor - 1) / divisor;
}
