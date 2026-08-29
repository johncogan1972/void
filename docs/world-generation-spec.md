# World Generation — Feature Spec

**Version:** 0.4
**Status:** Draft
**Companion to:** GDD §3, §4
**Sub-specs:** `world-data-model-spec.md`, `cave-generation-spec.md`

---

## 1. Overview

Feature spec for the world generation system. Provides implementation-level detail sufficient to break into GitHub issues. Complements the GDD; wherever this doc and the GDD disagree, this doc wins for world-gen specifics.

## 2. Requirements Recap

Pulled from the GDD:

- **Procedural**, seeded generation
- **Deterministic** per (seed, version) — hard constraint (GDD §3.2)
- **Chunked** storage and runtime loading
- **Pre-generated at world creation** — the entire world is built and saved to disk on world creation, not streamed lazily as the player explores
- **Configurable** world size and per-layer depth
- **Deep** generation (biome-aware terrain, structured caves, prefab structures, ore gradients)
- **Hybrid cave generation** — Perlin worms + cellular automata
- **4-layer vertical structure** — Outside / Underground / Deep / Void
- Runs identically across platforms (Windows / Linux) — no non-portable numeric behaviour

## 3. World Size Presets

Reference: Terraria's presets sit at roughly 4200×1200 (Small), 6400×1800 (Medium), 8400×2400 (Large).

| Preset | Width (tiles) | Height (tiles) | Total tiles | MVP? |
|--------|---------------|----------------|-------------|------|
| Small  | 4200          | 1200           | 5,040,000   | No   |
| Medium | 6400          | 1800           | 11,520,000  | **Yes (default)** |
| Large  | 8400          | 2400           | 20,160,000  | No   |

**MVP scope:** implement Medium only. Small and Large are post-MVP but the pipeline must be size-agnostic so adding them is data-only.

## 4. Vertical Layer Model

Default proportions (Terraria-like, all configurable per world type):

| Layer       | Default % | Medium world rows |
|-------------|-----------|-------------------|
| Outside     | 30%       | 0–540             |
| Underground | 25%       | 540–990           |
| Deep        | 30%       | 990–1530          |
| Void        | 15%       | 1530–1800         |

**Configurability:** each layer's proportion is a per-world-type config value in JSON. Home world and each portal world type can override defaults.

### 4.1 Outside (30%)
- Sky column + surface strip. Surface elevation varies within the band via heightmap (§6, Phase 1).
- Home biomes distribute across the surface — forest, desert, frozen north, etc.
- **Day/night cycle** is a runtime system that governs lighting here, with dawn and dusk transitions. **In MVP.**
- **Sky content (MVP):** decorative scudding clouds only — atmospheric, no gameplay content.
- **Sky content (post-MVP):** floating islands, sky biomes, weather effects.

### 4.2 Underground (25%)
- Feels like a continuation of the surface downward.
- **Biomes match the surface biome directly above** — surface desert produces underground desert-affinity space (sandy tiles, cactus roots), surface frozen north produces icy underground, and so on.
- Low-tier ores (copper, iron, coal).
- Moderate cave density; small-to-medium caverns.

### 4.3 Deep (30%)
- **Standalone biomes** — no longer tied to the surface. Deep is a "second world" thematically.
- Mid-tier ores.
- Dense cave and cavern layer.
- **Hazards:**
  - **Lava pools** — high damage, environmental.
  - **Poison water pockets** — pockets of tainted water in caverns; contact damage over time.
  - **Poison gas pockets** — ambient gas fills some sealed chambers; damage on exposure.
- Ambient darkness — torches required for reasonable visibility.

### 4.4 Void (15%)
The endgame layer. Distinct at a glance.

