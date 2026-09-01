# CLAUDE.md

Loaded at the start of every Claude Code session. Read this fully; only open other docs as needed for the specific task.

## Project

A 2D sandbox adventure game — spiritual successor to Terraria, blending technology and magic. Solo dev, single-player MVP, multiplayer post-MVP.

Stack: Godot 4.7. C# for simulation/hot paths. GDScript for UI and glue. Aseprite for art. Tiled for prefabs. Targets PC (Windows, Linux) plus Steam Deck as day-one first-class.

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

Commit at logical stopping points as the work proceeds, and push each commit.
Do not batch a feature into one commit at the end, and do not leave commits
sitting unpushed.

When the feature is done, open the PR. **Merge it without asking when — and only
when — the work is genuinely clean:** the full ladder green on the branch
(`tests/check.sh --strict`, every rung, no SKIPs), the PR mergeable, and no
design decision, spec contradiction, content choice or judgment call left open.
Then merge, close the ticket, and report.

**Stop and ask whenever any of that fails**, or when the work turned on a
decision the ticket did not settle. The point is not to save a step; it is to
spend the round trip on things that are actually undecided rather than on
formalities. A merge you are unsure about is exactly the one to ask about.

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
- CI test: regenerate reference seed → hash the payload → must match golden.
  `Void.Tests/ReferenceSeedTests.cs` holds the golden SHA-256;
  `src/Determinism/` builds the payload. The payload source is swappable
  (`ReferencePayload.Current`) — Phase 0 hashes a fixed RNG draw sequence,
  Phase 2 should point it at a generated world.
- **Regenerating the golden is a deliberate act, never a fix for a red build.**
  The failure message prints the actual hash; paste it into `GoldenHash` in the
  same commit as the generator change and say why in the commit message. A
  moved hash means every existing world generates differently. If you did not
  intend to change generation, the test found a bug — fix the bug.
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

## Godot commands
Project root: /run/media/system/Game_Drive_Two/gamedev/godot-projects/void
Always pass --path explicitly; never rely on cwd. Godot 4.7.

- Verify all:    tests/check.sh            (parse, lint, build, cstest, import, smoke, gdtest, export)
- Verify some:   tests/check.sh 1 2        (rung numbers; exits with failing rung)
- As CI runs it: tests/check.sh --strict   (a SKIP becomes a FAIL)
- Parse check:   godot --headless --path <root> --check-only --script res://path/to.gd
- Reimport:      godot --headless --path <root> --import
- Run tests:     godot --headless --path <root> --script res://tests/run_tests.gd
- Build C#:      dotnet build <root>/Void.csproj
- Test C#:       dotnet test <root>/Void.Tests/Void.Tests.csproj

Prefer `tests/check.sh` over the raw commands — it greps engine output and
truncates error dumps, which the raw commands do not.

Rules:
- A "Cannot go into subdir" error means the project root resolved wrong. Stop; do not retry.
- --import can exit 0 while printing SCRIPT ERROR / ERROR: to stderr. Grep the output, don't trust $?.
- Rung 6 (smoke) boots `run/main_scene` headless, which is
  `scenes/world_viewer.tscn` (VOID-057) — so that rung generates a world, builds
  the TileSets and paints a window every run. It is the only coverage the
  Godot-touching render path has. `scenes/main_menu.tscn` is still tracked as the
  placeholder it always was; it is no longer what boots, so don't delete it and
  don't assume it runs.
- `tests/test_example.gd` is a template. Delete it once real tests exist.
- `src/BuildInfo.cs` proves the C# assembly loads. Delete it once real C# exists.
- `Void.Tests/HarnessTests.cs` proves the xunit harness reaches the game assembly.
  Delete it once real C# tests exist.
- Rung 8 (export) builds a real .pck and reads `data/**/*.json` back out of it.
  Every other rung reads the loose files off disk, so only this one can catch
  content that an export would drop. It needs no export templates, but it does
  need `export_presets.cfg` — which is tracked, deliberately (VOID-013).
- A SKIP is not a PASS. Rungs 2/3/4 skip when their tool is missing. `--strict`
  (or `CHECK_STRICT=1`) turns those skips into failures; CI always uses it.
- Godot must be the .NET build (`godot --version` shows `.mono`), or rung 3 output
  compiles but the engine cannot load it.

## Coding conventions

- **Language split:** C# for simulation/physics/combat/streaming, in `src/`. GDScript for UI
  and light glue, in `scripts/`. Assembly name and root namespace are both `Void`.
- **Tests:** C# in `Void.Tests/` (xunit); GDScript in `tests/test_*.gd`. Anything using
  `uint64` must be tested in C# — GDScript has no unsigned 64-bit integer.
- **Data-driven:** items, biomes, recipes, loot tables, enemies, dialogue all in JSON. Registry pattern (see world-data-model-spec §7). New content = JSON entries, no code changes.
- **Save format:** binary + zstd + XOR obfuscation. See save-format-spec.
- **Godot conventions:** use TileMapLayer (Godot 4.3+), not TileMap. Consider Better Terrain plugin for deterministic auto-tile placement (see GDD §9.5).

## Comments

Code here is written to be read and edited by a human. Comment for that reader.

**Every file opens with a comment stating its purpose** — what this code is for
and where it sits in the system. In C# that is the XML `<summary>` on the file's
single public type (the file is named after it, so the type summary *is* the file
header); in GDScript it is a `##` block directly under `extends`.

**Every class, function, method, property, signal and constant gets a concise
comment** covering two things:

1. **Purpose** — what it is for, not what its name already says.
2. **Special requirements** — anything a human could break without noticing:
   ordering guarantees, determinism constraints, units, valid ranges, nullability,
   thread/frame timing, "must be called after X", "never call this from client
   code", why an error is fatal rather than skipped.

Rules that keep this useful rather than decorative:

- **Never paraphrase the signature.** `/// Gets the block id.` above
  `BlockId { get; }` is noise. If the name fully carries the meaning and there is
  no requirement to state, write the *why* — the reason it exists, or the
  constraint that made it look like this — or leave it out and say nothing.
- **Explain the load-bearing decisions**, not the mechanics. A reader can see
  *what* the loop does; they cannot see that the collection is sorted because
  registry order feeds world generation.
- **A run of near-identical members takes one shared comment above the run**, not
  the same sentence ten times (see the field-offset block in
  `src/Save/SaveEnvelope.cs`).
- **Tests are functions too.** A descriptive test name is not a substitute for
  saying what regression the test guards. Comment the intent — what breaks in the
  real game if this test goes red.
- Comments state current truth. When behaviour changes, the comment changes in
  the same commit or it becomes a lie the next reader trusts.

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
