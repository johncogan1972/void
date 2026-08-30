# World Data Model — Feature Spec

**Version:** 0.3
**Status:** Draft
**Companion to:** world-generation-spec.md (§5, §6, §10)

---

## 1. Overview

Defines the on-disk and in-memory data structures for tiles, chunks, world manifests, prefabs, and biomes. All world-generation and runtime chunk-streaming code reads and writes against these formats. Serialisation details (compression, encryption, obfuscation) are covered in the separate save-format spec.

## 2. Tile Format

Every tile in the world is one instance of this record.

**Fields (per tile):**

| Field         | Type      | Notes |
|---------------|-----------|-------|
| `block_id`    | uint16    | Foreground block. `0` = air. Registry lookup for behaviour, sprite, hardness, etc. |
| `wall_id`     | uint16    | Background wall. `0` = no wall. Required for NPC housing validity (GDD §5.5). |
| `liquid_type` | uint8     | `0` = none, `1` = water, `2` = lava, `3` = poison water, `4` = poison gas, `5` = liquid void. |
| `liquid_level`| uint8     | `0`–`255` fill amount for the liquid in this cell (for runtime flow — packed as 0-15 nibble is fine if space matters). |
| `flags`       | uint16    | Bit flags — see below. |
| `damage`      | uint8     | Current damage state for mining/destruction. `0` = pristine. |

**`flags` bit assignments (proposal — extend as needed):**

| Bit | Meaning |
|-----|---------|
| 0   | Placed by player (vs procedurally generated) |
| 1   | Reserved (used by generation for temp state) |
| 2   | Contains a wire (post-MVP tech) |
| 3   | Part of a prefab (do not overwrite in generation) |
| 4   | Structural — supports adjacent tiles (post-MVP structural integrity) |
| 5–15| Reserved for future use |

**Size per tile:** 9 bytes uncompressed. Fits into 8 bytes if `liquid_level` is packed into a nibble alongside `liquid_type`, or into 12 bytes rounded up for alignment. **Recommendation: pack to 8 bytes** — memory footprint matters at scale.

**Medium world raw tile data:**
- 11,520,000 tiles × 8 bytes = ~92 MB uncompressed.
- Expected zstd compression ratio: 4–8× depending on content homogeneity.
- Compressed disk footprint: ~12–24 MB for the tile arrays alone.

**Block ID `0` (air):** by convention, `block_id = 0` is empty space. Tiles with air still carry wall, liquid, and flags — a walled-in air tile is how you get an interior room.

## 3. Chunk Format

Chunks are the streaming and save unit. One chunk = **64 × 64 tiles** (4,096 tiles per chunk).

**Chunk file layout (on disk, before compression):**

```
[Chunk Header — 32 bytes]
  chunk_x            : int32   // world chunk coordinate
  chunk_y            : int32   // world chunk coordinate
  format_version     : uint16  // schema version
  flags              : uint16  // chunk-level flags
  biome_primary      : uint16  // dominant biome (registry ID)
  layer_primary      : uint8   // 0=outside, 1=underground, 2=deep, 3=void
  reserved           : bytes[15]

[Chunk Metadata — variable length]
  ore_density        : uint8[4]     // per-tier ore density hint
  structure_refs     : uint16[N]    // structure IDs whose bounds intersect this chunk
  walkable_ratio     : uint8        // 0–255, proportion of walkable space (spawn budgeting)
  special_flags      : uint32       // contains_boss_lair, contains_portal_candidate, contains_water_body, etc.

[Tile Data — 4096 × 8 bytes = 32 KB]
  tiles              : Tile[4096]   // row-major: index = y * 64 + x

[Entity Data — variable length]
  spawned_entities   : EntityRef[E] // persistent entities anchored to this chunk (chest inventories, NPCs at home, etc.)
```

**Chunk file naming:** `<chunk_x>_<chunk_y>.chunk` inside the world's chunk directory. Encoded as one file per chunk gives file-system-level LRU eviction "for free" — the OS handles hot/cold files well.

**Runtime flags (`flags` in chunk header):**

| Bit | Meaning |
|-----|---------|
| 0   | Modified since generation (dirty; must save on eviction) |
| 1   | Contains player-placed structures |
| 2   | Currently loaded (transient — set at runtime, not persisted) |
| 3–15| Reserved |

