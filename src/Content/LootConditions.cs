using System.Text.Json.Serialization;

namespace Void;

/// <summary>
/// Optional gates on a single loot entry (VOID-023), per loot-table-spec §4.
///
/// <para>The whole block is optional, and so is every field in it — all three
/// are nullable so "not authored" is distinguishable from "authored as false /
/// empty". An entry with no conditions always fires its roll.</para>
///
/// <para>Evaluating these is Phase 5 work; this ticket only fixes the shape the
/// data is authored in.</para>
/// </summary>
public sealed class LootConditions
{
    /// <summary>
    /// When true, the entry only fires the first time the source is killed —
    /// boss trophies and one-off recipe drops. Null means unconditional.
    /// JSON key <c>first_kill_only</c>.
    /// </summary>
    [JsonPropertyName("first_kill_only")]
    public bool? FirstKillOnly { get; init; }

    /// <summary>
    /// World or campaign flag that must be set for the entry to fire, or null
    /// for no requirement. JSON key <c>requires_flag</c>.
    /// </summary>
    [JsonPropertyName("requires_flag")]
    public string? RequiresFlag { get; init; }

    /// <summary>
    /// World or campaign flag that must <i>not</i> be set, or null for no
    /// requirement. Separate from <see cref="RequiresFlag"/> rather than a
    /// negation syntax, so both can gate the same entry.
    /// JSON key <c>requires_no_flag</c>.
    /// </summary>
    [JsonPropertyName("requires_no_flag")]
    public string? RequiresNoFlag { get; init; }
}
