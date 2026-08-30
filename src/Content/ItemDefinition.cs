using System.Text.Json.Serialization;

namespace Void;

/// <summary>
/// Definition backing one item id (VOID-023).
///
/// The <c>ItemRegistry</c> of world-data-model-spec §7. Every id that content
/// names as a drop — a block's <c>drop_item_id</c>, a loot table entry — resolves
/// here.
///
/// <para><b>This is the base shape only.</b> Items get their real treatment in
/// Phase 5 (inventory, equipment, crafting): stats, rarity ranges, equip slots,
/// tool power and categories all land there. They are deliberately absent rather
/// than guessed, because a field whose meaning is undecided is a field content
/// authors will fill in wrongly, and un-filling it later is expensive. Adding a
/// field is cheap; removing an authored one is not.</para>
///
/// <para>Items carry no numeric id: nothing in the tile record stores an item by
/// number, so there is no stable-forever numbering to defend. String ids are
/// still stable forever — saves reference inventory contents by id.</para>
///
/// <para>Loads through the plain <see cref="RegistryLoader"/>: an item names
/// nothing in another registry, so parsing it is the whole of validating it.</para>
/// </summary>
public sealed class ItemDefinition : IContentDefinition
{
    /// <summary>Stable unique string id, e.g. <c>void:stone_block</c>. JSON key <c>id</c>.</summary>
    public string Id { get; init; } = string.Empty;

    /// <summary>Human-facing name, shown in inventory and tooltips. JSON key <c>display_name</c>.</summary>
    public string DisplayName { get; init; } = string.Empty;

    /// <summary>
    /// Texture path, e.g. <c>res://assets/items/stone_block.png</c>. JSON key
    /// <c>sprite</c>. Not validated at load time: content data legitimately runs
    /// ahead of art, and a missing texture must not block a world load.
    /// </summary>
    [JsonPropertyName("sprite")]
    public string SpritePath { get; init; } = string.Empty;

    /// <summary>
    /// Maximum count in one inventory slot. Per-item by design — GDD §5.6 sets
    /// no universal cap, so torches stack high and weapons do not stack at all.
    /// Defaults to 1, the non-stacking case, so an item that omits the field can
    /// never accidentally stack. JSON key <c>max_stack</c>.
    /// </summary>
    [JsonPropertyName("max_stack")]
    public int MaxStack { get; init; } = 1;
}