**Save on eviction:** only chunks with the `modified` flag set are re-serialised when evicted. Untouched chunks were saved once at gen time and are never rewritten.

## 4. World Manifest

One manifest file per world (home or portal), stored at the world's root save directory.

**Manifest fields:**

```yaml
# Serialised as plain JSON inside the save envelope, which supplies zstd,
# obfuscation and the integrity hash (§9.1, save-format-spec §4). Field order is
# pinned so two saves diff line-for-line, and nulls are written rather than
# skipped — an explicit null is a value, a missing field is a corrupt file.
world_id             : UUID          # unique per world instance
world_type           : string        # "home", "portal_scorched", "portal_sunken", etc.
seed                 : int64         # the world's generation seed
gen_version          : string        # code version at generation time (semver)
size_preset          : string        # "small", "medium", "large"
dimensions:
  width_tiles        : int32
  height_tiles       : int32
  chunks_x           : int32
  chunks_y           : int32
layer_boundaries:    # row indices; supports configurable proportions
  outside_end        : int32
  underground_end    : int32
  deep_end           : int32
  # void extends from deep_end to height_tiles

# Static generation output
player_spawn:
  x                  : int32
  y                  : int32
main_boss_lair:
  x                  : int32
  y                  : int32
  prefab_id          : string
portal_candidates:   # from Phase 4 of generation
  - id               : uint16
    x                : int32
    y                : int32
  - id               : uint16
    x                : int32
    y                : int32
  # ... N candidates

# Runtime state (updated during play)
main_boss_killed     : bool
side_anchors:
  - candidate_id     : uint16      # which candidate slot this anchor occupied
    activated        : bool        # gate/mini boss killed?
    picked_up_by     : PlayerID?   # null if still in world
    placed_at:                     # if placed by player
      world_id       : UUID        # the world it currently sits in (home world for standard use)
      x              : int32
      y              : int32
active_hearth:                     # party spawn point (GDD §4.7)
  world_id           : UUID
  x                  : int32
  y                  : int32

# Chunk index
chunk_index:
  - chunk_x          : int32
    chunk_y          : int32
    file             : string      # relative path to chunk file
    modified         : bool
```

The manifest is the source of truth for world-level state. Chunks hold tile-level state. Anything relevant to multiple chunks (bosses, quests, world events) lives in the manifest.

**Campaign manifest (across worlds):**

A parent-level document lists all worlds discovered in this campaign (the home world + every portal world the party has entered).

```yaml
campaign_id          : UUID
created_at           : timestamp
worlds:
  - world_id         : UUID
    world_type       : string
    manifest_path    : string
    discovered_at    : timestamp
```

The campaign manifest is what save/load starts from. It resolves world IDs to world manifests to chunks.

## 5. Prefab Schema

Prefabs are hand-authored structures dropped into the world at generation time. Authored in Tiled, exported to JSON, converted at build time to the runtime prefab format.

**Prefab file layout:**

```yaml
prefab_id            : string             # unique registry ID (e.g. "ruin_stone_small_01")
category             : string             # "dungeon", "ruin", "shrine", "boss_lair"
dimensions:
  width              : int32              # tile width
  height             : int32              # tile height

# Placement constraints
constraints:
  allowed_biomes     : string[]           # empty = any
  allowed_layers     : string[]           # e.g. ["underground", "deep"]
  requires_ground    : bool               # must sit on solid tiles below
  requires_cavern    : bool               # must be inside carved space
  min_spacing:                            # minimum tile distance from other prefabs
    same_category    : int32
    any_category     : int32
  clearance_above    : int32              # required empty space above (for large lairs)

# Tile data
tiles:                                    # row-major, y * width + x
  block_ids          : uint16[width * height]
  wall_ids           : uint16[width * height]

# Markers — special tiles the placement engine recognises
markers:
  - type             : string             # "boss_spawn", "chest", "entrance", "loot_container"
    x                : int32              # tile-local coord
    y                : int32              # tile-local coord
    metadata         : map                # marker-specific (chest tier, boss type, etc.)

# Variant weighting
weight               : float              # relative probability when generator picks from variants
```

**Marker types (initial set):**

| Marker           | Purpose |
|------------------|---------|
| `boss_spawn`     | Where a boss will be placed (main boss lair prefabs) |
| `entrance`       | Where the player enters (used for reachability check) |
| `chest`          | Loot chest placement — metadata carries loot table ID |
| `spawner`        | Enemy spawner point — metadata carries spawn config |
| `decoration`     | Random decoration placeholder — filled from a decoration set |

