# [Project Name]

A 2D sandbox adventure game — spiritual successor to Terraria, blending technology and magic.

## Status

Pre-production. Design phase complete; implementation not yet started.

## Repo structure

```
.
├── CLAUDE.md              # Project rules and doc routing for AI-assisted development
├── README.md              # This file
├── docs/                  # Design and technical specifications
│   ├── GDD.md
│   ├── implementation-roadmap.md
│   ├── world-generation-spec.md
│   ├── world-data-model-spec.md
│   ├── cave-generation-spec.md
│   ├── save-format-spec.md
│   ├── combat-spec.md
│   ├── loot-table-spec.md
│   ├── multiplayer-spec.md
│   ├── biome-content-spec.md
│   ├── boss-content-spec.md
│   └── npc-content-spec.md
└── src/                   # Game code (not yet started)
```

## Where to start

- **New to the project?** Read `docs/GDD.md`.
- **Ready to implement?** Read `docs/implementation-roadmap.md` — phase-by-phase build order.
- **Working on a specific system?** See `CLAUDE.md` for the task → spec routing table.
- **Using Claude Code?** `CLAUDE.md` is loaded automatically.

## Tech stack

- **Engine:** Godot 4.x
- **Languages:** C# (simulation, hot paths) + GDScript (UI, glue)
- **Art:** Aseprite (16×16 base tiles, Terraria-scale characters)
- **Prefabs:** Tiled (.tmx exports)
- **Platforms:** PC (Windows, Linux) + Steam Deck (day-one first-class)
- **Distribution:** Steam
