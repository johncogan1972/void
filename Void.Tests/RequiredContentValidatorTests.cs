using System;
using System.Collections.Generic;
using System.Linq;
using Void;

namespace Void.Tests;

/// <summary>
/// VOID-014 acceptance tests: the game must refuse to boot on an empty required
/// registry, and must say which one and where it looked.
///
/// <para>The regression these guard is not a crash — it is the absence of one.
/// Before this check, a bad export filter or a mistyped content path produced a
/// game that started cleanly and then behaved as though no blocks, biomes or
/// items existed, with every visible symptom a long way from the cause.</para>
/// </summary>
public class RequiredContentValidatorTests
{
    /// <summary>
    /// A source holding nothing, standing in for a folder that was never
    /// exported or a path that resolved somewhere wrong. Its description carries
    /// a recognisable path so the failure message can be checked for it.
    /// </summary>
    private sealed class EmptyContentSource : IContentSource
    {
        /// <summary>Folder this stands in for; only ever used in the description.</summary>
        private readonly string _folder;

        public EmptyContentSource(string folder) => _folder = folder;

        /// <inheritdoc/>
        public string Description => $"directory '/nowhere/data/{_folder}'";

        /// <inheritdoc/>
        public IEnumerable<ContentDocument> ReadAll() => Array.Empty<ContentDocument>();
    }

    /// <summary>
    /// The whole-tree-missing case, driven through the real boot path. Nothing
    /// else in the loader notices it: every folder parses cleanly and there are
    /// no cross-references left to dangle, so without this check boot succeeds
    /// with seven empty registries.
    /// </summary>
    [Fact]
    public void EmptyContentTreeFailsBootNamingEveryRegistryAndItsPath()
    {
        ContentLoadException ex = Assert.Throws<ContentLoadException>(
            () => ContentLoader.LoadAll(static folder => new EmptyContentSource(folder)));

        foreach (ContentRegistrySpec spec in ContentLoader.Registries.Where(static s => s.Required))
        {
            Assert.Contains($"'{spec.Folder}'", ex.Message, StringComparison.Ordinal);
            Assert.Contains($"/nowhere/data/{spec.Folder}", ex.Message, StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// One empty folder among six populated ones must still be caught and named
    /// on its own — the realistic single-folder packaging slip, not just the
    /// total-loss case above.
    /// </summary>
    /// <remarks>
    /// <c>prefabs</c> is the folder to drop here because nothing loaded before
    /// it references a prefab by id: biome vegetation refs are the only prefab
    /// references in the tree and the shipped biomes declare none, so removing
    /// prefabs leaves no dangling reference for an earlier step to trip over.
    /// Dropping <c>blocks</c> instead would fail in biome palette validation and
    /// prove nothing about this check.
    /// </remarks>
    [Fact]
    public void SingleEmptyRequiredRegistryFailsBootNamingOnlyThatOne()
    {
        ContentLoadException ex = Assert.Throws<ContentLoadException>(
            () => ContentLoader.LoadAll(static folder => folder == ContentLoader.PrefabsFolder
                ? new EmptyContentSource(folder)
                : ContentPaths.Source(folder)));

        Assert.Contains($"'{ContentLoader.PrefabsFolder}'", ex.Message, StringComparison.Ordinal);
        Assert.Contains($"/nowhere/data/{ContentLoader.PrefabsFolder}", ex.Message, StringComparison.Ordinal);
        Assert.DoesNotContain($"'{ContentLoader.BlocksFolder}'", ex.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// The constraint lives at boot, not in the mechanism: a registry declared
    /// optional may legitimately hold nothing. Without this the "optional" flag
    /// would be decoration, and the first registry that can genuinely ship empty
    /// would have to be handled by editing the check.
    /// </summary>
    [Fact]
    public void OptionalRegistryMayBeEmpty()
    {
        GameContent content = ContentPaths.All();

        ContentRegistrySpec optional = new(
            "test_optional", Required: false, static _ => 0);

        RequiredContentValidator.Validate(
            content,
            new[] { optional },
            new Dictionary<string, string>(StringComparer.Ordinal));
    }

    /// <summary>
    /// A required folder with no recorded path still fails, rather than throwing
    /// over the missing diagnostic. Reporting the registry with a vaguer message
    /// beats losing the failure entirely.
    /// </summary>
    [Fact]
    public void MissingSearchedPathStillFailsNamingTheRegistry()
    {
        GameContent content = ContentPaths.All();

        ContentLoadException ex = Assert.Throws<ContentLoadException>(
            () => RequiredContentValidator.Validate(
                content,
                new[] { new ContentRegistrySpec("test_required", Required: true, static _ => 0) },
                new Dictionary<string, string>(StringComparer.Ordinal)));

        Assert.Contains("test_required", ex.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// The shipped tree satisfies its own declaration. This is what stops
    /// someone marking a registry required and shipping nothing in it — the
    /// suite goes red here rather than the game failing to boot for a player.
    /// </summary>
    [Fact]
    public void ShippedContentSatisfiesEveryRequiredRegistry()
    {
        GameContent content = ContentPaths.All();

        foreach (ContentRegistrySpec spec in ContentLoader.Registries.Where(static s => s.Required))
        {
            Assert.True(
                spec.CountIn(content) > 0,
                $"Required registry '{spec.Folder}' is empty in the shipped data/ tree.");
        }
    }

    /// <summary>
    /// Each spec's counter must read the registry its folder names. A
    /// copy-pasted selector pointing at the wrong property would leave the real
    /// registry's emptiness permanently unchecked while every other test still
    /// passed — silent, and exactly the failure mode this ticket exists to end.
    /// </summary>
    [Fact]
    public void EverySpecCountsItsOwnRegistry()
    {
        GameContent content = ContentPaths.All();

        foreach (ContentRegistrySpec spec in ContentLoader.Registries)
        {
            int expected = spec.Folder switch
            {
                ContentLoader.BlocksFolder => content.Blocks.Count,
                ContentLoader.WallsFolder => content.Walls.Count,
                ContentLoader.ItemsFolder => content.Items.Count,
                ContentLoader.LootTablesFolder => content.LootTables.Count,
                ContentLoader.EnemiesFolder => content.Enemies.Count,
                ContentLoader.BiomesFolder => content.Biomes.Count,
                ContentLoader.PrefabsFolder => content.Prefabs.Count,
                ContentLoader.WorldTypesFolder => content.WorldTypes.Count,
                _ => throw new InvalidOperationException(
                    $"Registry '{spec.Folder}' is declared but not covered by this test. "
                    + "Add it here — an uncovered spec can point at the wrong registry unnoticed."),
            };

            Assert.Equal(expected, spec.CountIn(content));
        }
    }

    /// <summary>
    /// <see cref="ContentLoader.LoadOrder"/> is derived from the same table, so
    /// the documented order and the checked declaration cannot drift. Pinned
    /// because the derivation is the reason the two lists were collapsed into
    /// one; hand-writing LoadOrder again would silently reintroduce the drift.
    /// </summary>
    [Fact]
    public void LoadOrderMirrorsTheRegistryDeclaration()
    {
        Assert.Equal(
            ContentLoader.Registries.Select(static s => s.Folder),
            ContentLoader.LoadOrder);
    }
}
