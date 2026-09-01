# World Generation — Feature Spec

**Version:** 0.8
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

**Step numbers are identities, not execution order.** They are referenced from code comments and shipped tickets, so they are never renumbered — the same rule that made the materialisation step W3b rather than a renumbering of W4-W14. Phase 1 executes in the order **1, 3, 4, 2**, and the list below is written in that order.

1. **Seed initialisation.** World seed drives a master RNG. Sub-streams derived per phase and per sub-system via a well-defined key (e.g. `hash(seed, "phase1.heightmap")`).
3. **Layer boundaries.** Compute row ranges for each layer from the world size + configured proportions. Written into world metadata.
4. **Biome map.** 2D noise + rule-based classification across the surface strip. Each surface column is assigned a primary biome. Biome transitions blend over a small horizontal band.

    The blend is **per tile, not per column** (VOID-060). Every column still belongs to exactly one biome, so `min_run_columns` is untouched and the two features cannot fight — the interleave lives below the resolution that rule operates at. Near a boundary a column also records the biome on the other side, where it sits in the band as a signed fraction, and which boundary it belongs to; a seeded field then displaces the boundary from row to row, and each tile takes whichever biome its side of that displaced edge belongs to.

    Two rejected alternatives, recorded because both look right on paper. Hashing each tile independently is white noise: the band comes out as salt-and-pepper speckle and reads as a rendering fault. Thresholding a coherent field against a per-column probability clumps correctly but collapses the band, because fBm concentrates near its midpoint and the low probabilities near a band's edge are almost never met. Displacing the boundary gives every row one clean edge in a different place, so the two biomes interlock with no islands and no speckle.

    **Each boundary draws its own width** from `transition.min_columns` / `max_columns`; a single width would make every border in a world look like the same border. A band is clamped to half the run on either side of it, so two nearby boundaries can never overlap — the configured maximum is an upper bound on intent, not a promise about any one border.

    Distinct from `blend_columns`, which offsets the sample position and therefore *moves* a boundary without softening it: that field is far lower frequency than a boundary is wide, so across a seam it is effectively constant.

    Runs **before** the heightmap (VOID-061). It reads the climate fields and never the surface, so nothing about it depended on the heightmap existing; the previous ordering was convention rather than a dependency.
2. **Heightmap.** 1D noise across world width defines base surface elevation. Multiple octaves for terrain variety. Output: array of surface Y-values, one per column.

    Runs **last in this phase**, because surface roughness is per biome and this step therefore has to know which biome owns a column.

    The surface is **two fields, not one**: a low-frequency base shape mapped onto the surface band, plus a high-frequency `detail` displacement measured in rows added on top, then the slope limiter. They are separate because fBm normalises to a fixed total amplitude — roughening the base octave stack pays for texture by taking amplitude away from the hills. Measured on the shipped config, raising persistence to 0.70 cut the world's elevation range from 83 rows to 71 while roughening it; the same roughness added as a detail term left the range at 87.

    Without the detail term the surface changes by at most one row per column and is flat in ~81% of them, so quantising a gentle ramp to whole rows lands the steps at even intervals and the ground reads as a **staircase** (VOID-061, found in the VOID-057 viewer). The detail term moves where each step falls, which is what breaks the regularity — it is not there to make terrain steeper on average.

    Roughness is authored per biome as `surface_detail` on the biome, falling back to the world type's `heightmap.detail`. Each biome samples a **decorrelated field**, derived per biome id, so one biome's roughness is not another's at a different amplitude.

    Across a transition band the two biomes' roughness is **crossfaded, not dithered** (VOID-060). The surface is a single row per column, so choosing one biome's displacement or the other's at random would read as noise where a gradient is wanted — and would put a one-row cliff wherever the choice flipped.

    `max_column_delta` remains a hard cap enforced by a left-to-right limiter, and is a **safety net rather than a shaping tool**: on shipped values it alters 0 of 4,199 columns. It exists so that no octave stack a data file can express — including one authored later — can produce a single-column cliff.

### Phase 2 — Terrain shaping

5. **Terrain materialisation.** Fill chunks with tiles from the heightmap and the biome map (VOID-056). Per column, using the biome's palette: air above the surface Y, `surface_block` at it, `subsurface_block` for the biome's `subsurface_depth` rows beneath, `base_block` below that to the bottom of the world. Walls are `wall_default` at and below the surface, none in open sky. Rows at or below the Outside boundary take the column's `underground_variant` palette, per the pairing rule in §8. Output: `Chunk` values.

    This step is **pull-based**: it is a pure function of the chunk coordinate, so chunks are materialised on demand rather than all at once — a Medium world is 2,900 chunks and about 92 MB of tiles. It draws no randomness at all, deriving nothing from the seed beyond what the heightmap and biome map already fixed.

    Everything after it carves, floods or scatters *into* these tiles, which is why it sits at the head of Phase 2 rather than at the end of Phase 1: Phase 1 is structural and produces arrays, and macro features (step 6) still operate on the heightmap rather than on tiles.
