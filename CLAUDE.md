# CLAUDE.md

Loaded at the start of every Claude Code session. Read this fully; only open other docs as needed for the specific task.

## Project

A 2D sandbox adventure game — spiritual successor to Terraria, blending technology and magic. Solo dev, single-player MVP, multiplayer post-MVP.

Stack: Godot 4.x. C# for simulation/hot paths. GDScript for UI and glue. Aseprite for art. Tiled for prefabs. Targets PC (Windows, Linux) plus Steam Deck as day-one first-class.

Full context in `docs/GDD.md` — but do not open it unless the task genuinely needs the whole picture.

## Tickets
Work is tracked as GitHub issues on `johncogan1972/void`. The issue
number *is* the ticket number: issue #7 is `VOID-007`, zero-padded to three
digits. There is no separate registry to keep in sync.

Every ticket carries exactly one type label: `feature`, `bug`, or `chore`.

**Titles are prefixed with the ticket ID**, so it is visible in the list and
searchable. The number does not exist until the issue does, so this is always
create-then-rename — two steps, never one:

    gh issue create --label feature --title "Chunk streaming budget"   # → #7
    gh issue edit 7 --title "VOID-007 Chunk streaming budget"

    gh issue list --label bug
    gh issue list --search VOID-007
    gh issue view 7

Numbers are unique but **not contiguous** — GitHub draws issues and pull
requests from one sequence, so PRs consume IDs. Gaps are expected; never
renumber to close them.

## Git workflow
`main` is the default branch. `dev` branches off `main`; features branch off `dev`.

Always start a new feature from an up-to-date `dev`:

    git checkout dev
    git pull
    git checkout -b VOID-<nnn>/<description>

Branch names are always `VOID-<nnn>/<description>`, using the same
zero-padded three-digit number as the ticket ID — issue #1 is `VOID-001`, so
its branch is `VOID-001/prep-project`. The description is short and
kebab-case. Never start a feature from whatever branch happens to be checked
out, and never commit straight to `main`.

Prefer letting `gh` name and link the branch — it derives the base and
attaches the branch to the issue:

    gh issue develop 7 --base dev --name VOID-007/hotbar-focus-bug --checkout

Open the PR back into `dev`, referencing the ticket:

    gh pr create --base dev --title "VOID-007 Fix hotbar focus" --body "Closes #7"

**Closing keywords do not fire here.** GitHub only auto-closes on merge into the
*default* branch, which is `main`. Every feature PR targets `dev`, so `Closes #7`
records the link but never closes anything. Close the ticket by hand once the PR
is merged and the ladder is green on `dev`:

    gh pr merge 7 --squash --delete-branch
    gh issue close 7 --comment "Merged to dev in #<pr>."

Keep writing `Closes #<n>` in the body regardless — it is what cross-links the PR
and the ticket in both directions.

## Non-negotiable rules

**Determinism (world generation only):**
- All randomness from the world seed via seeded streams. Use xoshiro256++, never `System.Random`, `DateTime.Now`, `Guid.NewGuid()`, hardware entropy.
- Iteration order affecting generated output must be explicit — sorted collections, never `HashSet`/`Dictionary` order.
- CI test: regenerate reference seed → hash the world → must match golden.
- Runtime (combat, loot) is deliberately non-deterministic; determinism rule applies to world gen only.

**Multiplayer-ready from day 1:**
- No "the player" singletons. Always a player list, even with 1 player.
- No hard-coded ownership of placed objects.
- Server code has no rendering / input / audio access. Client code never mutates world state directly — send intent, react to replicated state.

**Steam Deck day-1:**
- Every action reachable from controller. No keyboard-only shortcuts.
- Native Linux build. UI readable at 1280×800.
- Save state survives suspend/resume.

**Content specs are aspirational:**
- `biome-content-spec.md`, `boss-content-spec.md`, `npc-content-spec.md` contain full-game targets, not MVP commitments. MVP ships with substantially less. Confirm scope with user before implementing a spec's full roster.

## Doc routing

Open only what the task needs. Descriptions are the summary — use them to decide without opening.

| Doc | Contents |
|-----|----------|
| `docs/GDD.md` | Master design. Pillars, world/portal system, character, controls, rendering. Only open when working across multiple systems or unfamiliar with project shape. |
| `docs/implementation-roadmap.md` | Ordered build sequence, phase-by-phase. Open at the start of any implementation session to check "what's the next phase" or "what are the prerequisites." |
| `docs/world-generation-spec.md` | Chunk model, 5-phase pipeline, layers, size presets, epic list (W1-W14). |
| `docs/world-data-model-spec.md` | Tile / chunk / manifest / prefab / biome schemas. Registry pattern. |
| `docs/cave-generation-spec.md` | Perlin worms + cellular automata algorithms. Per-layer tuning. |
| `docs/save-format-spec.md` | File envelope, obfuscation, zstd, autosave, migration. |
| `docs/combat-spec.md` | Attack lifecycle, damage resolution, resistances, status effects, HP/mana. |
| `docs/loot-table-spec.md` | Loot table JSON schema, rarity rolls, Legendary names, guaranteed drops. |
| `docs/multiplayer-spec.md` | Host-auth model, chunk sync, entity replication, portal transitions. |
| `docs/biome-content-spec.md` | Home biomes, underground variants, deep/void biomes, portal-world themes. |
| `docs/boss-content-spec.md` | Main bosses, mini bosses, area bosses, event bosses. |
| `docs/npc-content-spec.md` | Guide (Aelis) design, discoverable roster, dialogue structure. |

## Task → spec mapping

| Task | Read |
|------|------|
| "What should I build next?" or starting a new session | implementation-roadmap (check current phase, prereqs, DoD) |
| World-gen pipeline code (any phase) | world-generation-spec, world-data-model-spec |
| Cave carving specifically | cave-generation-spec |
| Save/load I/O | save-format-spec, world-data-model-spec |
| Combat mechanics, damage math | combat-spec |
| Loot rolls, chest contents, drop tables | loot-table-spec |
| Networking, replication, sessions | multiplayer-spec |
| Enemy stats/behaviour for a biome | biome-content-spec |
| Boss design | boss-content-spec |
| NPC behaviour, dialogue, housing | npc-content-spec + GDD §5.4-5.5 |
| Controls (keyboard/mouse/controller) | GDD §5.7 |
| Rendering, tiles, sprites | GDD §9.4-9.5 |
| Anything cross-cutting or unfamiliar | GDD.md |

## Coding conventions

- **Language split:** C# for simulation/physics/combat/streaming. GDScript for UI and light glue.
- **Data-driven:** items, biomes, recipes, loot tables, enemies, dialogue all in JSON. Registry pattern (see world-data-model-spec §7). New content = JSON entries, no code changes.
- **Save format:** binary + zstd + XOR obfuscation. See save-format-spec.
- **Godot conventions:** use TileMapLayer (Godot 4.3+), not TileMap. Consider Better Terrain plugin for deterministic auto-tile placement (see GDD §9.5).

## When you find issues

- Missing information → ask the user; don't invent.
- Design contradiction between specs → flag it, propose resolution, wait for user confirmation before changing docs.
- Content decisions (names, personality, aesthetics) → user's territory; don't invent unprompted.
- Technical decisions → propose with rationale, get user confirmation before major refactors.
- Every doc has a version header — bump it when you change the doc.

## User preferences

- Direct communication, minimal preamble.
- Substantive pushback expected; do not just agree.
- Flag consequences the user might not have considered.
- MVP scope is defined in `docs/GDD.md §12`. Stay within it unless the user explicitly expands scope.
