# Cave Generation — Feature Spec

**Version:** 0.1
**Status:** Draft
**Companion to:** world-generation-spec.md (§6 Phase 2, §7)

---

## 1. Overview

Cave generation is a hybrid of two subsystems running sequentially:

1. **Perlin worm carving** — produces navigable tunnel networks that snake through solid rock.
2. **Cellular automata cavern carving** — produces organic open chambers that connect to and interrupt the tunnel network.

Both subsystems run per-layer with layer-specific tuning (worm density, cavern density, style). The result is the world's traversable underground: everything from tight tunnels to cathedral-scale voids.

## 2. Requirements Recap

From world-generation-spec:

- Runs in Phase 2 of the pipeline (after heightmap and biome placement, before ore/vegetation/structures).
- Deterministic — seeded from `hash(world_seed, "caves")` and derived sub-streams per subsystem.
- Layer-aware — different tuning per outside / underground / deep / void.
- Portable — uses xoshiro256++ (or equivalent), fixed-point where FP would drift.

## 3. Subsystem 1: Perlin Worms

### 3.1 Concept

A "worm" is a simulated agent that walks through the world, carving tunnel space as it moves. Direction changes are driven by low-frequency noise, so worms bend gracefully rather than turning randomly. Multiple worms per layer produce interconnected tunnel networks.

### 3.2 Algorithm

For each worm:

1. **Spawn.** Origin picked from a seeded list of spawn candidates within the layer's tile range. Candidate list is generated up-front from biome/layer eligibility.
2. **Initialise.** Set current position `(x, y)`. Set current direction `θ` (radians) from initial noise sample.
3. **Step.** For `N` steps:
    - Read noise value at `(x, y)` from a dedicated worm-direction noise field.
    - Adjust `θ` by a small delta proportional to the noise value (max ±`turn_rate` per step).
    - Move `(x, y)` forward by `step_length * (cos θ, sin θ)`.
    - Carve tiles within `radius` of the new position (set `block_id = 0`, preserve wall_id).
    - **Branching:** with probability `branch_chance`, spawn a child worm at the current position with adjusted direction. Child inherits parent parameters with slight variation.
4. **Terminate** when step count reached, when leaving the layer's tile range, or when the worm collides with an existing tunnel it did not spawn from (natural intersection).

### 3.3 Tuneable parameters

Per worm (data-driven per layer, per biome):

| Parameter        | Type    | Typical range | Notes |
|------------------|---------|---------------|-------|
| `step_count`     | int     | 100–500       | Length of the worm's walk |
| `step_length`    | float   | 0.5–2.0 tiles | Speed / smoothness |
| `radius`         | float   | 1.5–4.0 tiles | Tunnel width |
| `turn_rate`      | float   | 0.05–0.30 rad | Curviness |
| `branch_chance`  | float   | 0.0–0.05      | Per-step branching probability |
| `noise_scale`    | float   | 0.01–0.05     | Noise frequency for direction changes |

Per layer:

| Parameter        | Type    | Notes |
|------------------|---------|-------|
| `worm_count`     | int     | Total worms spawned in this layer |
| `spawn_biases`   | map     | Preferred spawn regions (e.g. bias toward biome edges) |

### 3.4 Determinism notes

- Every worm's parameters, spawn location, initial direction, and per-step noise samples derive from the seeded RNG.
- Child worm seeds derive from parent seed + branch index.
- The order worms are simulated matters (later worms may hit tunnels carved by earlier ones). This order is stable: worms are sorted by `(spawn_x, spawn_y, spawn_index)` before simulation.

### 3.5 Implementation notes

- Noise field is not stored — sampled on demand via `simplex_noise(x * noise_scale, y * noise_scale, seed_offset)`.
- Carving is destructive to `block_id` but preserves `wall_id`. Walls remain as the background inside tunnels.
- Do not clear `flags` bit 3 (`part of a prefab`) — worms respect prefab-reserved regions.

## 4. Subsystem 2: Cellular Automata Caverns

### 4.1 Concept

A patch of the world is seeded with random "alive" (empty) and "dead" (solid) tiles at some threshold. Then a cellular automaton rule iterates: each tile's next state depends on its neighbours. After `N` iterations, the pattern smooths into organic blob shapes — perfect for open caverns.

### 4.2 Algorithm

For each cavern site:

1. **Choose site** — from candidate list, per §4.3.
2. **Bound the region** — cavern generation happens within a bounded rectangle (avoids infinite spread). Typical bound: 40×40 tiles.
3. **Seed the grid** — for each tile in the region, set `alive` with probability `fill_probability` (typical: 0.45).
4. **Iterate** for `N` generations (typical: 4–6):
    - For each tile, count `alive` neighbours in the 8-cell Moore neighbourhood.
    - Apply CA rule (typical: "B678/S345678" — birth if 6+, survive if 3+ alive neighbours).
    - Update all tiles in lockstep (two-buffer approach for determinism).
5. **Post-process** — carve any tiles still marked `alive` into empty space in the world (`block_id = 0`, preserve `wall_id`).
6. **Connectivity check (optional per layer)** — flood-fill from cavern centre. If the largest connected region is under a threshold, discard the cavern (avoids pointless isolated pockets).

### 4.3 Cavern site selection

Sites are chosen from three sources:

