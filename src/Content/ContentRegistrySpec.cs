using System;

namespace Void;

/// <summary>
/// One row of the declaration that drives content loading (VOID-014): which
/// folder a registry comes from, whether the game can boot without it, and how
/// to count what was loaded.
///
/// <para>This exists so that "which registries must not be empty" is a
/// <b>declaration</b> rather than a list buried inside a validation routine.
/// Adding a registry means adding a row to
/// <see cref="ContentLoader.Registries"/> — which must happen anyway, since the
/// same table declares the load order — and the boot check picks it up with no
/// edit to the checking code.</para>
/// </summary>
/// <param name="Folder">
/// Directory name under <c>data/</c>, matched ordinally. Also the key used to
/// report which path was searched when the registry came up empty, so it must
/// be the exact string handed to the source factory.
/// </param>
/// <param name="Required">
/// False only for a registry the game can legitimately run with nothing in.
/// Default to true: an empty registry that nobody declared optional is far more
/// often a packaging or path mistake than an intent.
/// </param>
/// <param name="CountIn">
/// Reads this registry's entry count off a loaded <see cref="GameContent"/>.
/// A delegate rather than reflection so the table breaks at compile time if a
/// property is renamed or removed.
/// </param>
public sealed record ContentRegistrySpec(
    string Folder,
    bool Required,
    Func<GameContent, int> CountIn);
