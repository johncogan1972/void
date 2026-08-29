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

Numeric ids (`blocks/`, `walls/`):

- Tile records store `block_id` / `wall_id` as raw `uint16`, so those entries
  declare their number **explicitly in JSON**. It is never derived from load
  order, file name, or array position.
- A numeric id is **stable forever**. Changing one reinterprets every saved
  world. Removing an entry retires its number; never reuse it.
- A duplicate numeric id is a fatal load error naming the number and both files,
  exactly like a duplicate `id`.
- `block_id = 0` (air) and `wall_id = 0` (no wall) are real registry entries,
  not absences.

Current registries: `blocks/`, `walls/` (VOID-018). The VOID-006 `example/`
folder is gone — the real registries prove the loader now.
