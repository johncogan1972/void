namespace Void;

/// <summary>
/// Which liquid occupies a tile (VOID-019, world-data-model-spec §2).
///
/// These are <b>wire values</b>: they are stored in the packed <see cref="Tile"/>
/// and therefore land in save files. Never renumber one — retire a value and add
/// a new one instead, exactly like block and wall ids (spec §8).
///
/// The field is a nibble in the packed tile, so values must stay in 0–15. That
/// ceiling is the reason this is a small fixed vocabulary rather than an open
/// registry: liquids are simulated by the flow code, not data-driven content.
/// </summary>
public enum LiquidType : byte
{
    /// <summary>No liquid. The tile is dry regardless of its block.</summary>
    None = 0,

    /// <summary>Water.</summary>
    Water = 1,

    /// <summary>Lava. Damaging, and interacts with water on contact.</summary>
    Lava = 2,

    /// <summary>Poison water, found in the deep layer.</summary>
    PoisonWater = 3,

    /// <summary>Poison gas. Flows upward rather than down.</summary>
    PoisonGas = 4,

    /// <summary>Liquid void, the deepest-layer hazard.</summary>
    LiquidVoid = 5,
}
