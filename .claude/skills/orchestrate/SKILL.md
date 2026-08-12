---
name: orchestrate
description: Run a Godot feature through the developer / ui-developer / verifier agent pipeline. Use when work spans 3+ files or mixes gameplay logic with UI. For anything smaller, do it inline instead — orchestration costs several times more.
---

# Orchestrate a Godot feature

Three agents, run in sequence: `developer` (gameplay `.gd`), `ui-developer`
(UI and scenes), `verifier` (the ladder). You stay in the loop between stages.

## Step 0 — should this be orchestrated at all?

Orchestrate only if the work spans **3+ files** or **mixes gameplay logic with
UI**. Otherwise stop and do it inline.

This gate is the single biggest cost control in the setup. Every subagent
starts cold and re-reads context you already hold, so a one-file change costs
roughly 5-10x through the pipeline than done directly. Do not orchestrate to
look thorough.

## Step 1 — free checks first

Run `tests/check.sh 1 2 3`. Parse, lint and import cost no tokens and catch a
large share of what you'd otherwise pay an agent to find. If the tree is
already broken, fix that before spawning anyone.

## Step 2 — scout, then hand over exact paths

Do the file-finding yourself, once, with Grep/Glob. Then pass concrete paths
into the agent prompt:

> Add a dash to the player. Movement: `scripts/player/movement.gd:40-80`.
> Input map already has `dash`. Constants in `scripts/player/tuning.gd`.

Never send an agent to "find the player controller" — exploration is the
biggest avoidable token sink in the pipeline.

## Step 3 — run the pipeline, serially

1. **developer** — gameplay logic. Wait for it.
2. **ui-developer** — UI and scenes, given the developer's FILES output.
3. **verifier** — full ladder, given every changed path.

Serial, not parallel. Two agents editing Godot scripts and scenes at once
produce UID churn and import conflicts that cost more to untangle than the
wall-clock saved. Skip any stage the feature doesn't need — most features need
two of the three, and a logic-only change needs developer + verifier.

Pass each agent only what it needs: the task, exact paths, and the previous
stage's FILES/NOTES. Not the whole conversation.

## Step 4 — handle BLOCKED

Agents cannot ask you anything — they run detached. A blocked agent stops after
two attempts at the same failure and returns BLOCKED with its hypothesis and
one question.

When that happens: **do not re-spawn it and do not try to solve it silently.**
Surface it with AskUserQuestion — the agent's question, its hypothesis, and the
options you see. A second agent thrown at the same wall usually hits the same
wall, at full price.

## Step 5 — report

Show the human:

- what changed, as `git diff --stat`
- the verifier's ladder result
- any SCENE CHANGES block from the ui-developer, verbatim — these are manual
  editor steps, not applied changes
- anything left undone

**No agent commits, and neither do you** unless asked. The dirty working tree
is the human's review surface.

## Standing rules

- `.godot/` is regenerated cache — never edited, never read.
- `--import` and `godot` exit 0 while printing errors to stderr; trust grepped
  output, not `$?`. `tests/check.sh` already does this.
- "Cannot go into subdir" means the project root resolved wrong. Stop, do not
  retry.
- `.tscn` files are read with grep, never Read whole, by anyone.
