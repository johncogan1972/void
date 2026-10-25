namespace Void;

/// <summary>
/// The result of resolving a physical window size against the base viewport
/// (VOID-062): how far the pixel grid is magnified, how much world that leaves
/// visible, and whether the 2.0 aspect cap had to bite.
///
/// A plain value with no engine types in it, so <see cref="DisplayConfig"/> can
/// be tested headlessly and <see cref="DisplayBoot"/> stays a translator that
/// makes no decisions of its own.
/// </summary>
/// <param name="IntegerScale">
/// Whole-number magnification Godot will apply to the content scale size.
/// Always &gt;= 1: a fractional scale would resample the pixel art, and a scale
/// of 0 would be unrenderable, so a window smaller than the base viewport
/// stays at 1x and reveals less world instead of being magnified past its own
/// edges.
/// </param>
/// <param name="ContentScaleWidth">
/// Logical viewport width to hand the engine, in base pixels (one base pixel =
/// one art pixel). Larger than <see cref="DisplayConfig.BaseViewportWidth"/>
/// means extra world is revealed horizontally; smaller means a sub-base window
/// is showing less, which is deliberate rather than clamped.
/// </param>
/// <param name="ContentScaleHeight">Logical viewport height, same units as <paramref name="ContentScaleWidth"/>.</param>
/// <param name="AspectCapped">
/// True when the window is wider than <see cref="DisplayConfig.MaxAspectRatio"/>
/// and <paramref name="ContentScaleWidth"/> was therefore trimmed below what
/// the window could have shown. The caller must pillarbox in this case —
/// letting the engine expand freely is exactly what the cap exists to prevent.
/// </param>
public readonly record struct DisplayPlan(
    int IntegerScale,
    int ContentScaleWidth,
    int ContentScaleHeight,
    bool AspectCapped)
{
    /// <summary>
    /// Window pixels left over horizontally once the scaled content is centred —
    /// the width of the two pillarbox bars combined. Never negative. Wide bars
    /// mean <see cref="AspectCapped"/> bit; without the cap this is still up to
    /// <see cref="IntegerScale"/> - 1 pixels, the remainder integer division
    /// could not place, so test for the cap with the flag rather than by
    /// checking this against zero.
    /// </summary>
    public int PillarboxWidth(int windowWidth) => windowWidth - (ContentScaleWidth * IntegerScale);

    /// <summary>
    /// Window pixels left over vertically once the scaled content is centred —
    /// the combined height of the letterbox bars. Never negative, and never
    /// more than <see cref="IntegerScale"/> - 1, since the content height is
    /// the window height divided by that scale.
    /// </summary>
    public int LetterboxHeight(int windowHeight) => windowHeight - (ContentScaleHeight * IntegerScale);
}
