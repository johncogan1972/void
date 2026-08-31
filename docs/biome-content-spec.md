# Biome Content — Feature Spec

**Version:** 0.2
**Status:** Draft (first-pass proposals)
**Companion to:** GDD §3.4, world-generation-spec §4 & §8, world-data-model-spec §6

---

## 1. Overview

Defines the concrete biome roster for the game — home world surface biomes, their underground variants, deep-layer biomes, void-layer biomes, and portal-world themes. Each biome specifies its visual identity, tile palette direction, enemy pool, ore biases, hazards, and thematic role.

This document is a first-pass content proposal. Names, tile palettes, and creature identities are placeholders unless noted.

## 2. Design Principles

Biomes should:

- **Read at a glance.** A player entering a new biome should know it within one screen.
- **Have a mechanical identity, not just a palette swap.** Every biome offers something you can't get in others — a resource, an enemy type, a hazard, a structure.
- **Support the tech-vs-magic split thematically.** Some biomes lean magical, some technological, some balanced.
- **Escalate meaningfully into deeper layers.** Underground and deep versions aren't just "the same biome but darker" — they add complications.

## 3. Home World Surface Biomes

Numbers, exact tile art, and creature identities are proposals to react to.

**MVP roster — confirmed 2026-08-31.** MVP ships exactly three surface biomes:
**Meadow** (`void:meadow`), **Forest** (`void:forest`) and **Frostreach**
(`void:frostreach`). Scrubland, Ashwastes and Whisperbog stay in this section as
designed-but-not-committed content, per CLAUDE.md's rule that content specs are
aspirational; they are post-MVP unless scope is explicitly widened. Three is the
minimum that makes the biome classifier testable — a transition, a blend band and
a classification rule all need more than one candidate to demonstrate anything.

### 3.1 Meadow

**Role:** starter biome. The player spawns here. Safest, most forgiving.

- **Palette:** greens and warm earth tones. Grass surface, dirt substrate.
- **Vegetation:** oak-like trees (moderate density), wildflowers, tall grass, low shrubs.
- **Ambient:** birdsong, wind through grass, gentle daytime lighting.
- **Enemy pool (day):** small critters — passive, huntable for basic materials (rabbit-analogue for hide, deer-analogue for meat).
- **Enemy pool (night):** low-tier hostile creatures — small wolves, feral cats, restless skeletons on the darker fringes.
- **Ore bias:** copper concentration in the underground below.
- **Ambient hazards:** none.
- **Signature material:** hardwood from oaks.

### 3.2 Scrubland

**Role:** transition biome between Meadow and harsher zones. Moderate danger, useful materials.

- **Palette:** dry yellows, olive greens, and reddish-brown earth.
- **Vegetation:** sparse tough trees, brittle shrubs, occasional cacti at the edges.
- **Enemy pool (day):** larger predators (coyote-analogue), scavenger birds.
- **Enemy pool (night):** hyena-analogue packs, opportunistic raiders.
- **Ore bias:** iron concentration below.
- **Ambient hazards:** none, but reduced water sources.
- **Signature material:** dry hardwood, animal hides in variety.

### 3.3 Frostreach

**Role:** cold biome. Moderate-to-high danger. First environmental hazard.

- **Palette:** pale blues and whites over grey stone. Snow and ice everywhere on
  the surface.
- **Terrain:** the most vertical of the three MVP biomes — high, steep mountains,
  and a markedly higher cave density than Meadow or Forest.
- **Vegetation:** boreal conifer trees, hardy shrubs. Nothing grows on ice tiles.
- **Enemy pool (day):** ice-touched wolves, elk with jagged antlers.
- **Enemy pool (night):** frost wraiths (magic-tagged), yetis.
- **Ore bias:** silver-analogue below. Cold-affinity ore variants.
- **Ambient hazards:** cold damage (Cold damage type, mild) applied outdoors during blizzards or in prolonged exposure. Post-MVP weather. For MVP: cold damage in specific tile zones (icy caverns adjacent to surface).
- **Signature material:** hardwood conifer, frost-touched pelts.

**Feature dependencies.** Steep mountains are macro features (Sub-Phase B, epic W4)
overlaid on the base heightmap, not the base heightmap itself — world-generation-spec
§6 Phase 1 produces gentle bounded elevation and W4 shapes the drama into it. Raised
cave density is a per-biome tuning value consumed by cave carving (Sub-Phase B, W5).
Neither exists in Sub-Phase A, so Frostreach will initially generate as flat as
Meadow, differing only in palette and classification.