**Prefab registry:** all `prefab_id`s live in a central registry. Placement engine iterates the registry, filters by constraints, and picks weighted candidates. Adding a new prefab = drop a Tiled export in the folder, register it, ship.

## 6. Biome Schema

Biomes drive generation choices per world region. One biome definition per biome type.

```yaml
id                   : string             # e.g. "void:meadow" — the registry key (§7)
display_name         : string             # for UI / debug
layer_category       : string             # "surface", "underground", "deep", "void"

# Tile palette — string content ids, resolved through the block and wall
# registries at load. Not the raw uint16 that tile records store: a biome is
# authored content, and an author writing "void:grass" cannot silently name the
# wrong block the way a number can. The loader fails loudly on an id that does
# not resolve, which a number could never do.
palette:
  surface_block      : string             # top layer (grass, sand, snow)
  subsurface_block   : string             # layer just below surface
  base_block         : string             # bulk fill for the layer
  wall_default       : string
  wall_ambient       : string[]           # variations used stochastically

# Vegetation & decoration
vegetation:
  trees              : PrefabRef[]        # tree prefabs with weighted spawn probabilities
  plants             : PrefabRef[]
  decorations        : PrefabRef[]

# Ore biases — multipliers applied to base ore distribution. Keyed by ore
# content id. An ore not listed multiplies by 1.0: unbiased, never "absent".
# Iteration is ordinal-sorted by key, because this feeds generation and a
# hash-ordered map would make output depend on authoring order (§8).
ore_biases:
  void:copper        : float
  void:iron          : float
  # ... per ore type

# Enemy spawn pool
enemies:
  - enemy_id         : string
    weight           : float              # relative spawn weight in this biome
    time_of_day      : string             # "any", "day", "night"

# Underground variant reference (surface biomes only)
underground_variant  : string             # id of the matching underground biome, or null

# Ambient
ambient:
  music_theme        : string?            # post-MVP
  particle_effect    : string?            # post-MVP (motes, snow, dust)
  light_tint         : Color?             # subtle biome lighting shift

# Portal-world hazards (portal-world biomes only)
hazards:
  - type             : string             # "void_aura", "poison_gas", "lava", etc.
    intensity        : float
```

**Surface / underground pairing:** each surface biome names its `underground_variant`. The underground layer generator reads the surface biome column-by-column and places the matching underground biome directly below. Handles biome transitions cleanly (surface transitions from forest to desert → underground transitions in the same columns).

**Loading is a two-step, and the first step proves nothing.** A biome names blocks, walls and another biome, so a document that parses cleanly can still resolve to nothing. Biome definitions are therefore marked `ICrossRegistryValidated` and the generic `RegistryLoader` refuses them: they load only through `BiomeRegistryLoader`, which takes the registries they reference and fails loudly on an id that does not resolve, on an `underground_variant` naming a biome that does not exist, and on one whose target is not `layer_category: underground`. Prefab and enemy ids are the exception — those registries do not exist yet, so their check is deferred to `ValidateDeferredReferences` rather than skipped.

## 7. Data Registries

At runtime, all IDs above resolve through registries. One registry per data type:

- `BlockRegistry` — `block_id` → block definition (sprite, hardness, drop, physics)
- `WallRegistry` — `wall_id` → wall definition
- `BiomeRegistry` — `biome_id` → biome data
- `PrefabRegistry` — `prefab_id` → prefab data
- `EnemyRegistry`, `ItemRegistry`, `LootTableRegistry` — covered by their own specs

Registries are populated at startup from JSON data files. New content = new JSON entries. No code changes required for new blocks, biomes, or prefabs.

## 8. Determinism Requirements Recap

All world-gen code that produces the structures above must respect the determinism rules from GDD §3.2 and world-generation-spec §14:

- IDs are stable across versions (removing an ID requires migration, not silent reuse).
- Registry iteration order in generation-affecting code is explicitly sorted, never dependent on load order.
- Random selection from a registry always uses the seeded generation RNG.
- Prefab variant selection is deterministic given world seed + placement location.

## 9. Resolved Decisions

