## On clone
This project is cloned as a base for new Godot projects. Anything below that
names *this* project is stale in a copy. Update before doing real work:

- [ ] `CLAUDE.md` — the "Project root:" line just below
- [ ] `project.godot` — `config/name`
- [ ] `project.godot` — `run/main_scene`, once a real first scene exists
      (leave it pointing at something bootable, or rung 4 silently SKIPs)
- [ ] delete `scenes/main_menu.tscn` + `scripts/main_menu.gd` only when a real
      main scene replaces them, in the same change
- [ ] `git log` still shows the template's history — re-init if you want a
      clean one

Delete this section once it's all done.

## Godot commands
Project root: /run/media/system/Game_Drive_Two/gamedev/godot-projects/test-project
Always pass --path explicitly; never rely on cwd. Godot 4.7.

- Verify all:    tests/check.sh            (parse, lint, import, smoke, tests)
- Verify some:   tests/check.sh 1 2        (rung numbers; exits with failing rung)
- Parse check:   godot --headless --path <root> --check-only --script res://path/to.gd
- Reimport:      godot --headless --path <root> --import
- Run tests:     godot --headless --path <root> --script res://tests/run_tests.gd

Prefer `tests/check.sh` over the raw commands — it greps engine output and
truncates error dumps, which the raw commands do not.

Rules:
- A "Cannot go into subdir" error means the project root resolved wrong. Stop; do not retry.
- --import can exit 0 while printing SCRIPT ERROR / ERROR: to stderr. Grep the output, don't trust $?.
- .godot/ is regenerated cache. Never edit it.
- Prefer editing .gd files. Flag scene (.tscn) changes for manual review rather than hand-editing.
- Read .tscn files with grep, never whole — they are large and mostly noise.
- Do not git commit unless asked.

## Agents
Three subagents in `.claude/agents/`, driven by the `/orchestrate` skill:
- developer — gameplay .gd logic, no scenes
- ui-developer — UI, themes, and the only agent that reasons about .tscn
- verifier — runs the ladder, reports failures, has no Edit tool by design

Orchestrate only when work spans 3+ files or mixes logic and UI; otherwise
work inline, which is far cheaper.

## Tests
`tests/test_*.gd` extend `res://tests/test_case.gd`, methods named `test_*`.
Unit-test pure logic only — formulas, serialization, state transitions, seeded
generation. Physics, animation, input feel and layout are covered by the smoke
rung and by playing the game.
