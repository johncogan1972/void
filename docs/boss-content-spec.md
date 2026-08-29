# Boss Content — Feature Spec

**Version:** 0.1
**Status:** Draft (first-pass proposals)
**Companion to:** GDD §4, §6.2, combat-spec, biome-content-spec, world-generation-spec

---

## 1. Overview

Defines the concrete boss roster for MVP and post-MVP. Covers the four boss categories established in GDD §6.2 — main bosses (world guardians), mini bosses (portal side-anchor guardians), area bosses (biome/structure-tied), and event bosses (random world events).

This document is a first-pass content proposal. Boss names, movesets, and visual identities are placeholders unless noted.

## 2. Design Principles

Bosses should:

- **Be memorable in silhouette.** A player should be able to sketch the boss from memory after fighting it once.
- **Have a signature mechanic.** One thing they do that no other boss does.
- **Match their world's theme and difficulty tier.** A tier-1 boss in the home world shouldn't feel like a tier-3 boss in a portal world.
- **Encourage gear choices, not force them.** A fire-heavy boss makes fire resistance valuable but doesn't require it.
- **Reward multiple playstyles.** Melee, ranged, and magic all viable — no boss that hard-counters one playstyle.

## 3. Main Bosses (World Guardians)

One per world. Kills unlock the primary portal anchor and trigger side anchor spawn.

### 3.1 Home World — The Wound-Hollow

**Role:** the guardian of the home world. Fought after mid-tier gear is available.

**Silhouette:** a massive humanoid figure of woven bramble and stone, roughly three times player height. One arm ends in a splintered wooden claw; the other, in a mass of writhing roots. Its chest has a hollow "wound" that glows faintly.

**Lair:** a natural amphitheatre carved into a deep valley, ringed with ancient standing stones — biome-adaptive variants (Meadow / Frostreach / Ashwastes) so it fits wherever it spawns.

**Signature mechanic — Root Emergence:** periodically, roots erupt from the ground in a telegraphed pattern across the arena. Standing on a root emergence tile deals damage and briefly holds the player. Forces active movement and reading the arena.

**Attack pool:**
- **Sweep** — the wooden claw arcs across the ground in front, medium damage, wide but avoidable.
- **Root Lash** — three-hit combo of root tendrils striking at range.
- **Wound Pulse** — the chest hollow discharges a slow-expanding ring of magic damage. Get out of the ring or eat significant damage.
- **Root Emergence** (signature) — timed root pattern from the ground, escalating in density as HP drops.

**Damage types dealt:** Physical (primary), Magic (Wound Pulse).

**Resistances:** moderate physical resistance, low magic resistance. Encourages ranged / magic damage as viable.

**Death drop:** guaranteed primary portal anchor (per GDD §4). Loot table with guaranteed Wound-Hollow trophy (Legendary) + one guaranteed crafting recipe fragment + weighted roll of mid-tier materials.

**Tier:** home world main. Roughly equivalent to Terraria's Eye of Cthulhu / Eater of Worlds in progression pacing.

### 3.2 Scorched Portal World — The Charred Sentinel

**Role:** main boss of the MVP portal world.

**Silhouette:** a hulking armoured figure whose plate has been fused to its body by extreme heat. Cracks in the armour glow bright orange. Carries a massive greatsword permanently red-hot.

**Lair:** the ruined heart of a forge-temple, walls streaked with soot, lava streams running through the arena.

**Signature mechanic — Heat Corridors:** at set HP thresholds, the Sentinel plants its sword and releases a wave of superheated air along a corridor of the arena. The corridor stays hot for several seconds — walking through it deals fire DoT. Progressively more corridors spawn as fight goes on; late fight, only narrow safe zones remain.

**Attack pool:**
- **Greatsword Cleave** — slow but massive damage. Windup gives time to move.
- **Heat Blast** — cone of fire damage in front, medium range.
- **Slam** — jumps and lands hard, creating a shockwave.
- **Heat Corridors** (signature).

**Damage types dealt:** Physical (Cleave, Slam), Fire (Heat Blast, Corridors).

**Resistances:** high physical resistance, moderate fire resistance, low cold. Encourages cold-typed gear.

**Death drop:** guaranteed Cindercore Blade (Legendary), guaranteed recipe for next-tier forge station, weighted roll of Scorched-world materials.

**Tier:** tier 1 portal.

### 3.3 Post-MVP Main Bosses

Sketched briefly — each is one per remaining portal world.