### 3.6 Forest

**Role:** MVP. Denser, more vertical counterpart to Meadow. Moderate danger.

- **Palette:** deep saturated greens over dark earth, dappled shade.
- **Vegetation:** medium-to-tall thick-trunked trees, vines hanging from branches,
  undergrowth beneath the canopy.
- **Terrain:** rock overhangs; deep, narrow rivers and lakes rather than the broad
  shallow water of Meadow.
- **Ambient:** heavier canopy cover, less direct light at ground level than Meadow.
- **Ore bias:** to be decided alongside the ore registry.
- **Ambient hazards:** none.

**Feature dependencies — this biome's identity does not land in one ticket.**
Overhangs are not representable in the Sub-Phase A heightmap (one Y per column);
per world-generation-spec §17's W14 note they emerge for MVP only where cave
carving intersects the surface, and a purpose-built overhang pass is post-MVP.
Deep narrow rivers are Sub-Phase B (W6, L-system rivers). Hanging vines are
Sub-Phase C vegetation (W8). Until those land, Forest differs from Meadow only in
palette and classification.

### 3.4 Ashwastes

**Role:** hot / hostile biome. High danger. Tech-lean.

- **Palette:** deep reds, blacks, dull oranges. Cracked earth and volcanic stone.
- **Vegetation:** almost none — occasional charred husks of trees, red thorn scrub.
- **Enemy pool (day):** flame beetles, salamander-like ambushers.
- **Enemy pool (night):** ash phantoms (magic-tagged), scorch hounds.
- **Ore bias:** iron variants, first appearance of tech-affinity ores (bronze-analogues, sulphur nodes).
- **Ambient hazards:** hot ground tiles applying mild fire DoT while walking without appropriate boots.
- **Signature material:** volcanic stone, first tech-crafting ingredients.

### 3.5 Whisperbog

**Role:** magic-lean biome. Moderate-to-high danger. First mild arcane exposure.

- **Palette:** desaturated greens and greys, patches of violet fungal growth and glowing motes.
- **Vegetation:** twisted willows, luminescent mushrooms, hanging moss.
- **Enemy pool (day):** bog stalkers, marsh serpents.
- **Enemy pool (night):** will-o'-wisps (magic-tagged), drowned wraiths.
- **Ore bias:** first appearance of low-tier magic-affinity materials (mote crystals, dew-charged silver).
- **Ambient hazards:** small pockets of naturally occurring poison water (per world-generation-spec §11).
- **Signature material:** luminescent mushrooms, mote crystals.

## 4. Underground Layer Biomes

Per world-generation-spec §4.2, the underground layer matches the surface biome directly above. Each surface biome above defines an underground variant.

| Surface     | Underground variant | Notes |
|-------------|---------------------|-------|
| Meadow      | Root Hollows (`void:root_hollows`) | Tree-root walls, dirt tunnels, occasional cave beetles. Copper ore common. |
| Forest      | Root Tangle (`void:root_tangle`)   | Dense woven root walls, tighter winding tunnels, water seeping from above. |
| Scrubland   | Sandstone Warrens   | Sandstone walls, cactus-root ceilings, sand-flow tiles. Iron ore common. |
| Frostreach  | Frozen Halls (`void:frozen_halls`) | Ice-veined walls, small frozen ponds, ice-lurking predators. Silver + rare frost ore. Deliberately outside the `root_*` naming of the other two: there are no tree roots under a snow mountain, and the name should describe the place rather than match a pattern. |
| Ashwastes   | Ember Deeps         | Basalt walls, ambient warm light from lava seams, sulphur pockets. Iron + sulphur. |
| Whisperbog  | Fungal Root Caves   | Mushroom-lined walls, mote-lit tunnels, hostile spore-drops. Motes + luminescent stems. |

Each variant carries a distinct enemy pool (roughly 3–5 creature types per variant) that extends the surface theme downward.

## 5. Deep Layer Biomes

Per world-generation-spec §4.3, deep-layer biomes are standalone — no surface pairing. Three biomes ship for the deep layer.

### 5.1 The Sunken Vaults

**Theme:** flooded ruins of a forgotten civilisation, walls partially collapsed, waterlogged.

- **Palette:** blue-green stone, patches of dim underwater sections, algae growth.
- **Enemy pool:** amphibious constructs, drowned soldiers with rusted weapons, deep serpents.
- **Ore bias:** steel-grade ore, first appearance of mid-tier magic reagents (deep-water crystals).
- **Hazards:** underwater pockets — reduced movement, breath meter (post-MVP breath system, or immediate suffocation damage for MVP), poison water in some pockets per world-generation-spec §11.
- **Signature material:** deep-water crystals, ancient bronze plating.

