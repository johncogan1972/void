# Implementation Roadmap

**Version:** 0.1
**Status:** Draft
**Purpose:** Ordered feature-build sequence from empty repo to MVP ship, then post-MVP. Every phase is a workable chunk with clear prerequisites and definition-of-done.

---

## How to use this doc

- **Phases are ordered.** Complete a phase before starting the next unless the "parallelisable with" note says otherwise.
- **Definition of done is mandatory.** Do not proceed to the next phase until the current phase's DoD is met. This prevents building on foundations that aren't actually stable.
- **Content is minimal by default.** Phases specify MVP content quantities, not the full-game targets from the content specs. Do not implement the full content-spec rosters unless the user explicitly expands scope.
- **Post-MVP phases are outlined only.** Detailed sequencing for post-MVP will be added when MVP ships.
- **When asked "what should I build next?"** → find the next incomplete phase, confirm prerequisites are done, start with its first feature.

**Effort scale (rough, per phase):** S = a few days, M = 1–2 weeks, L = 2–4 weeks, XL = a month+.

---

## Overview

| # | Phase | Effort | MVP |
|---|-------|--------|-----|
| 0 | Foundation | M | ✓ |
| 1 | World Data Model | M | ✓ |
| 2 | World Generation Pipeline | L | ✓ |
| 3 | Rendering & Chunk Streaming | M | ✓ |
| 4 | Character & Input | M | ✓ |
| 5 | Inventory & Equipment | M | ✓ |
| 6 | Interaction & Building | S | ✓ |
| 7 | Combat: Melee & Core | M | ✓ |
| 8 | Combat: Projectiles, Magic, Status | M | ✓ |
| 9 | Enemies & Loot | L | ✓ |
| 10 | NPCs & Housing | M | ✓ |
| 11 | Hearth & Difficulty Modes | S | ✓ |
| 12 | Home World Main Boss | M | ✓ |
| 13 | Portal System | M | ✓ |
| 14 | First Portal World | L | ✓ |
| 15 | Event & Area Bosses | M | ✓ |
| 16 | Save/Load Integration | S | ✓ |
| 17 | Steam Deck & Polish | M | ✓ |
| 18 | **MVP Ship** | — | ✓ |
| P1+ | Post-MVP phases | — | — |

Approximate rough total for MVP: 6–12 months solo, depending on scope discipline and content depth.

---

## Phase 0 — Foundation

**Prerequisites:** none.
**Parallelisable with:** none — this is the base for everything.

Bootstrap the project and lay the non-negotiable foundations.

**Features:**
- Godot 4.x project with C# support enabled.
- Repo layout: `src/` for code, `docs/` for specs (already in place), `assets/` for art/audio, `data/` for JSON, `tests/` for CI tests.
- Deterministic RNG infrastructure (xoshiro256++ or portable equivalent), with sub-stream derivation via `hash(seed, key)`.
- Data-driven JSON loader — reads a folder of JSON files at boot into typed registries.
- Base registry pattern (generic `Registry<T>` with sorted iteration).
- Save envelope: header + zstd + XOR obfuscation + SHA-256 hash (per save-format-spec §4, §7, §8).
- Atomic file write helper (temp + rename pattern).
- CI: reference-seed test that regenerates a canonical byte payload and hashes it. Fails on non-match.

**Specs:** save-format-spec, world-generation-spec §3.2 (determinism rules).

**Definition of done:**
- Project builds and runs on Windows and Linux.
- CI runs on push, executes the reference-seed test successfully.
- A test can save an arbitrary payload to disk (via envelope), reload it, and get an identical payload back.
- Deterministic RNG passes basic stream tests (same seed → same sequence, different seeds → different sequences).

---

## Phase 1 — World Data Model

**Prerequisites:** Phase 0.
**Parallelisable with:** none.

Every data structure that world gen and runtime will read/write.

