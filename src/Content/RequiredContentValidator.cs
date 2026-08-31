using System;
using System.Collections.Generic;
using System.Text;

namespace Void;

/// <summary>
/// The boot-time check that every required registry actually holds something
/// (VOID-014).
///
/// <para><b>Why this is not in <see cref="Registry{T}"/>.</b> An empty registry
/// is a perfectly valid registry — VOID-006 built the generic mechanism that
/// way deliberately and a test pins it, because a registry merged from several
/// sources may legitimately get nothing from one of them. What is not valid is
/// <i>booting the game</i> on one. So the constraint lives here, at the boot
/// seam, and the mechanism stays unopinionated.</para>
///
/// <para>Without this, a bad export filter or a mistyped path produces a game
/// that starts cleanly and then behaves as though no blocks, biomes or items
/// exist — a world of air and a stream of unrelated errors a long way from the
/// cause. VOID-013 stopped one instance of that (the export filter); this makes
/// the whole class of it visible.</para>
/// </summary>
public static class RequiredContentValidator
{
    /// <summary>
    /// Fails if any registry declared <see cref="ContentRegistrySpec.Required"/>
    /// loaded zero entries.
    /// </summary>
    /// <param name="content">The registries as loaded; not mutated.</param>
    /// <param name="specs">
    /// The declaration to check against — normally
    /// <see cref="ContentLoader.Registries"/>. Passed in rather than read from
    /// there so tests can drive the optional-registry path without inventing a
    /// folder in the shipped tree.
    /// </param>
    /// <param name="searchedPaths">
    /// Folder name to the human-readable path it was read from
    /// (<see cref="IContentSource.Description"/>). The likely causes of an empty
    /// registry are all path or packaging problems, so the message is close to
    /// useless without this. A folder missing from the map degrades to naming
    /// the folder alone rather than throwing over a diagnostic.
    /// </param>
    /// <exception cref="ContentLoadException">
    /// If one or more required registries are empty. <b>All</b> of them are
    /// named in a single message: an empty registry usually means the whole
    /// content tree failed to arrive, and reporting one folder per run would
    /// turn one packaging bug into seven boot attempts.
    /// </exception>
    public static void Validate(
        GameContent content,
        IReadOnlyList<ContentRegistrySpec> specs,
        IReadOnlyDictionary<string, string> searchedPaths)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(specs);
        ArgumentNullException.ThrowIfNull(searchedPaths);

        List<ContentRegistrySpec> empty = new();

        foreach (ContentRegistrySpec spec in specs)
        {
            if (spec.Required && spec.CountIn(content) == 0)
            {
                empty.Add(spec);
            }
        }

        if (empty.Count == 0)
        {
            return;
        }

        StringBuilder message = new();
        message.Append(empty.Count == 1
            ? "Required content registry is empty: "
            : $"{empty.Count} required content registries are empty: ");

        for (int i = 0; i < empty.Count; i++)
        {
            if (i > 0)
            {
                message.Append("; ");
            }

            string folder = empty[i].Folder;
            message.Append('\'').Append(folder).Append("' (searched ");
            message.Append(searchedPaths.TryGetValue(folder, out string? path) ? path : "unknown path");
            message.Append(')');
        }

        message.Append(
            ". The content is missing from that location, or was dropped by the export filter " +
            "(see export_presets.cfg). Booting on empty registries would produce a world of air.");

        throw new ContentLoadException(message.ToString());
    }
}
