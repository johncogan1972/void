# Save Format — Feature Spec

**Version:** 0.2
**Status:** Draft
**Companion to:** GDD §10.3, world-data-model-spec.md

---

## 1. Overview

Defines how the game persists character, campaign, and world state to disk. Covers directory layout, per-file byte format, compression, obfuscation, integrity checks, versioning, and save/load flow.

The data *content* (fields per file) is defined by `world-data-model-spec.md`. This document is about how those structures serialise, protect themselves against casual editing, and survive crashes and format changes.

## 2. Design Principles

Direct pull from GDD §10.3:

- **Deter, don't prevent.** Casual players (open Notepad, change a number) are blocked. Determined cheaters are not the target — cryptographic security is out of scope.
- **Binary format** with a versioned header on every file.
- **Compressed** (zstd) — smaller files, faster I/O.
- **Lightweight obfuscation** (XOR with a rotating key derived from save metadata) — enough to defeat casual editing.
- **Integrity hash** (SHA-256) — mismatch triggers a warning, not a hard block.
- Explicitly **not** cryptographically signed or encrypted.

Additional principles for this doc:

- **Debuggable during development.** A dev/debug mode saves the same content unobfuscated and uncompressed (or as JSON) for easy inspection. Ship builds always use the full format.
- **Chunk saves are cheap.** Only modified chunks are re-serialised on eviction; untouched chunks live once on disk and never rewrite.
- **Crash-resilient.** Writes are atomic per file — a crash mid-save leaves the previous version intact.

## 3. Directory Layout

```
saves/
├── characters/
│   ├── <character_uuid>.character            # one file per character (Terraria-like split)
│   └── ...
├── campaigns/
│   └── <campaign_uuid>/
│       ├── campaign.manifest                 # campaign-level state
│       ├── worlds/
│       │   └── <world_uuid>/
│       │       ├── world.manifest            # world-level state
│       │       ├── chunks/
│       │       │   ├── 0_0.chunk
│       │       │   ├── 0_1.chunk
│       │       │   └── ...
│       │       └── entities/                 # persistent entities (chest contents, NPCs, etc.)
│       │           └── <entity_uuid>.entity
└── settings/
    ├── controls.json                          # keybinds — NOT obfuscated (user-editable by design)
    └── graphics.json
```

**Character/campaign split:** matches GDD §8.2 — characters travel between campaigns carrying their inventory and gear. World progress lives with the campaign. In MVP with single-player + single campaign this is trivially 1 character and 1 campaign, but the structure supports both scaling.

**File naming:** UUIDs prevent collisions when characters/campaigns migrate between installations or copies. Human-readable names (from character creation) live *inside* the files, not in filenames.

**Settings are plain text.** Keybinds, graphics preferences, audio levels — user-editable by design. Not part of the deterrent-format save data.

## 4. Common File Format

All save files (except plain-text settings) share this outer envelope:

```
[Envelope Header — 96 bytes]   little-endian throughout
  off  size  field
    0     4  magic              : uint32       # "MSAV" — quick sanity check
    4     2  format_version     : uint16       # schema version of the payload
    6     2  envelope_version   : uint16       # version of the envelope format itself
    8     1  file_kind          : uint8        # 1=character, 2=campaign_manifest, 3=world_manifest, 4=chunk, 5=entity
    9     1  flags              : uint8        # bit 0: obfuscated, bit 1: compressed, bit 2: debug (plaintext)
   10     2  reserved           : uint16
   12     4  payload_size       : uint32       # uncompressed size
   16     4  compressed_size    : uint32       # size of body on disk
   20     8  seed_input         : uint64       # key-derivation input (§7)
   28     4  file_salt          : uint32       # key-derivation input, randomised per write (§7)
   32     8  reserved           : bytes[8]
   40    32  integrity_hash     : bytes[32]    # SHA-256 of payload (pre-obfuscation, pre-compression)
   72    12  reserved           : bytes[12]
   84    12  reserved           : bytes[12]    # padding to 96
  ---------
         96

[Body]
  # Ship mode:  XOR(zstd(payload), keystream)
  # Debug mode: raw payload bytes (no compression, no XOR)
```

