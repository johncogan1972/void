using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Void;

namespace Void.Tests;

/// <summary>
/// VOID-006 acceptance tests. These run with no Godot engine initialised, which
/// is the whole reason the content core is engine-free: nothing here may touch
/// <c>GodotContentSource</c>.
/// </summary>
public class RegistryTests : IDisposable
{
    private readonly string _root;

    /// <summary>
    /// Creates a throwaway content directory per test, so cases cannot see each
    /// other's JSON files.
    /// </summary>
    public RegistryTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "void-registry-tests-" + Path.GetRandomFileName());
        Directory.CreateDirectory(_root);
    }

    /// <summary>
    /// Removes the temp directory. Failures here are not worth failing a test over,
    /// but leftovers would accumulate across runs.
    /// </summary>
    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }

        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Drops a JSON document into the temp content root, creating subdirectories.
    /// </summary>
    private void WriteFile(string name, string json)
    {
        string path = Path.Combine(_root, name);
        string? dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }

        File.WriteAllText(path, json);
    }

    /// <summary>
    /// Loads the whole temp root through the filesystem source — never the Godot
    /// source, which needs an initialised engine this suite does not have.
    /// </summary>
    private Registry<SampleDefinition> Load() =>
        RegistryLoader.Load<SampleDefinition>(new DirectoryContentSource(_root));

    private static string Entry(string id, string displayName, int sortOrder) =>
        $"{{ \"id\": \"{id}\", \"display_name\": \"{displayName}\", \"sort_order\": {sortOrder} }}";

    /// <summary>
    /// The happy path: a folder of JSON becomes a registry addressable by id, with
    /// non-string fields intact.
    /// </summary>
    [Fact]
    public void LoadsFolderOfJsonAndResolvesIds()
    {
        WriteFile("stone.json", Entry("void:stone", "Stone", 10));
        WriteFile("nested/dirt.json", Entry("void:dirt", "Dirt", 20));

        Registry<SampleDefinition> registry = Load();

        Assert.Equal(2, registry.Count);
        Assert.Equal("Stone", registry.Get("void:stone").DisplayName);
        Assert.Equal(20, registry["void:dirt"].SortOrder);
        Assert.True(registry.Contains("void:dirt"));
    }

    /// <summary>
    /// A document may hold one object or an array of them, so content authors can
    /// group or split files however reads best. Both shapes must load identically.
    /// </summary>
    [Fact]
    public void ArrayDocumentYieldsMultipleEntries()
    {
        WriteFile("pack.json", $"[{Entry("void:a", "A", 1)}, {Entry("void:b", "B", 2)}]");

        Registry<SampleDefinition> registry = Load();

        Assert.Equal(new[] { "void:a", "void:b" }, registry.Ids);
    }

    /// <summary>
    /// The determinism guarantee, and the reason this whole type is sorted.
    ///
    /// Registry order feeds world generation, so it must be identical on every
    /// machine. Sorting is ordinal, not culture-aware: a Turkish locale must not
    /// reorder the world. If this goes red, worlds stop being reproducible.
    /// </summary>
    [Fact]
    public void IterationIsOrdinalSortedByIdRegardlessOfFileOrder()
    {
        // File names deliberately in the reverse of id order, so any reliance on
        // enumeration order would show up here.
        WriteFile("01_zeta.json", Entry("void:zeta", "Zeta", 1));
        WriteFile("02_alpha.json", Entry("void:alpha", "Alpha", 2));
        WriteFile("03_Beta.json", Entry("void:Beta", "Beta", 3));

        List<string> ids = Load().Select(d => d.Id).ToList();

        // Ordinal, not culture-aware: uppercase 'B' sorts before lowercase.
        Assert.Equal(new[] { "void:Beta", "void:alpha", "void:zeta" }, ids);
    }

    /// <summary>
    /// A built registry is immutable. Content that could change after boot would
    /// let two players with the same seed generate different worlds.
    /// </summary>
    [Fact]
    public void RegistryIsFrozenAfterBuild()
    {
        RegistryBuilder<SampleDefinition> builder = new();
        builder.Add(new SampleDefinition { Id = "void:a" }, "a.json");
        Registry<SampleDefinition> registry = builder.Build();

        // Adding to the builder afterwards must not affect the built registry.
        builder.Add(new SampleDefinition { Id = "void:b" }, "b.json");

        Assert.Single(registry);
        Assert.False(registry.Contains("void:b"));
    }

    /// <summary>
    /// Duplicate ids are fatal and name both files. Last-writer-wins would make the
    /// world depend on filesystem enumeration order, and the error has to point at
    /// both culprits or the author cannot find the collision.
    /// </summary>
    [Fact]
    public void DuplicateIdNamesIdAndBothFiles()
    {
        WriteFile("a_first.json", Entry("void:dupe", "First", 1));
        WriteFile("b_second.json", Entry("void:dupe", "Second", 2));

        ContentLoadException ex = Assert.Throws<ContentLoadException>(() => Load());

        Assert.Contains("void:dupe", ex.Message, StringComparison.Ordinal);
        Assert.Contains("a_first.json", ex.Message, StringComparison.Ordinal);
        Assert.Contains("b_second.json", ex.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// A missing id is a bug in data or code, so lookup fails loudly with the id in
    /// the message rather than returning null for someone to trip over later.
    /// </summary>
    [Fact]
    public void UnknownIdLookupThrowsNamingTheId()
    {
        WriteFile("stone.json", Entry("void:stone", "Stone", 10));
        Registry<SampleDefinition> registry = Load();

        ContentLoadException ex = Assert.Throws<ContentLoadException>(() => registry.Get("void:missing"));

        Assert.Contains("void:missing", ex.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// The probing form, for the callers that treat absence as normal.
    /// </summary>
    [Fact]
    public void TryGetProbesWithoutThrowing()
    {
        WriteFile("stone.json", Entry("void:stone", "Stone", 10));
        Registry<SampleDefinition> registry = Load();

        Assert.True(registry.TryGet("void:stone", out SampleDefinition found));
        Assert.Equal("Stone", found.DisplayName);
        Assert.False(registry.TryGet("void:missing", out _));
    }

    /// <summary>
    /// A bad JSON drop must be diagnosable without a debugger: the message names the
    /// file and the parse problem.
    /// </summary>
    [Fact]
    public void MalformedJsonNamesFileAndProblem()
    {
        WriteFile("broken.json", "{ \"id\": \"void:broken\", ");

        ContentLoadException ex = Assert.Throws<ContentLoadException>(() => Load());

        Assert.Equal("broken.json", ex.FileName);
        Assert.Contains("broken.json", ex.Message, StringComparison.Ordinal);
        Assert.Contains("Malformed JSON", ex.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// A root that is neither object nor array is rejected by name, rather than
    /// silently contributing nothing.
    /// </summary>
    [Fact]
    public void NonObjectRootNamesFile()
    {
        WriteFile("scalar.json", "42");

        ContentLoadException ex = Assert.Throws<ContentLoadException>(() => Load());

        Assert.Equal("scalar.json", ex.FileName);
        Assert.Contains("object or array", ex.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// Every entry needs a usable id. Blank, whitespace and absent all fail the same
    /// way, because an entry with no id cannot be addressed or sorted.
    /// </summary>
    [Theory]
    [InlineData("{ \"display_name\": \"No Id\" }")]
    [InlineData("{ \"id\": \"\", \"display_name\": \"Empty\" }")]
    [InlineData("{ \"id\": \"   \", \"display_name\": \"Blank\" }")]
    public void MissingOrEmptyIdNamesFile(string json)
    {
        WriteFile("nameless.json", json);

        ContentLoadException ex = Assert.Throws<ContentLoadException>(() => Load());

        Assert.Equal("nameless.json", ex.FileName);
        Assert.Contains("'id'", ex.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// A missing content directory is fatal, not an empty registry. Silently booting
    /// with no content would surface much later as an unrelated crash.
    /// </summary>
    [Fact]
    public void MissingDirectoryFailsLoudly()
    {
        string missing = Path.Combine(_root, "does-not-exist");

        ContentLoadException ex = Assert.Throws<ContentLoadException>(
            () => RegistryLoader.Load<SampleDefinition>(new DirectoryContentSource(missing)));

        Assert.Contains("does-not-exist", ex.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// An existing but empty directory is legitimate — the failure above is about the
    /// directory being absent, not unpopulated.
    /// </summary>
    [Fact]
    public void EmptyDirectoryProducesEmptyRegistry()
    {
        Assert.Empty(Load());
        Assert.Empty(Registry<SampleDefinition>.Empty);
    }

    /// <summary>
    /// Several sources merge into one builder, which is how base content plus later
    /// mods or portal-world packs will share a registry.
    /// </summary>
    [Fact]
    public void MultipleSourcesCanFeedOneRegistry()
    {
        WriteFile("base/a.json", Entry("void:a", "A", 1));
        WriteFile("extra/b.json", Entry("void:b", "B", 2));

        RegistryBuilder<SampleDefinition> builder = new();
        RegistryLoader.LoadInto(builder, new DirectoryContentSource(Path.Combine(_root, "base")));
        RegistryLoader.LoadInto(builder, new DirectoryContentSource(Path.Combine(_root, "extra")));

        Assert.Equal(new[] { "void:a", "void:b" }, builder.Build().Ids);
    }
}