**Features:**
- Tile struct (8 bytes packed: block_id, wall_id, liquid_type, liquid_level, flags, damage).
- Chunk struct (header + metadata + tile array).
- World manifest schema.
- Campaign manifest schema.
- Prefab schema (with Tiled `.tmx` → runtime prefab converter at build time).
- Biome schema.
- Item base schema (extended in Phase 5).
- Loot table schema.
- Enemy schema (base fields — full spec in Phase 9).
- Registries populated at boot: Block, Wall, Biome, Prefab, LootTable, Enemy, Item.
- Serialisation tests: round-trip every schema.

**Specs:** world-data-model-spec (all sections).

**Definition of done:**
- Every schema deserialises from JSON, serialises back to identical JSON.
- A test can define a new block type in JSON, load it, place it in a chunk, save the chunk, load the chunk, and verify the tile matches.
- Tiled `.tmx` files convert to runtime prefab format at build time.

---

## Phase 2 — World Generation Pipeline

**Prerequisites:** Phase 1.
**Parallelisable with:** Phase 3 can start once Phase 2's Sub-Phase A is done (data flowing into chunks).

Full 5-phase generation pipeline from world-generation-spec.

### Sub-Phase A: Structural (world-gen-spec Phase 1)

- Heightmap generation (1D noise per column).
- Layer boundary computation from configurable proportions (default Terraria-like: 30/25/30/15).
- Biome map (2D noise + rule-based classification across surface).

### Sub-Phase B: Terrain shaping (world-gen-spec Phase 2)

- Macro features (mountains, valleys — extend heightmap with low-frequency noise).
- **Cave carving:** hybrid worms + cellular automata (per cave-generation-spec).
- Water and liquid placement (lakes, rivers via L-system, underground reservoirs, deep-layer lava/poison, void liquid).

### Sub-Phase C: Composition (world-gen-spec Phase 3)

- Ore distribution (depth-tiered random-walk veins).
- Vegetation placement (biome-driven).
- Structure placement (prefab-based, Tiled prefabs from Phase 1).

### Sub-Phase D: Reservations & metadata (world-gen-spec Phase 4)

- Player spawn point selection.
- Main boss lair reservation (hidden location, biome-adaptive prefab variant).
- Portal spawn candidate slots (underground / deep / void only).
- Chunk metadata population.

### Sub-Phase E: Validation & polish (world-gen-spec Phase 5)

- Reachability check with corrective worms.
- Post-processing (tile fixups, biome transition smoothing).

### Sub-Phase F: Pre-gen UX

- Progress bar UI during generation.
- Cancellation support.

**Specs:** world-generation-spec, cave-generation-spec, world-data-model-spec.

**Definition of done:**
- Medium world (6400 × 1800) generates in <60s on target hardware.
- CI reference-seed test regenerates a known world and its byte-hash matches.
- Reachability check passes for a battery of seeds without corrective worms triggering.
- Generated world saves to disk cleanly (chunk shards + manifest).

**Content quantity for MVP:** 2–3 surface biomes minimum (e.g., Meadow + Ashwastes), one deep biome, one void biome. Additional biomes deferred to post-MVP or content passes.

---

## Phase 3 — Rendering & Chunk Streaming

**Prerequisites:** Phase 1. Phase 2 Sub-Phase A minimum.
**Parallelisable with:** later parts of Phase 2 (can render partial worlds while gen work continues).

Make the world visible.

**Features:**
- Godot TileMapLayer with terrain autotile config (Wang / marching-squares for MVP per GDD §9.5).
- Tile atlas loaded from PNG + Godot TileSet resource.
- Wall/background rendering (separate TileMapLayer, lower z-index).
- Camera 2D with configurable zoom (1x/2x/3x).
- Chunk streaming: 9×9 window around player, LRU cache to 200 chunks.
- Save-modified-chunks-on-eviction rule.
- Pre-generated world loads from disk into the chunk cache.
- Basic lighting: torches (placeable), ambient (layer-based).
- **Day/night cycle** with dawn/dusk transitions (in MVP per GDD §3.5).
- Cloud parallax on the outside layer (decorative scudding clouds).

**Specs:** world-generation-spec §5, GDD §3.5, GDD §9.4, GDD §9.5.

