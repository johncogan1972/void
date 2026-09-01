using System;
using Godot;

namespace Void;

/// <summary>
/// Generates a world and draws a bounded window of it to two
/// <see cref="TileMapLayer"/>s, so a human can look at what the generator
/// actually produced (VOID-057).
///
/// <para><b>This is a diagnostic, not the game.</b> Everything generated so far
/// has been generated blind: the C# suite proves the surface stays inside its
/// band and that biome runs never fragment, and not one of those tests can say
/// whether the result <i>reads</i> as terrain. This scene answers that in
/// seconds, and answers it now — while VOID-047's elevation and VOID-048's
/// classification are still cheap to change.</para>
///
/// <para><b>Only a window is materialised.</b> The smallest size preset is
/// 4200x1200, about 5 million tiles; populating that many cells at once is not a
/// sensible target and chunk streaming (a later ticket) exists precisely so that
/// nobody holds a whole world. <see cref="ViewWindow"/> decides which chunks the
/// window covers and the camera is limited to it, so panning can never reach
/// space that was never filled.</para>
///
/// <para><b>Not in scope, deliberately:</b> chunk streaming and eviction,
/// autotiling, lighting, the player. Each is its own Phase 3 ticket. This renders
/// a static world and nothing else.</para>
///
/// <para>Input is handled here rather than in a GDScript camera because panning
/// and window movement are the same concern: crossing a window edge is what
/// decides when terrain has to be materialised again, which is simulation work,
/// not presentation. The HUD, which is presentation, is GDScript and is fed by
/// <see cref="ViewChanged"/>.</para>
/// </summary>
public partial class WorldViewer : Node2D
{
    /// <summary>
    /// Identity of the world being viewed. Fixed rather than a fresh
    /// <c>Guid</c>: <see cref="WorldGenerator.Generate"/> takes world identity as
    /// an input so that generation is reproducible, and a viewer that invented a
    /// new id each run would be a viewer whose output could not be compared
    /// against yesterday's.
    /// </summary>
    private static readonly Guid ViewerWorldId = new Guid("00000000-0000-0000-0000-0000000057ee");

    /// <summary>
    /// Emitted whenever the view moves, for the HUD to render. Carries raw
    /// values rather than a formatted string so that how it is worded and laid
    /// out stays entirely in the GDScript that draws it.
    /// </summary>
    [Signal]
    public delegate void ViewChangedEventHandler(Godot.Collections.Dictionary status);

    /// <summary>World type to generate. Must exist in the world-type registry.</summary>
    [Export]
    public string WorldTypeId { get; set; } = "void:home";

    /// <summary>
    /// Size preset to generate at. Defaults to the smallest shipped preset:
    /// this is a look-at-it tool, and a bigger world costs generation time
    /// without showing anything a small one does not.
    /// </summary>
    [Export]
    public string SizePresetId { get; set; } = "small";

    /// <summary>
    /// Seed to generate from. A fixed default, never a clock-derived one, so
    /// that running the viewer twice shows the same world and a change in what
    /// you see means a change in the generator.
    /// </summary>
    [Export]
    public long Seed { get; set; } = 20260901;

    /// <summary>
    /// Window width in tiles, rounded up to whole chunks by
    /// <see cref="ViewWindow"/>.
    /// </summary>
    /// <remarks>
    /// Wide and short on purpose. The interesting output is the surface — where
    /// elevation moves and where one biome becomes the next — and the world-type
    /// config puts a temperature zone at roughly 2500 tiles across, so a window
    /// only a few hundred tiles wide would show a single biome every time and
    /// could never show a transition. Even at this width a transition is not
    /// guaranteed in one screen; zoom out, or jump the window along the world.
    /// </remarks>
    [Export]
    public int WindowWidthTiles { get; set; } = 1024;

    /// <summary>
    /// Window height in tiles. Sized to cover the whole band the surface is
    /// allowed to occupy (the world type allows 0.45-0.80 of the outside layer)
    /// plus sky above and fill below, so the elevation range is visible without
    /// panning vertically.
    /// </summary>
    [Export]
    public int WindowHeightTiles { get; set; } = 320;

    /// <summary>Camera pan speed in tiles per second, before zoom compensation.</summary>
    [Export]
    public float PanTilesPerSecond { get; set; } = 240f;

    /// <summary>
    /// Zoom bounds. The low end is what makes this tool work at all: a biome run
    /// is thousands of tiles, so surveying one means drawing tiles smaller than a
    /// pixel and reading the result as colour bands rather than as tiles.
    /// </summary>
    private const float MinZoom = 0.03f;
    private const float MaxZoom = 4f;

    /// <summary>Multiplier applied per zoom step, so zooming feels even at every scale.</summary>
    private const float ZoomStep = 1.06f;

    private TileMapLayer _blockLayer = null!;
    private TileMapLayer _wallLayer = null!;
    private Camera2D _camera = null!;

    /// <summary>Generation state, kept for the life of the scene so the window can move.</summary>
    private GenerationContext _context = null!;
    private LayerBoundaries _boundaries = null!;

