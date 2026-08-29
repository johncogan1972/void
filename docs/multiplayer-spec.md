# Multiplayer & Networking Architecture — Feature Spec

**Version:** 0.2
**Status:** Draft
**Companion to:** GDD §8, world-generation-spec, save-format-spec, combat-spec, loot-table-spec

---

## 1. Overview

Defines the networking architecture for co-op multiplayer: how players connect, how the world state stays synchronised, how authority is assigned for gameplay decisions, and how the system scales to the game's shape (multiple concurrent worlds, tile-level mutability, party-shared spawn).

**Scope:** target architecture for post-MVP multiplayer. MVP ships single-player, but MVP code must respect the architectural principles here so multiplayer implementation later does not require system rewrites.

## 2. Requirements Recap

From GDD §8 and cross-referenced specs:

- **Co-op, target 6 players.**
- **First-class from day one** — no singletons, no "the player" assumptions, party-aware Hearth from the start.
- **Host-authoritative** with a design path to dedicated server post-launch.
- **Drop-in / drop-out** preferred but not guaranteed for initial multiplayer release.
- **Shared world and portal worlds** across the party. All players share the same home world and every discovered portal world.
- **Shared Hearth** = shared respawn point.
- **Character progression is per-player** — characters travel between campaigns carrying their own state.
- **Multi-world save shards** (per save-format-spec) — each world is its own file structure.
- **Authoritative combat and loot** (per combat-spec and loot-table-spec) — server rolls damage and loot; clients render results.
- **Determinism does not extend to runtime** — world generation is deterministic per seed, but combat and loot rolls are not.

## 3. Architecture Model

**Model:** host-authoritative client/server, with the host running an embedded server alongside their client. Non-host players run client-only.

- **Host** simulates the world (chunks, entities, physics, combat, loot rolls) and holds the source-of-truth state.
- **Clients** send inputs to the host and receive replicated state.
- **The host is also a player** — their local client submits inputs to the local server just like remote clients, keeping the code path uniform.

**Why host-authoritative:**

- Simpler to ship than dedicated server infrastructure.
- Fits a solo-dev budget — no server hosting costs, no online services to maintain.
- Matches Terraria's proven pattern for the same genre.

**Why "design path to dedicated":**

- The client/server code split is real from day one — no shortcuts that assume client and server are the same process.
- Post-launch, a dedicated server binary can be extracted with modest work: the host's server component runs standalone.
- Community-hosted persistent servers become a natural post-launch feature.

**What this means in practice:**

- Server code has no direct access to client-only concerns (rendering, input polling, audio).
- Client code never mutates world state directly — it sends intent (via input events or RPCs) and reacts to replicated state.
- Any code path that reads world state from the client without going through replication is a bug to be caught in review.

## 4. Networking Library Choice

**Primary:** Godot 4's high-level multiplayer API (built on ENet). Provides:
- Reliable ordered messages for critical events.
- Unreliable messages for high-frequency updates.
- Built-in RPC and MultiplayerSynchronizer / MultiplayerSpawner nodes.
- Cross-platform, actively maintained, well-integrated with Godot.

**Discovery / connection:** Steam networking (via GodotSteam or equivalent) for the primary UX — friend invites, lobbies, no port forwarding. Direct IP connection as fallback for LAN and non-Steam scenarios.

