using System;
using System.Collections.Generic;
using System.IO;
using Void;

namespace Void.Tools;

/// <summary>
/// Command-line entry point for author-facing build tooling (VOID-026).
///
/// Today it holds one command, <c>tmx-convert</c>, which regenerates
/// <c>data/prefabs/generated/</c> from the authored maps in
/// <c>content/tiled/</c>:
///
/// <code>dotnet run --project Void.Tools -- tmx-convert</code>
///
/// <para>This is a convenience, not the guarantee. The guarantee is
/// <c>TiledPrefabConverterTests</c>, which reconverts in memory and compares
/// bytes, so forgetting to run this fails rung 4 rather than shipping a prefab
/// that no longer matches its map.</para>
///
/// <para>Deliberately not a Godot project: it references the engine-free content
/// layer only, so it runs on a machine with no editor installed.</para>
/// </summary>
public static class Program
{
    /// <summary>Repo-relative locations, so the command needs no arguments in the normal case.</summary>
    private const string TiledFolder = "content/tiled";
    private const string OutputFolder = "data/prefabs/generated";
    private const string TilesetMapFile = "tileset_map.json";

    /// <summary>
    /// Runs one command. Returns 0 on success and 1 with the failure on stderr,
    /// so a script or a pre-commit hook can rely on the exit code; a conversion
    /// error must never leave a half-written output tree looking like success.
    /// </summary>
    /// <param name="args">
    /// <c>tmx-convert [repoRoot]</c>. The optional root defaults to the current
    /// directory, which is where <c>dotnet run --project Void.Tools</c> leaves it
    /// when invoked from the repo.
    /// </param>
    public static int Main(string[] args)
    {
        if (args.Length == 0)
        {
            Console.Error.WriteLine("usage: Void.Tools tmx-convert [repo-root]");
            return 1;
        }

        try
        {
            switch (args[0])
            {
                case "tmx-convert":
                    return TmxConvert(args.Length > 1 ? args[1] : Directory.GetCurrentDirectory());

                default:
                    Console.Error.WriteLine($"unknown command '{args[0]}'; expected 'tmx-convert'.");
                    return 1;
            }
        }
        catch (TiledConversionException ex)
        {
            Console.Error.WriteLine(ex.Message);
            return 1;
        }
    }

    /// <summary>
    /// Converts every <c>.tmx</c> in <c>content/tiled/</c> and writes the result
    /// to <c>data/prefabs/generated/</c>. Maps are processed in ordinal file-name
    /// order so a run that fails halfway blames the same map on every machine.
    /// </summary>
    private static int TmxConvert(string repoRoot)
    {
        string tiled = Path.Combine(repoRoot, TiledFolder);
        string output = Path.Combine(repoRoot, OutputFolder);

        if (!Directory.Exists(tiled))
        {
            Console.Error.WriteLine($"no '{TiledFolder}' under '{repoRoot}'; pass the repo root as the second argument.");
            return 1;
        }

        TilesetMap tilesets = TilesetMap.FromFile(Path.Combine(tiled, TilesetMapFile));

        List<string> maps = new(Directory.GetFiles(tiled, "*.tmx"));
        maps.Sort(StringComparer.Ordinal);

        Directory.CreateDirectory(output);

        foreach (string map in maps)
        {
            string json = TiledPrefabConverter.ConvertFile(map, tilesets);
            string destination = Path.Combine(output, TiledPrefabConverter.OutputFileName(map));

            // Written with the converter's own newlines: File.WriteAllText does no
            // line-ending translation, which is what keeps the committed bytes
            // identical to what the staleness test reconverts.
            File.WriteAllText(destination, json);
            Console.WriteLine($"{Path.GetFileName(map)} -> {OutputFolder}/{Path.GetFileName(destination)}");
        }

        Console.WriteLine($"{maps.Count} map(s) converted.");
        return 0;
    }
}
