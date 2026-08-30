using System;
using System.Globalization;
using System.IO;
using System.Text.Json.Serialization;

namespace Void;

/// <summary>
/// One row of a world manifest's chunk index: where a chunk file lives and
/// whether play has touched it (world-data-model-spec §4, save-format-spec §3).
///
/// The index exists so a load can enumerate a world's chunks without scanning
/// the directory, and so eviction can skip rewriting chunks nobody modified
/// (§3, "save on eviction").
/// </summary>
/// <param name="ChunkX">Chunk column, matching <see cref="Chunk.ChunkX"/>.</param>
/// <param name="ChunkY">Chunk row, matching <see cref="Chunk.ChunkY"/>.</param>
/// <param name="File">
/// Path to the chunk file, <b>relative to the campaign directory</b> and always
/// using <c>/</c> as its separator — see <see cref="Separator"/>. Build it with
/// <see cref="Create"/> rather than by hand, and turn it into a real path only
/// at the point of I/O, with <see cref="ResolvePath"/>.
/// </param>
/// <param name="Modified">
/// True once gameplay has changed the chunk since it was generated. Only
/// modified chunks are re-serialised on eviction, so a stale <c>false</c> here
/// silently discards a player's edits.
/// </param>
public sealed record ChunkIndexEntry(
    [property: JsonPropertyOrder(0), JsonRequired] int ChunkX,
    [property: JsonPropertyOrder(1), JsonRequired] int ChunkY,
    [property: JsonPropertyOrder(2), JsonRequired] string File,
    [property: JsonPropertyOrder(3), JsonRequired] bool Modified)
{
    /// <summary>
    /// The one separator that ever appears in a stored path.
    /// </summary>
    /// <remarks>
    /// Load-bearing for portability: a save folder copied from Windows to Linux
    /// (or to a Steam Deck) must still resolve, and a backslash is a legal
    /// filename character on Linux, so a Windows-written path would not merely
    /// fail — it would look for one file with a very odd name.
    /// </remarks>
    public const char Separator = '/';

    /// <summary>Directory holding a world's chunk files, relative to the campaign directory.</summary>
    /// <remarks>
    /// Mirrors save-format-spec §3 exactly:
    /// <c>worlds/&lt;world_uuid&gt;/chunks/</c>. Changing this shape moves every
    /// existing save's chunks.
    /// </remarks>
    public static string ChunkDirectory(Guid worldId) =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"worlds{Separator}{worldId:D}{Separator}chunks");

    /// <summary>Relative path of one chunk file within a campaign directory.</summary>
    /// <remarks>
    /// The file name comes from <see cref="Chunk.ChunkFileName"/> and nowhere
    /// else. The naming rule lives with the chunk format, so a second copy of
    /// <c>$"{x}_{y}.chunk"</c> here would be a second place to forget when it
    /// changes — and the index would then point at files the loader cannot open.
    /// </remarks>
    public static string ChunkPath(Guid worldId, int chunkX, int chunkY) =>
        ChunkDirectory(worldId) + Separator + Chunk.ChunkFileName(chunkX, chunkY);

    /// <summary>Builds an index entry whose path is guaranteed to match the chunk loader's naming.</summary>
    public static ChunkIndexEntry Create(Guid worldId, int chunkX, int chunkY, bool modified = false) =>
        new ChunkIndexEntry(chunkX, chunkY, ChunkPath(worldId, chunkX, chunkY), modified);

    /// <summary>
    /// Turns the stored relative path into a platform path under
    /// <paramref name="campaignDirectory"/>, ready to hand to
    /// <see cref="Chunk.Load"/>.
    /// </summary>
    /// <remarks>
    /// The only place a stored path becomes a real one: conversion happens here
    /// so <see cref="File"/> stays portable everywhere else.
    /// </remarks>
    public string ResolvePath(string campaignDirectory)
    {
        ArgumentNullException.ThrowIfNull(campaignDirectory);
        return Path.Combine(campaignDirectory, File.Replace(Separator, Path.DirectorySeparatorChar));
    }
}