- **Standalone biomes and tile palette** — visually differentiated from every other layer, unmistakable.
- Highest-tier ores (magic crystals, void metals).
- **Hazards & identity:**
  - **Void damage aura** in some regions — passive tile-source damage, mitigable only by high-tier gear.
  - **Liquid void rivers and pools** — a distinct fluid that deals *more damage than lava* on contact.
  - **Reduced natural light** — torches burn dimmer here; players need higher-tier light sources for comfortable navigation.
  - **Larger caverns** — cathedral-scale open spaces separated by thick dead rock.
  - **Void-exclusive monsters** — dangerous, appear only in this layer, deal void damage that bypasses shields (§7.4 of GDD).

**"Outside" definition confirmed:** the outside layer includes the sky column above the surface. Meaningful sky content is post-MVP but the layer is structured to accommodate it — sky is not treated as decoration only.

## 5. Chunk & Storage Model

- **Chunk size:** 64 × 64 tiles (recommended). Balances streaming granularity against per-chunk overhead. Larger (128×128) reduces chunk count 4× but each is heavier to serialise; smaller adds streaming overhead.
- **Chunks per Medium world:** 6400/64 × 1800/64 = 100 × 28.125 → 100 × 29 chunks with edge padding = 2,900 chunks.
- **Per-tile data:** block ID, wall ID, metadata (liquid state, wire, custom flags). Data model to be finalised in a separate tile-format spec.
- **Serialisation:** each chunk is compressed (zstd) and stored as a blob on disk, indexed by chunk coordinates.
- **World manifest:** a top-level file per world listing the chunk index, world seed, size preset, layer proportions, generation version, and other metadata.
- **Pre-generation:** all chunks are generated and saved at world creation time. The player waits on a progress bar. Estimated time budget: <60 seconds for Medium on target hardware (TBD).
- **Runtime:** chunks are loaded on demand around active players and cached. LRU eviction when the cache exceeds a size cap.

## 6. Generation Pipeline

Generation runs as five sequential phases. Each phase is deterministic, seeded from a derived RNG stream (world seed + phase identifier).

### Phase 1 — Structural

1. **Seed initialisation.** World seed drives a master RNG. Sub-streams derived per phase and per sub-system via a well-defined key (e.g. `hash(seed, "phase1.heightmap")`).
2. **Heightmap.** 1D noise across world width defines base surface elevation. Multiple octaves for terrain variety. Output: array of surface Y-values, one per column.
3. **Layer boundaries.** Compute row ranges for each layer from the world size + configured proportions. Written into world metadata.
4. **Biome map.** 2D noise + rule-based classification across the surface strip. Each surface column is assigned a primary biome. Biome transitions blend over a small horizontal band.

### Phase 2 — Terrain shaping

5. **Macro features.** Overlay mountain ranges, valleys, plateaus, oceans onto the heightmap using low-frequency noise.
6. **Cave carving.** Hybrid system (see §7).
7. **Water simulation.** Place lakes in heightmap depressions; rivers flow from high to low along gradient; underground reservoirs placed in eligible caverns. Water is placed at gen time and settled to a stable state.

### Phase 3 — Composition

8. **Ore & material distribution.** Depth-tiered (see §9).
9. **Vegetation.** Biome-appropriate trees, plants, mushrooms. Density and spread per biome.
10. **Structure placement.** Hand-authored prefabs stitched at valid locations. Dungeons, ruins, shrines. Placement respects prefab constraints (biome, layer, elevation).

### Phase 4 — Reservations & metadata

11. **Player spawn point.** Selected on the surface in a safe location — flat ground, no immediate hazards, in the starter biome.
12. **Main boss lair.** A reserved zone protected from other generation. Prefab-based. Location: TBD per world design.
13. **Portal spawn candidate slots.** Pre-computed list of valid tile positions where side anchors may spawn post-main-boss. Constrained to underground / deep / void layers (per GDD §4). Additional constraints: in accessible cave space, minimum distance from main boss lair and from each other.
14. **Region annotations per chunk.** Each chunk's metadata is populated: primary biome, primary layer, ore density estimate, structure count, special flags (contains boss lair, contains portal candidate, etc.). Used at runtime by other systems.

### Phase 5 — Validation & polish

