# data/

Runtime, data-driven content: JSON only, no code.

Loaded at boot by the `ContentBoot` autoload, which calls
`ContentLoader.LoadAll` (`src/Content/`) with a `GodotContentSource` over
`res://data/<folder>`. Tests call the same `LoadAll` with a
`DirectoryContentSource` over this tree, so there is exactly one load path.
Adding an entry is a JSON change only — no recompile.

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

Cross-registry entries (`biomes/`, `loot_tables/`, `enemies/`, `prefabs/`):

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
- A prefab's `block_ids` / `wall_ids` are raw numbers, so it loads through
  `PrefabRegistryLoader.Load` (blocks and walls first). A tile array whose
  length is not `width * height`, a marker outside the footprint, and a numeric
  id that does not resolve are all fatal — each one stamps a scrambled or
  hollow structure into the world with no error anywhere downstream.
- Load order is therefore: blocks + walls → items → loot tables → enemies →
  biomes → prefabs. It is declared once, as `ContentLoader.LoadOrder`, and the
  loader body follows it step for step. It is a fixed sequence, never a retry
  over unresolved references — a retry turns a content bug into a silent
  partial load.
- A biome's vegetation prefab ids and spawn-pool `enemy_id`s close the one cycle
  in that order (biomes load before prefabs), so they are checked last, by
  `BiomeRegistryLoader.ValidateDeferredReferences`, which `ContentLoader.LoadAll`
  calls once everything is loaded. A dangling ref there is fatal like any other.
- Anything fatal aborts boot: `ContentBoot` pushes the error and quits rather
  than running on empty registries.

`items/` carries base fields only (`id`, `display_name`, `sprite`,
`max_stack`); stats, rarity and equip slots land in Phase 5. `enemies/` likewise
carries no health, damage or AI — that is Phase 9. Fields are added when their
meaning is settled, never guessed ahead of time.

Current registries: `blocks/`, `walls/` (VOID-018), `biomes/` (VOID-022),
`items/`, `enemies/`, `loot_tables/` (VOID-023), `prefabs/` (VOID-024), all seven
wired into boot by VOID-025. The VOID-006 `example/` folder is gone — the real
registries prove the loader now.

`void:meadow`'s vegetation lists are empty on purpose: they named oak, wildflower
and tall-grass prefabs that do not exist, which boot now rejects. VOID-026 (the
Tiled prefab converter) restores them with the real prefabs; the removed entries
are recorded in a comment in `biomes/biomes.json`.