**Definition of done:**
- Player character (placeholder sprite) can move through a generated world.
- Chunks load and unload as player moves.
- Day/night cycle visibly progresses.
- Zoom levels work; pixel-perfect at all zooms.
- Modified chunks (test placeholder edits) persist across quit and reload.

**Content quantity for MVP:** placeholder terrain tiles (dirt, stone, grass, sand, ash, wall variants). ~10 terrain tile types is enough to prove the system.

---

## Phase 4 — Character & Input

**Prerequisites:** Phase 3.
**Parallelisable with:** Phase 5 partially (some UI shared).

Player character, movement, and both input methods.

**Features:**
- Player entity with physics (movement, jump, gravity, collision with tiles).
- Character creation UI (cosmetic customisation + starting archetype selection).
- Character save format (per save-format-spec §5 character payload).
- Keyboard + mouse input via Godot InputAction.
- Controller input via Godot InputAction (default bindings per GDD §5.7).
- Input rebinding UI.
- HP system (base 100, cap 400, regen 1/sec out-of-combat per combat-spec §7.1).
- Mana system (base 100, cap 200, regen 2/sec per GDD §5.6).
- Health/mana UI bars.
- "In-combat" flag driven by "took damage OR dealt damage OR in enemy aggro range."

**Specs:** GDD §5.1, §5.6, §5.7. combat-spec §7, §9. save-format-spec §5.

**Definition of done:**
- Character can be created (cosmetics chosen, archetype picked).
- Character spawns in world at generated spawn point.
- Movement works with keyboard/mouse and controller.
- HP and mana bars display and update.
- HP regens out of combat, stops in combat.
- Character save file written and loaded correctly.

---

## Phase 5 — Inventory & Equipment

**Prerequisites:** Phase 4.
**Parallelisable with:** Phase 6.

The item-holding, item-wielding systems.

