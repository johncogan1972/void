using System;
using System.Collections.Generic;

namespace Void;

/// <summary>
/// Every tunnel the worm pass carved, stored as the discs that make them up and
/// indexed by chunk (VOID-065, <c>cave-generation-spec</c> §3).
///
/// <para><b>This exists because worms are global and materialisation is
/// pull-based.</b> A worm walks hundreds of tiles and crosses chunk boundaries
/// freely, while <see cref="TerrainMaterializer.MaterializeChunk"/> is a pure
/// function of the chunk coordinate — which is what lets the viewer, and later
/// chunk streaming, materialise a handful of chunks without generating a world's
/// worth of tiles. Carving that needed the whole world walked before any chunk
/// was correct would destroy that.</para>
///
/// <para>The resolution is to split the work: the <i>paths</i> are computed once,
/// for the whole world, as Phase 2 output on the <see cref="GenerationContext"/>
/// — they are cheap, a few hundred points each. <i>Rasterising</i> them is the
/// expensive part and stays per chunk, touching only the discs that reach into
/// the chunk being built.</para>
///
/// <para>Immutable once built, and safe to share: carving reads it and never
/// writes.</para>
/// </summary>
public sealed class CaveNetwork
{
    /// <summary>
    /// Carve discs, as parallel arrays. Flat rather than an array of structs so
    /// a rasterising loop reads three tight streams instead of striding a
    /// larger record.
    /// </summary>
    private readonly double[] _x;
    private readonly double[] _y;
    private readonly double[] _radius;

    /// <summary>
    /// Chunk key to the discs that reach into it, ascending. Built once so
    /// carving a chunk never scans the whole world's worth of discs.
    /// </summary>
    /// <remarks>
    /// A dictionary is safe here even though CLAUDE.md bans iteration order from
    /// deciding generated output: nothing iterates this map. Carving looks up one
    /// key and walks the array it finds, and that array is in ascending disc
    /// order. Whether a tile is carved is an OR over the discs covering it, which
    /// is order-independent anyway.
    /// </remarks>
    private readonly Dictionary<long, int[]> _byChunk;

    /// <summary>World extents, so the network can never claim space outside the world.</summary>
    private readonly int _widthTiles;
    private readonly int _heightTiles;

    /// <summary>An empty network — the world stays solid. Used when a world type configures no caves.</summary>
    public static CaveNetwork Empty { get; } = new CaveNetwork([], [], [], 0, 0);

    /// <summary>
    /// Builds the network and its chunk index.
    /// </summary>
    /// <param name="x">Disc centre x, in tiles. One entry per carve stamp.</param>
    /// <param name="y">Disc centre y, in tiles.</param>
    /// <param name="radius">Disc radius, in tiles. Must be positive.</param>
    /// <param name="widthTiles">World width. Space outside it is never carved.</param>
    /// <param name="heightTiles">
    /// World height. Discs legitimately overhang the bottom row — a worm walking
    /// along the floor of the void layer stamps a disc whose lower half is below
    /// the world — and clamping here is what stops that overhang being reported
    /// as carved space that does not exist.
    /// </param>
    /// <exception cref="ArgumentException">
    /// If the arrays disagree in length. A caller bug that would otherwise carve
    /// a partly-built network and look like a tuning problem.
    /// </exception>
    public CaveNetwork(double[] x, double[] y, double[] radius, int widthTiles, int heightTiles)
    {
        ArgumentNullException.ThrowIfNull(x);
        ArgumentNullException.ThrowIfNull(y);
        ArgumentNullException.ThrowIfNull(radius);

        if (x.Length != y.Length || radius.Length != x.Length)
        {
            throw new ArgumentException(
                "A cave network needs one radius and one y for every x.", nameof(radius));
        }

        _x = (double[])x.Clone();
        _y = (double[])y.Clone();
        _radius = (double[])radius.Clone();
        _widthTiles = widthTiles;
        _heightTiles = heightTiles;
        _byChunk = BuildIndex(_x, _y, _radius);
    }

    /// <summary>Number of carve discs. Diagnostic; a rough measure of how much was dug.</summary>
    public int StampCount => _x.Length;

