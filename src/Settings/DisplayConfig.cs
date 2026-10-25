using System;

namespace Void;

/// <summary>
/// The engine-free display rules (VOID-062): base viewport, legal zoom range,
/// the fairness cap on aspect ratio, and the integer-scale maths that turns a
/// physical window size into a <see cref="DisplayPlan"/>.
///
/// <para><b>Stretch model.</b> <c>project.godot</c> sets
/// <c>stretch/mode="canvas_items"</c>, <c>stretch/aspect="expand"</c> and
/// <c>stretch/scale_mode="integer"</c>. Integer scale mode is what satisfies
/// GDD §9.4's pixel-perfect requirement: the engine picks the largest whole
/// multiple of the base viewport that fits, so an art pixel is always a whole
/// number of screen pixels and never resampled. Whatever window space the whole
/// multiple does not consume becomes revealed world rather than black bars,
/// which is the same trade <c>expand</c> already makes — the two settings agree
/// rather than fight.</para>
///
/// <para><b>Why the aspect cap.</b> Uncapped <c>expand</c> ties horizontal
/// awareness to hardware. A 32:9 display reveals roughly 2.2x the width a
/// 1280x800 Steam Deck does, and with multiplayer arriving post-MVP that lands
/// as a permanent, unfixable advantage inside a single co-op session — the
/// ultrawide player sees enemies the Deck player cannot. Capping the revealed
/// ratio at <see cref="MaxAspectRatio"/> bounds the gap while leaving ultrawide
/// fully usable: everything at or under 2.0 (16:10, 16:9, 18:9) is untouched,
/// and only wider panels (21:9, 32:9) pillarbox instead of revealing more.</para>
/// </summary>
public static class DisplayConfig
{
    /// <summary>
    /// Base viewport width in art pixels; must stay in step with
    /// <c>display/window/size/viewport_width</c> in <c>project.godot</c>, since
    /// every scale decision here is relative to it.
    /// </summary>
    public const int BaseViewportWidth = 1280;

    /// <summary>
    /// Base viewport height, matching <c>display/window/size/viewport_height</c>.
    /// 1280x800 is the Steam Deck panel, which is the day-one minimum target
    /// (CLAUDE.md), so the base viewport is also the smallest supported window.
    /// </summary>
    public const int BaseViewportHeight = 800;

    /// <summary>Art pixels per tile edge at 1x (GDD §9.4), used only to report tiles revealed.</summary>
    public const int TilePixelSize = 16;

    /// <summary>
    /// Widest width:height ratio of world the game will reveal. Decided for the
    /// multiplayer-fairness reasons in the type comment; changing it changes how
    /// much of the world ultrawide players can see, so it is a design decision,
    /// not a tuning knob.
    /// </summary>
    public const double MaxAspectRatio = 2.0;

    /// <summary>
    /// Lowest camera zoom. GDD §9.4 supports 1x, 2x and 3x and deliberately
    /// does not support zooming below 1x: sub-1x would shrink tiles under 16
    /// physical pixels, which both breaks pixel-perfect rendering and hands the
    /// player more world awareness than the cap above is trying to bound.
    /// </summary>
    public const int MinZoomLevel = 1;

    /// <summary>Highest camera zoom (GDD §9.4): 3x shows a tile as 48 physical pixels.</summary>
    public const int MaxZoomLevel = 3;

    /// <summary>Zoom a fresh install starts at — native 1x, the widest view that is still pixel-perfect.</summary>
    public const int DefaultZoomLevel = 1;

    /// <summary>
    /// Resolves a physical window size into the scale and logical viewport to
    /// use.
    /// </summary>
    /// <remarks>
    /// Mirrors Godot's own integer-scale choice — the largest whole factor by
    /// which the base viewport still fits in both axes — so that this layer and
    /// the engine never disagree about the scale.
    ///
    /// <para><b>Windows below the base viewport reveal less world rather than
    /// cropping.</b> 1280x800 is the supported minimum, but a shorter panel
    /// (1366x768 and 1280x720 are both still common) has to do *something*
    /// coherent. Clamping the content size up to 1280x800 would ask the engine
    /// to fit 800 art pixels into 768 screen pixels at a scale that cannot go
    /// below 1x, so 32 rows would fall off the bottom edge with nothing to
    /// indicate it — world and UI hidden rather than shown smaller. Revealing
    /// only what fits is the honest failure: the player sees 48 tiles of height
    /// instead of 50 and loses nothing silently. Note this is the *inverse* of
    /// what <see cref="MaxAspectRatio"/> guards — that cap bounds how much
    /// advantage a large display may buy, and a small window is a
    /// disadvantage, which is the player's own trade to make.</para>
    ///
    /// <para>The consequence worth knowing: the returned content size is never
    /// larger than the window at the chosen scale, so
    /// <see cref="DisplayPlan.PillarboxWidth"/> and
    /// <see cref="DisplayPlan.LetterboxHeight"/> are never negative. A negative
    /// bar width is meaningless and was how this bug announced itself.</para>
    ///
    /// <para>The returned content size is in art pixels and is what the caller
    /// must set as the window's content scale size; <see cref="MaxAspectRatio"/>
    /// is applied to it here, so a caller that honours
    /// <see cref="DisplayPlan.AspectCapped"/> needs no aspect logic of its own.</para>
    /// </remarks>
    /// <param name="windowWidth">Physical window width in screen pixels; values below 1 are treated as 1.</param>
    /// <param name="windowHeight">Physical window height in screen pixels; values below 1 are treated as 1.</param>
    public static DisplayPlan Plan(int windowWidth, int windowHeight)
    {
        int width = Math.Max(1, windowWidth);
        int height = Math.Max(1, windowHeight);

        int scale = Math.Max(1, Math.Min(width / BaseViewportWidth, height / BaseViewportHeight));

        // Integer division is the point: a content size that is not a whole
        // number of art pixels would put geometry on half-pixel boundaries. The
        // floor is 1, not the base viewport — flooring at the base would make
        // the content larger than the window on any sub-base panel and crop the
        // difference away (see the remarks).
        int contentWidth = Math.Max(1, width / scale);
        int contentHeight = Math.Max(1, height / scale);

        int cappedWidth = (int)(contentHeight * MaxAspectRatio);
        bool capped = contentWidth > cappedWidth;
        if (capped)
        {
            contentWidth = cappedWidth;
        }

        return new DisplayPlan(scale, contentWidth, contentHeight, capped);
    }

    /// <summary>
    /// Whole tiles visible across a logical viewport dimension at the given
    /// zoom. Partial tiles are not counted, so this is the guaranteed-visible
    /// span — what gameplay reasoning (chunk prefetch, spawn distance) should
    /// use rather than the rounded-up span.
    /// </summary>
    public static int TilesVisible(int contentPixels, int zoomLevel) =>
        contentPixels / (TilePixelSize * Math.Clamp(zoomLevel, MinZoomLevel, MaxZoomLevel));
}