- **Sunken portal — The Drowned Sovereign.** Coral-crowned figure emerging from a flooded throne. Signature: tidal surges wash across the arena, changing safe zones dynamically. Deals Cold + Physical.
- **Clockwork portal — The Machine King.** Massive automaton with independent limbs. Signature: dismembered limbs continue attacking as separate mini-encounters. Deals Physical + Magic.
- **Verdant portal — The Heartwood Warden.** A colossal tree-guardian. Signature: summons waves of smaller wooden constructs; environmental growth alters the arena. Deals Physical + Poison.
- **Shattered portal — The Rift Warden.** A being of jagged void-crystal. Signature: fights across floating platforms, teleports the player between shards. Deals Void + Magic.

## 4. Mini Bosses (Side Anchor Guardians)

Per GDD §4.2, two side anchors per world are guarded by mini bosses. These are meaningfully weaker than main bosses but stronger than any regular enemy.

Mini bosses should share a "family" identity with their world — they read as lieutenants of the main boss.

### 4.1 Home World Mini Bosses

**Bramble Warden.** A quadrupedal bramble creature, wolf-sized. Fast-moving.
- **Signature:** leaves brambles behind while moving that damage anyone stepping through.
- **Attack pool:** lunge, root lash (single-tendril mini version of main boss's Root Lash), thorn spit.
- **Damage:** physical.

**Stonewretch.** A hunched humanoid of moss-covered stone. Slow-moving but heavily armoured.
- **Signature:** briefly turns to solid stone (immune to damage) to heal a small amount.
- **Attack pool:** boulder throw, ground pound, charge.
- **Damage:** physical.

### 4.2 Scorched Portal Mini Bosses

**Ember Stalker.** A four-legged predator wreathed in fire. Aggressive.
- **Signature:** leaves burning ground trails that persist.
- **Attack pool:** pounce, flame breath, tail sweep.
- **Damage:** physical + fire.

**Ashen Warlock.** A wizened figure in charred robes, keeps distance.
- **Signature:** summons small ash phantoms as adds during the fight.
- **Attack pool:** fireball, cinder rain (AoE), teleport short distance.
- **Damage:** fire + magic.

### 4.3 Post-MVP Mini Bosses

One pair per post-MVP portal world, following the same "signature + attack pool" structure. Deferred to later content passes.

## 5. Area Bosses

Per GDD §6.2 and combat-spec §7.4, area bosses are biome/structure-tied optional content. They respawn every 3–5 in-game days (world-generation-spec §4.9).

Each biome (home world) gets one area boss. Each is designed as an optional challenge that rewards a specific playstyle or resource haul.

### 5.1 Home World Area Bosses

**Meadow — The Old Antler.** A stag the size of a barn, its antlers glowing softly. Ambient guardian of the biome.
- Encountered by triggering (harming a certain critter density, blowing an "Old Horn" item, TBD).
- Signature: gore charges that reshape terrain slightly (breaks trees along its charge line).
- Reward: hardwood in quantity, Old Antler trophy.

**Scrubland — The Sand Reaver.** A giant burrowing worm.
- Encountered when the player has been in one area of Scrubland for long enough; erupts from the ground.
- Signature: burrows and re-emerges, unpredictable positioning.
- Reward: reaver plating (mid-tier crafting material), scrubland-specific materials.

**Frostreach — Ice-Broken Elk.** An enormous elk with ice crystals fused into its antlers and hide.
- Encountered rarely wandering the biome.
- Signature: frost aura that slows nearby players and applies mild cold damage.
- Reward: elk crystal (cold-affinity crafting), quality hides.

**Ashwastes — The Cinder Lord.** A tall skeletal figure crowned in fire.
- Found only near active lava vents.
- Signature: ambient fire aura, plus can dive into lava and re-emerge elsewhere.
- Reward: cinder heart (fire-affinity crafting).

**Whisperbog — The Nine-Voiced Chorus.** A creature made of many smaller humanoid figures fused into one shifting mass.
- Encountered near mushroom rings.
- Signature: splits into smaller versions of itself at low HP.
- Reward: chorus essence (magic-affinity crafting), luminescent mushrooms in bulk.

### 5.2 Deep and Void Area Bosses

Deep-layer biomes and void biomes also get area bosses:

- **The Sunken Vaults — The Drowned Ferryman.** Skeletal figure in a broken boat, appears near underwater passages.
- **The Boneyard Strata — The Marrow Colossus.** A giant assembled from many smaller skeletons.
- **The Molten Rifts — Forge-Broken.** A construct that survived its creators; hostile automaton.
- **The Hollow Cathedral — The Silent Choir.** Rare void encounter.
- **The Void Sea — Depth-Called.** An entity summoned by the void sea's currents.

Each follows the same "signature mechanic + reward" design. Full stat/moveset design deferred.

## 6. Event Bosses

Per GDD §3.5 and world-generation-spec §4.9, event bosses spawn during random world events. They respawn implicitly — every occurrence of a triggering event spawns a fresh instance.

### 6.1 MVP Event Bosses

**Blood Moon Hunter.** During a Blood Moon night event (post-MVP weather-tied event, or on random nights), a large armoured beast prowls the home world's surface.
- Reward: blood-touched materials.

**Meteor Herald.** After a meteor shower event, a meteor-shard creature spawns at the impact site. Time-limited: the herald leaves after several minutes.
- Reward: meteor-forged crafting materials.

**Ancient Sentinel.** Rare event: a dormant construct activates somewhere in the world. Its location is broadcast via a UI notification.
- Reward: rare tech reagents.

### 6.2 Post-MVP Event Bosses

Additional world-event bosses as post-MVP events are designed:

- Storm-bound elemental (during storms).
- Frost giant (during blizzards, Frostreach only).
- Ember dragon (rare Ashwastes event).
- Fungal titan (Whisperbog event tied to biome saturation).

## 7. Boss Data Schema Reference

Bosses use the same enemy data structure as regular enemies (per combat-spec §11) with expanded fields:

```yaml
enemy_id: "wound_hollow"
display_name: "The Wound-Hollow"
tier: "main_boss"
world_type: "home"
max_hp: 3000  # illustrative
resistance_by_type:
  physical: 0.35
  fire: 0.10
  cold: 0.10
  poison: 0.20
  magic: 0.0
  void: 0.0
attacks:
  - attack_id: "sweep"
    windup_frames: 30
    active_frames: 8
    recovery_frames: 40
    damage_by_type: { physical: 40 }
    range: "cone_wide_short"
    telegraph: "arm_raise"
  - attack_id: "root_lash"
    # ...
  - attack_id: "wound_pulse"
    # ...
  - attack_id: "root_emergence"
    # signature — handled by boss-specific script
knockback_resistance: 0.1
loot_table_id: "wound_hollow_loot"
ai_profile_id: "main_boss_ai_home"
immunities:
  - "stagger"  # deprecated — stagger was removed
  - "freeze"   # main bosses immune to freeze
phase_thresholds:
  - hp_percent: 66
    behaviour_change: "escalate_root_emergence"
  - hp_percent: 33
    behaviour_change: "add_wound_pulse_frequency"
lair_prefab_variants:
  - "wound_hollow_lair_meadow"
  - "wound_hollow_lair_frostreach"
  - "wound_hollow_lair_ashwastes"
```

Boss-specific logic (signature mechanics, phase changes) lives in per-boss script files rather than pure data — bosses have unique behaviour that would be awkward to encode purely in JSON.

## 8. Boss Design Bandwidth

Boss design bandwidth is called out as a Medium risk in GDD §13. This spec proposes:

**MVP shipping list:**
- 2 main bosses (home + Scorched)
- 4 mini bosses (2 home + 2 Scorched)
- ~5 area bosses (one per home biome) — some can share behaviour patterns
- 3 event bosses
- **Total MVP boss count: 14.**

Achievable for a solo dev with disciplined reuse. Each main boss is a unique design; mini bosses share family patterns; area bosses can lean on shared "creature archetype" behaviours; event bosses are simpler encounter units.

**Post-MVP shipping additions:**
- 4 more main bosses (one per additional portal world)
- 8 more mini bosses (2 per additional portal world)
- ~10 more area bosses (deep and void layer + portal worlds)
- More event bosses as events are added
- **Full game boss count: ~40+.**

## 9. Open Questions

- **Signature mechanic complexity.** Some proposed signatures (Heat Corridors, boss splitting into smaller versions) require nontrivial implementation. Are these too ambitious for MVP, or fine given they define what makes the boss memorable?
- **Boss soul-item drops.** Should each main boss drop a unique "soul" or "essence" that unlocks a special crafting recipe or ability? Post-MVP consideration.
- **Boss lair prefab count.** 3 biome-adaptive variants per main boss proposed. Confirm this is the right count, or bump to 5 for more variety per world seed.
- **Music per boss.** All bosses should get unique music at launch. Deferred to when music work begins.
- **Boss respawn for area/event.** Area bosses respawn (3–5 days). Main and mini bosses do not (per GDD §4.9). Event bosses respawn on event occurrence. Sanity check.
- **Adds during boss fights.** Some proposed bosses summon smaller enemies (Ashen Warlock's phantoms, Marrow Colossus split). Cap on total on-screen enemies during a boss fight for readability?
- **Boss telegraphs.** Every attack should have a clear telegraph (visual + audio cue). Consistency of telegraph style across bosses matters for game feel — is there a house style to establish?
- **Difficulty scaling in co-op.** Should boss HP or damage scale with party size? Some games do, some don't. Recommend: HP scales (linear-ish), damage does not.
- **Boss trophy meaningfulness.** Every main boss should drop a Legendary trophy weapon or item that fills a real gear slot. Currently proposed. Confirm this design principle.