15. **Reachability check.** Verify critical areas (spawn, main boss lair, all portal candidate zones) are reachable via connected traversable space. On failure: log, potentially regenerate a section.
16. **Post-processing.** Fix impossible tile configurations (floating single tiles, unsupported blocks). Smooth harsh biome transitions. Apply tile decorators (cracks, moss).

## 7. Cave Generation (Hybrid)

Two subsystems layered together.

**Perlin worms.** Simulated "worms" walk through the world guided by 3D noise gradients, carving tunnel networks. Configurable per layer: worm count, worm length, tunnel radius, branching probability. Produces the game's navigable tunnel system.

**Cellular automata caverns.** Seeded from noise, iterated for N generations with standard birth/survival rules, producing organic blob-shaped chambers. Placed at:
- Worm tunnel intersections (natural cavern-at-junction feel)
- Random valid locations per layer
- Reserved sites for structures / portal candidates

**Layer-specific tuning:**

| Layer       | Worm density | Cavern density | Style |
|-------------|--------------|----------------|-------|
| Outside     | Very low     | None           | Only occasional surface-opening tunnel mouths |
| Underground | Moderate     | Small, frequent | Familiar mining feel |
| Deep        | High         | Medium, dense  | Dangerous exploration terrain |
| Void        | Low          | Large, sparse  | Cathedral chambers separated by dead rock |

## 8. Biomes

MVP home world biome set (per GDD §3.4):

- **Forest / grassland** (starter)
- **Desert**
- **Frozen north**
- **Underground caverns** (auto-assigned to the underground layer)
- **Mild corrupted / arcane** (optional surface strip; stronger versions live in portal worlds)

Each biome definition (JSON) specifies:
- Surface tile palette (grass / sand / snow / etc.)
- Wall tile palette
- Vegetation set (trees, plants, decorative)
- Enemy spawn pool
- Ambient effects (post-MVP)
- Biome-specific ore or material biases (post-MVP)

Portal-world biomes are defined per portal-world type in separate data files, following the same schema.

**Underground biome behaviour (confirmed):**
- The **underground layer** biome matches the surface biome directly above it. Each surface biome definition includes an "underground variant" — same theme, adapted for below-ground (e.g. surface desert → underground desert with sandstone walls, cactus-root ceilings).
- The **deep** and **void** layers use their own standalone biomes, not tied to the surface.

## 9. Ore & Material Distribution

Depth-tiered per GDD requirement. All values data-driven.

| Layer       | Tier    | Example ores (illustrative)              |
|-------------|---------|------------------------------------------|
| Outside     | 0       | Stone, occasional exposed copper         |
| Underground | 1       | Copper, iron, coal                       |
| Deep        | 2       | Steel-grade ores, silver, gold analogues |
| Void        | 3       | Magic crystals, void-metals, endgame     |

**Placement algorithm:** vein placement per layer, seeded by derived RNG. Each vein is a random-walk from a seed point, with configurable length, thickness, and branching. Vein counts and clustering vary by layer and biome.

Biome influence: certain surface biomes may bias the underground below them (desert → sandstone/copper concentration, frozen north → cold-affinity ores, etc.). Details post-MVP.

## 10. Structure Placement

Structures = hand-authored prefabs stored as external files (see prefab format below), placed at generation time.

**Prefab authoring format: Tiled (.tmx).** Structures are authored in the free Tiled tile-map editor and exported to JSON. This lets structure variants be added without programmer intervention: paint the layout, export, drop into the `prefabs/` folder, and the generator picks them up. A build-time conversion step normalises Tiled's JSON to the runtime prefab format.

**Categories:**
- **Dungeons** (large, underground or deep) — one per world, contain a boss lair prefab.
- **Ruins** (medium, outside or underground) — multiple per world.
- **Shrines** (small, various) — sprinkled throughout.

**Main boss lair:**
- **Biome-adaptive prefab variants** — 2–3 variants per world type, generator picks one matching the biome it lands in.
- **Location is procedurally chosen and not marked** — the player must explore to find it. No compass, no map arrow. Cavern hints, structural clues, and enemy density gradients help guide players nearby without giving it away.

