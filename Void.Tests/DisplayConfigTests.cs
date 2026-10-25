using System;
using Void;
using Xunit;

namespace Void.Tests;

/// <summary>
/// Integer-scale and aspect-cap coverage for the display decisions in VOID-062.
///
/// These numbers are the decision, not an implementation detail: they are what
/// a 16:10, 16:9 or ultrawide player actually gets to see. If they move, the
/// game's rendering contract (GDD §9.4) and the multiplayer fairness bound both
/// moved with them, which is a design change rather than a passing refactor.
/// </summary>
public sealed class DisplayConfigTests
{
    /// <summary>
    /// The four reference resolutions from the ticket decision. Guards the
    /// pixel-perfect promise: a fractional scale here would mean art pixels
    /// landing on fractions of a screen pixel, which is the exact artefact
    /// integer scale mode exists to prevent.
    /// </summary>
    [Theory]
    [InlineData(1280, 800, 1, 1280, 800)]   // Steam Deck / base: exactly 1x, no reveal.
    [InlineData(1920, 1080, 1, 1920, 1080)] // 1.5x would be fractional, so 1x and the rest reveals.
    [InlineData(2560, 1600, 2, 1280, 800)]  // Exact double of base: 2x, still no reveal.
    [InlineData(3840, 2160, 2, 1920, 1080)] // 2.7x floors to 2x, leaving the 1920x1080 reveal.
    public void PlanMatchesReferenceResolutions(
        int windowWidth, int windowHeight, int expectedScale, int expectedContentWidth, int expectedContentHeight)
    {
        DisplayPlan plan = DisplayConfig.Plan(windowWidth, windowHeight);

        Assert.Equal(expectedScale, plan.IntegerScale);
        Assert.Equal(expectedContentWidth, plan.ContentScaleWidth);
        Assert.Equal(expectedContentHeight, plan.ContentScaleHeight);
        Assert.False(plan.AspectCapped);
    }

    /// <summary>
    /// Tile counts for the same four resolutions. Stated separately because
    /// tiles, not pixels, are the unit that gameplay awareness is measured in —
    /// this is the table a balance discussion will quote.
    /// </summary>
    [Theory]
    [InlineData(1280, 800, 80, 50)]
    [InlineData(1920, 1080, 120, 67)]
    [InlineData(2560, 1600, 80, 50)]
    [InlineData(3840, 2160, 120, 67)]
    public void TilesVisibleMatchesReferenceResolutions(
        int windowWidth, int windowHeight, int expectedTilesWide, int expectedTilesHigh)
    {
        DisplayPlan plan = DisplayConfig.Plan(windowWidth, windowHeight);

        Assert.Equal(expectedTilesWide, DisplayConfig.TilesVisible(plan.ContentScaleWidth, 1));
        Assert.Equal(expectedTilesHigh, DisplayConfig.TilesVisible(plan.ContentScaleHeight, 1));
    }

    /// <summary>
    /// Everything at or under 2.0 must be left alone. If this goes red, the cap
    /// has started pillarboxing ordinary 16:9 and 18:9 displays — a visible
    /// regression for the majority of players.
    /// </summary>
    [Theory]
    [InlineData(1280, 800)]   // 16:10
    [InlineData(1920, 1080)]  // 16:9
    [InlineData(2160, 1080)]  // 18:9, exactly at the cap
    public void AspectAtOrUnderCapIsNotCapped(int windowWidth, int windowHeight)
    {
        DisplayPlan plan = DisplayConfig.Plan(windowWidth, windowHeight);

        Assert.False(plan.AspectCapped);
        Assert.Equal(0, plan.PillarboxWidth(windowWidth));
    }

