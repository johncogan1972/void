# NPC Content — Feature Spec

**Version:** 0.1
**Status:** Draft (first-pass proposals)
**Companion to:** GDD §5.4, biome-content-spec

---

## 1. Overview

Defines the concrete NPC roster for MVP and post-MVP. Covers the Guide's personality and dialogue role, the discoverable post-MVP NPC set, and the shared behaviour and vendor systems that support them.

Per GDD §5.4, the Guide is the sole starting NPC. All others are discovered through world exploration and progression.

## 2. Design Principles

NPCs should:

- **Feel like individuals, not services.** A vendor is a personality who happens to sell things, not a menu with a face.
- **Have opinions on the world's events.** Bosses defeated, portals discovered, seasons — NPCs should acknowledge these when talked to.
- **Match their world thematically.** A tech-lean NPC comes from a tech biome or portal; a magic-lean NPC from magic-lean space.
- **Never block progression.** No NPC dialogue is required to advance — hints and services are always available but the game is playable without engaging deeply with NPCs.
- **Be discoverable without a mandatory quest chain.** Finding them is exploration reward, not a fetch quest.

## 3. The Guide (MVP)

The starting NPC. Spawns near the player at character creation and approaches them.

### 3.1 Identity

**Name:** *Aelis* (placeholder — replace with something that feels right when named).

**Appearance:** an older figure, humble travelling clothes, patched cloak. Carries a staff not as a weapon but as a walking aid. No armour. Slightly hunched. Weathered but warm face.

**Personality:** wry, warm, patient, faintly amused by the player's ignorance without being condescending. Has clearly been at this a long time — talks about "the last one who tried" occasionally. Doesn't take herself seriously. Genuine concern for the player's safety.

**Backstory (light):** she was once a guide for adventurers who came before. Most did not survive. She stays because someone has to help the newcomers. Never elaborates unless asked, and even then answers are gentle deflections rather than lore dumps.

### 3.2 Role

Per GDD §5.4:
- Appears and approaches the character at first spawn.
- Provides ongoing progression cues.
- Sells items.
- Moves into the player's base once a valid room is available.
- Fights (weakly) when the base is under attack; respawns at Hearth after 10 minutes.

### 3.3 Dialogue Structure

Dialogue is context-aware: the same NPC has different available lines depending on world state.

**Categories of Guide dialogue:**

- **Greetings** — 3–5 rotating opening lines. Warm but not sappy.
- **Progression hints** — surface based on current progression state. See §3.4.
- **Vendor menu** — access to her shop.
- **World reflections** — comments on the state of the world (main boss killed, first portal opened, seasons changed post-MVP).
- **Idle chatter** — small comments the player can trigger repeatedly. Reveal a little personality per interaction.
- **Small talk hooks** — questions the player can ask her ("tell me about yourself" / "what do you know about X"). Answers should feel authentic — a little revealed, a little kept back.

### 3.4 Progression Hint Ladder

Hints escalate as the player progresses. The Guide's job is to nudge, not lead. She's clearest at the start when the player is most lost, and more sparing later when the player knows the loop.

- **On first meeting:** teach controls implicitly ("you'll find a pickaxe in your pack — try knocking some stone loose. It's the beginning of everything.").
- **After first tools crafted:** hint at the main boss's existence ("something restless lives out there. You'll feel it before you see it.").
- **When gear is roughly appropriate for main boss:** stronger hint about direction ("the old stones ring louder when the wind's from the [direction]. Might be worth a look.").
- **After main boss killed:** acknowledge the moment, hint at portals ("the ground remembers what you did. Doorways open where they weren't. Two more, if you look.").
- **After portal world entered:** hint that other portals exist too.
- **When stalled for long periods without progression:** occasional gentle prompts ("stuck? sometimes the way forward is downward.").

### 3.5 Vendor Stock

The Guide sells starter and utility items — nothing tier-critical, but useful convenience.

- Torches (bulk)
- Basic potions (small healing, small mana)
- Rope (traversal)
- Basic ammunition (arrows, standard bullets)
- **Hint items** (post-MVP) — consumables that reveal a nearby structure or portal candidate. Very rare, expensive.

