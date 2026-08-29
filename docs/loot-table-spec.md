# Loot Tables & Item Generation — Feature Spec

**Version:** 0.4
**Status:** Draft
**Companion to:** GDD §7.5, combat-spec §7.4 (enemy death), world-data-model-spec §7 (registries)

---

## 1. Overview

Defines the loot table system: how sources (enemies, chests, containers, breakables, mined ores) determine what items drop, at what rarity, and with what rolled stats. Loot tables tie together combat (enemy death drops), world generation (chest placement, boss lair markers), and crafting (recipes producing rolled items).

Loot tables are pure data — JSON entries in a registry — so pacing and reward tuning can shift without code changes.

## 2. Requirements Recap

From GDD §7.5:

- **Four rarity tiers:** Common, Uncommon, Rare, Legendary.
- **Per-tier stat ranges** on every item — actual numbers rolled at generation time.
- **Legendaries** roll at (or near) maximum stat values and receive a randomly generated special name.
- **Legendary drops are very rare** — tuned via loot table.
- **Loot tables are JSON** — editable without code changes.

## 3. Item Generation Flow

When a source needs to drop loot:

1. **Look up loot table** by `loot_table_id` in the source's data.
2. **Process guaranteed drops.** Every entry in `guaranteed_drops` produces its item without rolling.
3. **Process weighted entries.** For each entry in `entries`:
    - Roll `drop_chance`. If it fails, skip.
    - Check `conditions` (first-kill flags, etc.). If failing, skip.
    - Roll rarity from `rarity_weights`.
    - Roll count from `count_range`.
    - Emit `count` items of the entry's `item_id` at the rolled rarity.
4. **For each emitted item,** instantiate a rolled instance:
    - Look up item definition in `ItemRegistry`.
    - Extract stat ranges for the rolled rarity.
    - Roll each stat.
    - If Legendary, generate a special name.
    - Return the fully-rolled item instance.
5. **Spawn drops** at the source's position (enemy corpse, chest contents, etc.).

## 4. Loot Table Schema

```yaml
loot_table_id: string          # unique registry ID (e.g. "goblin_scout_loot")
description: string            # for editor / debug (optional)

# Guaranteed drops — always fire, no roll
guaranteed_drops:
  - item_id: string
    rarity: string             # fixed rarity (skips rarity roll)
    count: int
    name_override: string?     # optional fixed name (skips Legendary name generation)

# Weighted entries — each rolled independently
entries:
  - item_id: string
    drop_chance: float         # 0.0 – 1.0
    rarity_weights:            # relative weights (do not need to sum to 1)
      common: float
      uncommon: float
      rare: float
      legendary: float
    count_range: [int, int]    # inclusive min–max
    conditions:                # optional gates
      first_kill_only: bool?   # only fires on first kill of the source (boss trophies, etc.)
      requires_flag: string?   # world/campaign flag that must be set
      requires_no_flag: string?# world/campaign flag that must NOT be set
```

**Design choice — per-entry rolls (not weighted pool):** every entry rolls independently. This matches Terraria's mental model, gives designers direct control over per-item drop rates, and makes tuning transparent ("the goblin has a 5% chance to drop a torch"). The alternative — pooling all entries and rolling N picks — is more predictable but harder to reason about.

## 5. Rarity Roll

Given `rarity_weights` for an entry:

1. Sum all four weights.
2. Roll a random value in `[0, sum)`.
3. Walk the weights in order (common → uncommon → rare → legendary), subtracting each from the roll until it goes below zero. That determines the tier.

**Zero weights are skipped.** An entry can restrict itself to just Common by giving only `common` a weight.