6. **Macro features.** Overlay mountain ranges, valleys, plateaus, oceans onto the heightmap using low-frequency noise.
7. **Cave carving.** Hybrid system (see §7).
8. **Water simulation.** Place lakes in heightmap depressions; rivers flow from high to low along gradient; underground reservoirs placed in eligible caverns. Water is placed at gen time and settled to a stable state.

### Phase 3 — Composition

9. **Ore & material distribution.** Depth-tiered (see §9).
10. **Vegetation.** Biome-appropriate trees, plants, mushrooms. Density and spread per biome.
11. **Structure placement.** Hand-authored prefabs stitched at valid locations. Dungeons, ruins, shrines. Placement respects prefab constraints (biome, layer, elevation).

### Phase 4 — Reservations & metadata

12. **Player spawn point.** Selected on the surface in a safe location — flat ground, no immediate hazards, in the starter biome.
13. **Main boss lair.** A reserved zone protected from other generation. Prefab-based. Location: TBD per world design.
14. **Portal spawn candidate slots.** Pre-computed list of valid tile positions where side anchors may spawn post-main-boss. Constrained to underground / deep / void layers (per GDD §4). Additional constraints: in accessible cave space, minimum distance from main boss lair and from each other.
15. **Region annotations per chunk.** Each chunk's metadata is populated: primary biome, primary layer, ore density estimate, structure count, special flags (contains boss lair, contains portal candidate, etc.). Used at runtime by other systems.

### Phase 5 — Validation & polish

16. **Reachability check.** Verify critical areas (spawn, main boss lair, all portal candidate zones) are reachable via connected traversable space. On failure: log, potentially regenerate a section.
17. **Post-processing.** Fix impossible tile configurations (floating single tiles, unsupported blocks). Smooth harsh biome transitions. Apply tile decorators (cracks, moss).

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

### 14.1 Floating-Point Determinism (resolved, VOID-045)

MVP targets x86-64 Windows and Linux only. **Decision: noise and world-gen maths
use IEEE-754 `double` throughout; fixed-point is not required.** .NET specifies
IEEE-754 semantics, and on x86-64 the four basic operations (add, subtract,
multiply, divide) are correctly rounded and therefore bit-identical across both
target platforms and across runtime versions.

The reproducibility hazards are not the basic arithmetic, so the decision comes
with rules that generation code must follow:

- **No transcendental libm calls in any sampling or generation path** — no
  `Math.Pow`, `Sin`, `Cos`, `Exp`, `Log`, `Sqrt`. These are not required to be
  correctly rounded and do differ between platforms and runtime versions.
  Octave frequency and amplitude stepping uses iterative multiplication, never
  `Math.Pow(lacunarity, octave)`. Irrational constants (1/sqrt(2), sqrt(2)) are
  written as literals, not computed.
- **`double` only** — no `float`, no `MathF`. A single-precision intermediate
  introduces widening behaviour that need not match.
- **No fused multiply-add** — `Math.FusedMultiplyAdd` and FMA-contracting
  intrinsics skip an intermediate rounding and change results.
- `Math.Floor`, `Math.Abs`, `Math.Clamp`, `Math.Min`/`Max` are exact and allowed.
- Integer or exact-power-of-two stepping is preferred wherever it is natural.

Revisit this if the target set ever includes a platform without correctly-rounded
hardware doubles; at that point fixed-point becomes the answer. Reference
implementation and the same decision in code form:
`src/Noise/PerlinNoise.cs`, `src/Noise/FbmNoise.cs`.

## 15. Resolved Design Decisions

All original open questions locked in v0.2:

1. **Chunk size:** 64 × 64 tiles.
2. **Prefab authoring format:** Tiled (.tmx export → JSON).
3. **Main boss lair:** biome-adaptive prefab variants. Location procedurally chosen and not marked — player must explore to find it.
4. **Weather system:** post-MVP. **Day/night cycle with dawn/dusk transitions is in MVP.**
6. **Sky content:** MVP = decorative scudding clouds only. Post-MVP = floating islands, sky biomes, weather.
7. **Underground biomes:** underground layer matches surface biome directly above; deep and void use standalone biomes.
8. **River generation:** L-system-based simplified paths for MVP.
9. **Ore vein algorithm:** random-walk placement per vein.
10. **Deep-layer hazards:** lava pools, poison water pockets, poison gas pockets.
11. **Void-layer identity:** distinct tile palette, void damage aura, liquid void rivers/pools (more damaging than lava), reduced natural light, larger caverns, void-exclusive monsters.
12. **Pre-generation time budget:** target <60s for Medium on target hardware. Feasible with C# / GDExtension + parallelised phases.
13. **Chunk load cache:** 9×9 chunk window around each player, up to 200 chunks cached with LRU eviction. All values tuneable via config.

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
- **W3b — Phase 2 step 5: terrain materialisation.** Fill chunks with tiles from the heightmap and biome map (VOID-056). Lettered rather than renumbered because W4-W14 are referenced by shipped tickets and code comments. Blocks W5-W8: caves, water, ores and vegetation all read and write tiles that nothing else creates.
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

Rough sequencing: W1 → W2 → W3 → W3b → W4/W5/W6 (parallel) → W7/W8/W9 (parallel) → W10 → W11 → W12/W13 (parallel).

---

*Living document. Every open question above becomes a follow-up decision as design deepens.*
