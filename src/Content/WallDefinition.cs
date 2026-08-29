using System.Text.Json.Serialization;

namespace Void;

/// <summary>
/// Definition backing one background <c>wall_id</c> (VOID-018).
///
/// The <c>WallRegistry</c> of world-data-model-spec §7. Walls are purely
/// background: they never collide, but they block light leaking in and are what
/// makes an enclosed room count as valid NPC housing (GDD §5.5).
///
/// <para><b>"No wall" is a real entry.</b> <c>wall_id = 0</c> means bare
/// background (spec §2) and is registered like any other wall, for the same
/// reason air is a registered block: every lookup is by number and should always
/// succeed.</para>
///
/// <para>Numeric ids are stable forever (spec §8) — saved tiles store the raw
/// number, so a renumbering rewrites existing worlds.</para>
/// </summary>
public sealed class WallDefinition : INumericContentDefinition
{
    /// <summary>Stable unique string id, e.g. <c>void:stone_wall</c>. JSON key <c>id</c>.</summary>
    public string Id { get; init; } = string.Empty;

    /// <summary>
    /// Stable numeric key stored in tile records. JSON key <c>wall_id</c> —
    /// declared explicitly, never inferred from load order. <c>0</c> is "no wall".
    /// </summary>
    [JsonPropertyName("wall_id")]
    public ushort NumericId { get; init; }

    /// <summary>Human-facing name. JSON key <c>display_name</c>.</summary>
    public string DisplayName { get; init; } = string.Empty;

    /// <summary>
    /// Texture path, e.g. <c>res://assets/tiles/stone_wall.png</c>. JSON key
    /// <c>sprite</c>. Not validated at load time; art may lag the data.
    /// </summary>
    [JsonPropertyName("sprite")]
    public string SpritePath { get; init; } = string.Empty;
}