1. **Worm tunnel intersections** — where two or more worm tunnels cross, spawn a cavern with high probability. Feels like "the tunnel opens into a room."
2. **Random valid locations** — sample seeded points within the layer's tile range. Reject sites too close to other caverns or to structures.
3. **Reserved sites** — pre-marked locations from Phase 1 metadata (portal candidate zones, structure reservation zones).

Site type mix per layer (§5).

### 4.4 Tuneable parameters

Per cavern (data-driven per layer):

| Parameter           | Type  | Typical range | Notes |
|---------------------|-------|---------------|-------|
| `region_width`      | int   | 20–80 tiles   | Bounding box width |
| `region_height`     | int   | 20–80 tiles   | Bounding box height |
| `fill_probability`  | float | 0.35–0.55     | Initial density of alive tiles |
| `iterations`        | int   | 3–8           | CA generations to run |
| `birth_threshold`   | int   | 4–7           | Neighbours to become alive |
| `survival_threshold`| int   | 3–5           | Neighbours to stay alive |
| `min_size_ratio`    | float | 0.10–0.40     | Discard threshold for connectivity check |

### 4.5 Determinism notes

- The initial seed grid is generated from the seeded RNG, iterated deterministically.
- Two-buffer update (read from grid A, write to grid B, swap) ensures no dependence on tile-processing order.
- Cavern site selection order is stable: sites sorted by `(y, x, source_type)` before generation.

## 5. Blending Strategy

The two subsystems run **sequentially per layer**:

1. **Worm pass** — spawn all worms for the layer, simulate each, carve tunnels.
2. **Cavern site pass** — identify sites (worm intersections + random + reserved).
3. **Cavern pass** — generate each cavern, carve.

This ordering produces the desired feel: primary tunnel network first, then chambers open up at intersections and points of interest. Worms carved after caverns would sometimes cut straight lines through cavern edges — visually worse.

## 6. Layer-Specific Tuning

Recap from world-generation-spec §7, expanded:

### 6.1 Outside layer (30%)

- Worm density: very low (2–5 worms per world width).
- Cavern density: **none** by default. Surface caves are rare and small.
- Style: occasional surface-opening tunnel mouths that lead to the underground layer.
- Rationale: outside is meant to be traversed on the surface, not underground.

### 6.2 Underground layer (25%)

- Worm density: moderate (~1 worm per 40 columns of world width).
- Cavern density: many small-to-medium chambers.
- Cavern parameters: region 20–40 tiles, 4 iterations, moderate connectivity requirement.
- Style: familiar mining feel. Frequent chambers with visible ore veins along their walls.

### 6.3 Deep layer (30%)

- Worm density: high (~1 worm per 20 columns).
- Cavern density: dense, medium-large.
- Cavern parameters: region 30–60 tiles, 5–6 iterations.
- Style: dangerous exploration. Overlapping caverns create complex spaces; multi-layer chambers stacked vertically.
- Hazards placed post-carving: lava pools in cavern floors, poison water in cavern pockets, poison gas in sealed chambers.

### 6.4 Void layer (15%)

- Worm density: low (~1 worm per 60 columns) — but longer worms.
- Cavern density: sparse, large.
- Cavern parameters: region 60–80 tiles, 6–8 iterations, high connectivity requirement (want big open spaces, not fractured pockets).
- Style: cathedral chambers separated by thick dead rock. Long stretches of dead rock between chambers with only narrow worm tunnels connecting them.
- Liquid void placed post-carving in a subset of cavern floors.

## 7. Reachability & Correction

After both passes on all layers:

1. **Global reachability check** — flood-fill from the player spawn point. Every reserved location (main boss lair, portal candidates) must be reachable.
2. **On failure** — spawn a "corrective worm" from the nearest reachable point toward the unreachable target. Deterministic, so re-generating the same seed produces the same correction.
3. **Iteration cap** — if reachability still fails after N corrective attempts, fail world generation and surface the error. This should never happen for tuned parameters; it's a safety net.

## 8. Data Flow

Inputs:
- World seed (for RNG derivation).
- Layer boundaries (from Phase 1).
- Biome map (for spawn biases).
- Prefab reservation zones (do not carve here).

Outputs:
- Modified tile array (`block_id = 0` in carved space).
- Updated chunk metadata (`walkable_ratio` on affected chunks).

Side-effects:
- None — cave gen only modifies tile data.

## 9. Testing & Validation

- **Determinism test:** generate a reference seed, hash the resulting tile array's carved-mask (bit array of "is tile air"). Compare against a golden hash in CI.
- **Reachability test:** for a battery of seeds, assert 100% pass rate on the reachability check without corrective worms triggering.
- **Density test:** for a battery of seeds, assert `walkable_ratio` per layer falls within expected ranges (catches mis-tuning).

## 10. Open Questions

- **Noise library** — do we roll our own simplex noise for portability, or use a Godot / NuGet library and pin its version? Recommend our own implementation — small, portable, and eliminates a "does this library return identical values across platforms?" risk.
- **Worm-vs-cavern collision handling** — if a worm walks into a cavern, does it stop? Continue? Currently spec'd as "collides with existing tunnel it did not spawn from, terminates." Confirm this applies to caverns too? (Recommendation: yes.)
- **Cave decoration** — stalactites, mushrooms, cave water drips. Post-MVP content that lives on top of this generation; noted here for completeness.