All reserved bytes are written as zero and ignored on read, so a future field
can claim a reserved slot without an `envelope_version` bump (§9).

**Order of operations on save:**

1. Serialise payload → raw bytes.
2. Compute SHA-256 of raw bytes → `integrity_hash`.
3. Compress with zstd → compressed bytes.
4. Derive keystream from `seed_input`, `file_salt`, and format-version (see §7).
5. XOR compressed bytes with keystream → body bytes.
6. Assemble header + body, write to disk atomically (§10).

**Order of operations on load:**

1. Read header, validate `magic` and `envelope_version`.
2. Derive keystream from same inputs.
3. XOR body bytes with keystream → compressed bytes.
4. Decompress with zstd → raw payload bytes.
5. Compute SHA-256 of raw bytes.
6. Compare to `integrity_hash`. Mismatch → user-visible warning ("This save has been modified. Continue?") but load proceeds if confirmed.
7. Deserialise payload into game structures.

## 5. Payload Formats

Every payload is a versioned binary structure. Fields per file type come from `world-data-model-spec.md`.

**Payload encoding:**

- Little-endian for all multi-byte integers.
- Length-prefixed strings (uint16 length + UTF-8 bytes).
- Length-prefixed arrays (uint32 count + elements).
- Optional fields use an explicit "present" flag byte.

**Per-file-kind payloads:**

- **Character** (`file_kind = 1`): character UUID, display name, cosmetic data (sprite variant, palette choices), starting archetype, current campaign UUID (nullable), inventory contents (up to 10 base + bag slots), worn equipment (all 10 armour/back/jewellery slots), hotbar contents (10 slots), currently active hand + tool state, difficulty mode (Standard / Hardcore), current HP and mana pool state, active buffs and their remaining durations, discovered recipes.

- **Campaign manifest** (`file_kind = 2`): campaign UUID, created-at timestamp, list of world entries (each with world UUID, world type, discovered-at timestamp, manifest path), current campaign-wide flags (e.g. "main boss killed on home world"), discovered NPC list (per campaign).

- **World manifest** (`file_kind = 3`): all fields from `world-data-model-spec.md §4` — seed, gen version, size, layer boundaries, spawn point, boss lair location, portal candidate list, side anchor state, active Hearth location, chunk index.

- **Chunk** (`file_kind = 4`): the chunk structure from `world-data-model-spec.md §3` — chunk header, metadata block, tile array, entity refs.

- **Entity** (`file_kind = 5`): persistent entity state — chest inventories, NPC positions and dialogue flags, deployed portal anchors. One entity per file to keep them independently loadable.

## 6. Compression

- **Algorithm:** zstd, via the `ZstdSharp.Port` NuGet package (pure managed, no native
  dependency, works both inside Godot and in the plain xunit harness). Standard zstd
  frames, so save bodies remain readable by external zstd tooling.
- **Compression level:** 3 (default balance of speed and ratio). Room to tune later.
- **Compression is per-file**, not across files. Simpler to load individually.
- **Dictionaries:** not used for MVP. If chunk save size becomes an issue, a chunk-specific zstd dictionary trained on representative data could reduce chunk size ~30–50%. Post-MVP optimisation.

## 7. Obfuscation

Purpose: prevent trivial editing. Nothing more.

**Keystream derivation:**

```
Given: seed_input (uint64) and file_salt (uint32) from the envelope header.

key_material = SHA-256(
    seed_input      as bytes ||
    file_salt       as bytes ||
    format_version  as bytes ||
    magic           as bytes
)

Seed xoshiro256++ with the first 32 bytes of key_material.
Generate keystream bytes by pulling from the RNG until the body length is met.
```

**XOR:** simple byte-wise `body[i] = compressed[i] XOR keystream[i]`.

**Per-file variation:**