**Features:**
- Inventory data model (base 10 slots + bag expansion).
- Bag equip/unequip rules (bag can't be removed if excess slots occupied).
- Equipment slots: 5 armour (head, chest, gloves, legs, boots), 2 back (bag, attachment), 3 jewellery (2 rings + amulet).
- Hand-slot display (right/left hand) driven by hotbar activation.
- Hotbar: 10 slots, hotkeys 1–0, LB/RB cycling on controller.
- Item instance data (rolled stats per rarity).
- Item stacking rules (per-item stack size from JSON data).
- Inventory UI (grid layout, Terraria-style).
- Equipment UI panel.
- Hotbar UI.

**Specs:** GDD §5.7. loot-table-spec §14 (item instance model).

**Definition of done:**
- Player can open inventory UI.
- Player can drag items between inventory slots and equipment slots.
- Hotbar hotkeys work (weapons → right hand, shields → left hand, two-handed → both, tools → active tool).
- Removing a bag with excess items is blocked with clear feedback.
- Rings, amulet, back attachments visibly grant their passive effects to the player.

**Content quantity for MVP:** ~10 placeholder items (a sword, a bow, a shield, 2 armour pieces, 2 tools, a torch, a bag). Enough to prove the system.

---

## Phase 6 — Interaction & Building

**Prerequisites:** Phase 5.
**Parallelisable with:** Phase 7.

Player interacts with the world — mining, placing, opening containers.

**Features:**
- Tile mining (right-click in tool mode, mining time based on tool tier and tile hardness).
- Tile placement (place from hotbar item).
- Tool mode toggle (F key / Y button) with weapon-mode fallback.
- Object interaction (E key / X button — open containers, talk to NPCs, use portals).
- Container entity (chest) with per-instance inventory.
- Item pickup on ground (walk over — no magnetism per loot-table-spec §17).
- Item despawn (10 minutes for standard items, never for guaranteed/Legendary).
- Ground item stack merging on drop.
- Building constraints (must be placed adjacent to existing tile, etc.).

**Specs:** GDD §5.7. loot-table-spec §17.

**Definition of done:**
- Player can mine a stone tile with a pickaxe.
- Player can place a dirt block from their hotbar.
- Player can open a placed chest and see its inventory.
- Placed items can be picked up.
- Items despawn after 10 minutes (verified via test).

---

## Phase 7 — Combat: Melee & Core

**Prerequisites:** Phase 5 (equipment).
**Parallelisable with:** Phase 6.

Combat foundation with melee weapons only.

**Features:**
- Attack lifecycle (wind-up / active / recovery based on weapon attack_rate).
- Melee weapon attack (hitbox sweep during active frames).
- AttackEvent system (as per combat-spec §4.1).
- Damage resolution flow per combat-spec §4.2.
- Six damage types (Physical, Fire, Cold, Poison, Magic, Void).
- Multi-type damage per weapon.
- Armour resistance calculation with 75% cap.
- Shield passive resistance with +5% cap (excluding splash and void).
- Void damage bypass rules (shield bypass, only explicit void resistance mitigates).
- Hit-stop on hits (weapon-configured frames).
- Knockback (weapon-configured strength, direction based on hit).
- Knockback resistance per enemy (0.0 to 1.0 multiplier).
- Player death → respawn at spawn (Hearth in Phase 11).

**Specs:** combat-spec §1–§7, §10.

**Definition of done:**
- Player can equip a sword, swing at a training dummy.
- Damage numbers match formulas from combat-spec.
- Dummy dies when HP reaches 0.
- Sword-and-shield loadout works (shield contributes passive resistance).
- Void damage on the dummy bypasses shield contribution correctly.
- Player dies at HP 0 and respawns.

---

## Phase 8 — Combat: Projectiles, Magic, Status

**Prerequisites:** Phase 7.
**Parallelisable with:** none (extends combat).

Projectiles, magic weapons, status effects — completes combat coverage.

**Features:**
- Projectile entity system with physics (arc, straight, gravity as configured per projectile).
- Projectile spawn from weapon (fire rate, range, projectile speed).
- Projectile properties: piercing, splash, homing, status-effect application.
- Splash damage delivery (single AttackEvent per target in area, `is_splash_damage = true`).
- Magic weapons with mana cost per use.
- Ammo consumption (firearms → bullets, bows → arrows, crossbows → bolts).
- Basic ammo types only for MVP (standard arrow, standard bullet).
- Status effect system per combat-spec §5.
- MVP status effects: burn, poison, freeze, void-burn.
- Environmental damage: lava (fire DoT), poison water (poison DoT), poison gas (poison DoT), void aura (void DoT), liquid void (higher void DoT).

**Specs:** combat-spec §4.5, §4.4a, §5. loot-table-spec (for weapon damage rolling).

**Definition of done:**
- Bow fires arrows; arrows deal damage to enemies.
- Fireball wand consumes mana, deals fire damage.
- Splash projectile hits multiple targets in area.
- Homing projectile tracks nearest enemy.
- Burn/poison/freeze effects apply and tick correctly.
- Standing in lava deals fire damage over time.
- Standing in liquid void deals void damage (bypasses shield).

---

## Phase 9 — Enemies & Loot

**Prerequisites:** Phase 7 (combat), Phase 2 (world), Phase 6 (drops on ground).
**Parallelisable with:** none.

Enemies to fight, loot tables to roll.

**Features:**
- Enemy entity base class.
- Enemy AI baseline: proximity aggro, simple pathing (A* or navigation mesh — TBD).
- Enemy attack execution (same AttackEvent system as player).
- Enemy spawning based on biome + time-of-day + player interest set.
- Enemy despawn when far from all players.
- Enemy death event → loot roll → drop items.
- Loot table system (per loot-table-spec).
- Rarity roll math (weighted selection).
- Stat rolling from rarity ranges.
- Legendary name generation (prefix + base + suffix pools).
- Guaranteed drops.
- First-kill flag tracking (in campaign manifest).
- Loot table inheritance via `includes` field.

**Specs:** loot-table-spec (all sections), combat-spec §7.4, §8, biome-content-spec.

**Definition of done:**
- Enemies spawn in appropriate biomes at correct times.
- Enemies engage player, attack, and are attacked.
- On death, enemies roll from their loot table and drop items.
- Rarity distribution matches configured weights across many rolls (statistical test).
- Legendary drops produce generated names.
- First-kill bonus fires exactly once per enemy source.

**Content quantity for MVP:** 5–10 enemy species is enough. Enemy design is the largest content bottleneck; start small.

---

## Phase 10 — NPCs & Housing

**Prerequisites:** Phase 4 (character), Phase 6 (interaction), Phase 5 (inventory for vendor).
**Parallelisable with:** Phase 11.

The Guide NPC and the housing system that lets NPCs settle.

**Features:**
- NPC base entity.
- Guide NPC (Aelis) — spawns at character creation, approaches player.
- Housing validity check (per GDD §5.5): enclosed background walls, accessible door, light source, bed. One NPC per room.
- NPC pathfinding to base perimeter, then to assigned room.
- Vendor system UI.
- Guide's vendor stock definition (torches, basic potions, rope, basic ammo).
- Guide's ongoing hint dialogue (progression-state-aware).
- Dialogue tree system (data-driven, per npc-content-spec §6).
- NPC combat behaviour (weak defender per GDD §5.4).
- NPC death → respawn at Hearth after 10 min cooldown.

**Specs:** GDD §5.4, §5.5. npc-content-spec §3.

**Definition of done:**
- Guide appears near player at character creation.
- Guide follows player until a valid room is built.
- Guide moves into the room and stays there.
- Guide's vendor menu opens on interaction; items can be bought.
- Guide's dialogue changes based on progression state (before/after main boss kill).
- Guide dies to hostile enemies at base, respawns at Hearth after 10 minutes.

---

## Phase 11 — Hearth & Difficulty Modes

**Prerequisites:** Phase 5 (inventory), Phase 7 (death).
**Parallelisable with:** Phase 10.

The party spawn point and the two difficulty modes.

**Features:**
- Hearth entity (placeable furniture).
- Party spawn point mechanic (one Hearth active at a time; placing new one deactivates old).
- Home Potion item (teleport to Hearth from anywhere).
- Standard difficulty mode (default): respawn at Hearth with items intact.
- Hardcore difficulty mode: on death, all carried items dropped into a death container at death location.
- Death container entity: named ("[Player]'s Remains"), persists indefinitely until looted, world-local.
- Difficulty mode selection during character creation.

**Specs:** GDD §4.7, §5.3.

**Definition of done:**
- Player can place a Hearth; respawn on death goes to the Hearth.
- Home Potion teleports to Hearth.
- Placing a new Hearth deactivates the old one.
- Hardcore character dies → all inventory in a container at death spot → container can be looted to recover items.
- Character creation offers Standard vs Hardcore choice.

---

## Phase 12 — Home World Main Boss

**Prerequisites:** Phase 7 (combat), Phase 9 (enemies), Phase 2 (world with boss lair reservation).
**Parallelisable with:** Phase 13 (portal system, sans boss integration).

The first main boss.

**Features:**
- Boss entity base class (extends enemy with phases, larger HP, unique behaviour scripts).
- Boss AI baseline (phase-triggered behaviour changes).
- Wound-Hollow implementation (per boss-content-spec §3.1).
- Boss lair prefab (biome-adaptive — 3 variants).
- Boss lair placement via world-gen reservation (hidden location, no map marker).
- Signature mechanic: Root Emergence (telegraphed root pattern from ground).
- Boss death event.
- Loot table with guaranteed Wound-Hollow trophy (Legendary) + one guaranteed crafting recipe fragment.

**Specs:** boss-content-spec §3.1, world-generation-spec §6 Phase 4, loot-table-spec §8.

**Definition of done:**
- Wound-Hollow spawns in its reserved lair location.
- Boss executes its full attack pool.
- Root Emergence signature triggers at appropriate HP thresholds.
- Player can defeat the boss.
- On death, trophy weapon and recipe fragment drop as guaranteed drops.

---

## Phase 13 — Portal System

**Prerequisites:** Phase 12 (main boss triggers side anchor spawn).
**Parallelisable with:** Phase 14 partially — portal world content can start in parallel.

The portal anchors, side anchor mechanic, mini bosses.

**Features:**
- Portal anchor entity (placeable furniture).
- Portal activation flow (walking over unclaimed anchor → activation).
- Primary anchor pickup after main boss killed (drops at boss lair).
- Two side anchor spawns after main boss death (at portal candidate slots from world gen).
- Mini boss entity: Bramble Warden (per boss-content-spec §4.1).
- Mini boss entity: Stonewretch (per boss-content-spec §4.1).
- Anchor extraction: walk over to pick up after guardian defeated.
- Anchor placement in home base (or anywhere in home world).
- Anchor cannot be destroyed (only picked up and re-placed).

**Specs:** GDD §4. boss-content-spec §4.1.

**Definition of done:**
- Main boss killed → primary anchor available at lair.
- Two additional anchors spawn at random underground/deep/void locations with mini boss guardians.
- Mini bosses can be defeated.
- Anchors can be picked up (after guardian defeated) and re-placed elsewhere in the home world.

---

## Phase 14 — First Portal World

**Prerequisites:** Phase 13 (portals), Phase 2 (world generation).
**Parallelisable with:** portal content can be authored while other phases run.

Enter the Scorched portal world.

**Features:**
- Portal transition (fade + world switch).
- Multi-world save shard support: each active world is a separate shard on disk.
- Scorched portal world generation config (JSON): biome mix (Ashen Plains, Ember Ridges), layer overrides, hazards.
- Scorched-specific biomes and tile palette.
- Scorched enemies (5–8 unique enemy species for MVP).
- Portal-world exclusive material (Cindercore).
- Charred Sentinel main boss implementation (per boss-content-spec §3.2).
- Charred Sentinel signature: Heat Corridors.
- Loot table with Cindercore Blade (Legendary) trophy.
- Return travel: walk through portal on the far side, or use Home Potion, or die and respawn at Hearth.

**Specs:** GDD §4, biome-content-spec §7.1, boss-content-spec §3.2.

**Definition of done:**
- Player can enter Scorched via a placed anchor.
- Scorched world generates and loads (deterministic per seed).
- Scorched-specific biomes render with correct palettes.
- Scorched enemies spawn correctly.
- Cindercore drops from Scorched-specific sources.
- Charred Sentinel spawns, executes moveset including Heat Corridors, drops trophy.
- Player can return home via any of the three return methods.

---

## Phase 15 — Event & Area Bosses

**Prerequisites:** Phase 9 (enemy framework).
**Parallelisable with:** Phase 16, 17.

Extra optional content that fleshes out the world.

**Features:**
- Event system: periodic random events with configurable rarity.
- Event boss framework (spawns during event, despawns on event end or death).
- MVP event bosses: Blood Moon Hunter, Meteor Herald, Ancient Sentinel (per boss-content-spec §6.1).
- Area boss framework (biome/structure-tied, respawn 3–5 in-game days after death).
- Home world area bosses (per boss-content-spec §5.1 — Old Antler, Sand Reaver, Ice-Broken Elk, Cinder Lord, Nine-Voiced Chorus).

**Specs:** boss-content-spec §5, §6.

**Definition of done:**
- Random events trigger and spawn their event bosses.
- Event bosses drop unique loot.
- Area bosses respawn at correct cadence.
- All bosses' signature mechanics work.

**Content quantity for MVP:** 3 event bosses + 2–3 area bosses (one per shipping biome). Full roster deferred to post-MVP.

---

## Phase 16 — Save/Load Integration

**Prerequisites:** Phase 4 (character), Phase 11 (Hearth), all major systems producing save state.
**Parallelisable with:** any polish phase.

Complete the save/load loop end-to-end.

**Features:**
- Full save integration: character files + campaign manifest + world manifests + all chunks + all entity files.
- Full load flow: character → campaign → world → chunks around Hearth.
- Autosave every 5 minutes of active play (skipped in combat per save-format-spec §11).
- Crash recovery: `last-clean-shutdown` marker; on missing marker, offer recovery from autosave.
- Save modified chunks only (per world-data-model-spec §3 modified flag).
- Save conflict warning if character file is newer than server-held version.
- Integrity hash mismatch warning on load.

**Specs:** save-format-spec (all sections).

**Definition of done:**
- Full session saveable, quit, reopenable — state fully preserved.
- Autosave triggers on cadence.
- Killing the process mid-save leaves the previous save intact.
- Tampering with a save byte produces the integrity warning on reload.

---

## Phase 17 — Steam Deck & Polish

**Prerequisites:** everything else.
**Parallelisable with:** ongoing polish.

Ship-ready polish for the day-one platforms.

**Features:**
- Native Linux build via Godot export.
- Steam Deck resolution testing (1280 × 800).
- Steam Input controller mapping configured; ships as an official Steam layout.
- Suspend/resume testing on Steam Deck hardware.
- Steam Cloud integration.
- UI readability pass for 7-inch handheld screen (font sizes, touch targets, contrast).
- Placeholder SFX authored and wired in.
- Full bug-fix pass.
- Final performance profiling (target: stable 60 FPS on Steam Deck for typical play).

**Specs:** GDD §1.3, §5.7 controller mapping.

**Definition of done:**
- Game runs cleanly on Steam Deck hardware, controller-only playable.
- UI is legible without leaning close.
- Suspend/resume cycle preserves state.
- Steam Cloud saves work.
- No known crash bugs.

---

## Phase 18 — MVP Ship

**Prerequisites:** Phases 0–17 complete.

Release preparation.

**Features:**
- Steam page setup, screenshots, trailer.
- Store listing copy.
- Age rating, regional compliance.
- Launch trailer / marketing.
- Community setup (Discord, subreddit, etc. — optional).

**Definition of done:**
- Game is publicly available on Steam.

---

## Post-MVP Phases (outlined)

Detailed sequencing will be added when MVP ships. Rough dependency order:

| P# | Phase | Prereqs |
|----|-------|---------|
| P1 | Multiplayer implementation (multiplayer-spec) | MVP ship |
| P2 | Additional biomes & content depth | MVP ship |
| P3 | Additional portal worlds (Sunken, Clockwork, Verdant, Shattered) | P1 optional |
| P4 | Discovered NPC roster (Merchant, Mechanic, Sage, Medic, Warrior, Farmer, Wanderer) | MVP ship |
| P5 | Weather system + sky content (floating islands, sky biomes) | MVP ship |
| P6 | Music & full SFX pass | MVP ship |
| P7 | Perks / skill trees | MVP ship |
| P8 | Automation systems (tech) + Ritual systems (magic) | MVP ship |
| P9 | Reforge / upgrade system | MVP ship |
| P10 | Named enemies / rare mob variants | MVP ship |
| P11 | Surface feature pass for richer overhangs (world-gen-spec W14) | MVP ship |
| P12 | Ambient audio, biome music themes | P6 |
| P13 | Additional area/event bosses | MVP ship |
| P14 | Cloud save advanced features | P1 |
| P15 | Localisation | MVP ship |
| P16 | Modding tools (if pursued) | MVP ship |

---

## Notes on scope discipline

- **Content specs are aspirational.** Every phase specifies minimal MVP content. Do not implement full content-spec rosters unless the user has explicitly expanded scope.
- **Prefer working systems over feature-complete content.** A working combat loop with 5 enemies proves the game; a beautiful roster of 65 enemies with broken combat doesn't.
- **Every phase's DoD is a real gate.** Do not proceed to the next phase before the current phase passes its DoD. Debt from skipped DoDs compounds fast.
- **Timelines vary widely by developer.** The effort estimates (S/M/L) are relative sizes, not calendar predictions. A solo dev's actual pace depends heavily on hours per week and interruption frequency.
