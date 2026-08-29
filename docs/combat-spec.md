# Combat & Damage Resolution — Feature Spec

**Version:** 0.3
**Status:** Draft
**Companion to:** GDD §2.3, §5.6, §5.7, §7.4

---

## 1. Overview

Defines the combat flow — from attack initiation through damage resolution, resistance application, status effect handling, hit reactions, and death — for both players and enemies. Assumes the equipment, weapon, and armour data models from the GDD are the source of truth for stats; this document is about how those stats *interact* at runtime.

## 2. Design Principles (Recap)

From GDD §2.3 and §7.4:

- **Weightier than Terraria, lighter than Souls-likes.** Punchy hits with impact; never sluggish.
- **Weapon class defines feel** — light / medium / heavy melee, ranged, magic.
- **Six damage types** — Physical, Fire, Cold, Poison, Magic, Void.
- **Armour = resistances**, not flat defence.
- **75% per-type resistance cap** (raised to 80% by shields for the shield's damage types, but not against splash or void).
- **No dodge, no block, no combos, no stamina.** Only magic consumes a resource (mana).
- **Hit-stop, knockback, and stagger** are universal reactions.
- **Magic is item-based**, not a skill system — spells are properties of equipped gear.

## 3. Attack Lifecycle

Every attack goes through the same lifecycle, whether player-initiated or enemy-initiated.

### 3.1 Phases

| Phase       | What happens |
|-------------|--------------|
| **Wind-up** | Attack committed. Animation begins. Attacker is briefly locked into the swing/cast. Cannot cancel to a different attack. |
| **Active**  | Damage-dealing frames. Melee: hitbox is live. Projectile: projectile spawned. Magic: cast resolves or channel begins. |
| **Recovery** | Post-attack pause. Attacker can move but cannot initiate a new attack until recovery ends. |

**Total attack duration** = wind-up + active + recovery, derived from the weapon's `attack_rate` (attacks per second). A weapon with `attack_rate = 2.0` completes one full cycle in 0.5 seconds; per-phase timing is data-driven per weapon class (light melee has short wind-up; heavy melee has long wind-up and heavy recovery).

### 3.2 Attack modes

Three flavours of attack behaviour, flagged in weapon data:

- **Instant strike** — single wind-up → active → recovery cycle per click. Covers most melee, bows, single-shot magic. Default mode.
- **Auto-repeat** — while the input is held, cycles repeat at `attack_rate`. Covers firearms, auto-cast wands.
- **Channelled** — wind-up begins; on entering active, damage / effect ticks each frame at `attack_rate` while input is held. Mana (if magic) drains per tick. Release ends the channel. Post-MVP for MVP scope; framework present but implemented for one MVP magic staff to prove it.

### 3.3 Initiation checks

Before wind-up begins:

1. Attacker is alive and not stunned/staggered.
2. Weapon is equipped (right hand for L-click, left hand for R-click, per GDD §5.7).
3. If magic weapon: current mana ≥ `mana_cost`. If not, attack rejected (visual + audio feedback).
4. If projectile weapon: required ammo present (if `ammo_type` specified). If not, attack rejected.
5. Not currently in tool mode (see GDD §5.7 mouse controls).

If all checks pass:
- Deduct mana / ammo immediately (at start of wind-up, not on active frame).
- Enter wind-up.

## 4. Damage Resolution

The core system. Runs when an attack's active phase makes contact with a target.

### 4.1 Attack event

Every hit produces an **AttackEvent** with these fields:

```
AttackEvent {
    source_id           : EntityID         # who is attacking
    source_position     : Vector2
    target_id           : EntityID         # who is being hit
    hit_position        : Vector2          # world-space impact point
    damage_by_type      : Map<DamageType, float>   # per-type raw damage
    is_splash_damage    : bool             # from a splash-property projectile's area effect
    knockback_strength  : float            # weapon's knockback stat
    knockback_direction : Vector2          # normalised
    hit_stop_frames     : int              # weapon's hit_stop stat
    status_effects      : List<StatusEffectApplication>  # any effects to try applying
    weapon_class        : string           # for animation/feel classification
}
```

### 4.2 Resolution flow

For each AttackEvent, the target processes it as follows:

1. **Sum raw damage** across all types in `damage_by_type`.
2. **For each damage type**, compute effective resistance:
    - **Base resistance** = sum of that type's resistance from all equipped armour pieces (§5.7 armour).
    - **Shield contribution** = if the target holds a shield and the type is not Void, add the shield's resistance for that type.
    - **Splash-damage exclusion** — if `is_splash_damage`, ignore the shield contribution entirely (shields don't help against splash).
    - **Apply cap** — cap the effective resistance at 75%, or 80% for damage types the shield covers (except Void and splash, per above).
3. **Compute effective damage per type:** `raw_damage_type * (1 - effective_resistance_type)`.
4. **Sum across types** for `total_damage`.
5. **Apply damage** to target's current HP.
6. **Apply hit reactions** — hit-stop and knockback (§6).
7. **Attempt status effects** (§5).
8. **Check for death** and process consequences (§7).

### 4.3 Resistance stacking rules

- Multiple armour pieces resisting the same damage type: values **sum**.
- Shield adds on top of armour total.
- **Cap is applied last**, on the summed value.
- Example: three armour pieces each giving 30% fire resistance = 90% pre-cap, capped to 75%. With a shield contributing 20% fire, cap becomes 80%, so 90 + 20 = 110% → capped to 80%.

### 4.4 Void damage specifics

Void is a special damage type that bypasses most defences.

- **Shield contribution ignored.** Even if a shield lists void resistance in its data, that contribution is skipped when resolving Void damage.
- **Only explicit Void resistance protects.** Void damage is reduced only by sources that specifically grant Void resistance:
  - Armour pieces with a `resistance.void` stat.
  - Buffs from potions, magic gear, or consumables that grant Void resistance.
  - Character-permanent Void resistance from progression items (post-MVP).
- Ordinary armour resistances (Physical, Fire, Cold, Poison, Magic) provide **no protection** against Void damage. A player in top-tier fire-resistant plate is just as vulnerable to Void as one in cloth robes if neither has explicit Void resistance.
- The 75% cap applies to summed Void resistance from armour + buffs. Shields cannot raise it.

### 4.4a Environmental Void damage

Two environmental sources of Void damage exist in the world (see world-generation-spec §4.4):

- **Void aura zones.** Passive Void damage-over-time applied to any character within a void aura tile region. Base damage rate is data-driven per aura zone. Damage is reduced by total Void resistance up to the 75% cap.
- **Liquid Void** (rivers and pools in the Void layer). Contact-tick Void damage while in contact with the fluid. Higher per-tick damage than the void aura, and higher than lava contact damage. Same resistance model — reduced by total Void resistance up to the 75% cap.

Both sources produce AttackEvents per tick against the affected character. They pass through §4.2 resolution normally, respecting Void's bypass-shields rule.

**Design intent:** Void gear becomes genuinely necessary for endgame Void-layer exploration. Players can't just tank into Void territory with high-tier fire armour — they must specifically hunt Void resistance items.

### 4.5 Splash damage specifics

- Splash damage is dealt by projectiles with the `splash` property, in an area around impact.
- **One AttackEvent per target in the splash radius.** The damage does not stack with a "direct hit" on the same target — a rocket with splash generates one damage instance per entity in its area of effect, no more.
- All AttackEvents from a splash impact carry `is_splash_damage = true`.
- Shield resistance and cap boost do **not** apply to splash-flagged events (per GDD §7.4). Armour resistances still apply.
- Practical effect: splash weapons trade the per-target damage of a focused hit for the ability to reliably ignore shielded builds and hit multiple targets at once.

## 5. Status Effects

### 5.1 Model

Every status effect is defined in data with these fields:

```
StatusEffect {
    effect_id           : string
    display_name        : string
    tick_interval       : float       # seconds between ticks
    default_duration    : float       # seconds
    per_tick_effect     : Effect      # what happens each tick (damage, movement penalty, etc.)
    stacking_rule       : string      # "refresh", "stack_intensity", "stack_duration", "no_stack"
    icon                : string      # UI ref
    tint                : Color?      # visual tint on affected entity
}
```

### 5.2 Application

When an AttackEvent carries a `StatusEffectApplication`, the target checks:

1. **Immunity** — does the target have an immunity to this effect (data-flagged per entity type)? If yes, skip.
2. **Resistance** — some entities may resist certain statuses (e.g. fire-typed enemies resist burn). If a resistance check exists and fails the roll, skip.
3. **Apply according to stacking rule:**
    - `refresh` — if the effect is already active, reset its remaining duration to `default_duration`.
    - `stack_intensity` — increase intensity by one step (e.g. Poison I → Poison II) up to a cap. Damage-per-tick scales with intensity.
    - `stack_duration` — add `default_duration` to the current remaining duration.
    - `no_stack` — if already active, skip.

### 5.3 Effect processing

- Each frame, decrement remaining duration.
- When `tick_interval` elapses, apply `per_tick_effect` (usually a damage-per-tick with a damage type — burn ticks Fire damage, poison ticks Poison damage). Damage-per-tick goes through the same resolution as an AttackEvent (§4), including resistances.
- When duration reaches zero, effect is removed.

### 5.4 MVP status effects

- **Burn** — Fire DoT, `stack_intensity`, applied by lava contact, fire weapons, some projectiles.
- **Poison** — Poison DoT, `stack_intensity`, applied by poison water, poison gas, poison weapons.
- **Freeze** — movement speed reduction (percentage-based), `refresh`, applied by cold projectiles.
- **Shock** — brief attack-rate reduction, `refresh`, applied by specific electric weapons (post-MVP).
- **Void-burn** — Void DoT, `no_stack`, applied by void aura and void liquid.

## 6. Hit Reactions

Two universal reactions apply on every direct hit.

### 6.1 Hit-stop

- On landing a hit, both source and target briefly freeze for `hit_stop_frames`.
- Purpose: gives every hit visual weight; sells the impact.
- Duration is data-driven per weapon (heavy weapons have longer hit-stop, up to ~5 frames at 60fps; light weapons have 1–2 frames).
- During hit-stop, animations pause but audio and particles continue.
- Hit-stop does **not** trigger for status-effect ticks or environmental damage ticks — only for direct AttackEvent hits.

### 6.2 Knockback

- The target receives an impulse in `knockback_direction`, magnitude `knockback_strength`.
- Target movement during knockback: physics-driven, subject to friction and terrain collision.
- Knockback is not a stun — target can input actions during it, but movement is dominated by the impulse until it decays.
- Some enemies (bosses, heavy elites) have knockback resistance flagged in their data, scaling the impulse down.

## 7. HP, Healing, and Death

### 7.1 Player HP

- **Base HP:** 100 for a new character.
- **Expansion cap:** 400 (4× the base). Expansion mechanism itself is TBD — heart crystals are not the design; a different progression path for HP growth will be defined later.
- **Regeneration:** 1 HP per second when out of combat. "Out of combat" = 5 seconds since last damage taken. Modifiable upward by gear and consumables.
- **Healing sources:** potions, food, magic gear that heals, restorative buffs.

### 7.2 Player death

Per GDD §5.3:

- **Standard mode:** respawn at Hearth. Full HP and mana. Items retained.
- **Hardcore mode:** respawn at Hearth. Full HP and mana. All carried items are deposited in a death container at the location of death. Items can be recovered (see GDD §5.3 death container rules).

Both modes: respawn happens after a brief death animation and fade-out. All active status effects are cleared on respawn.

### 7.3 Enemy HP

- Enemies have a single HP value, defined per enemy in data.
- No status effects on enemy HP recovery in MVP — no healing enemies for MVP.
- Bosses may have HP phases: on hitting certain thresholds, the boss shifts behaviour and may trigger scripted events.

### 7.4 Enemy death

On enemy death:
1. Death animation plays.
2. Any active status effects (visual only at this point) are removed.
3. Loot table for this enemy is rolled (see forthcoming loot-table-spec).
4. Rolled items spawn at the enemy's position with a brief pickup delay.
5. Kill event is fired for progression tracking (main boss defeat, quest triggers, etc.).
6. Enemy entity is removed from the world.

## 8. Enemy Combat Interaction

Enemies use the same AttackEvent system for outbound damage. Some notes specific to their side:

- **Enemy resistances** — enemies can define resistances per damage type, using the same model as players. Cap is 75% (no shields on enemies for MVP).
- **Enemy attacks** are defined in data with the same fields as player weapons (damage_by_type, knockback, hit_stop_frames, etc.).
- **Enemy AI** is out of scope for this spec — a separate AI/behaviour spec will define aggro, pathing, ability selection. This spec covers only how enemies deal and take damage.
- **Aggro / threat** — for MVP, simple proximity-based. Enemies within their aggro radius engage the nearest player; enemies that took damage engage the source. Detailed threat mechanics post-MVP.

## 9. Mana Consumption

Recap and detail on GDD §5.6:

- Magic weapons declare `mana_cost` per use in item data.
- On attack initiation (start of wind-up), if `current_mana >= mana_cost`, mana is deducted and the attack proceeds.
- If `current_mana < mana_cost`, the attack is rejected — visual (a small "not enough mana" flash) and audio feedback.
- For channelled magic: mana drains per tick during the active phase. If mana runs out mid-channel, the channel ends (recovery phase begins).
- Regen: 2 mana per second, unmodifiable in base; modifiable by gear and buffs. Regeneration is continuous — no combat gate.

## 10. Combat Control Recap

Pulled from GDD §5.7 for one-stop reference:

- **L-click** — right-hand weapon attack.
- **R-click** — left-hand weapon attack (or nothing if left hand holds a shield or is empty).
- **Two-handed weapon** — L-click swings; R-click unused.
- **Tool mode (F)** — R-click uses active tool; L-click swings right-hand weapon and returns to weapon mode.
- **E** — interact with world (open containers, talk to NPCs, use portals). Not used in combat.

## 11. Combat Data References

The following data structures (defined elsewhere) feed the combat resolution:

- **Weapon data** — GDD §7.4 combat item data model. Fields: attack_rate, range, mana_cost, ammo_type, damage_by_type, projectile_ref (for projectile weapons), knockback_strength, hit_stop_frames, weapon_class, status_effects.
- **Armour data** — GDD §7.4. Fields per piece: resistance_by_type, movement_modifier, mana_regen_modifier, other secondary effects.
- **Shield data** — GDD §7.4. Fields: resistance_by_type, cap_boost (fixed 5%).
- **Enemy data** — separate enemy design spec. Fields: max_hp, resistance_by_type, attacks (list of attack definitions), knockback_resistance, loot_table_id, ai_profile_id, immunities.
- **Status effect data** — this doc §5.1.

## 12. Testing & Validation

- **Damage math test suite** — canonical AttackEvents against canonical targets with known resistances; assert damage output matches formulas.
- **Resistance cap test** — verify caps are respected (75% baseline, 80% with shield, void/splash bypass rules).
- **Status effect stack tests** — for each stacking rule, verify behaviour matches spec.
- **Hardcore death test** — dying in Hardcore mode drops items into a container at the correct location, container is lootable, character respawns with empty inventory.
- **Rejection tests** — no-mana casts are rejected without deducting mana; no-ammo shots are rejected without consuming ammo.
- **Determinism note** — combat resolution is deterministic given identical inputs (RNG for status resistance rolls must be a seeded stream if replays or netcode need determinism). For MVP single-player, deterministic combat is not required, but for multiplayer post-MVP it will matter.

## 13. Resolved Design Decisions

All original combat open questions locked in v0.3:

1. **Player base HP:** 100 at character creation.
2. **HP expansion cap:** 400. Expansion mechanism to be designed (not heart crystals).
3. **HP regen rate:** 1 HP per second out of combat, modifiable upward by gear and consumables.
4. **Out-of-combat trigger:** 5 seconds since last damage taken.
5. **Hit-stop on player's own attacks:** yes for melee (sells impact), no for ranged or magic (would break aim).
6. **Knockback resistance model:** flat multiplier per enemy in enemy data (0.0 = immune, 1.0 = normal). Data-driven, tuned per enemy tier.
7. **Friendly fire in co-op:** off by default. Post-MVP could offer a toggle.
8. **Combat music / ambience trigger:** the "in combat" flag drives both regen gating and audio switching. Audio work itself is post-MVP but the flag is available for it now.
9. **Enemy AI reactions to being hit (MVP):** enemy resumes previous action after hit-stop ends; if knocked back, reorients toward the player and continues attack cycle. Refined behaviour deferred to the AI/behaviour spec.

**Follow-up items surfaced during resolution:**

- **HP expansion mechanism design** — needs its own decision. Options to consider later: consuming rare items to raise cap, quest rewards, boss drops, feats/achievements. Deferred to a future progression pass.
- **In-combat flag mechanics** — the flag is a shared runtime concern (drives regen, audio, possibly enemy behaviour). Worth spec'ing precisely once the runtime systems come online. Rules of thumb: enter combat on taking damage OR dealing damage OR entering an aggro range of a hostile; exit combat after 5 seconds of none of the above.
