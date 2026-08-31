using System.Linq;
using System.Text.Json;
using Void;
using Xunit;

namespace Void.Tests;

/// <summary>
/// VOID-023 acceptance tests for the item schema and the shipped item registry.
/// Items are the root of the content reference chain — blocks drop them and loot
/// tables grant them — so these tests guard what everything else resolves
/// against. Engine-free, like the rest of <c>Void.Tests</c>.
/// </summary>
public class ItemDefinitionTests
{
    /// <summary>
    /// The shipped item file must load clean with every field as authored. If
    /// this goes red the game boots into a fatal content error and nothing that
    /// drops an item can be loaded at all.
    /// </summary>
    [Fact]
    public void ShippedItemsLoadAndCarryEveryAuthoredField()
    {
        Registry<ItemDefinition> items = ContentPaths.Items();

        Assert.Equal(4, items.Count);
        Assert.Equal(
            new[] { "void:dirt_block", "void:sand_block", "void:stone_block", "void:wood_block" },
            items.Ids);

        ItemDefinition dirt = items.Get("void:dirt_block");
        Assert.Equal("Dirt Block", dirt.DisplayName);
        Assert.Equal("res://assets/items/dirt_block.png", dirt.SpritePath);
        Assert.Equal(999, dirt.MaxStack);
    }

    /// <summary>
    /// Every <c>drop_item_id</c> in the shipped block registry must resolve
    /// against the shipped items. VOID-018 left these refs dangling on purpose;
    /// if this goes red, mining a block yields nothing and says nothing.
    /// </summary>
    [Fact]
    public void EveryShippedBlockDropResolvesToAnItem()
    {
        Registry<ItemDefinition> items = ContentPaths.Items();
        Registry<BlockDefinition> blocks = ContentPaths.Blocks();

        foreach (BlockDefinition block in blocks)
        {
            if (block.DropItemId is null)
            {
                continue;
            }

            Assert.True(
                items.Contains(block.DropItemId),
                $"Block '{block.Id}' drops '{block.DropItemId}', which is not a registered item.");
        }
    }

    /// <summary>
    /// Round-trip must be lossless. Tooling rewrites content files, so a dropped
    /// field would quietly delete authored data on the next save. The
    /// serialise-compare alone passes vacuously if a field is dropped
    /// symmetrically, so specific values are asserted on the reloaded object too.
    /// </summary>
    [Fact]
    public void RoundTripIsByteIdenticalAndKeepsFieldValues()
    {
        foreach (ItemDefinition item in ContentPaths.Items())
        {
            string first = JsonSerializer.Serialize(item, RegistryLoader.Options);
            ItemDefinition? reloaded = JsonSerializer.Deserialize<ItemDefinition>(first, RegistryLoader.Options);
            Assert.NotNull(reloaded);
            string second = JsonSerializer.Serialize(reloaded, RegistryLoader.Options);

            Assert.Equal(first, second);
            Assert.Equal(item.Id, reloaded.Id);
            Assert.Equal(item.DisplayName, reloaded.DisplayName);
            Assert.Equal(item.SpritePath, reloaded.SpritePath);
            Assert.Equal(item.MaxStack, reloaded.MaxStack);
        }
    }

    /// <summary>
    /// An item omitting <c>max_stack</c> must default to 1, not to 0. A zero
    /// default would make the item impossible to pick up; 1 is the safe
    /// non-stacking case. GDD §5.6: stack size is per-item, no universal cap.
    /// </summary>
    [Fact]
    public void MaxStackDefaultsToOneWhenOmitted()
    {
        ItemDefinition? item = JsonSerializer.Deserialize<ItemDefinition>(
            """{ "id": "test:thing", "display_name": "Thing" }""", RegistryLoader.Options);

        Assert.NotNull(item);
        Assert.Equal(1, item.MaxStack);
    }

    /// <summary>
    /// Items name nothing in another registry, so they must load through the
    /// generic loader. If this goes red, <c>ItemDefinition</c> has picked up the
    /// <c>ICrossRegistryValidated</c> marker it must not have, and every plain
    /// item load in the codebase starts throwing.
    /// </summary>
    [Fact]
    public void GenericRegistryLoaderAcceptsItems()
    {
        Assert.False(typeof(ICrossRegistryValidated).IsAssignableFrom(typeof(ItemDefinition)));
        Assert.NotEmpty(RegistryLoader.Load<ItemDefinition>(ContentPaths.Source("items")).ToList());
    }
}