Prices are calibrated to be "worth it early, ignorable late" — the Guide's shop is a beginner's crutch, not endgame relevant.

### 3.6 Guide Death Behaviour

Per GDD §5.4:
- Engages hostile enemies attacking the base but is weak.
- On death, respawns at the Hearth after 10 minutes.
- Death dialogue: brief, low-key acknowledgement on respawn. Not dramatic — "back on my feet. Where's my staff?"

## 4. Post-MVP Discoverable NPCs

Per GDD §5.4, all NPCs beyond the Guide must be discovered in the world. Each has a discovery condition and travels to the current Hearth base once found.

### 4.1 Discovery Patterns

Standard discovery patterns to draw from:

- **Rescue** — found in trouble (imprisoned, cornered by enemies, trapped). Player intervenes; NPC agrees to relocate.
- **Encounter** — found alone in a biome, willing to move if the player offers.
- **Progression-triggered** — appears after a specific event (first boss killed, first portal opened, etc.).
- **Location-triggered** — appears near a specific structure or biome only after another condition is met.

### 4.2 Proposed Roster

**Merchant.** *Discovery: found in a small caravan camp in Scrubland after home world main boss defeated.* General vendor with rotating stock. Personality: shrewd but fair, deals-first, warm underneath. Sells and buys goods; stock rotates weekly (in-game days).

**Mechanic.** *Discovery: found working on a broken automaton in Ashwastes.* Tech-affinity vendor and crafter. Personality: enthusiastic tinkerer, over-explains. Sells tech reagents, offers tech crafting bench upgrades.

**Sage.** *Discovery: found meditating in a mushroom circle in Whisperbog.* Magic-affinity vendor and crafter. Personality: cryptic but not obnoxiously so, speaks in short weighted sentences. Sells magic reagents, offers magic crafting station upgrades.

**Medic.** *Discovery: found tending an injured traveller in Frostreach.* Vendor of consumables and healing items. Personality: no-nonsense, care-through-competence. Sells health potions, buff foods, cures for status ailments.

**Warrior.** *Discovery: found sparring with a training dummy at a ruined outpost. Player must "prove themselves" (survive a brief spar, or defeat an area boss in her presence).* Vendor of higher-tier weapons and armour. Personality: gruff, respectful of competence, dry humour. Doesn't sell to those who haven't earned it.

**Farmer.** *Discovery: found trying to reclaim overgrown land in a Meadow ruin.* Vendor of seeds, food ingredients, farming implements. Enables the (post-MVP) farming system. Personality: patient, quietly proud, generous with knowledge.

**Wanderer.** *Discovery: found at random remote outdoor locations, changing spawn every N days if unhoused.* Rare NPC — appears sometimes, doesn't always want to be housed permanently. Sells travel-oriented items (grappling hooks, potions of speed, teleport charms if we add them). Personality: restless, brief, always about to leave.

**Faction Envoys (post-post-MVP).** Once faction NPCs are designed, envoys of tech and magic factions can be found in their respective portal worlds. Deferred.

### 4.3 Discovery Pacing

Rough intended pacing across a full playthrough:

- **Post-home-boss:** Merchant, Medic accessible.
- **Post-first-portal:** Mechanic and Sage accessible (if their biomes have been touched).
- **Mid-game:** Warrior and Farmer accessible.
- **Late-game:** Wanderer starts appearing.
- **Endgame:** Faction envoys (post-post-MVP).

Not every player will find every NPC in one playthrough — some are exploration rewards for players who wander off the critical path.

## 5. NPC Systems (Recap)

Per GDD §5.4:

**Housing:** each NPC requires a valid room to settle (per GDD §5.5 room criteria — background walls, accessible door, light, bed). One NPC per room.

**Travel:** discovered NPCs make their way to whichever base currently hosts the Hearth. If no room is available at the current Hearth on discovery, the NPC waits at the base perimeter. If the Hearth moves and there's no room available at the new location, NPCs simply don't appear until rooms are built.