    /// <summary>
    /// Sets every tile inside a tunnel to air, leaving its wall in place.
    /// </summary>
    /// <remarks>
    /// <para><b>Walls survive carving</b> (spec §3.2). A tunnel opens onto the
    /// biome's background wall rather than onto a hole through to nothing, which
    /// is what makes an underground space read as enclosed. It is also the first
    /// thing in the game that makes the wall layer visible at all — before
    /// carving, every wall tile was hidden behind the block in front of it.</para>
    ///
    /// <para>Called after the chunk is filled, never before: carving is
    /// subtractive, so a fill afterwards would put the rock straight back.</para>
    /// </remarks>
    public void CarveInto(Chunk chunk)
    {
        ArgumentNullException.ThrowIfNull(chunk);

        if (!_byChunk.TryGetValue(Key(chunk.ChunkX, chunk.ChunkY), out int[]? discs))
        {
            return;
        }

        int originX = chunk.ChunkX * Chunk.Width;
        int originY = chunk.ChunkY * Chunk.Height;
        Span<Tile> tiles = chunk.Tiles;

        foreach (int i in discs)
        {
            double cx = _x[i];
            double cy = _y[i];
            double r = _radius[i];
            double rSquared = r * r;

            // Only the rows and columns the disc actually reaches, clamped to the
            // chunk. Scanning the whole chunk per disc would be four thousand
            // tests to change a few dozen tiles.
            // Clamped to the chunk and to the world. The last chunk of a row or
            // column hangs over the world edge by design (chunk counts round up),
            // and tiles out there must stay untouched -- the manifest's dimensions
            // say they do not exist.
            int minX = Math.Max(originX, (int)Math.Floor(cx - r));
            int maxX = Math.Min(
                Math.Min(originX + Chunk.Width - 1, _widthTiles - 1), (int)Math.Ceiling(cx + r));
            int minY = Math.Max(originY, (int)Math.Floor(cy - r));
            int maxY = Math.Min(
                Math.Min(originY + Chunk.Height - 1, _heightTiles - 1), (int)Math.Ceiling(cy + r));

            for (int worldY = minY; worldY <= maxY; worldY++)
            {
                double dy = worldY - cy;
                double dySquared = dy * dy;

                for (int worldX = minX; worldX <= maxX; worldX++)
                {
                    double dx = worldX - cx;

                    // Compared squared, so no Math.Sqrt -- banned on the
                    // generation path by world-generation-spec §14.1.
                    if ((dx * dx) + dySquared > rSquared)
                    {
                        continue;
                    }

                    int index = Chunk.Index(worldX - originX, worldY - originY);
                    tiles[index] = tiles[index].WithBlockId(ContentIds.AirBlock);
                }
            }
        }
    }

    /// <summary>
    /// Whether a tile falls inside any tunnel.
    /// </summary>
    /// <remarks>
    /// The single-tile counterpart to <see cref="CarveInto"/>, answering the same
    /// question without materialising anything. Reachability checking and spawn
    /// placement both need to ask "is this open space?" about a scattered handful
    /// of tiles, for which building the chunks around them would be absurd.
    ///
    /// <para>Costs a dictionary lookup plus a scan of the discs in that chunk, so
    /// it is the wrong tool for a whole chunk — use <see cref="CarveInto"/>
    /// there.</para>
    /// </remarks>
    public bool IsCarved(int tileX, int tileY)
    {
        // Outside the world is never carved, whatever the discs say. A worm
        // walking the floor of the void layer stamps discs that overhang the
        // bottom row, and reporting that overhang as open space would have
        // reachability and spawn placement reasoning about tiles that do not
        // exist.
        if (tileX < 0 || tileX >= _widthTiles || tileY < 0 || tileY >= _heightTiles)
        {
            return false;
        }

        long key = Key(
            (int)Math.Floor(tileX / (double)Chunk.Width),
            (int)Math.Floor(tileY / (double)Chunk.Height));

        if (!_byChunk.TryGetValue(key, out int[]? discs))
        {
            return false;
        }

        foreach (int i in discs)
        {
            double dx = tileX - _x[i];
            double dy = tileY - _y[i];

            // Squared, so no Math.Sqrt -- banned on the generation path by
            // world-generation-spec §14.1.
            if ((dx * dx) + (dy * dy) <= _radius[i] * _radius[i])
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Buckets each disc into every chunk its bounding box touches.
    /// </summary>
    /// <remarks>
    /// By bounding box rather than by exact coverage: a disc that clips the
    /// corner of a chunk is cheap to test and expensive to miss, and a false
    /// positive costs one wasted bounds check while a false negative leaves a
    /// visible plug of rock in the middle of a tunnel.
    /// </remarks>
    private static Dictionary<long, int[]> BuildIndex(double[] x, double[] y, double[] radius)
    {
        Dictionary<long, List<int>> buckets = new();

        for (int i = 0; i < x.Length; i++)
        {
            double r = radius[i];

            int minChunkX = (int)Math.Floor((x[i] - r) / Chunk.Width);
            int maxChunkX = (int)Math.Floor((x[i] + r) / Chunk.Width);
            int minChunkY = (int)Math.Floor((y[i] - r) / Chunk.Height);
            int maxChunkY = (int)Math.Floor((y[i] + r) / Chunk.Height);

            for (int chunkY = minChunkY; chunkY <= maxChunkY; chunkY++)
            {
                for (int chunkX = minChunkX; chunkX <= maxChunkX; chunkX++)
                {
                    long key = Key(chunkX, chunkY);

                    if (!buckets.TryGetValue(key, out List<int>? list))
                    {
                        list = new List<int>();
                        buckets[key] = list;
                    }

                    list.Add(i);
                }
            }
        }

        Dictionary<long, int[]> index = new(buckets.Count);
        foreach (KeyValuePair<long, List<int>> bucket in buckets)
        {
            index[bucket.Key] = bucket.Value.ToArray();
        }

        return index;
    }

    /// <summary>
    /// Packs a chunk coordinate into one key. Both halves are offset into
    /// unsigned space first, so negative coordinates -- which the index sees
    /// whenever a worm strays past the world edge -- cannot collide with
    /// positive ones.
    /// </summary>
    private static long Key(int chunkX, int chunkY) =>
        ((long)(uint)chunkX << 32) | (uint)chunkY;
}