    /// <summary>
    /// Wider than 2.0 must reveal no more world than 2.0 would, and must report
    /// that so the caller pillarboxes. This is the multiplayer fairness bound:
    /// without it a 32:9 player permanently out-sees a Steam Deck player in the
    /// same co-op session.
    /// </summary>
    [Theory]
    [InlineData(2560, 1080)] // 21:9
    [InlineData(3440, 1440)] // 21:9 at higher density
    [InlineData(5120, 1440)] // 32:9
    public void AspectWiderThanCapIsPillarboxed(int windowWidth, int windowHeight)
    {
        DisplayPlan plan = DisplayConfig.Plan(windowWidth, windowHeight);

        Assert.True(plan.AspectCapped);
        Assert.Equal(
            (int)(plan.ContentScaleHeight * DisplayConfig.MaxAspectRatio),
            plan.ContentScaleWidth);
        Assert.True(plan.PillarboxWidth(windowWidth) > 0);
    }

    /// <summary>
    /// A window smaller than the base viewport reveals less world; it must never
    /// be handed a content size bigger than itself, and must never pick scale 0
    /// (unrenderable).
    /// </summary>
    /// <remarks>
    /// The earlier version of this clamped the content size up to 1280x800,
    /// which cropped every sub-base window instead of shrinking the view — at
    /// 800x600 it hid 480x200 pixels of world off-screen with nothing to say so.
    /// If this goes red, that clamp is back.
    /// </remarks>
    [Theory]
    [InlineData(800, 600)]
    [InlineData(1, 1)]
    [InlineData(0, -10)]
    public void WindowsBelowBaseRevealLessRatherThanCrop(int windowWidth, int windowHeight)
    {
        DisplayPlan plan = DisplayConfig.Plan(windowWidth, windowHeight);

        Assert.Equal(1, plan.IntegerScale);
        Assert.Equal(Math.Max(1, windowWidth), plan.ContentScaleWidth);
        Assert.Equal(Math.Max(1, windowHeight), plan.ContentScaleHeight);
    }

    /// <summary>
    /// The wide-but-short panel: wide enough to pass the base width, too short
    /// to pass the base height. 1366x768 and 1280x720 are both still common
    /// laptop panels, and both used to crop the bottom 32 and 80 rows
    /// respectively because the scale could not drop below 1x to compensate.
    /// </summary>
    [Theory]
    [InlineData(1366, 768)]
    [InlineData(1280, 720)]
    [InlineData(1920, 720)]
    public void WideButShortWindowsAreNotCropped(int windowWidth, int windowHeight)
    {
        DisplayPlan plan = DisplayConfig.Plan(windowWidth, windowHeight);

        Assert.Equal(1, plan.IntegerScale);
        Assert.True(plan.ContentScaleHeight <= windowHeight);
        Assert.True(plan.ContentScaleWidth <= windowWidth);
    }

    /// <summary>
    /// The invariant behind both cases above, swept across the shapes a window
    /// can actually take: scaled content never exceeds the window, so neither
    /// bar measurement can go negative. A negative bar width is how the original
    /// crop bug surfaced — <c>LetterboxHeight(768)</c> returned -32 — so this
    /// guards the whole class of mistake rather than the two sizes that found it.
    /// </summary>
    [Theory]
    [InlineData(1366, 768)]
    [InlineData(1280, 720)]
    [InlineData(800, 600)]
    [InlineData(1280, 800)]
    [InlineData(1920, 1080)]
    [InlineData(2560, 1600)]
    [InlineData(3840, 2160)]
    [InlineData(5120, 1440)]
    [InlineData(600, 2000)]
    [InlineData(1, 1)]
    public void BarsAreNeverNegative(int windowWidth, int windowHeight)
    {
        DisplayPlan plan = DisplayConfig.Plan(windowWidth, windowHeight);

        Assert.True(plan.PillarboxWidth(windowWidth) >= 0);
        Assert.True(plan.LetterboxHeight(windowHeight) >= 0);
    }

    /// <summary>
    /// Zoom scales tiles, not the viewport: at 3x a 1280-wide viewport shows a
    /// third of the tiles. Guards the reporting helper against being used with
    /// physical pixels by mistake.
    /// </summary>
    [Fact]
    public void TilesVisibleShrinksWithZoom()
    {
        Assert.Equal(80, DisplayConfig.TilesVisible(1280, 1));
        Assert.Equal(40, DisplayConfig.TilesVisible(1280, 2));
        Assert.Equal(26, DisplayConfig.TilesVisible(1280, 3));
    }
}