- `seed_input` is the world's or campaign's seed. Deterministic per save.
- `file_salt` is randomised on every write (from a non-deterministic source — this is one place where non-determinism is fine because the file has to work standalone).
- Result: every write of the same chunk produces a completely different byte pattern on disk, defeating "diff two saves to find what changed."

**What this protects against:**

- Opening a save file in a hex editor and changing visible numbers.
- Reading tile IDs directly from disk to see what's around a chest.
- Finding chest contents by grep.

**What this does NOT protect against:**

- Anyone reverse-engineering the game binary to extract the format and key derivation.
- Automated tooling that implements the full read/write cycle.

That's the accepted trade-off — cryptographic security is out of scope per GDD §10.3.

## 8. Integrity Check

**Hash:** SHA-256 of the **raw payload bytes** (before compression, before XOR). Stored in the envelope header.

**On load:**
- If the recomputed hash matches → save is intact.
- If it doesn't match → the save has been modified. Show a user-visible warning:

  > "This save file appears to have been modified. Loading may cause unexpected behaviour. Continue anyway?"

- Player can choose to load anyway. Never a hard block (per GDD).

**Why hash the payload, not the file bytes:**

- Payload hashes are stable across implementation changes to compression or XOR.
- Compression parameters can change (e.g. we tune zstd level) without invalidating hashes.
- The file salt randomises the on-disk bytes; hashing them would be useless.

## 9. Versioning & Migration

Every file has a `format_version` (payload schema version) and an `envelope_version` (envelope schema version). These evolve independently.

**Envelope version rules:**

- Envelope changes are extremely rare (adding a field to the header).
- Old envelope versions are readable indefinitely — the reader picks the right parser based on `envelope_version`.

**Payload version rules:**

- Every payload schema has a version bump when fields are added, removed, or reinterpreted.
- **Additive changes** (new optional fields) are backward-compatible — the reader skips unknown trailing bytes gracefully; the writer starts including the new field.
- **Breaking changes** (removed or reinterpreted fields) require a migrator.

**Migrator library:**

- A registry of migration functions keyed by `(file_kind, from_version, to_version)`.
- On load, if the file's `format_version` is below the current version, migrations are chained in order until it reaches current.
- Migrations run in memory; the file is not touched until the next save writes the new version.

**Determinism guarantee (recap):** the world's *content* is stable across game versions once generated (it's on disk). Only *new* world generation is affected by game version. See GDD §3.2.

## 10. Save/Load Flow

### 10.1 Atomic write

Every save file is written using the standard temp-and-rename pattern:

1. Compute the target path (e.g. `worlds/<world_uuid>/chunks/0_5.chunk`).
2. Write to `worlds/<world_uuid>/chunks/0_5.chunk.tmp`.
3. Fsync the temp file.
4. Rename over the target (atomic on POSIX; nearly atomic on Windows via `MoveFileEx`).
5. Delete any stale `.tmp` file older than N seconds on startup (crash cleanup).

A crash during step 2 or 3 leaves the previous file intact — no corrupted saves.

### 10.2 Save events

Saves happen at these points:

- **World creation.** Full write of world manifest + all chunks (this is the pre-generation flush; can be many MB).
- **Chunk eviction from the LRU cache.** Only if `modified` flag is set.
- **Player triggers "save & quit."** All dirty chunks + campaign manifest + character files flushed. This is the "clean" checkpoint.
- **Autosave.** See §11.
- **Significant world events.** Main boss killed, portal anchor picked up, Hearth moved — trigger targeted saves of the affected manifest.

### 10.3 Load events

- **Campaign load.** Read campaign manifest → read world manifest for the world the player is entering → load chunks around the player's spawn.
- **World transition through a portal.** Save modified chunks in the outgoing world → load the destination world manifest → load chunks around the entry point.
- **Chunk streaming.** Runtime loads chunks as the player moves (9×9 window around player, per world-generation-spec §5).

## 11. Autosave

Autosave triggers periodically to protect against crashes and unclean shutdowns.