**Combat and mortality:** all NPCs follow the Guide's pattern — fight weakly when base is attacked, respawn at Hearth 10 minutes after death.

**Vendor system (shared):**

- Each NPC has a stock definition (JSON data).
- Stock refreshes on a cadence (weekly in-game days, per NPC).
- Stock is influenced by world state — a defeated main boss unlocks new items at the Merchant, etc.
- Players buy with a currency (design decision — see open questions).
- Some NPCs also buy items from players (Merchant especially).

## 6. Dialogue System (Overview)

A shared dialogue system supports all NPCs. Data-driven per NPC.

**Dialogue tree structure:**

```yaml
npc_id: "aelis_guide"
greetings:
  - "Back again. Good."
  - "Careful out there today. The air's got a strange note."
  - "Rest for a moment before you go."
context_lines:
  - condition: "main_boss_killed == false"
    lines:
      - "Something restless lives out there. You'll feel it before you see it."
  - condition: "main_boss_killed == true AND portal_count < 2"
    lines:
      - "The ground remembers what you did. Doorways open where they weren't. Two more, if you look."
topics:
  - topic_id: "yourself"
    text: "Tell me about yourself."
    response: "Not much to tell. I stay because someone should be here when the new ones arrive. Most who came before didn't stay long."
  - topic_id: "world"
    text: "What do you know about this place?"
    response: "Enough to be careful. Not enough to explain. Ask when you've seen more of it."
```

Dialogue text lives in string files that can be localised later.

## 7. NPC Data Schema (Overview)

```yaml
npc_id: "aelis_guide"
display_name: "Aelis"
role: "guide"  # guide / merchant / mechanic / sage / etc.
discovery:
  method: "spawn_with_character"  # or "rescue" / "encounter" / "progression" / "location"
  conditions: {}
appearance:
  sprite_variant: "elder_woman_traveller"
  palette_ref: "npc_palette_default"
housing:
  requires_room: true
  can_wait_at_perimeter: true
vendor:
  stock_definition: "aelis_stock"
  refresh_cadence_days: 7
  buys_from_player: false  # Guide doesn't buy
combat:
  is_defender: true
  hp: 200
  attacks: [{ attack_id: "staff_swing", damage_by_type: { physical: 5 } }]
  respawn_cooldown_seconds: 600  # 10 minutes
dialogue:
  dialogue_tree: "aelis_dialogue"
```

## 8. Open Questions

- **Currency system.** How does the vendor economy work? Options: single currency (gold-analogue), item-based barter (specific materials as trade), or hybrid. Not spec'd anywhere yet. Recommend: hybrid — a general currency (dropped in small amounts from enemies/chests) for most trades, plus specific "trophy items" for premium purchases. Not urgent, but needed before vendor implementation.
- **Guide name.** Aelis is a placeholder. Confirm or replace.
- **NPC dialogue voice lines.** Full VO is post-launch (or never for a solo project). Text-only for MVP and post-MVP.
- **Idle NPC animations at home.** NPCs shouldn't just stand in one place — they should walk between rooms, sit at furniture, occasionally leave the base briefly. Post-MVP polish.
- **NPC-NPC interaction.** Terraria has NPC "happiness" tied to who they're housed near. Add anything similar, or keep NPCs indifferent to each other? Recommend indifferent for MVP — happiness systems add complexity that doesn't clearly improve the game.
- **NPC uniqueness.** Should each NPC be truly unique (one per world), or can post-MVP add multiples (e.g. two Merchants with different stock)? Recommend unique — each NPC is a personality, not a role.
- **Dialogue localisation.** Text in string files enables it, but doing translation work is post-launch. Note here that the system supports it.
- **Discovery timeouts.** If a player never finds a discoverable NPC after many hours, should any hint be given? Recommend: after very long play without discovering an NPC, the Guide can drop a vague hint about them ("Heard a rumour of someone tinkering in the deep south. Might be worth finding.").
- **NPC gifts.** Some games (Stardew) have NPCs who respond to gifts. Post-MVP consideration, not core.