    /// <summary>The window currently painted, so a re-centre that lands on it can skip the repaint.</summary>
    private ViewWindow _window;

    /// <summary>True once <see cref="_window"/> describes something actually drawn.</summary>
    private bool _painted;

    /// <summary>
    /// Generates the world and paints the first window.
    /// </summary>
    /// <remarks>
    /// Content comes from the <c>ContentBoot</c> autoload, which loads the
    /// registries in <c>_EnterTree</c> — before this runs, and before any scene
    /// node exists. If content failed, <see cref="ContentBoot.Content"/> throws
    /// with the real cause attached rather than handing back a null that would
    /// surface much later as an unrelated error.
    /// </remarks>
    public override void _Ready()
    {
        _blockLayer = GetNode<TileMapLayer>("%BlockLayer");
        _wallLayer = GetNode<TileMapLayer>("%WallLayer");
        _camera = GetNode<Camera2D>("%Camera");

        GameContent content = GetNode<ContentBoot>("/root/ContentBoot").Content;

        _context = new GenerationContext(content, WorldTypeId, Seed, SizePresetId);
        WorldManifest manifest = WorldGenerator.Generate(_context, ViewerWorldId);
        _boundaries = manifest.LayerBoundaries;

        _blockLayer.TileSet = TileSetBuilder.ForBlocks(content.Blocks);
        _wallLayer.TileSet = TileSetBuilder.ForWalls(content.Walls);

        // Start at the middle of the world rather than at column 0. The left
        // edge is the one place the heightmap has no left neighbour to smooth
        // against, so it is the least representative column in the world.
        RecentreOn(_context.SizePreset.WidthTiles / 2);
    }

    /// <summary>
    /// Materialises the window around <paramref name="column"/> and paints it,
    /// then parks the camera on that column's surface.
    /// </summary>
    /// <param name="column">
    /// World tile column to centre on. Clamped into the world by
    /// <see cref="ViewWindow.Around"/>, so a caller may pass anything.
    /// </param>
    /// <remarks>
    /// Vertical centring follows the heightmap rather than the middle of the
    /// world: the surface is what there is to look at, and a window centred on
    /// world height/2 would open on solid stone.
    ///
    /// <para>Public because it is this scene's one verb — "look at that column"
    /// is the whole interface — and because screenshot tooling and later Phase 3
    /// tickets drive the viewer through it rather than by synthesising input
    /// events.</para>
    /// </remarks>
    public void RecentreOn(int column)
    {
        int widthTiles = _context.SizePreset.WidthTiles;
        int heightTiles = _context.SizePreset.HeightTiles;

        column = Math.Clamp(column, 0, widthTiles - 1);
        int surfaceRow = _context.Heightmap[column];

        ViewWindow window = ViewWindow.Around(
            widthTiles, heightTiles, WindowWidthTiles, WindowHeightTiles, column, surfaceRow);

        // Repaint only when the window actually moved. A re-centre inside the
        // current window is a camera move, and repainting it would be a visible
        // stall for no change on screen.
        if (!_painted || window != _window)
        {
            Paint(window);
            _window = window;
            _painted = true;
        }

        _camera.Position = new Vector2(
            (column + 0.5f) * TileSetBuilder.TileSizePixels,
            (surfaceRow + 0.5f) * TileSetBuilder.TileSizePixels);

        ApplyCameraLimits();
        EmitStatus(column, surfaceRow);
    }

    /// <summary>
    /// Fills both layers from freshly materialised chunks.
    /// </summary>
    /// <remarks>
    /// <para>Chunks are materialised here and dropped immediately: painting is
    /// the only thing this scene does with them, and holding the window's worth
    /// would be tens of megabytes for no reader.
    /// <see cref="TerrainMaterializer.MaterializeChunk"/> is a pure function of
    /// the chunk coordinate, so a chunk that scrolls back into view is
    /// reproduced identically.</para>
    ///
    /// <para>Air and "no wall" are skipped rather than painted. An unset cell
    /// already draws nothing, and above the surface most of the window is sky —
    /// so skipping is both correct and the difference between one pass and
    /// several times the work.</para>
    ///
    /// <para>Cell coordinates are world tile coordinates, not window-relative
    /// ones. The layers sit at the origin, so tile (x, y) is always cell (x, y)
    /// however the window moves, and no offset has to be tracked or undone.</para>
    /// </remarks>
    private void Paint(ViewWindow window)
    {
        _blockLayer.Clear();
        _wallLayer.Clear();

        foreach ((int chunkX, int chunkY) in window.Chunks())
        {
            Chunk chunk = TerrainMaterializer.MaterializeChunk(_context, _boundaries, chunkX, chunkY);
            Span<Tile> tiles = chunk.Tiles;

            int originX = chunkX * Chunk.Width;
            int originY = chunkY * Chunk.Height;

            for (int localY = 0; localY < Chunk.Height; localY++)
            {
                for (int localX = 0; localX < Chunk.Width; localX++)
                {
                    Tile tile = tiles[Chunk.Index(localX, localY)];

                    if (tile.BlockId == ContentIds.AirBlock && tile.WallId == ContentIds.NoWall)
                    {
                        continue;
                    }

                    Vector2I cell = new Vector2I(originX + localX, originY + localY);

                    if (tile.WallId != ContentIds.NoWall)
                    {
                        _wallLayer.SetCell(cell, tile.WallId, Vector2I.Zero);
                    }

                    if (tile.BlockId != ContentIds.AirBlock)
                    {
                        _blockLayer.SetCell(cell, tile.BlockId, Vector2I.Zero);
                    }
                }
            }
        }
    }

