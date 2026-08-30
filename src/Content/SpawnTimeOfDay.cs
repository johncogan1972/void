namespace Void;

/// <summary>
/// When an entry in a biome's enemy pool is eligible to spawn (VOID-022).
///
/// Snake_case string in JSON (<c>"any"</c>, <c>"day"</c>, <c>"night"</c>).
/// <see cref="Any"/> is the default so an entry that omits the field spawns
/// around the clock rather than never.
/// </summary>
public enum SpawnTimeOfDay
{
    /// <summary>Eligible at any time.</summary>
    Any = 0,

    /// <summary>Daytime only.</summary>
    Day = 1,

    /// <summary>Night only.</summary>
    Night = 2,
}