**Fallback consideration:** ENet directly (bypassing Godot's high-level API) if we hit ceilings on the high-level API's flexibility. Unlikely to be needed for target player count.

## 5. Session Lifecycle

### 5.1 Session start (host)

1. Player selects "Host game" from campaign menu.
2. Host loads campaign state (all world manifests, currently-active Hearth world's chunks around Hearth).
3. Server component starts and begins listening for connections.
4. Host's client connects to the local server as player 1.
5. Host receives replicated state exactly as a remote client would (no shortcuts).

### 5.2 Client join

1. Client selects "Join game" and connects to host (via Steam or direct IP).
2. Handshake: exchange protocol version, game version, campaign ID.
3. **Character submission:** client uploads their character data (per save-format-spec §5 character payload). Host validates.
4. Host assigns player slot in campaign, chooses spawn point (current Hearth location).
5. Host streams initial world context to client: the world manifest of the world containing the Hearth, plus chunks around the spawn point (~9×9 chunks per world-generation-spec §5).
6. Client acknowledges receipt, spawns their player entity in the world.
7. Regular replication begins.

### 5.3 Client leave / drop

- **Graceful leave:** client sends a leave message. Host despawns their player entity, sends the character's updated state back to the client so their local character file can be saved. Host removes them from the party.
- **Ungraceful drop:** timeout expires without heartbeat. Host despawns the player entity but retains a grace window (~60 seconds) for the client to reconnect and resume from where they were. After the window, they're fully removed and their character file may be out of date (the client hopefully saved before crashing).

### 5.4 Host disconnect

- All clients are disconnected.
- Each client's character state was last synced from the host — if the client saved recently, they lose only the delta since last sync.
- **No host migration for MVP.** Post-MVP could add.

### 5.5 Drop-in / drop-out

- **Drop-in** (joining an in-progress session): fully supported — same flow as §5.2.
- **Drop-out**: fully supported — same as §5.3.

## 6. World and Player Association

At any moment, each connected player is associated with exactly one world (home world or a specific portal world). The server tracks this mapping:

```
player_id → active_world_id
```

Consequences:
- A player in the home world receives no updates from portal worlds and vice versa.
- Players in different worlds are effectively in different simulations — no cross-world visibility.
- Chat may still cross worlds (party-wide) but gameplay state does not.

The **Hearth** is party-shared but exists in exactly one world (per GDD §4.7). Players respawning appear at the Hearth's location, which may be in a different world than where they died.

## 7. Chunk Synchronisation

Chunks are the streaming unit for world state.

### 7.1 Interest sets

Each player has an **interest set** — the chunks around their position that they need to render and interact with. Per world-generation-spec §5, the interest window is 9×9 chunks centred on the player.

The server maintains a per-player interest set for each world. When a player moves and their interest set shifts:
- **New chunks entering interest:** server sends full chunk state to the client.
- **Chunks leaving interest:** client may cache them briefly for backtracking, or discard them.

### 7.2 Chunk snapshot on entry

When a chunk enters a player's interest set, the server sends its current state as a single reliable message:
- Chunk header (biome, layer, flags)
- Tile array (compressed with zstd)
- Persistent entity references within the chunk

For a 64×64 chunk at 8 bytes/tile: ~32KB uncompressed, ~4-8KB compressed. Sending 9×9 = 81 chunks on join transfers ~500KB total — brief but tolerable one-time load.

### 7.3 Tile delta broadcasts

When any tile in an interest-set chunk changes:
- Server broadcasts a **tile delta** to every client whose interest set includes that chunk.
- Delta format: `(chunk_id, tile_offset, new_tile_data)` — ~16 bytes per changed tile.
- Reliable ordered delivery within a chunk (order matters for chained changes).

Consequence: bulk changes (a player mining 20 tiles in a burst) produce a stream of small deltas. Under active mining, ~200–500 bytes/sec per active area. Sustainable.

### 7.4 Compression and batching

- Multiple tile deltas within the same tick are batched into one message.
- Deltas targeting the same chunk within the same tick may be run-length encoded.
- Chunk snapshots on entry are always zstd-compressed.

## 8. Entity Synchronisation

Non-tile game state — enemies, projectiles, dropped items, NPCs, players — is replicated as **entities**.

### 8.1 Entity classification

- **Player entities** — one per connected player. Special handling for local prediction (see §11).
- **NPC entities** — the Guide and discovered NPCs.
- **Enemy entities** — mobs, elites, bosses.
- **Projectile entities** — arrows, bullets, spells in flight.
- **Item entities** — dropped loot on the ground.
- **Interactive entities** — chests, doors, portals, anchors.

### 8.2 Interest-based replication

Each entity has a position. The server replicates entity state only to clients whose interest set covers that position.

- **Entity enters interest:** server sends full entity spawn (type, ID, position, state).
- **Entity in interest:** server periodically sends state updates (position, health, animation state) — see §8.3.
- **Entity leaves interest:** server sends despawn message; client removes the entity from its local view.

### 8.3 Update rates

- **Server tick rate:** 30 Hz (33ms per tick) for physics and combat resolution.
- **Client send rate:** 20 Hz (50ms) for entity updates to clients.
- **Client interpolation:** clients interpolate between the last two received snapshots for smooth motion at their own frame rate.

### 8.4 Delta vs full updates

- **Delta updates** for high-frequency changes (position, animation state) — sent unreliably; if lost, the next snapshot corrects.
- **Reliable events** for one-shot occurrences (took damage, applied status effect, fired a projectile) — sent reliably ordered.
- **Full state resync** on entity re-entering interest — the client gets the current authoritative state as a snapshot.

## 9. Player Replication and Input

### 9.1 Input flow

- Client polls input each frame.
- Client sends input as compact events to server (movement direction, click actions, hotkey presses) — sent unreliably at 20 Hz batched.
- Server receives inputs, applies them to the authoritative player state, produces resulting world state changes.

### 9.2 Client-side prediction (own player)

For smooth own-player movement despite server round-trip latency, the client predicts its own player's movement locally:

- Client applies inputs to a local prediction of own-player state.
- Server also applies the same inputs authoritatively.
- Server-authoritative state periodically arrives back at the client.
- Client reconciles: if predicted state matches authoritative, no correction. If it diverges, client snaps or smoothly corrects.

Prediction covers movement, jumping, and animation state — **not** damage resolution, mana consumption, or inventory changes (those are authoritative-only).

### 9.3 Remote player rendering

- Remote players are rendered from server snapshots with interpolation between the last two received states.
- Small position corrections are smoothed over a few frames.
- Large teleports (portal transitions, respawn) are snapped instantly.

## 10. Combat Authority

Per combat-spec, all damage resolution is server-authoritative.

### 10.1 Attack flow (client → server → clients)

1. Client detects a "attack" input (left-click).
2. Client sends **AttackIntent** to server with weapon hand, aim position, timestamp.
3. Server validates: is the player alive, in weapon mode, weapon equipped, mana/ammo sufficient, cooldown elapsed?
4. If valid, server begins the attack lifecycle (wind-up / active / recovery).
5. During active phase, server checks hitboxes / projectile collisions.
6. On hit, server resolves damage per combat-spec §4 and broadcasts **HitEvent** to interested clients.
7. Clients render the hit visually (hit-stop, knockback, damage numbers).

### 10.2 Cheat resistance

Because damage is server-side, a compromised client cannot:
- Deal higher damage than their weapon defines.
- Bypass resistances or immunities.
- Roll better loot than their table defines.

They can, however, misreport their own position or actions unless the server validates further (rate-limits, sanity checks on movement speed, etc.). Host-authoritative doesn't defend against a malicious host — accepted trade-off per the "not cryptographic" ethos.

## 11. Loot Authority

Per loot-table-spec, all loot rolls happen on the server.

- Enemy death → server rolls loot table → broadcasts spawned item entities to interested clients.
- Chest first-open → server rolls contents → broadcasts item entities to the opener's client (chest state is now `is_opened = true`, replicated to all).
- Legendary names are generated server-side and stored in the item instance data.

## 12. Multi-World Handling

The server may run multiple worlds simultaneously — the home world and any portal worlds currently containing players.

### 12.1 World lifecycle on the server

- **World idle:** no players present. World is unloaded from active memory (save shards remain on disk).
- **World active:** at least one player present. World is loaded and simulating.
- **Chunks within an active world** follow their own load-around-players logic per world-generation-spec §5.

Server loads/unloads worlds dynamically as players transition through portals.

### 12.2 Simulation per world

Each active world simulates independently:
- Its own tick clock.
- Its own physics and combat resolution.
- Its own enemy AI updates.
- Its own event bosses / world events.

Enemies in an unloaded world are paused — they resume when the world loads again.

### 12.3 Bandwidth per world

Each client receives updates only for the world they're currently in. A player in a portal world sees no updates from the home world. This keeps total bandwidth linear in "players actively engaging one world" rather than in "worlds the server has loaded."

## 13. Portal Transitions

When a player enters a portal:

1. Client sends **PortalEnterIntent** to server.
2. Server validates: player is at the portal, portal is activated (per world-generation-spec §4).
3. Server:
    - Removes the player entity from the source world.
    - Ensures the destination world is loaded (loads it if idle).
    - Places the player entity in the destination world at the portal's exit location.
    - Updates player-to-world mapping.
4. Server unsubscribes the client from source-world updates and subscribes them to destination-world updates.
5. Server streams destination-world chunks around the entry point to the client.
6. Client sees a brief transition (loading screen or fade) then arrives.

**Party portal use:** the party doesn't have to travel together. Player A can be in the home world while Player B is in a portal world — the server handles both worlds simultaneously.

## 14. Character Synchronisation

Per GDD §8.2 and save-format-spec §3, characters travel between campaigns. When joining a session, a client submits their character; when leaving, they receive their updated character.

### 14.1 Join

- Client uploads its character file (see save-format-spec §5).
- Server validates format and version.
- Server stores the character state in memory alongside the player's session state.
- All character-level state (inventory, gear, mana pool state, difficulty mode) is now source-of-truth on the server for the session.

### 14.2 During play

- Character state changes (inventory pickups, gear swaps, mana consumption) are all server-authoritative.
- Server periodically pushes character snapshot updates back to the client — mainly for UI display and for the client to have a recent copy in case of a disconnect.

### 14.3 Leave

- On graceful leave, server sends the final character state to the client.
- Client saves updated character file locally.
- Server drops the character state from memory.

### 14.4 Character conflicts

If a player somehow joins a session with a character file that's newer than the server-held version (edge case, shouldn't happen in normal use), server rejects the join and asks the client to reconcile. Simple version-check based on character save timestamp.

## 15. Latency Compensation

Target: reasonable co-op play at 50–150ms RTT.

### 15.1 Player movement

- Client-side prediction (§9.2) hides local input latency.
- Interpolation for remote players (§9.3) smooths their motion.

### 15.2 Attack timing

- Attacks are timestamped on the client and validated server-side.
- Server accepts attacks within a small window of "when the server would have received them by now" to allow for latency.
- No lag compensation for hitscan-style shots (backwards raycast) — projectile-based combat sidesteps most of that class of problem, since projectiles have flight time.

### 15.3 Predicted feedback

Client can play immediate visual/audio feedback for its own actions (swing animation, click sound) without waiting for server acknowledgement. If the server rejects the action, the client rolls back the feedback.

## 16. Bandwidth Budget

Targets per player:

| Direction | Target | Peak |
|-----------|--------|------|
| Down (server → client) | ~25 KB/sec | ~80 KB/sec during heavy activity |
| Up (client → server) | ~5 KB/sec | ~15 KB/sec during heavy input |

Load contributors:
- Entity updates dominate steady-state bandwidth (~15 KB/sec down for a busy area).
- Tile deltas spike during building / mining (~10 KB/sec extra during heavy edits).
- Chunk snapshots on entry are one-shot bursts, spread out as the player moves.

Numbers to validate in prototype. Godot's high-level MP has some overhead that will be visible in profiling.

## 17. Cheat Protection

Server-authoritative model gives us:

- Damage: server-computed, cannot be inflated by client.
- Loot: server-rolled, cannot be re-rolled by client.
- Mana / ammo: server-tracked, cannot be topped up by client.
- Inventory: server-tracked, cannot be duplicated by client.

Server-side rate limits and sanity checks:
- Movement speed capped to a reasonable maximum per class.
- Attack rate capped by weapon's attack_rate.
- Tile modifications validated (correct tool, correct range, mining time elapsed).

**Not defended against:**
- Malicious host (host-authoritative model concedes this — private co-op sessions with friends).
- Client-side rendering tricks (map hacks visualising un-seen chunks — depends on client not receiving those chunks in the first place; the interest set model already gates this).

## 18. Post-MVP: Dedicated Server

Because the client/server split is real from day one, extracting a dedicated server binary is a modest post-launch project:

- Take the server component out of the host process.
- Ship it as a standalone executable.
- Add configuration (world seed, port, MOTD, admin controls).
- Consider a matchmaking / server browser service (out of scope for spec but noted).

Community-hosted persistent servers become a natural feature for long-tail engagement.

## 19. Testing & Validation

- **Local host smoke test:** host + 1 client on the same machine (localhost) — validates the entire pipeline without network variance.
- **LAN test:** host + 1 client on same LAN — validates real network variance without WAN latency.
- **Simulated latency:** LAN + artificial delay (100ms, 200ms) — validates latency compensation.
- **Party size stress:** host + 5 clients — validates target load at max party size.
- **Chunk churn test:** one player mining rapidly while another watches — validates tile-delta broadcast bandwidth and correctness.
- **Portal transition test:** two players in the same world, one enters a portal — validates world lifecycle and per-player subscription updates.
- **Drop-in / drop-out test:** clients repeatedly join, play briefly, and leave — validates character sync doesn't lose state.
- **Host disconnect test:** host quits mid-session — validates clients receive clean disconnect and can rejoin a new session.
- **Character travel test:** client joins campaign A with character X, leaves, joins campaign B with same character — validates character state is preserved and portable.

## 20. Resolved Design Decisions

All original open questions locked in v0.2:

1. **Steam integration timing:** ship first MP release with **direct IP only**. Add Steam networking (lobbies, friend invites, presence) as a post-launch UX upgrade. GodotSteam or equivalent is the target integration.
2. **Voice chat:** **out of scope for the game.** Players use Steam voice or Discord.
3. **Text chat scope:** **party-wide.** Works across worlds — parties often split up between worlds and need to coordinate.
4. **Chunk delta throttling:** **buffer with soft cap + server-side back-pressure.** If a client's delta backlog exceeds the cap, the server applies a small cooldown to that client's mining/building actions. Cap value tuned in prototype.
5. **Server tick rate:** **30 Hz for MVP+1.** Upgrade to 60 Hz only if playtesting reveals genuine feel problems.
6. **Interest set size:** **9×9 chunks per player**, monitor server memory in prototype. Drop to 7×7 if memory or CPU tightens.
7. **Cross-world party UI:** **a party panel** showing member name, current world, health, and distance/location hint. Not a full map — just enough for the party to find each other across worlds. Detailed UI design deferred to a UI pass.
8. **Host migration:** **not supported.** Session ends when host leaves. Post-MVP dedicated server binary is the permanent solution — persistent servers mean no one "is" the host.
9. **Anti-cheat for public matchmaking:** **not on the roadmap.** The game is designed for friend-group co-op. Public matchmaking would be a different product design.
10. **Save conflict resolution:** **latest-timestamp wins**, with a UI warning if a client's local character is newer than server state ("Your character has been played more recently in another session. Which do you want to use?").
