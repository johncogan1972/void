using System.Text.Json.Serialization;

namespace Void;

/// <summary>
/// How a block interacts with entity movement (VOID-018).
///
/// Serialised as a lowercase string (<c>"none"</c>, <c>"solid"</c>,
/// <c>"platform"</c>) rather than a bare integer, so data files stay readable
/// and a reordering of the enum can never silently change existing content.
/// </summary>
public enum BlockCollision
{
    /// <summary>No collision at all — air, and non-solid decoration blocks.</summary>
    None = 0,

    /// <summary>Full collision on every side.</summary>
    Solid = 1,

    /// <summary>One-way platform: collides from above only, drop-through from below.</summary>
    Platform = 2,
}

/// <summary>
/// Definition backing one foreground <c>block_id</c> (VOID-018).
///
/// Resolves the <c>block_id</c> stored in every tile record
/// (world-data-model-spec §2) to sprite, hardness, drop and physics behaviour —
/// the <c>BlockRegistry</c> of spec §7.
///
/// <para><b>Air is a real entry.</b> <c>block_id = 0</c> is empty space by
/// convention (spec §2), but it is registered like any other block rather than
/// being a magic absence: generation, mining and lighting all look blocks up by
/// number, and a nullable "no definition" case at every call site would be pure
/// noise. Air simply has <see cref="BlockCollision.None"/>,
/// <see cref="Hardness"/> 0 and no drop.</para>
///
/// <para><b>Numeric ids are stable forever</b> (spec §8). Every saved world
/// stores raw numbers, so changing an entry's <c>block_id</c> silently rewrites
/// existing worlds. Retire numbers; never reuse them.</para>
/// </summary>
public sealed class BlockDefinition : INumericContentDefinition
{
    /// <summary>Stable unique string id, e.g. <c>void:stone</c>. JSON key <c>id</c>.</summary>
    public string Id { get; init; } = string.Empty;

    /// <summary>
    /// Stable numeric key stored in tile records. JSON key <c>block_id</c> —
    /// spelled out in the data file, never inferred from load order.
    /// <c>0</c> is air by convention.
    /// </summary>
    [JsonPropertyName("block_id")]
    public ushort NumericId { get; init; }

    /// <summary>Human-facing name. JSON key <c>display_name</c>.</summary>
    public string DisplayName { get; init; } = string.Empty;

    /// <summary>
    /// Texture path, e.g. <c>res://assets/tiles/stone.png</c>. JSON key
    /// <c>sprite</c>. Not validated at load time: content data legitimately runs
    /// ahead of art, and a missing texture must not block world generation.
    /// </summary>
    [JsonPropertyName("sprite")]
    public string SpritePath { get; init; } = string.Empty;

    /// <summary>
    /// Mining resistance. Higher takes longer to break; <c>0</c> means it cannot
    /// be mined as such (air). Compared against tool power at mining time.
    /// </summary>
    public int Hardness { get; init; }

    /// <summary>
    /// Item id yielded when broken, or <c>null</c> for no drop (air, and blocks
    /// that vanish). Deliberately nullable rather than empty-string sentinel.
    /// JSON key <c>drop_item_id</c>.
    /// </summary>
    public string? DropItemId { get; init; }

    /// <summary>Movement collision behaviour. JSON key <c>collision</c>.</summary>
    public BlockCollision Collision { get; init; } = BlockCollision.Solid;

    /// <summary>
    /// True if the block stops propagated light. Separate from
    /// <see cref="Collision"/> because glass is solid but transparent, and
    /// platforms are semi-solid but let light through. JSON key <c>blocks_light</c>.
    /// </summary>
    public bool BlocksLight { get; init; } = true;
}