**Placement rules per prefab:**
- Allowed biomes
- Allowed layers
- Minimum spacing from other structures of same category
- Anchor / stitching requirements (must sit on ground, must be inside cavern, etc.)

Multiple prefab variants per structure type for run-to-run variety.

## 11. Water & Liquid Simulation

Static liquid placement at generation time. Runtime flow (Terraria-style cellular water) is a separate system.

**Regular water:**
- **Lakes** — placed in heightmap depressions on the outside layer.
- **Rivers** — flow from high elevation to low, following heightmap gradient. Configurable width and flow length. **L-system-based path generation** (simplified) for MVP; upgrade to gradient descent if needed later.
- **Underground reservoirs** — water pockets in some caverns (moderate density in underground, rare in deep).

**Deep-layer hazards (liquids and gas):**
- **Lava pools** — placed in the deep layer only.
- **Poison water pockets** — tainted water pools in some deep caverns.
- **Poison gas pockets** — ambient gas fills some sealed chambers.

**Void-layer liquid:**
- **Liquid void** — rivers and pools of void substance. Deals more damage than lava on contact. Placed sparingly in the void layer; provides visual and mechanical identity.

Runtime liquid flow (Terraria's cellular water) is a separate system — see runtime-water-spec (not yet written).

## 12. Portal Spawn Candidate Placement

Per GDD §4.2, when the main world boss falls, two side anchors spawn at random locations. This spec covers where those locations *can* be.

**Candidate generation (at world gen time):**
- Iterate accessible cave/cavern space in underground / deep / void layers
- For each candidate position, evaluate constraints:
  - Not within N tiles of the main boss lair
  - Not within M tiles of another candidate
  - In a cavern of minimum size (must fit the anchor placeable + player approach)
- Store the resulting candidate list in world metadata
- Runtime picks 2 candidates from the list at random when the main boss dies (using a runtime RNG seeded from world seed + kill event, keeping determinism if desired)

## 13. Chunk Metadata Model

Each chunk carries lightweight metadata used by runtime systems:

- Primary biome
- Primary layer
- Ore density estimate (rolling number per ore tier)
- Structure count and types within chunk
- Special flags: `contains_boss_lair`, `contains_portal_candidate`, `contains_water_body`, `contains_ambient_hazard`
- Approximate walkable-space ratio (for enemy spawn budgeting)

Written during Phase 4 of generation. Read at runtime by enemy spawner, ambient effects, minimap systems.

## 14. Determinism Rules

Recap of GDD §3.2 — enforced across all generation code:

- All randomness derives from the world seed via derived sub-streams
- Use **xoshiro256++** (or equivalent portable RNG) — never `System.Random`
- Iteration order is deterministic (ordered collections or explicit sort)
- Fixed-point / integer math where FP would drift
- Parallel generation must produce identical output regardless of thread scheduling
- CI check: regenerate reference seed and hash the resulting world

## 15. Resolved Design Decisions

All original open questions locked in v0.2:

1. **Chunk size:** 64 × 64 tiles.
2. **Prefab authoring format:** Tiled (.tmx export → JSON).
3. **Main boss lair:** biome-adaptive prefab variants. Location procedurally chosen and not marked — player must explore to find it.
4. **Weather system:** post-MVP. **Day/night cycle with dawn/dusk transitions is in MVP.**
5. **Sky content:** MVP = decorative scudding clouds only. Post-MVP = floating islands, sky biomes, weather.
6. **Underground biomes:** underground layer matches surface biome directly above; deep and void use standalone biomes.
7. **River generation:** L-system-based simplified paths for MVP.
8. **Ore vein algorithm:** random-walk placement per vein.
9. **Deep-layer hazards:** lava pools, poison water pockets, poison gas pockets.
10. **Void-layer identity:** distinct tile palette, void damage aura, liquid void rivers/pools (more damaging than lava), reduced natural light, larger caverns, void-exclusive monsters.
11. **Pre-generation time budget:** target <60s for Medium on target hardware. Feasible with C# / GDExtension + parallelised phases.
12. **Chunk load cache:** 9×9 chunk window around each player, up to 200 chunks cached with LRU eviction. All values tuneable via config.

**Follow-up questions resolved in v0.3:**

- **Void-exclusive monster roster** — deferred to a future enemy design doc. No block on world gen.
- **L-system parameters for rivers** — data-driven, tuned during implementation. Live in JSON world-gen config.
- **Ore vein parameters (step count, thickness, branching)** — data-driven per ore in the ore registry. Live in JSON.
- **Boss lair discovery hints** — resolved: environmental cues rather than UI hints. Enemy density increases as the player nears the lair; unique "lair guardian" enemies appear more frequently; ambient audio shifts (post-MVP); structural remnants (broken pillars, ruined statues, cracked walls) form a subtle trail. No compass, no map arrow, no NPC dialogue giving the location. The Guide may offer a vague hint after significant time without progress ("something ancient rests below…") but never a direction.

## 16. Companion Sub-Specs

The following documents provide implementation-level detail for the areas most likely to benefit from focused specs. Together with this document they constitute the world-gen documentation set:

- **`world-data-model-spec.md`** — Tile, chunk, world manifest, prefab schema, biome schema, and campaign manifest formats. Covers the "shape of the data" everything else reads and writes.
- **`cave-generation-spec.md`** — Detailed algorithm for the hybrid Perlin worms + cellular automata cave carving system. Covers Phase 2's most complex step.

Other epics from §17 do not currently need sub-specs; they are straightforward enough to convert directly into GitHub issues.

## 17. Ticket-Ready Epics

Breaking this spec into workable chunks for GitHub. Each is an epic that will spawn multiple issues.

- **W1 — World data model.** Tile format, chunk format, world manifest, prefab schema, biome schema. Spec: `world-data-model-spec.md`.
- **W2 — Deterministic RNG infrastructure.** xoshiro256++, sub-stream derivation, CI reference-seed test.
- **W3 — Phase 1 pipeline: structural.** Heightmap, layer boundaries, biome map.
- **W4 — Phase 2a: terrain shaping.** Macro features, heightmap application.
- **W5 — Phase 2b: hybrid cave carving.** Perlin worms + cellular automata. Spec: `cave-generation-spec.md`.
- **W6 — Phase 2c: water and liquid placement.** Lakes, rivers (L-system), reservoirs, deep hazards (lava, poison water, poison gas), void liquid.
- **W7 — Phase 3a: ore distribution.** Depth-tiered random-walk vein placement.
- **W8 — Phase 3b: vegetation.** Biome-driven placement.
- **W9 — Phase 3c: structure placement.** Tiled prefab conversion, placement engine, initial prefab set including biome-adaptive main boss lairs.
- **W10 — Phase 4: reservations & metadata.** Spawn point, boss lair, portal candidates, chunk annotations.
- **W11 — Phase 5: validation & polish.** Reachability check, corrective worms, tile fixups.
- **W12 — Pre-generation UX.** Progress bar, cancellation, resume-on-crash considerations.
- **W13 — Chunk streaming runtime.** Load-around-player (9×9 window), LRU eviction to 200-chunk cap, save-modified-chunks on unload.

**Post-MVP additions:**

- **W14 — Surface feature pass (post-MVP).** A dedicated Phase 2 sub-step that places overhang formations, cliff undercuts, plateau features, and other dramatic surface silhouettes. For MVP, overhangs emerge naturally where cave carving intersects the surface layer — this is functional but produces less dramatic vertical variety than a purpose-built pass would. Add when playtesting shows the outside layer feeling too flat-topped.

Rough sequencing: W1 → W2 → W3 → W4/W5/W6 (parallel) → W7/W8/W9 (parallel) → W10 → W11 → W12/W13 (parallel).

---

*Living document. Every open question above becomes a follow-up decision as design deepens.*
