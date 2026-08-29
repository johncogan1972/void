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

    public RegistryTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "void-registry-tests-" + Path.GetRandomFileName());
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }

        GC.SuppressFinalize(this);
    }

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

    private Registry<ExampleDefinition> Load() =>
        RegistryLoader.Load<ExampleDefinition>(new DirectoryContentSource(_root));

    private static string Entry(string id, string displayName, int sortOrder) =>
        $"{{ \"id\": \"{id}\", \"display_name\": \"{displayName}\", \"sort_order\": {sortOrder} }}";

    [Fact]
    public void LoadsFolderOfJsonAndResolvesIds()
    {
        WriteFile("stone.json", Entry("void:stone", "Stone", 10));
        WriteFile("nested/dirt.json", Entry("void:dirt", "Dirt", 20));

        Registry<ExampleDefinition> registry = Load();

        Assert.Equal(2, registry.Count);
        Assert.Equal("Stone", registry.Get("void:stone").DisplayName);
        Assert.Equal(20, registry["void:dirt"].SortOrder);
        Assert.True(registry.Contains("void:dirt"));
    }

    [Fact]
    public void ArrayDocumentYieldsMultipleEntries()
    {
        WriteFile("pack.json", $"[{Entry("void:a", "A", 1)}, {Entry("void:b", "B", 2)}]");

        Registry<ExampleDefinition> registry = Load();

        Assert.Equal(new[] { "void:a", "void:b" }, registry.Ids);
    }

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

    [Fact]
    public void RegistryIsFrozenAfterBuild()
    {
        RegistryBuilder<ExampleDefinition> builder = new();
        builder.Add(new ExampleDefinition { Id = "void:a" }, "a.json");
        Registry<ExampleDefinition> registry = builder.Build();

        // Adding to the builder afterwards must not affect the built registry.
        builder.Add(new ExampleDefinition { Id = "void:b" }, "b.json");

        Assert.Single(registry);
        Assert.False(registry.Contains("void:b"));
    }

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

    [Fact]
    public void UnknownIdLookupThrowsNamingTheId()
    {
        WriteFile("stone.json", Entry("void:stone", "Stone", 10));
        Registry<ExampleDefinition> registry = Load();

        ContentLoadException ex = Assert.Throws<ContentLoadException>(() => registry.Get("void:missing"));

        Assert.Contains("void:missing", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void TryGetProbesWithoutThrowing()
    {
        WriteFile("stone.json", Entry("void:stone", "Stone", 10));
        Registry<ExampleDefinition> registry = Load();

        Assert.True(registry.TryGet("void:stone", out ExampleDefinition found));
        Assert.Equal("Stone", found.DisplayName);
        Assert.False(registry.TryGet("void:missing", out _));
    }

    [Fact]
    public void MalformedJsonNamesFileAndProblem()
    {
        WriteFile("broken.json", "{ \"id\": \"void:broken\", ");

        ContentLoadException ex = Assert.Throws<ContentLoadException>(() => Load());

        Assert.Equal("broken.json", ex.FileName);
        Assert.Contains("broken.json", ex.Message, StringComparison.Ordinal);
        Assert.Contains("Malformed JSON", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void NonObjectRootNamesFile()
    {
        WriteFile("scalar.json", "42");

        ContentLoadException ex = Assert.Throws<ContentLoadException>(() => Load());

        Assert.Equal("scalar.json", ex.FileName);
        Assert.Contains("object or array", ex.Message, StringComparison.Ordinal);
    }

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

    [Fact]
    public void MissingDirectoryFailsLoudly()
    {
        string missing = Path.Combine(_root, "does-not-exist");

        ContentLoadException ex = Assert.Throws<ContentLoadException>(
            () => RegistryLoader.Load<ExampleDefinition>(new DirectoryContentSource(missing)));

        Assert.Contains("does-not-exist", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void EmptyDirectoryProducesEmptyRegistry()
    {
        Assert.Empty(Load());
        Assert.Empty(Registry<ExampleDefinition>.Empty);
    }

    [Fact]
    public void MultipleSourcesCanFeedOneRegistry()
    {
        WriteFile("base/a.json", Entry("void:a", "A", 1));
        WriteFile("extra/b.json", Entry("void:b", "B", 2));

        RegistryBuilder<ExampleDefinition> builder = new();
        RegistryLoader.LoadInto(builder, new DirectoryContentSource(Path.Combine(_root, "base")));
        RegistryLoader.LoadInto(builder, new DirectoryContentSource(Path.Combine(_root, "extra")));

        Assert.Equal(new[] { "void:a", "void:b" }, builder.Build().Ids);
    }
}
