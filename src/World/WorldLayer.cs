namespace Void;

/// <summary>
/// The vertical band of the world a chunk sits in, stored as the header's
/// <c>layer_primary</c> byte (VOID-020, world-data-model-spec §3).
///
/// Values are <b>wire values</b> fixed by the spec's "0=outside, 1=underground,
/// 2=deep, 3=void" and must never be renumbered — they are written into every
/// chunk file and drive which generation and content tables apply.
/// </summary>
public enum WorldLayer : byte
{
    /// <summary>Surface and sky.</summary>
    Outside = 0,

    /// <summary>The first underground band, below the surface transition.</summary>
    Underground = 1,

    /// <summary>The deep band, below underground.</summary>
    Deep = 2,

    /// <summary>The lowest band.</summary>
    Void = 3,
}