### 5.2 The Boneyard Strata

**Theme:** massive ancient bones fossilised into the strata. A civilisation's or a beast's remains.

- **Palette:** cream and yellow tones, walls striated with bone. Occasional whole vertebra-arches spanning caverns.
- **Enemy pool:** revenant skeletons, marrow wraiths, calcified guardians (heavier stagger-resistant enemies).
- **Ore bias:** steel-grade ore, first appearance of mid-tier physical-affinity materials.
- **Hazards:** bone-shard traps in some tile clusters — piercing damage on contact.
- **Signature material:** intact bone segments (crafting material), marrow essence.

### 5.3 The Molten Rifts

**Theme:** active volcanic fissures below the crust. High-heat, tech-leaning.

- **Palette:** deep reds, hot oranges, ambient glow from lava seams.
- **Enemy pool:** magma elementals, forge-worms, salvager drones (early tech enemy — mechanical, drops scrap).
- **Ore bias:** steel-grade ore, gold-analogues, first tech reagents in real quantity.
- **Hazards:** lava pools everywhere (per world-generation-spec §11), poison gas pockets in sealed side chambers.
- **Signature material:** forge-metals, salvage scrap (tech crafting).

## 6. Void Layer Biomes

Per world-generation-spec §4.4, void has a distinct palette and identity. Two void biomes for MVP.

### 6.1 The Hollow Cathedral

**Theme:** vast empty cathedral-scale caverns, echoing silence, unnatural geometry.

- **Palette:** deep purples and blacks, occasional pale-white void motes drifting through space.
- **Enemy pool:** void wraiths, silent stalkers (invisible until attacking), cathedral guardians (large single-encounter enemies).
- **Ore bias:** void metals, magic crystals of the highest tier.
- **Hazards:** void aura zones in some regions (per world-generation-spec §4.4).
- **Signature material:** cathedral silverstone, void-glass shards.

### 6.2 The Void Sea

**Theme:** the layer where liquid void pools and rivers dominate. Structural remnants poke through a sea of nothing.

- **Palette:** deep violet-black liquid, rare pinpricks of starlight from cavern ceilings, jagged floating rock platforms.
- **Enemy pool:** floating void manifestations, rift-callers (summoners), depth-called horrors (heavy hitters).
- **Ore bias:** highest-tier void metals, endgame magic reagents.
- **Hazards:** liquid void rivers and pools (per world-generation-spec §11), void aura everywhere at reduced intensity.
- **Signature material:** liquid void distillate (crafting reagent, dangerous), starstone.

## 7. Portal-World Themes (MVP + Post-MVP)

MVP ships one portal world (per GDD §12.1). Post-MVP adds more. Each portal world is a themed variant of the four-layer world structure — surface / underground / deep / void proportions may vary — with its own biome mix.

### 7.1 MVP: The Scorched (portal-world type)

**Theme:** a world consumed by an ancient conflagration. Skies choke with ash, ground is cracked and glowing.

- **Difficulty tier:** slightly harder than home world (tier 1 portal).
- **Signature material:** cindercore (mid-tier crafting reagent, fire-affinity).
- **Surface biomes:** Ashen Plains, Ember Ridges (analogous roles to Meadow / Scrubland but scorched).
- **Underground:** magma-veined stone.
- **Deep:** perpetual fire caverns (variant of Molten Rifts).
- **Void:** small void seepage — hints of what a full void layer looks like in later portals.
- **Main boss:** a Charred Sentinel — fire-typed heavy hitter. See bosses spec.

### 7.2 Post-MVP: The Sunken (portal-world type)

**Theme:** a drowned world, oceanic. Everything is underwater or on small islands.

- **Difficulty tier:** considerably harder (tier 2 portal).
- **Signature material:** abyssal pearl.
- **Surface biomes:** Kelp Isles, Storm Coast.
- **Underground:** flooded ruins.
- **Deep:** the actual deep sea, dangerous.
- **Void:** rifts in the seabed.

### 7.3 Post-MVP: The Clockwork (portal-world type)

**Theme:** a world built by tech-focused ancients, everything mechanical, geometric.

- **Difficulty tier:** considerably harder (tier 2 portal).
- **Signature material:** aetherium (high-tier tech reagent).
- **Surface biomes:** Brass Fields, Gear Yards.
- **Underground:** conveyor tunnels.
- **Deep:** the great furnace.
- **Void:** stalled machines drifting in null space.