**Legendary weights are tiny.** Typical values: 0.001 to 0.01 (0.1% to 1%). Boss loot tables may go higher (e.g. 0.05 for a main boss's Legendary drop).

**Example:**

```yaml
rarity_weights:
  common: 0.60       # 60% relative weight
  uncommon: 0.30
  rare: 0.09
  legendary: 0.01    # 1%
```

Sum = 1.00, so these read directly as percentages. Doesn't have to sum to 1 — the math normalises.

## 6. Stat Rolling

Once tier is determined, look up the item's tier definition:

```json
"longsword": {
  "common": {
    "damage_type": ["physical"],
    "attack_rate": [0.5, 1.0],
    "range": [2, 2],
    "damage": {
      "physical": [1, 5]
    }
  },
  "uncommon": {
    "attack_rate": [0.7, 1.2],
    "damage": { "physical": [3, 8] }
  },
  "rare":     { "..." },
  "legendary":{ "..." }
}
```

For each stat that is a `[min, max]` range: roll uniformly in `[min, max]` and assign.

**Legendary variance:** stats roll from `[max * 0.95, max]` for each field — 5% variance keeps each Legendary a *little* unique without diluting the "trophy item" feel. A Legendary is always among the best-in-slot for its item type.

**Fixed values (no range):** if a stat is a scalar rather than a `[min, max]` pair, use it as-is.

**Missing stats:** stats not listed in the tier's definition inherit from the item's base (default) values.

## 7. Legendary Name Generation

Legendary items receive a randomly generated name in the form:

`[Prefix] [Base Name] [Suffix]`

Each of Prefix and Suffix is independently optional — a Legendary might get just a prefix ("Ancient Longsword"), just a suffix ("Longsword of Vecna"), or both ("Ancient Longsword of the Void"). At least one must appear.

**Component pools:**

- **Prefix pool** — evocative adjectives: *Ancient, Forgotten, Blessed, Shattered, Radiant, Cursed, Whispered, Grand, Wretched* (extend freely).
- **Suffix pool** — thematic name references: *Vecna, the Shattered Star, the Void, the First Dawn, Silence, the Hollow King, the Depths, Ash and Ember* (extend freely).
- Pools can be **global** (used by any item type) or **type-restricted** (a suffix pool only for wands, for example — post-MVP flexibility).

**Non-deterministic generation:** the RNG stream used for name generation is the same runtime PRNG driving the loot roll itself. Two Legendaries of the same base type generated in the same playthrough will have different names; running the same playthrough again on the same world seed will produce different names too.

**Fixed names:** `name_override` in a `guaranteed_drops` entry skips generation — used for iconic items like main boss trophies ("Guardian's Fang") that should never be randomly named.

## 8. Guaranteed Drops

Bypass the rarity / count / condition rolls entirely. Used for:

- **Boss trophies** — iconic items that always drop from a specific boss.
- **Story items** — quest rewards, progression unlocks.
- **Chest fixed contents** — a "boss chest" always contains the boss room key, for example.

```yaml
guaranteed_drops:
  - item_id: "main_boss_trophy"
    rarity: "legendary"
    count: 1
    name_override: "Guardian's Fang"
```

Guaranteed drops still emit through the normal instantiation flow — stats still roll from the specified rarity's range. Only the drop, rarity, count, and name are pre-decided.

## 9. First-Kill Bonuses

Some items only drop the first time a source is killed (a boss's crafting recipe, a unique lore fragment). Tracked in the campaign manifest per source ID.

```yaml
- item_id: "main_boss_recipe"
  drop_chance: 1.0
  rarity_weights: { legendary: 1.0 }
  count_range: [1, 1]
  conditions:
    first_kill_only: true
```

On resolving a drop with `first_kill_only: true`:
- Check the campaign manifest for `first_killed_sources` (list of source IDs).
- If the source ID is present, skip this entry.
- If not, emit the drop and add the source ID to the list.

For main bosses this doubles as a progression signal — the recipe drop is the "you've completed this milestone" reward.

## 10. Loot Table Sources

Anything that can drop loot references a `loot_table_id`:

- **Enemies** — each enemy type has one loot table (`goblin_scout_loot`, `void_wraith_loot`, etc.).
- **Bosses** — each boss has a loot table with heavy guaranteed drops (trophy + recipe + rare materials) plus a modest weighted pool.
- **Chests** — chests come in tiers (wood, iron, gold, arcane, void). Each tier has a loot table appropriate to its tier's material rarity.
- **Breakables** — pots, crates, urns. Small loot tables usually pointing at low-value consumables and occasional surprises.
- **Ores** — each ore block has a loot table for what mining it produces (usually just the ore itself, but higher-tier ores could roll additional bonus materials).
- **Structure containers** — chests placed inside prefabs (§10 of world-gen spec) can override the standard chest table with a prefab-specific one, via a marker in the prefab data.

## 11. Chest Loot: Rolled Per Open (Non-Deterministic)

Design decision: **chest contents are rolled at the moment the chest is first opened, using a standard runtime PRNG.**

- Roll uses non-deterministic RNG — different playthroughs on the same world seed will get different chest contents.
- Once opened, contents are stored with the chest entity so re-opening the same chest in the same save always shows the same items (state is now fixed).
- Chest storage carries: `chest_spawn_uuid`, `loot_table_id`, `is_opened` flag, and (once opened) the rolled contents.
- Multiplayer: server rolls on first open and broadcasts contents to all clients.

Consequences:
- **Save-scumming is possible.** A player who reloads a save from before opening a chest and re-opens will get a fresh roll. Standard genre behaviour; not defended against.
- **World layout stays deterministic per seed** — biomes, terrain, structure placement, chest *locations*, ore veins, portal candidate positions are all reproducible from the world seed. Only the *loot inside* varies per play.
- Chest content doesn't count against world-gen time (rolls happen on demand at play).

## 12. Enemy Loot: Rolled Per Kill (Non-Deterministic)

Enemy loot rolls at the moment of death, using a standard runtime PRNG.

- Different playthroughs on the same world seed will get different enemy drops.
- Once dropped, items are world entities — reloading a save preserves the drops that already exist in the world.
- Multiplayer: server rolls on kill and broadcasts to clients.

Save-scumming applies here too — a player who reloads from before a kill can re-kill the enemy and receive fresh loot. Standard behaviour, not defended against.

## 13. Crafting and Rolled Stats

Crafting recipes produce rolled items just like drops. Recipe definition specifies:

- Target item ID
- Target rarity (usually Common for basic recipes; higher-tier recipes can target higher rarity)
- Any locked stat overrides (if the recipe should produce a specific stat value rather than rolling)

Consequences:
- A player crafting a "Longsword" gets a Common Longsword with rolled stats — two crafted longswords are not identical.
- Higher-tier stations can craft at higher target rarities (e.g. an "Arcane Forge" can craft Rare weapons directly).
- Legendary items **cannot be crafted** for MVP — they only drop from loot tables. This preserves their scarcity and trophy status.

Post-MVP: a "reforge" or "upgrade" system that consumes resources to re-roll stats on an existing item could be added, if desired.

## 14. Data Registry

Loot tables live in `LootTableRegistry`, populated from JSON at startup. Each loot table is looked up by `loot_table_id`.

Rolled item instances are stored per-instance — they carry their own rolled stats, not a reference to a template. This is essential because two dropped longswords may have very different numbers.

**Item instance model:**

```yaml
instance_uuid: UUID          # unique per instance
base_item_id: string         # references ItemRegistry
rarity: string
rolled_stats:                # concrete values (not ranges)
  attack_rate: float
  range: int
  damage:
    physical: float
    fire: float
    ...
generated_name: string?      # populated for Legendaries
```

Instance data is what appears in inventory, chests, and the world.

## 15. Debug & Preview Tools

Two dev tools to build:

**Loot preview tool:** given a `loot_table_id` and a seed, simulate N rolls (e.g. 10,000) and report:
- Rarity distribution observed vs expected
- Item frequency observed vs expected
- Sample of rolled instances
- Sample of generated Legendary names

Used for tuning drop rates without running the game.

**In-game drop log:** when a debug flag is set, drops are logged to the console:
`[LOOT] Goblin dropped: Iron Dagger (Uncommon) — damage: [4.2 physical]`

## 16. Testing

- **Determinism tests** — the loot roll RNG is a standard PRNG, but tests can seed it explicitly for reproducibility. Given a fixed seed and loot table, output is reproducible.
- **Distribution tests** — run 100,000 rolls per loot table (with a fixed test seed) and assert observed rarity distribution matches configured weights within tolerance (typically ±2%).
- **Guaranteed drop tests** — guaranteed items always drop, at the specified rarity, with the specified count.
- **Condition tests** — `first_kill_only` fires exactly once per source; `requires_flag` respects the flag state.
- **Legendary name tests** — no empty names, all components drawn from configured pools, at least one of prefix/suffix present.
- **Stat range tests** — rolled stats fall within declared `[min, max]` ranges (or `[0.95*max, max]` for Legendaries).

## 17. Resolved Design Decisions

All original open questions locked in v0.4:

**MVP behaviour:**

1. **Loot magnetism:** none. Items stay where they drop. Player must walk over them to pick up.
2. **Drop despawn:** 10 minutes for regular ground items. **Guaranteed drops and Legendary items never despawn** — trophy items are never lost by walking away.
3. **Item stack merging on drop:** stackable items (ammo, materials, consumables) merge into a single stack when dropped together from the same source. Non-stackable items (weapons, armour, jewellery) always spawn as separate ground entities.
4. **Loot table inheritance / composition:** supported via an `includes` field. A loot table can pull entries from other tables and add its own on top.

    ```yaml
    loot_table_id: "wood_chest_loot"
    includes:
      - "common_consumables"
      - "basic_materials"
    entries:
      - item_id: "small_potion"
        drop_chance: 0.5
        # ...
    ```

    Included tables' entries are evaluated as if they were declared locally. Cycles (A includes B includes A) are detected at load time and rejected.

**Post-MVP features (not in scope for MVP):**

- **Named enemies (rare mob variants).** Elite-tier enemy spawns with names, boosted stats, and unique loot tables (e.g. "Grath the Broken" spawning in place of a common goblin). Adds encounter variety.
- **Reforge / upgrade system.** Consuming resources to re-roll stats on an existing item. Needs careful balance thinking to avoid trivialising Legendary rarity.
- **Global loot buffs.** Consumables or events granting temporary drop-rate bonuses.
