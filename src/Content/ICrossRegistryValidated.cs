namespace Void;

/// <summary>
/// Marks a content definition that is not fully checkable on its own, because
/// some of its fields name entries in <i>other</i> registries (VOID-022).
///
/// <see cref="BiomeDefinition"/> is the first: its palette names blocks and
/// walls, and its <c>underground_variant</c> names another biome. Parsing the
/// document proves the JSON is well formed and says nothing about whether any
/// of those ids resolve.
///
/// <b>What this interface does:</b> it makes the generic
/// <see cref="RegistryLoader.Load{T}(IContentSource)"/> refuse the type
/// outright. That path returns a registry the moment the documents parse, so a
/// biome registry built through it would look loaded and complete while holding
/// palette ids that point at nothing — the failure would surface much later, as
/// generation producing a world of air. A dedicated loader that takes the other
/// registries as arguments is the only way in; see
/// <see cref="BiomeRegistryLoader"/>.
///
/// The interface carries no members. It exists so the rule is enforced by the
/// loader at runtime and named in the type system, rather than living only in a
/// comment that the next caller does not read.
/// </summary>
public interface ICrossRegistryValidated : IContentDefinition
{
}
