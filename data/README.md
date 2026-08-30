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

Cross-registry entries (`biomes/`, `loot_tables/`, `enemies/`):

- A biome names blocks, walls and another biome, so parsing its JSON proves
  nothing about whether those ids resolve. Its definition is marked
  `ICrossRegistryValidated`, and `RegistryLoader` **refuses** such a type: load
  it through `BiomeRegistryLoader.Load`, which takes the registries it points at
  and fails loudly on an id that does not resolve.
- A loot table names items, so it loads through `LootTableRegistryLoader.Load`
  (items first). An `item_id` that does not resolve is fatal — a table that
  grants nothing is invisible in play.
- An enemy names one loot table, so it loads through
  `EnemyRegistryLoader.Load` (loot tables first). A dangling `loot_table_id` is
  fatal; `null` is legal and means "drops nothing".
- Load order is therefore: blocks + walls → items → loot tables → enemies →
  biomes.
- Prefab and enemy ids in a biome still dangle **at load**.
  `BiomeRegistryLoader.ValidateDeferredReferences` is written and fatal on a
  dangling ref, but nothing calls it yet — prefabs land in VOID-024, and the
  boot sequence that would pass both registries to it is VOID-025. Until then
  the only thing holding those refs honest is a test asserting that every
  `enemy_id` in `biomes/` exists in `enemies/`.

`items/` carries base fields only (`id`, `display_name`, `sprite`,
`max_stack`); stats, rarity and equip slots land in Phase 5. `enemies/` likewise
carries no health, damage or AI — that is Phase 9. Fields are added when their
meaning is settled, never guessed ahead of time.

Current registries: `blocks/`, `walls/` (VOID-018), `biomes/` (VOID-022),
`items/`, `enemies/`, `loot_tables/` (VOID-023). The VOID-006 `example/` folder
is gone — the real registries prove the loader now.
