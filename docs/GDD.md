# Game Design Document — [Untitled Project]

**Version:** 0.34 (High-Level Draft)
**Status:** Pre-production
**Author:** John

---

## 1. Overview

### 1.1 Elevator Pitch
A 2D sandbox adventure game in the spirit of Terraria, blending **technology and magic** into a single progression tree. Players explore a procedurally generated home world, defeat its guardian to claim the first portal anchor, hunt down optional side anchors for more choices, and dive into pocket worlds themed around distinct biomes and difficulty tiers — solo or in co-op.

### 1.2 Core Pillars
1. **Sandbox freedom** — the world is destructible and buildable; player intent drives play.
2. **Tech + magic as equals** — neither path is "the right one"; they intersect, complement, and can be combined.
3. **Shared discovery** — co-op is first-class; the game is designed with multiple players in mind, not retrofitted.
4. **Boss-gated portals as progression** — a mandatory main-path portal per world, plus optional side portals for players who explore.

### 1.3 Target Platform
- **Primary:** PC (Windows, Linux/SteamOS) — with **Steam Deck as a first-class day-one target**.
- Steam distribution primary channel.
- **Engine:** Godot (2D pipeline, C# for simulation hot paths).

**Steam Deck day-one requirements:**
- Full controller support — no action requires keyboard or mouse.
- Native Linux build (avoids Proton translation layer overhead).
- Rendering scales cleanly to Steam Deck's 1280 × 800 (16:10) display.
- UI text and interactive elements readable on the 7-inch handheld screen at arm's length.
- Suspend/resume compatibility — game state survives the Steam Deck's frequent sleep/wake cycle.
- Steam Cloud save integration (small individual save files per world/chunk already suit this — see save-format-spec).
- Goal (post-MVP): Steam Deck Verified badge.

### 1.4 Genre
2D sandbox / action-adventure / crafting-survival.

---

## 2. Gameplay

### 2.1 Core Loop
Explore → gather → craft → build → fight the world's guardian → claim primary anchor → (optionally hunt side anchors) → dive → fight the portal world's guardian → repeat with new materials one tier deeper.

### 2.2 Perspective & Controls
- 2D side-scrolling view, à la Terraria.
- Mouse-aimed tools/weapons, WASD movement, hotbar for quick swaps.

### 2.3 Combat
- Real-time action combat.
- **Feel:** weightier than Terraria, lighter than Souls-likes. Reference points: Dead Cells, Blasphemous. Punchy, responsive, hits have impact — never sluggish.
- **Weapon class defines feel.** Light melee (daggers, short swords), medium melee (swords, spears), heavy melee (hammers, greataxes), ranged (bows, firearms), and magic. See §7.4 for the data model.
- **Universal mechanics:**
  - **Hit-stop** on impacts — small freeze frame when a hit lands, sells weight without slowing the game.
  - **Knockback** scales with weapon weight.
  - **Enemy stagger** states for heavy hits.
- **What's not in the game:**
  - **No dodge roll or block.** Mobility comes from movement, jumping, and traversal gear (grappling hook, jetpack, boots of speed, etc.).
  - **No combo chains.** Every swing is standalone.
  - **No stamina resource.** Only magic gear consumes a resource — mana (see §5.6).
- **Magic is item-based, not skill-based.** There is no separate spell system; spells are properties of the equipped magic gear. Using the gear casts the spell.

**Detailed spec:** the full attack lifecycle, damage calculation, resistance stacking, status effect model, hit reactions, and death handling live in `combat-spec.md` (companion document).

### 2.4 Building
- Full block placement/destruction.
- Structural integrity as an **optional** simulation (design decision to make later — powerful but scope-heavy).

---

## 3. World

### 3.1 World Structure Overview
The game world is not a single map. Players inhabit a **home world** and, after defeating its guardian, gain access to portals leading to **pocket worlds** — separate procedurally generated worlds themed around distinct biomes and difficulty tiers. See §4 for the full portal system.

### 3.2 Generation
- **Procedural, seeded** world generation for both home and portal worlds.
- Seed is a shareable value (integer or short string) exposed on world creation and in save metadata.
- Same seed + same version = identical world.
- Portal world seeds derive deterministically from the home seed + portal identifier, so a shared seed reproduces the full multi-world set.
- **World size is configurable** at generation time (Small / Medium / Large or similar), applying to home and portal worlds alike.

**Determinism guarantee (hard constraint)**
Given the same seed + same code version, world generation must produce a byte-for-byte identical world on any machine, any platform. This is not a "nice to have" — the seed-sharing feature and portal seed derivation both depend on it. Rules that follow from this:

- **All randomness derives from the seed.** No `System.Random()` without seeding, no `DateTime.Now`, no `Guid.NewGuid()`, no hardware or wall-clock inputs anywhere in generation code.
- **Portable RNG algorithm** — target **xoshiro256++** (or equivalent well-tested portable RNG). Do not rely on `System.Random`, whose behaviour has changed across .NET versions.
- **Deterministic iteration order.** Anywhere iteration order affects output, use ordered collections (`SortedDictionary`, `List` with explicit sort). Never iterate `HashSet` / `Dictionary` in generation-affecting logic.
- **Floating-point discipline.** For determinism-critical math, use integer or fixed-point arithmetic where practical. FP results can vary across CPU architectures.
- **No thread-scheduling dependencies.** Parallel generation is allowed but outputs must be ordered/merged deterministically — never dependent on which thread finished first.
- **Version-locked.** Determinism is per code version. A save from v1.0.0 stays valid at v1.1.0 (already on disk), but fresh generation of seed 42 will differ across versions — expected.
- **CI check:** a "regenerate reference seed and hash the output" test runs in CI from day one. Any accidental non-determinism is caught immediately.

**Detailed spec:** the full generation pipeline, chunk model, layer proportions, cave-gen approach, and ticket-ready epic breakdown live in `world-generation-spec.md` (companion document).

### 3.3 Structure
- Each world is a horizontal 2D map with vertical layers (surface, underground, deep caves, [something below]).
- Biome-based surface generation.
- Handcrafted "structure" prefabs sprinkled into procedural output (dungeons, ruins, labs, shrines).

### 3.4 Home World Biomes (initial set — TBD)
- Forest / grassland (starter)
- Desert
- Frozen north
- Underground caverns
- A "seeded" corrupted or arcane biome (mild — the strong versions live in portal worlds)

### 3.5 Day/Night & Events
- **Day/night cycle with dawn and dusk transitions** — in MVP. Affects lighting, enemy spawns, and certain magic/tech mechanics.
- Random world events (invasions, storms, meteor showers) as pacing tools.
- Some events spawn **event bosses** — non-tier bosses that appear alongside random world events for loot and challenge, separate from the main progression bosses.
- **Weather system** (rain, snow, storms) — post-MVP.

---

## 4. Portals & Multi-World Structure

### 4.1 Concept
The game is structured as a **home world plus discoverable pocket worlds** reached through portals. The home world is the hub; portal worlds gate progression, hold exclusive materials, and escalate in difficulty.

Each world provides **up to three portal anchors**:
- One **primary anchor**, claimed by defeating the world's main boss (mandatory path).
- Two **side anchors** that spawn at random locations after the main boss falls, each guarded by a mini boss (optional path).

Players may progress with only the primary anchor, or invest in exploration to unlock all three portals — trading time for variety.

### 4.2 Home World
- The player's starting world; baseline difficulty.
- Contains a mix of standard biomes (§3.4).
- Contains one **main boss** — the guardian of the home world. Defeating it grants access to the primary portal anchor.
- Killing the main boss also triggers **two side anchors** to spawn at random locations across the world, each defended by a mini boss. Side anchors have **no indicators or map markers** — finding them is a pure exploration reward.
- Also contains **area bosses** (found in specific biomes/structures) and **event bosses** (random events) as optional content and loot sources — not required for progression.
- Typically where the player's primary base and spawn point live.

### 4.3 Portal Worlds
- Each portal, when first entered, generates a new procedurally seeded world.
- Portal worlds have their own biome mixes, often thematically coherent (e.g., a scorched world, a sunken world, a clockwork world, an arcane world).
- **Size is configurable, up to the size of the home world** — some portals may open into small focused biomes, others into full-scale expanses.
- Each portal world has a **difficulty tier** (Slightly Harder / Considerably Harder / Extreme / …), **fixed at generation**. It does not scale to player level on entry.
- Each portal world contains:
  - One **main boss** — the guardian of that world, and the key milestone for its tier.
  - Optional **area bosses** and **event bosses** as in the home world.
- Portal worlds contain **exclusive materials** required for higher-tier crafting.
- Portal worlds are **persistent** — leaving and returning re-enters the same world, not a fresh one.
- Portal worlds are **terminal** — they contain no further portals of their own.

### 4.4 Portal Guardians
Every anchor is guarded. Nothing is free.

- **Main world boss** — the guardian of the primary anchor. Killing it grants the primary anchor and triggers the spawn of the two side anchors.
- **Mini bosses** — one per side anchor. Each side anchor cannot be picked up or activated until its mini boss is defeated.
- Guardians of a portal (whether the main boss's world or a mini boss's) do **not respawn** once killed.
- Guardians scale to their world's tier — a tier-1 mini boss is manageable, a tier-3 mini boss is a serious fight.
- **Design intent:** guardians are the primary steamroll prevention. Difficulty is the wall — you cannot brute-force into a higher tier without the gear that lets you kill its guardian.

### 4.5 Portal Anchors
- Once a guardian is defeated, the player can **extract the corresponding anchor** and carry it.
- Anchors can be placed anywhere in the world where they were obtained — most naturally, in the player's home base.
- Anchors **cannot be destroyed**. They can be picked up and re-placed elsewhere freely.
- Anchors **cannot be detached from their originating world**. Once activated, an anchor is permanently tied to its portal world; moving it moves the doorway within the same home world, not between worlds.
- **Design intent:** over time the player's base becomes a "portal room" of collected doorways — a visible, physical representation of progression.

### 4.6 Return Travel
Players can leave a portal world by:
- Walking back through the portal on the far side.
- Using a **Home Potion** (or equivalent tool) that teleports to the party's spawn point.
- Dying — respawn at the shared Hearth.

### 4.7 Spawn Points & Bases (Hearth)
- The party has **exactly one active spawn point**, set at a shared **Hearth** (placeable furniture).
- A Hearth can be placed in any world — home or portal.
- Placing a new Hearth deactivates the previous one.
- All players respawn at the Hearth on death.
- **Design intent:** shared Hearth simplifies co-op respawn logic and reinforces the party as a single expedition. Players can collectively choose to "move house" into a portal world if it suits their playstyle.

### 4.8 Co-op Interactions
- Portal worlds belong to the party's shared campaign, not individual players.
- Any party member can enter any activated portal.
- Progression events (main bosses, mini bosses) are shared party progress — a boss killed once stays killed.

### 4.9 Enemy & Boss Respawn Rules
- Regular enemies **always respawn** in all worlds (home and portal).
- **Main bosses do not respawn** once defeated.
- **Mini bosses (guardians of side anchors) do not respawn** once defeated.
- **Area bosses** — respawn 3–5 in-game days after death.
- **Event bosses** — respawn implicitly whenever their triggering event fires.

### 4.10 Progression Flow
The intended player experience:

1. Spawn in home world. Explore, gather, craft, build a base.
2. Gear up enough to challenge the home world's main boss.
3. Defeat main boss → claim the primary anchor. Two side anchors spawn at random locations across the world.
4. **Choice point:** rush the primary portal, or hunt down the side anchors (defeat their mini boss guardians) for more options.
5. Place anchor(s) in home base for convenience.
6. Enter a portal world. Gather its exclusive materials, engage its content, defeat its main boss.
7. Return to home world with new materials. Craft next-tier gear.
8. Move to another portal world (if collected) for its exclusive materials, or advance to the next-tier's content.

---

## 5. Character System

### 5.1 Character Creation
Players create a character before entering the world:
- Cosmetic customisation (sprite variants from Aseprite).
- Optional starting background/archetype (Scholar, Engineer, Ranger, etc.) — grants a thematic starting kit (e.g. Engineer starts with a wrench and some scrap; Scholar with a research book and torches). Cosmetic-adjacent, not a hard class lock.

### 5.2 Progression Feel
The game has **no character levels, no XP, and no stats.** Character progression is entirely **gear-driven** (Terraria-style): what you can craft, find, and wear defines what you can do.

The differentiation between playstyles comes from:
- **Gear class** — heavy armour vs light armour vs magic robes shape combat feel and resistances.
- **Tech vs magic investment** — the crafting trees the player chooses to pursue.
- **Weapon type** — melee vs ranged vs magic, each with its own feel (see §2.3 and §7.4).
- **Rarity of finds** — better rolls on the same item slot mean measurably better performance. Rarity effectively replaces the numeric depth stats would have provided (see §7.5).

Post-MVP consideration: **unlockable perks** tied to gear class or crafting tree (rather than stats).

### 5.3 Difficulty Modes & Death Mechanics
Two modes, selected at character creation:

**Standard** — die, respawn at the Hearth. Items retained. Baseline experience.

**Hardcore** — die, respawn at the Hearth. All carried items are automatically deposited into a **death container** at the location of death. The player must return to retrieve them.

**Death container rules:**
- Persists indefinitely until looted.
- World-local — die in a portal world, container spawns in that portal world.
- In co-op, party members can loot each other's containers (helpful for group recovery).
- Multiple deaths produce multiple containers; they do not merge.
- Visually distinct and labelled (e.g. "[Player]'s Remains").

**No permadeath mode.** Death never destroys progress, only relocates it.

**Design intent:** Hardcore adds meaningful risk without punishing casual players. A death deep in a hostile portal world becomes a tense recovery run, not a save-file setback.

**Recovery in portal worlds:** because anchors are permanently linked to the world they opened (§4.5), Hardcore items dropped in a portal world are always recoverable — the player returns via the linked anchor (even if the anchor has since been moved to a new location in the home world). Items are never lost, only stranded until the player is willing and able to return.

### 5.4 NPCs

Non-player characters give the world life and provide services (guidance, crafting, vendoring). One NPC starts alongside the player; all others must be **discovered** in the world.

**Starting NPC — Guide** *(name TBD)*
- Appears and approaches the player character at first spawn.
- Provides **ongoing progression cues** — post-boss, post-portal, post-material — keeping less-experienced players oriented throughout the game, not just during onboarding.
- **Sells items** — starter/utility gear, hint items, consumables.
- Moves into the player's base permanently once a valid room is available.
- **Combat & mortality:** the Guide will engage hostile enemies attacking the base, but is weak in a fight and offers only token defence. On death, respawns at the Hearth after a **10-minute cooldown**.

**Discovered NPCs**
- All other NPCs must be **found in the world** — in specific biomes, structures, or as consequences of progression (bosses defeated, materials collected, events triggered).
- On discovery (sometimes after a small unlock condition — a rescue, a quest, a payment), the NPC will **make their way to the player's home base** and settle in an available valid room.
- If the Hearth is moved to a new base, discovered NPCs follow — provided rooms are available for them. If no rooms are available at the new location, NPCs **do not appear** until rooms are built for them (they don't loiter at the base perimeter in a Hearth-move scenario, unlike a fresh discovery).
- If no room is available on **initial discovery**, the NPC waits at the base perimeter until one is provided.
- NPCs travel to the base at which the player's Hearth currently resides.

**Discovered NPC combat & mortality**
All discovered NPCs follow the same pattern as the Guide (§5.4 Guide entry): they will engage hostile enemies attacking the base, are weak in a fight and offer only token defence, and on death respawn at the Hearth after a 10-minute cooldown. This applies uniformly regardless of NPC role.

**Housing (light spec)**
- NPCs require a valid "room" in a player's base to settle in.
- Room validity criteria (walls, door, floor, furniture, lighting) will be spelled out under **Player Bases & Housing** (§5.5).
- **One NPC per room.**
- Rooms can be built in any world (home or portal) — NPCs will settle wherever the player has provided housing.

**Post-MVP roster**
Candidate discoverable NPCs (roles, not final names):
- Merchant (general vendor)
- Mechanic (tech vendor/crafter)
- Sage (magic vendor/crafter)
- Medic
- Faction representatives (see §11)

MVP includes the Guide only. Discovered NPCs are the first post-MVP content pass.

### 5.5 Player Bases & Housing

Players build bases wherever they choose. Bases serve as storage, crafting hubs, and NPC housing. Rooms within a base host NPCs and are validated with a lightweight Terraria-style rule set.

**Room validity criteria**
A room is a valid NPC dwelling when it has:
- **Enclosed background walls** — all interior tiles must have background wall behind them, and the room must be fully enclosed (Terraria-style). Open-air or partially walled spaces do not qualify.
- **At least one accessible door** — a standard door, platform door, or equivalent placeable. The door must be reachable, not walled off.
- **A light source** — a torch, lamp, or equivalent placed inside the room.
- **A bed** — required as NPC sleeping furniture. Beds have no mechanical function for the player (spawn setting is handled entirely by the Hearth — see §4.7).

Everything else (chairs, tables, decor, storage) is **optional**. No hard minimum or maximum room size for MVP; may revisit if playtesting shows abuse (e.g. players spamming tiny rooms to house many NPCs, or building single enormous halls).

**Base ownership**
- Any structure the player builds is theirs; no explicit "claim" mechanic.
- Multiple bases per world are permitted — players may homestead in the home world and in portal worlds simultaneously.
- Only one Hearth (the party spawn point) is active at any time (§4.7).
- Discovered NPCs travel to whichever base currently hosts the Hearth (§5.5).

**Portal anchor placement**
- Portal anchors are standard placeables (§7.3) and can be placed inside any room, including the room containing the Hearth.
- Anchors do not have to be housed. A dedicated "portal room" is a natural pattern many players will adopt.

### 5.6 Mana & Resources
The only combat resource the player manages is **mana**, consumed by magic gear.

**Mana pool**
- **Starting pool:** 100.
- **Hard cap:** 200.
- Base pool is expandable **only temporarily** — via consumables or buffs that push the current pool above the base 100, up to the 200 cap. Base 100 is not permanently upgradable in the way Terraria's mana crystals work; the mana bar is a base-plus-buff model.
- All values above are **configurable** in game data (JSON) for easy tuning.

**Mana regeneration**
- **Baseline: 2 mana per second** when not casting.
- Modifiable by gear (magic robes, accessories) and consumables (mana potions).
- Configurable in game data.

**No other resources for MVP**
- No hunger.
- No stamina.
- No fatigue, morale, or comparable systems.

### 5.7 Equipment Slots, Hotbar & Inventory

**Worn equipment (10 slots)**
These slots hold gear that provides passive effects (resistances, stats, buffs). They are assigned via inventory management, not the hotbar:

*Armour (5)* — Head, Chest, Gloves, Legs, Boots
*Back (2)* — Bag slot (increases inventory capacity by bag type); Attachment slot (jetpacks, wings, gliders)
*Jewellery (3)* — Left ring, Right ring, Amulet / necklace

Rings, amulet, and back attachments are the primary carriers of "accessory-style" effects (movement bonuses, extra resistances, mana regen, life regen, situational buffs). Armour pieces may also carry secondary effects (§7.4), but the accessory slots are where those effects concentrate.

**Active state slots (driven by hotbar, not directly editable)**
These display what the character is currently wielding. The player cannot manually place items here — they populate automatically based on hotbar activations (see below):

- **Right hand** — main-hand weapon slot (one-handed weapon, or half of a two-handed weapon).
- **Left hand** — offhand slot (shield, or a second one-handed weapon for dual-wield; or half of a two-handed weapon).
- **Active tool** — the tool the character will use on right-click.

**Hotbar (10 slots, hotkeys 1–0)**
The hotbar is where the player parks weapons, shields, tools, consumables, and quick-use items. Pressing a hotkey activates that slot's item, with behaviour depending on item type:

- **One-handed weapon** — placed in the **right hand**. Any shield in the left hand stays put.
- **Shield** — placed in the **left hand**. Any main-hand weapon stays put.
- **Two-handed weapon** — placed in **both hands**, replacing anything held.
- **Tool** — set as the **active tool**. Does not affect hand slots.
- **Consumable / quick-use** — used immediately on hotkey press.

**State persistence rules**
- Items stay in the hotbar when activated — they aren't moved out.
- Rearranging items **within** the hotbar does not change what is equipped in hand or active tool.
- **Removing** an item from the hotbar unequips it from the hand or active tool slot.

**Worked example** *(as described)*
Hotbar: [1] Sword, [2] Shield, [3] Bow (two-handed), [4] Pickaxe, [5] Axe.
- Press 1 → sword in right hand, left hand empty.
- Press 2 → shield in left hand. Now sword-and-shield equipped.
- Press 4 → pickaxe set as active tool. Sword and shield remain in hand.
- Press **F** → enter tool mode. Right-click now mines with pickaxe (can be held / repeated). Left-click swings sword and returns to weapon mode.
- Press 3 → bow occupies both hands, replacing sword and shield. Exits tool mode (weapon draw implies combat intent). Pickaxe is still the active tool for next time F is pressed.
- Press 5 → axe becomes the new active tool. Bow still in hand. Press F to enter tool mode and chop.

**Mouse and mode controls**
The game has two mouse-input modes: **weapon mode** (default) and **tool mode** (toggled by F).

*Weapon mode*
- **Left-click** — attack with the **right-hand weapon**.
- **Right-click** — attack with the **left-hand weapon** (if a weapon; shields are passive, so right-click does nothing when a shield is offhand).
- Two-handed weapons: left-click swings, right-click unused.

*Tool mode* (entered with F)
- **Right-click** — use the active tool (mine, chop, dig, place). Holdable / repeatable.
- **Left-click** — swings the right-hand weapon **and returns to weapon mode**. Acts as an "attack now" panic button — no fumbling to switch modes mid-fight.
- **F again** — return to weapon mode without attacking.

*Mode auto-exits*
- Pressing a hotkey for a **weapon, shield, or two-handed weapon** exits tool mode. The player has explicitly drawn combat gear; combat is the intent.
- Pressing a hotkey for a **tool** sets the active tool but does **not** auto-enter tool mode — the player still presses F when they want to use it.
- Pressing a hotkey for a **consumable** uses it immediately; mode is unchanged.

*Other*
- **E** — interact with objects: open containers, talk to NPCs, use anchors and portals.

**Design intent**
Two mouse buttons carry three actions (main-hand attack, offhand attack, tool use) by making tool use a mode rather than a button. Left-click is always "attack now," which suits a game where interruption by enemies is constant. Dual-wielded weapon combinations (sword + wand, sword + dagger, etc.) get real mechanical distinction — each hand fires on its own button.

**Inventory**
- **Base capacity:** 10 slots, always available regardless of bag.
- **Bag expansion:** when a bag is equipped in the back-slot (§5.7), it adds a number of slots defined by that bag's item data. Base 10 slots + bag slots = total inventory capacity.
- **Removing a bag:** allowed only when the bag's extra slots are empty. If the player's current inventory count exceeds 10, they must clear the bag's contents before unequipping. Prevents item loss and forces explicit management.
- **Swapping bags:** allowed freely if the new bag has the **same or more** slots than the current bag. Swapping to a smaller bag requires the excess slots to be cleared first.
- **Item stack size:** defined per-item in that item's JSON data entry. Different item types may have very different stack limits (torches: high; weapons: 1 / non-stacking; potions: moderate). No universal stack cap.
- Grid layout, Terraria-style.

**Controller mapping (gamepad / Steam Deck)**

Every mouse/keyboard action has a controller equivalent. All bindings are rebindable via Steam Input.

*Movement and aim*
- **Left stick** — character movement (WASD equivalent).
- **Right stick** — aim cursor / crosshair (mouse-position equivalent).
- **A** — jump.

*Weapon mode (default)*
- **RT (right trigger)** — right-hand weapon attack (L-click equivalent).
- **LT (left trigger)** — left-hand weapon attack (R-click equivalent, in weapon mode).

*Tool mode*
- **Y** — toggle tool mode on/off (F equivalent).
- In tool mode: **LT** uses the active tool (mine, chop, dig, place); **RT** swings the right-hand weapon and returns to weapon mode (mirroring the mouse behaviour).

*Hotbar*
- **LB (left bumper)** — previous hotbar slot.
- **RB (right bumper)** — next hotbar slot.
- Selecting a slot activates it immediately (same as pressing 1–0 on keyboard).

*Interact and UI*
- **X** — interact with objects (E equivalent) — open containers, talk to NPCs, use anchors and portals.
- **B** — cancel / close menu / drop item (context-dependent).
- **Back / Select button** — open inventory.
- **Start button** — pause menu.

*Aim*
- Right-stick aim is free (fully player-controlled, no snap or lock).
- Configurable sensitivity in settings.
- Aim assist (snap-to-nearest-enemy) — post-MVP consideration if playtesting shows right-stick precision is a problem.

**Steam Deck-specific input**
- Trackpads and gyro exposed as configurable options via Steam Input — Steam Deck power-users can bind trackpad-as-mouse for aim if desired.
- Back paddles / grip buttons available for rebinding by end users.
- **Everything above is a default binding** — Steam Input allows any player to rebind, and we ship a curated "official layout" via Steam's controller config system so Verified reviewers see a considered default.

---

## 6. Progression

### 6.1 Vertical Progression
Tiered materials and equipment (copper → iron → steel → arcane alloy → …), same shape as Terraria but with **parallel tech and magic tiers** at each level so players aren't forced down one path.

### 6.2 Boss / Milestone Progression
Bosses are the pacing spine. The layered structure:

- **Main bosses** — one per world. Guard the primary anchor and drive tier progression.
- **Mini bosses** — guard side anchors. Optional but rewarding.
- **Area bosses** — biome/structure-tied, optional. Loot rewards.
- **Event bosses** — random world events, optional. Loot rewards.

### 6.3 Tech + Magic Intersection (differentiator)
Design principle: at every tier, there should exist **hybrid items** requiring both a tech and a magic ingredient — e.g. an "arcane battery," a "runed circuit." This rewards players who explore both trees and gives co-op partners with different specialisations a reason to collaborate.

### 6.4 Portal-Gated Materials
Each portal world contributes at least one signature material that cannot be found elsewhere. Progression is therefore also **horizontal** — you can only reach the top of the tree by visiting multiple portal worlds, which incentivises collecting the side anchors, not just the primary.

### 6.5 Target Playtime
**Main progression target: 30–50 hours** from character creation to defeating the final boss. Roughly Terraria-scale.

This calibrates content scope for the shipped game:
- Multiple portal worlds beyond MVP (target: 3+ discoverable per home world, with tier variety).
- Multiple tiers of tech and magic gear.
- Terraria-scale item roster (hundreds of items).
- Multiple main bosses across worlds, plus gate / area / event bosses.

Sandbox play (building, exploration, farming, co-op) remains open-ended beyond that.

**MVP targets a much shorter loop** (~5–10 hours) — enough to prove the core mechanics work end to end. Content scale expands toward the 30–50 hour target through post-MVP passes.

---

## 7. Crafting

### 7.1 Recipes
- Recipe-based crafting system.
- Recipes defined in JSON data files (see §10).
- Recipes discovered through: pickup (schematics/tomes), NPC purchase, biome interaction, and experimentation (for a small subset — a "hint" system where partial recipes unlock).

### 7.2 Crafting Stations
Tiered stations gate recipe access (workbench → forge → arcane altar → …). Tech and magic have parallel station lines that eventually cross-pollinate at high tiers.

### 7.3 Item Categories
- Weapons (melee, ranged, magic, hybrid)
- Shields (hand-slot; passive resistance rather than active block — see §5.7 combat notes)
- Armour (light / medium / heavy, plus magic robes and tech suits)
- Jewellery (rings, amulets — carry accessory-style effects)
- Bags (back-slot; increase inventory capacity)
- Back attachments (jetpacks, wings, gliders — non-bag back slot)
- Tools (mining, chopping, building)
- Consumables (potions, tech gadgets — including the **Home Potion**)
- Building materials
- Placeables (furniture, machinery, magical fixtures — including the **Hearth**, **beds**, and portal anchors)
- Ammunition / reagents

Large item count is a design goal (Terraria-scale).

### 7.4 Combat Item Data Model
All combat items are data-driven, defined in JSON. Stats live on the weapon, on the projectile, or on the armour piece.

**Damage types**
Six damage types:
- **Physical** (default)
- **Fire**
- **Cold**
- **Poison**
- **Magic**
- **Void**

Weapons and projectiles deal one or more damage types simultaneously. Armour resists one or more damage types. No elemental "reactions" or interactions between types for MVP — damage is applied independently per type and reduced by matching resistance.

**Common weapon properties**
- Attack rate (swings / shots / casts per second)
- Range
- Mana cost per use (magic weapons only; zero otherwise)
- Ammo requirement
- Damage type(s) tag; damage numbers themselves are per-type

**Melee weapons** — the weapon carries damage:
- Damage per hit, broken down by damage type
- Knockback strength
- Optional special properties

**Projectile weapons** — damage lives on the projectile:
- The **weapon** carries: fire rate, range, mana cost, ammo type.
- The **projectile** carries: damage per hit (per damage type), projectile speed, special properties.

**Magic weapons** follow the same split:
- Projectile magic (fireball wand) uses the projectile model.
- Close-range magic (arcane strike, magic gauntlet) uses the melee model.
- All magic weapons carry a mana cost per use.

**Projectile special properties (all in scope for MVP)**
- **Piercing** — passes through multiple enemies.
- **Splash** — area damage on impact.
- **Homing** — tracks the nearest valid target after firing.
- **Status effects** — applies a status (burn, poison, freeze, etc.) on hit.

**Ammo (MVP scope)**
- Melee and most magic use no ammo.
- Firearms, bows, crossbows require ammo.
- **MVP ships basic ammo only** — standard arrow, standard bullet. Variants (fire arrows, armour-piercing rounds) are post-MVP.

**Armour data model**
Armour is defined by **resistances**, not a single flat defence value.
- Each armour piece can have resistances to one or more of the six damage types.
- Resistance is a percentage reduction (0% = no protection). **Per-damage-type resistance is capped at 75%** — a player cannot achieve full immunity to any damage type from armour alone.
- Additional properties (per piece): movement modifier, mana regen modifier, other secondary effects — TBD.
- Armour equips to the five armour slots defined in §5.7 (head, chest, gloves, legs, boots).
- Armour may carry secondary effects; large accessory-style effects concentrate in the ring / amulet / back attachment slots (§5.7).

**Weapon and armour durability:** none. Gear does not degrade with use.

**Shields (hand-slot defensive gear)**
Shields are equipped in a hand slot (§5.7) and are **passive** — there is no active block button, consistent with §2.3.

- **Passive resistance contribution:** while equipped, a shield adds to the character's resistance percentages for its listed damage types. Shield resistances stack on top of armour resistances (subject to the caps below).
- **Resistance cap boost:** for damage types the shield resists, the per-type resistance cap is raised by **5%** (baseline 75% → 80%) while the shield is equipped.
- **Exclusions:** shield resistance contribution and cap boost do **not** apply against:
  - **Splash damage** from splash-property projectiles (see projectile properties above).
  - **Void damage** (regardless of source).
- **Design intent:** shields are a strong defensive investment for most encounters, but splash-projectile users and void-damage enemies bypass them entirely. This keeps shield builds meaningful without being universal, and gives splash and void identities as "shield-cracker" damage patterns.

**Design intent:** data-driven weapons and armour let combat variety scale without code changes. Every weapon, projectile, and armour piece is a JSON entry — see §7.5 for how rarity ranges and loot rolls plug in.

### 7.5 Item Rarity, Loot Tables & Generation

**Rarity tiers**
Combat items and gear are generated at one of four rarity tiers:
- **Common**
- **Uncommon**
- **Rare**
- **Legendary**

**Stat ranges by rarity**
Each item definition specifies a **range** (or fixed value) for every stat at every rarity tier. When an item is generated (dropped by an enemy, found in a chest, crafted), each stat is **rolled from the range** for its rarity tier. Two commons are not identical — a lucky roll on a low tier can still feel rewarding.

Higher rarity tiers have better ranges: higher damage, better resistances, faster attack rates, and so on.

**Legendary items**
- Roll at (or near) the maximum end of every stat range.
- Receive a **randomly generated special name** — e.g. "Longsword of Vecna," "Wand of the Shattered Star." Names are assembled from prefix / suffix / thematic pools defined per item type.
- Legendary drops are **very rare** — target frequency is set per loot table (data-driven), and tuned to make Legendary discovery a genuine event.

**Example item definition** *(illustrative)*
```json
{
  "longsword": {
    "common": {
      "damage_type": ["physical"],
      "attack_rate": [0.5, 1.0],
      "range": [2, 2],
      "damage": {
        "physical": [1, 5],
        "fire": 0, "cold": 0, "poison": 0, "void": 0, "magic": 0
      }
    },
    "uncommon": { /* wider/higher ranges */ },
    "rare":     { /* higher again, possibly a small non-physical damage roll */ },
    "legendary":{ /* top of every range, plus generated name */ }
  }
}
```

**Loot tables**
Sources of loot — enemies, chests, world containers, boss drops — reference **loot tables** that specify:
- Which items can drop.
- The rarity distribution (probability of each tier).
- The number of items dropped (single, range, weighted counts).
- Special-case rules (guaranteed drops, first-kill bonuses, boss-unique items).

Loot tables are JSON data, editable without code changes.

**Design intent**
- Stat ranges + rarity effectively replace stats/levels as the numeric progression axis. Better gear = better rolls = more powerful character.
- Legendary drops with generated names give players trophies to keep, name, and share with co-op partners.
- Loot tables give designers control over pacing without code changes.

**Detailed spec:** the full loot table schema, roll math, Legendary name generation, guaranteed drops, first-kill flags, and per-source rules live in `loot-table-spec.md` (companion document).

---

## 8. Multiplayer

### 8.1 Approach
Co-op is a **first-class feature designed in from day one.** All core systems (world sim, save format, entity handling, ownership of placed objects, spawn logic) are architected to support multiple concurrent players from the start.

MVP focuses on **single-player as the initial validation target** — but the codebase must not be single-player-only in its assumptions:
- No global "the player" singletons.
- No hard-coded ownership of placed objects.
- Party-aware Hearth and spawn logic, even with a party of one.
- All entity/event systems assume a player list, not a player.

### 8.2 Model
- **Co-op**, target **6 players**.
- Host-authoritative or dedicated server option — TBD.
- Drop-in / drop-out preferred but not guaranteed for MVP.

### 8.3 Shared World
- All players share the same home world and all discovered portal worlds.
- Shared Hearth means shared respawn point.
- Character progression is per-player (character travels between campaigns).

### 8.4 Networking Notes
Full authoritative-server networking on a mutable Terraria-scale world is one of the biggest technical risks — and the portal system multiplies the number of worlds the server must manage. Networking prototype should happen early, once core systems (world sim, save format) are stable enough to test against.

**Detailed spec:** the full multiplayer architecture — host-authoritative model, session lifecycle, chunk sync, entity replication, combat authority, portal transitions, character sync, bandwidth budget — lives in `multiplayer-spec.md` (companion document).

---

## 9. Audio & Visual

### 9.1 Art
- 2D pixel art, all assets authored in Aseprite.
- Consistent palette and pixel-per-unit standard, defined in §9.4.

### 9.2 Music
- None for MVP; placeholder ambient loops acceptable.
- Full soundtrack post-MVP.

### 9.3 SFX
- Placeholder SFX authored by developer.
- Full pass in post-production.

### 9.4 Rendering & Sprite Standards

**Base tile size:** 16 × 16 pixels per tile. Matches Terraria's convention — widely used, huge community art scale, well-supported by Tiled and Aseprite.

**Pixels per world unit:** 16 pixels = 1 world unit. Position, velocity, and physics work in world-unit space; the tile grid is the primary coordinate system for terrain.

**Character sprite dimensions:**
- **Player character frame:** ~40 × 56 pixels (Terraria-scale — roughly 2.5 tiles wide × 3.5 tall). Visible character sits within this frame with a small margin for animation.
- **NPC sprites:** ~24 × 40 to 40 × 56 depending on the NPC. The Guide is human-scaled; other NPCs vary.
- **Enemy sprites:** wide range. Small critters at ~12 × 12, humanoid enemies around 40 × 56, large elites at 64 × 64+, boss silhouettes at 128 × 128 or larger.

**Item sprite dimensions:**
- Standard items: 16 × 16 to 32 × 32 pixels.
- Weapon sprites: often larger (up to 64 × 64 for greatswords, longbows, staves).
- All items reduce to a consistent inventory-slot representation.

**Rendering:**
- Native rendering at 1× zoom shows tiles at 16 physical pixels each.
- Player-controlled zoom levels supported at 1x, 2x, 3x (adjustable in settings). At 3x zoom, one tile displays as 48 physical pixels.
- Pixel-perfect rendering (no bilinear filtering on tiles or sprites) to preserve pixel-art clarity at all zoom levels.

**Chunk dimensions in pixels:**
- 64 × 64 tiles per chunk (per world-generation-spec §5) = 1024 × 1024 pixels per chunk at 1× zoom.

**Character frame vs tile grid reference:**
Player = ~2.5 tiles wide × ~3.5 tiles tall. A player standing beside a tree, a common enemy, or a doorway should feel proportionally correct — most in-world objects use the same scale reference.

### 9.5 Tile Spritesheet Requirements

Terrain tiles use **auto-tiling** — the visible sprite for each tile depends on which neighbouring tiles share its type. The engine picks the right sprite at runtime; the author provides an atlas covering the required neighbour configurations.

**MVP: Wang / marching-squares tiling**
- **~13–16 sprites per tile type**, covering: centre, 4 edges (top / bottom / left / right), 4 outer corners, 4 inner corners, and isolated tile.
- Uses Godot 4's TileMap Terrain Set feature (Corners and Sides mode) natively. No custom auto-tile code required.
- No random art variants per position in MVP.
- No cross-type blending sheets in MVP.

Rough count: 10 base terrain types × ~13 sprites = **~130 terrain tile sprites** for MVP, plus a small number of wall (background) variants and liquid/effect frames.

**Post-MVP: Terraria-style visual richness**
- **Blob tiling (47+ sprites per tile type)** for smoother, more organic terrain — Godot 4 TileMap supports this via the "Corners and Sides Full" 256-tile mode.
- **3 random art variants per position** picked stochastically per tile — matches Terraria's approach; the reason Terraria's terrain never looks tiled or repetitive.
- **Cross-biome blending sheets** — separate transition atlases for common biome-tile pairs (dirt→stone, sand→dirt, ash→stone) so biome boundaries fade organically rather than snapping hard.
- Consider the **Better Terrain** Godot plugin for deterministic, procedurally-friendly tile placement (relevant given our determinism principles in world gen).

**Authoring workflow**
1. Tiles authored in Aseprite (v1.3+) using its Tilemap Layer feature — enables non-destructive tile editing where changing one tile updates all placed instances.
2. Export as a flat PNG atlas.
3. Import to Godot 4 as a TileSet resource.
4. Configure a Terrain Set with peering bits so the engine knows how tiles connect.

**Placed vs. terrain tiles**
- **Terrain tiles** (dirt, stone, sand, walls) use auto-tiling.
- **Placed tiles** (chests, doors, workbenches, torches, furniture) are single-sprite, no auto-tiling. Author as standalone 16×16 or multi-tile sprites.
- Terraria calls these "framed" tiles (auto-tiling) vs. "frame-important" tiles (placed objects with fixed sprites).

**Learning references**
The following are current, high-quality resources for the tile authoring and Godot integration workflow:

- Godot official TileMap tutorial: https://docs.godotengine.org/en/stable/tutorials/2d/using_tilemaps.html
- Godot 4 TileMap complete guide (all features covered): https://godot-mcp.abyo.net/guides/godot4-tilemap
- Godot 4 Terrains autotile step-by-step walkthrough: https://uhiyama-lab.com/en/notes/godot/terrains-autotile-setup/
- Aseprite tilemapping tutorial (creates a tileset from scratch): https://itch.io/t/6637822/aseprite-tutorial-tilemapping
- Auto-tiling techniques compared (Wang / blob / marching squares): https://excaliburjs.com/blog/Dual%20Tilemap%20Autotiling%20Technique/
- Better Terrain Godot plugin (deterministic auto-tiling for procedural generation): https://github.com/Portponky/better-terrain
- Example CC0 blob-autotile tileset for reference: https://dandeliondino.itch.io/overworld-autotiles

---

## 10. Technical

### 10.1 Engine & Language
- **Engine:** Godot 4.x
- **Scripting:** C# for simulation/hot paths (world sim, lighting, pathfinding), GDScript acceptable for UI and glue code.

### 10.2 Data Format
- **Game data** (recipes, items, biomes, enemies, loot tables, portal world definitions) stored as JSON files, loaded at boot.
- Data-driven design goal: designers can add content without touching code.

### 10.3 Save Format
- Requirement: **not trivially editable** by casual players — deter, don't prevent.
- Approach (proposed):
  - Binary serialisation with a versioned header.
  - Save contents compressed (zstd or gzip).
  - Lightweight obfuscation layer (e.g., XOR with a rotating key derived from the save's own metadata) — enough to defeat "open in Notepad and change a number."
  - Integrity hash (e.g., SHA-256 of payload) stored in the header; mismatch = "save modified" warning, not a hard block.
- Explicitly **not** doing signed/encrypted saves — determined cheaters will always win; the bar is "casual players can't just edit a text file."

**Detailed spec:** the full save format, directory layout, envelope structure, obfuscation scheme, and migration strategy live in `save-format-spec.md` (companion document).

### 10.4 World Storage
- Chunked world representation for streaming/save efficiency.
- Chunks serialised individually so partial-world updates are cheap.
- Multi-world saves: each discovered world (home + portals) is its own save shard, referenced by a campaign manifest.

### 10.5 Modding
- Deferred decision. JSON data-driven approach makes light modding natural even if not officially supported.

---

## 11. Ideas for Differentiation

Beyond stats, the tech/magic mix, and the portal system, some further directions worth considering:

- **Automation lite.** Lean into the tech theme with simple automation (conveyor belts, sorters, basic logic gates). Not Factorio-deep, but enough to make late-game bases feel alive. Magic could have a parallel with familiars or golems that perform tasks.
- **Rituals as a magic mechanic.** Rather than magic being "cast spell = damage," rituals could require placed components, time, and reagents for powerful effects. Feels distinct from tech's crafting-then-use loop.
- **World reactivity.** Chopping too many trees corrupts a forest biome; over-mining destabilises caves. Gives the sandbox teeth.
- **Faction NPCs.** Tech-aligned and magic-aligned NPC groups with reputation — helps flavour the world and gates certain recipes.
- **Character death consequences (optional mode).** A "hardcore lite" mode where death is meaningful but not permadeath — lose stat points, drop items, etc. Complements the character-first design.
- **Shared-world persistent events for co-op.** World events that scale or change based on party size, giving multiplayer runs their own flavour rather than being "singleplayer with extra people."
- **Portal world uniqueness.** Beyond materials, each portal world could have a unique mechanic — one has permanent darkness, one has low gravity, one has periodic reality shifts. Gives players stories to tell.

Pick and choose; not all of these belong in MVP.

---

## 12. Scope & MVP

### 12.1 MVP Definition
The smallest thing that proves the game works — including the full portal loop end-to-end:
- One home world biome (or a small mix), procedurally generated with seed sharing.
- **Home world main boss** — the guardian.
- **NPC — Guide** functional — appears at spawn, offers ongoing hints, sells items, moves into a valid room in the base.
- Primary anchor extraction after main boss defeat.
- **One side anchor** with a **mini boss** to prove the side-anchor mechanic.
- **One portal world** at "slightly harder" tier with one exclusive material and its own **main boss**.
- Character creation with cosmetics and starting archetype functional.
- ~20 items across tech and magic branches, all wired into the rarity + loot table system (§7.5).
- Core loop: gather → craft → fight main boss → activate portal → dive → fight portal main boss.
- Hearth + Home Potion + anchor pickup/placement functional.
- **Single-player validation target**, but built on multiplayer-capable architecture (see §8.1).
- JSON data pipeline in place.
- Save/load functional with the deterrent format, including multi-world save shards.
- Standard and Hardcore difficulty modes both functional (Hardcore proves the death container).
- **Full controller input support** (Steam Deck day-one requirement).
- **Native Linux build** with rendering scaling verified at 1280 × 800 (Steam Deck resolution).

### 12.2 Post-MVP
- Multiplayer.
- Additional home biomes and portal worlds.
- Full item roster.
- Additional bosses (area, event, additional main and mini bosses).
- More portal worlds accessible from the home world.
- Music, final SFX.
- Perks / skill trees.
- Automation / ritual systems.

### 12.3 Out of Scope (for now)
- 3D / any perspective change.
- Console ports.
- Mod tooling.
- Structural integrity simulation.
- **Nested portals** (scrapped — portal worlds are terminal).

---

## 13. Risks

| Risk | Impact | Mitigation |
|------|--------|------------|
| Multiplayer complexity on mutable world | High | First-class design from day one; prototype netcode early once core systems stabilise |
| Multi-world save/load complexity | Medium | Design the shard-based save format up front, before any world code stabilises |
| Feature creep (tech + magic + stats + portals + automation…) | High | Ruthless MVP scoping; each pillar earns its way in |
| Performance at Terraria-scale simulation | Medium | C# for hot paths from day one; chunked design |
| Solo dev burnout | High | Ticket-driven work with clear milestones |
| Content volume (recipes, items, portal worlds) | Medium | Data-driven JSON pipeline lets non-code content scale |
| Boss design bandwidth | Medium | Bosses are the pacing spine; each tier needs a memorable one. Reuse phases/patterns where possible. |
| Non-determinism creeping into world gen | Medium | Enforce via CI (regenerate reference seed + hash check) from day one. Discipline in code review — no wall-clock inputs, no unordered iteration in generation logic. |
| Controller UX complexity | Medium | Steam Deck as day-1 target means controls must feel native on gamepad, not tacked-on. Test on Steam Deck hardware early. Right-stick aim less precise than mouse; may need aim assist for ranged/magic combat. |
| UI readability on Steam Deck (7-inch, 1280×800) | Medium | Design UI with minimum touch/click targets and font sizes suitable for handheld. Test on device early; don't assume desktop UI scales down cleanly. |

---

## 14. Open Questions

**Portals & worlds** — all decisions locked.

**General**
All initial general design questions resolved. New questions will accumulate here as design deepens (numeric tuning, edge cases, post-MVP feature specs).
- Inventory model — Terraria-style slots or something denser?
- Progression length — target hours to "credits roll" moment?

---

*This is a living document. Sections will be broken into feature specs and then into GitHub issues as design decisions firm up.*
