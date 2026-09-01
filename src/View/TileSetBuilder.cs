using System;
using System.Collections.Generic;
using Godot;

namespace Void;

/// <summary>
/// Builds the <see cref="TileSet"/> resources the world viewer's
/// <see cref="TileMapLayer"/>s draw with, straight from the block and wall
/// registries (VOID-057).
///
/// <para><b>Source id is the content's numeric id.</b> That is the whole trick
/// here: a tile's <c>block_id</c> is already a stable <c>uint16</c> that every
/// saved world stores, so using it as the atlas source id makes painting a tile
/// <c>SetCell(coords, tile.BlockId, Vector2I.Zero)</c> with no lookup table in
/// between, and makes an unpainted cell mean exactly what <c>block_id 0</c>
/// means. Nothing may renumber sources afterwards.</para>
///
/// <para><b>Blocks and walls get separate TileSets</b>, because their numeric
/// ids are independent sequences that both start at 0 — one shared TileSet would
/// have <c>void:dirt</c> and <c>void:dirt_wall</c> fighting over source id 1.
/// The two layers draw with different resources, which is also what lets walls
/// be tinted as a group later.</para>
///
/// <para>Engine-touching by nature, so the xunit suite must never load it; rung
/// 6 (smoke) boots the viewer headless and is the real coverage for this
/// path.</para>
/// </summary>
public static class TileSetBuilder
{
    /// <summary>
    /// Edge length of one tile in pixels. Every terrain texture must match this
    /// exactly: <see cref="TileSetAtlasSource.TextureRegionSize"/> is what slices
    /// the image, so a 32px texture declared at 16 would silently render its
    /// top-left quarter.
    /// </summary>
    public const int TileSizePixels = 16;

    /// <summary>
    /// The TileSet for the foreground block layer. Air is skipped — it declares
    /// no sprite, and an empty cell is what air looks like.
    /// </summary>
    public static TileSet ForBlocks(Registry<BlockDefinition> blocks)
    {
        ArgumentNullException.ThrowIfNull(blocks);

        List<(ushort NumericId, string SpritePath)> sources = new(blocks.Count);
        foreach (BlockDefinition block in blocks)
        {
            sources.Add((block.NumericId, block.SpritePath));
        }

        return Build(sources);
    }

    /// <summary>
    /// The TileSet for the background wall layer. "No wall" is skipped, for the
    /// same reason air is.
    /// </summary>
    public static TileSet ForWalls(Registry<WallDefinition> walls)
    {
        ArgumentNullException.ThrowIfNull(walls);

        List<(ushort NumericId, string SpritePath)> sources = new(walls.Count);
        foreach (WallDefinition wall in walls)
        {
            sources.Add((wall.NumericId, wall.SpritePath));
        }

        return Build(sources);
    }

    /// <summary>
    /// One atlas source per entry that both declares a sprite and has one on
    /// disk.
    /// </summary>
    /// <remarks>
    /// <para><b>A declared-but-missing texture warns and is skipped, rather than
    /// throwing.</b> <see cref="BlockDefinition.SpritePath"/> is documented as
    /// unvalidated at load time precisely because content legitimately runs
    /// ahead of art, and a hard failure here would mean adding a block to JSON
    /// breaks the game until someone draws it. The cost is that the block
    /// renders as a hole, so the warning names the id and the path — that
    /// message is the only thing standing between a missing texture and a
    /// mystery gap in the terrain.</para>
    /// </remarks>
    private static TileSet Build(List<(ushort NumericId, string SpritePath)> sources)
    {
        Vector2I tileSize = new Vector2I(TileSizePixels, TileSizePixels);
        TileSet tileSet = new TileSet { TileSize = tileSize };

        foreach ((ushort numericId, string spritePath) in sources)
        {
            // No sprite declared is the normal case for air and "no wall", not a
            // problem: they are registered so every numeric lookup succeeds, and
            // they are meant to draw nothing.
            if (string.IsNullOrEmpty(spritePath))
            {
                continue;
            }

            if (!ResourceLoader.Exists(spritePath))
            {
                GD.PushWarning(
                    $"Tile texture '{spritePath}' (numeric id {numericId}) does not exist; "
                    + "that tile will render as empty space.");
                continue;
            }

            Texture2D? texture = GD.Load<Texture2D>(spritePath);
            if (texture is null)
            {
                GD.PushWarning(
                    $"Tile texture '{spritePath}' (numeric id {numericId}) could not be loaded "
                    + "as a Texture2D; that tile will render as empty space.");
                continue;
            }

            TileSetAtlasSource source = new TileSetAtlasSource
            {
                Texture = texture,
                TextureRegionSize = tileSize,
            };

            // Single-tile atlas: one 16x16 texture per content entry, so the
            // region is always the origin. Multi-tile atlases arrive with
            // autotiling, which needs atlas coordinates in the content schema.
            source.CreateTile(Vector2I.Zero);

            tileSet.AddSource(source, numericId);
        }

        return tileSet;
    }
}