**Cadence:** every 5 minutes of active play (configurable). Skipped if:

- Player is in combat (defer to end of combat).
- A save was triggered in the last 60 seconds (dedupe).

**Scope of autosave:**

- All dirty chunks.
- Current world manifest.
- Current campaign manifest.
- All characters currently in the campaign.

**UI:** brief unobtrusive notification ("Autosaving…") — no modal, no pause.

## 12. Crash Recovery

On startup, the game checks each campaign directory for signs of an unclean shutdown:

- Presence of `.tmp` files older than N seconds → deleted (§10.1).
- Presence of a `last-clean-shutdown` marker file per campaign. Written on clean save-and-quit; absent means the last session ended abruptly.
- If absent, the player sees an "Recover from crash?" prompt on loading that campaign. Recovery uses the last autosave.

MVP: keep this simple. Post-MVP could add save history / rewind.

## 13. Modified Chunk Tracking

Per `world-data-model-spec.md §3`, each chunk has a `modified` flag in its header. Rules:

- Fresh generation writes all chunks with `modified = false`.
- Any tile edit (player mining, placing, monster damage, water flow) sets `modified = true` on the chunk holding that tile.
- Chunk eviction from LRU cache: if `modified = true`, serialise and save. If `false`, skip — the disk copy is still authoritative.
- On save-and-quit: iterate all loaded chunks, flush the modified ones.

Trade-off: `modified` starts `false` at gen time and can never go back to `false` without a full rewrite. Once modified, a chunk is dirty forever (its on-disk copy is not the generation output). Fine for our purposes.

## 14. Debug Mode

Set via a launch flag (`--save-debug` or a config toggle). Not shipped in release builds.

- Envelope header flag bit 2 (`debug`) is set on write.
- Body is written **as raw payload bytes** — no zstd, no XOR.
- Hash is still computed and stored (helpful for validating format changes).
- Load path checks the flag bit and skips decompression/de-obfuscation accordingly.

Enables inspecting save contents in a hex editor or diffing across changes during development. Also useful for regression tests.

**Constraint:** never mix debug and ship saves in the same campaign directory. Debug mode is for developer use only.

## 15. Testing & Validation

- **Round-trip tests.** For each file kind: create a canonical payload, save it, load it, assert deep equality.
- **Cross-mode round-trip.** Save in debug mode, load in ship mode (both should reject with a warning). Save in ship, load in ship, hash matches.
- **Tamper detection tests.** Modify a byte in a saved file, load it, assert the integrity warning fires.
- **Migration tests.** For each version bump, keep a golden file at the old version and assert the migrator produces the current-version equivalent.
- **Atomicity test.** Kill the process mid-save (in a test harness) and assert the previous file is intact on restart.
- **Deterministic gen + save reproduces the same content.** Combined with the world-generation-spec's CI seed hash, a fresh-generation save should hash-match across machines.

## 16. Open Questions

- **Encoding of enum-like fields** (e.g. `difficulty_mode`, `layer_category`) — small ints (space-efficient, requires registry) or short strings (readable, larger)? Recommend small ints with a registry file living alongside the game data.
- **Character portability across installations** — should character files be copyable/shareable between players, or tied to a machine? Recommend fully portable (no machine binding). Fits the "not cryptographic security" ethos.
- **Save slot vs single save per campaign** — Terraria has one save per world; some games have multiple slots. Recommend one per campaign for MVP; post-MVP add manual snapshot slots if wanted.
- **Cloud save integration** (Steam Cloud, etc.) — post-MVP. Format should be cloud-friendly (small individual files, no massive monolithic blob) — current design already suits this.
- **Backup on migration.** When a save is migrated to a new format version, keep a `.pre-migration-backup` of the original file? Recommend yes, but auto-delete after N successful launches.
- **File count.** A Medium world has ~2,900 chunk files. Some file systems slow down on directories with thousands of files. Consider sharded subdirectories (`chunks/0/0_5.chunk`, `chunks/1/...`) if this proves problematic on target platforms.