The four questions this section originally held are settled. They are kept here
with their reasoning rather than deleted, because the reasoning is the part that
is expensive to reconstruct. One new question is open at the end.

### 9.1 Manifest serialisation format — JSON inside the save envelope

**Decision:** manifests are plain JSON payloads written inside the standard save
envelope. Compression is the envelope's job, not the schema's.

The original question was JSON (debuggable, larger) versus binary (compact,
opaque), with a recommendation of "JSON with zstd compression at rest". That is
precisely what the save envelope already does: it takes an opaque payload and
applies zstd, XOR obfuscation and a SHA-256 integrity hash (save-format-spec
§4, §7, §8). `SaveFileKind` already enumerates `CampaignManifest` and
`WorldManifest` as payload kinds.

So the question resolves to "both", with no new machinery and no second format
to maintain. Manifests stay debuggable — dump the decoded payload and read it —
while paying binary's size cost at rest.

**Implemented by:** VOID-007. **Consumed by:** VOID-021.

### 9.2 Format version migration — version tags now, migrator later

**Decision:** every file carries a schema version from day one. The migrator
library is deliberately deferred until there is a shipped version to migrate
from.

The original recommendation was "a version tag on every file, with a migrator
library". The first half is already true: `SaveEnvelope` carries both
`FormatVersion` (the payload's schema version) and `EnvelopeVersion` (the
container's), on every save file the game writes. The chunk header carries its
own `format_version` as well (§3).

The second half is deferred on purpose. A migrator written before its first real
migration encodes a guess about what will change, and that guess is usually
wrong; worse, it creates a code path that nothing exercises. What the current
phase must guarantee instead is that the versions written are correct and
non-zero, and that a version this build does not recognise fails loudly rather
than being parsed optimistically. Given that, the first migration can be written
when the first breaking change is actually known.

**Implemented by:** VOID-007. **Enforced by:** VOID-020, VOID-021.

### 9.3 Chunk file granularity — one file per chunk

**Decision:** one file per chunk, named `<chunk_x>_<chunk_y>.chunk`, as
described in §3. The single append-only container per world is rejected.

The container's only advantage is file count, and file count is not a problem at
this scale: a Medium world is roughly 2,900 chunks, orders of magnitude inside
any filesystem's practical limits. Against that, one-file-per-chunk gets
filesystem-level LRU caching for free, lets a single corrupted chunk be
quarantined without touching the rest of the world, and makes partial writes
survivable through the existing atomic write helper. A container would have to
reimplement all three.

Revisit only if file count becomes a measured problem — for instance if Large
worlds land far above the current estimate, or a target platform turns out to
have a hostile filesystem.

### 9.4 In-memory tile representation — packed, not struct-of-arrays

**Decision:** tiles stay a packed array as specified in §2. Struct-of-arrays is
not adopted.

The original text already called this a micro-optimisation to revisit under
profiling, and that judgement stands. Struct-of-arrays would help a workload
that sweeps one field across many tiles while ignoring the rest; most real
access — generation writing a tile, streaming serialising a chunk, rendering
reading block and wall together — touches several fields of the same tile at
once, which is the case the packed layout already suits.

Revisit when chunk iteration actually appears in a profile, not before. This is
an in-memory representation only: it can change without a save migration, which
is exactly why it does not need deciding early.

### 9.5 Open — liquid field packing costs fill resolution

**Status: open.** This is the live question for Phase 1.

§2's field table sums to **9 bytes**, not the 8 the same section recommends:
`uint16 + uint16 + uint8 + uint8 + uint16 + uint8`. §2 closes the gap by packing
`liquid_type` and `liquid_level` into a single byte as two nibbles — which drops
liquid fill resolution from `0`–`255` to `0`–`15`.

That is a real trade, not a free win, and unlike §9.4 it **is** baked into the
on-disk format: changing it later is a save migration.

- **Nibble-pack to 8 bytes.** 16 fill levels. Liquid reads as a visual gradient
  rather than a measurement, and comparable games ship with similar granularity
  without it being visible.
- **Accept 9 bytes.** Full `0`–`255` fill, roughly 46 MB more resident on a
  Medium world, and alignment padding to 10 or 12 bytes wastes most of what the
  extra byte bought.

Nibble-packing is recommended, on the grounds that the memory is better spent on
something the player can perceive. **Not yet confirmed.**

**Tracked on:** VOID-019.