### 7.4 Post-MVP: The Verdant (portal-world type)

**Theme:** overgrown, alive, magic-leaning. Everything grows too fast, too big.

- **Difficulty tier:** considerably harder (tier 2 portal).
- **Signature material:** heartroot (high-tier magic reagent).
- **Surface biomes:** overgrown jungles, floating gardens.
- **Underground:** root-choked caverns.
- **Deep:** the great heartwood.
- **Void:** where nothing grows — voids in the endless life.

### 7.5 Post-MVP: The Shattered (portal-world type)

**Theme:** a world literally broken into floating shards. Vertical, dangerous.

- **Difficulty tier:** extreme (tier 3 portal).
- **Signature material:** shardsilk (endgame crafting).
- **Surface:** islands floating in void, connected by ancient bridges.
- **Underground/deep:** the anchors that keep the islands aloft.
- **Void:** the space between shards.

## 8. Biome Data Schema Reference

Per world-data-model-spec §6, each biome is a JSON entry. Placeholder skeleton for a MVP biome:

```yaml
biome_id: "meadow"
display_name: "Meadow"
layer_category: "surface"
palette:
  surface_block: "grass"
  subsurface_block: "dirt"
  base_block: "stone"
  wall_default: "dirt_wall"
  wall_ambient: ["dirt_wall_mossy", "dirt_wall_rooted"]
vegetation:
  trees:
    - { prefab: "oak_small", weight: 0.5 }
    - { prefab: "oak_medium", weight: 0.3 }
    - { prefab: "oak_large", weight: 0.2 }
  plants: [{ prefab: "wildflower_yellow", weight: 0.4 }, ...]
  decorations: [{ prefab: "grass_tall", weight: 1.0 }]
ore_biases:
  copper: 1.2
  iron: 0.9
  # other ores default to 1.0
enemies:
  - { enemy_id: "rabbit", weight: 0.6, time_of_day: "day" }
  - { enemy_id: "deer", weight: 0.3, time_of_day: "day" }
  - { enemy_id: "grey_wolf", weight: 0.4, time_of_day: "night" }
  - { enemy_id: "small_skeleton", weight: 0.3, time_of_day: "night" }
underground_variant: "root_hollows"
ambient:
  light_tint: [1.0, 0.98, 0.92, 1.0]
hazards: []
```

Each biome ships with a companion tile palette, prefab set, and enemy definitions — the biome file itself is a coordination point that references those resources.

## 9. Enemy Roster (Overview)

This spec proposes enemy identities per biome but does not spec their full stats. That belongs in a dedicated enemy design document later. For each MVP-shipping biome, we need approximately 3–5 enemy definitions to feel populated.

**Rough count for MVP:**

- Home world surface biomes: 5 × ~4 enemies = ~20 surface enemies
- Home world underground: 5 × ~3 enemies = ~15 underground enemies
- Deep layer biomes: 3 × ~4 enemies = ~12 deep enemies
- Void layer biomes: 2 × ~4 enemies = ~8 void enemies
- Portal world (Scorched, MVP): ~10 enemies unique to this world
- **Total MVP enemy roster: ~65 unique enemies.**

Substantial but achievable — a lot of these can share behaviour patterns and only differ in stats, palette, and one signature ability.

## 10. Open Questions

- **Weather-based biome effects.** Blizzards in Frostreach, ash storms in Ashwastes, magic fogs in Whisperbog. Post-MVP weather system dependency.
- **Biome-tied music themes.** Post-MVP.
- **Meta-biomes.** Should some regions carry a secondary "corruption" style overlay (a poisoned zone, an arcane zone) that overrides the base biome? Post-MVP — adds nice replay variety.
- **Underground variant depth control.** The underground variant matches the surface biome. What about biome boundary transitions vertically? If the surface transitions from Meadow to Scrubland at column 500, the underground follows the same boundary. Confirm this is desired behaviour (mostly self-evident but worth flagging).
- **Void biome count.** Two proposed. Enough for MVP given void's small relative size (15% of world height)?
- **Portal world variety.** Five post-MVP portals proposed. Is this the right count, or do we want more with less variety per, or fewer with more depth per?
- **Signature material naming.** All placeholders. Naming pass needed at some point but not urgent.
- **Underground biome for a "corrupted" surface variant.** If we add a mildly corrupted surface biome in MVP (per GDD §3.4), what's its underground?