    /// <summary>
    /// Pens the camera inside the painted window.
    /// </summary>
    /// <remarks>
    /// Load-bearing rather than a nicety: outside the window nothing was
    /// materialised, and empty cells are indistinguishable from a world of air.
    /// Without limits, panning off the edge looks exactly like a generator that
    /// produced nothing.
    /// </remarks>
    private void ApplyCameraLimits()
    {
        _camera.LimitLeft = _window.TileMinX * TileSetBuilder.TileSizePixels;
        _camera.LimitTop = _window.TileMinY * TileSetBuilder.TileSizePixels;
        _camera.LimitRight = (_window.TileMinX + _window.TileWidth) * TileSetBuilder.TileSizePixels;
        _camera.LimitBottom = (_window.TileMinY + _window.TileHeight) * TileSetBuilder.TileSizePixels;
    }

    /// <summary>Hands the HUD the numbers describing where the view now is.</summary>
    private void EmitStatus(int column, int surfaceRow)
    {
        EmitSignal(SignalName.ViewChanged, new Godot.Collections.Dictionary
        {
            ["seed"] = Seed,
            ["world_type"] = WorldTypeId,
            ["size_preset"] = SizePresetId,
            ["world_width"] = _context.SizePreset.WidthTiles,
            ["column"] = column,
            ["surface_row"] = surfaceRow,
            ["biome"] = _context.BiomeMap[column],
            ["chunks"] = _window.ChunkCount,
            ["zoom"] = _camera.Zoom.X,
        });
    }

    /// <summary>
    /// Continuous input: camera panning, which is the only thing that has to
    /// happen every frame. Discrete actions are handled in
    /// <see cref="_UnhandledInput"/> so a held key cannot fire them repeatedly.
    /// </summary>
    public override void _Process(double delta)
    {
        Vector2 pan = Input.GetVector(
            "viewer_pan_left", "viewer_pan_right", "viewer_pan_up", "viewer_pan_down");

        if (pan == Vector2.Zero)
        {
            return;
        }

        // Divided by zoom so panning covers a constant distance *on screen*
        // rather than in the world: zoomed out to survey a whole biome, a
        // world-space pan speed would crawl.
        float tiles = PanTilesPerSecond * (float)delta / _camera.Zoom.X;
        _camera.Position += pan * tiles * TileSetBuilder.TileSizePixels;

        EmitStatus(CameraColumn(), _context.Heightmap[CameraColumn()]);
    }

    /// <summary>
    /// Discrete actions: re-centring, jumping the window along the world,
    /// zooming and quitting.
    /// </summary>
    /// <remarks>
    /// Every action here has a joypad binding as well as a key (see the
    /// <c>[input]</c> block in <c>project.godot</c>), per the Steam Deck rule in
    /// CLAUDE.md — a viewer that could only be driven from a keyboard would be a
    /// tool that cannot be used on the target device.
    /// </remarks>
    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event.IsActionPressed("viewer_quit"))
        {
            GetTree().Quit();
            return;
        }

        if (@event.IsActionPressed("viewer_recentre"))
        {
            RecentreOn(CameraColumn());
            return;
        }

        // A jump moves by a whole window, so consecutive jumps tile the world
        // without overlap or gaps -- which is what makes "walk along the world
        // and look at every biome" a finite job.
        if (@event.IsActionPressed("viewer_window_next"))
        {
            RecentreOn(CameraColumn() + _window.TileWidth);
            return;
        }

        if (@event.IsActionPressed("viewer_window_prev"))
        {
            RecentreOn(CameraColumn() - _window.TileWidth);
            return;
        }

        if (@event.IsActionPressed("viewer_zoom_in"))
        {
            Zoom(ZoomStep);
        }
        else if (@event.IsActionPressed("viewer_zoom_out"))
        {
            Zoom(1f / ZoomStep);
        }
    }

    /// <summary>Scales the camera zoom, clamped so the view cannot invert or vanish.</summary>
    private void Zoom(float factor)
    {
        float zoom = Math.Clamp(_camera.Zoom.X * factor, MinZoom, MaxZoom);
        _camera.Zoom = new Vector2(zoom, zoom);
        EmitStatus(CameraColumn(), _context.Heightmap[CameraColumn()]);
    }

    /// <summary>
    /// The world column the camera is looking at, clamped into the world so it
    /// is always a legal heightmap index.
    /// </summary>
    private int CameraColumn() => Math.Clamp(
        (int)(_camera.Position.X / TileSetBuilder.TileSizePixels),
        0,
        _context.SizePreset.WidthTiles - 1);
}
