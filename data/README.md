# data/

Runtime, data-driven content: JSON only, no code.

Loaded at boot by `RegistryLoader` (`src/Content/`) through
`GodotContentSource` over `res://data/...`. Adding an entry is a JSON change
only — no recompile.

Rules:

- Every entry needs a unique, non-empty `id` (convention: `void:snake_case`).
- Duplicate ids are a fatal load error naming both files. Ids are not
  namespaced by folder.
- JSON keys are `snake_case` and map to PascalCase C# properties.
- A file may hold one object or an array of objects.
- Registry iteration is sorted by `id` (ordinal), never by file order.

`example/` is a worked example proving the loader; delete it once real
registries land in Phase 1.
