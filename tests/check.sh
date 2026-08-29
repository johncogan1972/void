#!/usr/bin/env bash
# Verification ladder, cheapest rung first. Stops at the first failure.
#
#   tests/check.sh            # all rungs
#   tests/check.sh 1 2 3      # only these rungs
#   tests/check.sh --strict   # a SKIP is a failure (this is what CI runs)
#
# Rungs: 1 parse  2 lint  3 build  4 cstest  5 import  6 smoke  7 gdtest
#
# Output is deliberately terse: one line per rung. Error detail is printed
# only for the rung that fails. Exit code is the rung number that failed.

set -uo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
GODOT="${GODOT:-godot}"

# Godot exits 0 while printing these to stderr, so grep the output, not $?.
ERR_RE='SCRIPT ERROR|ERROR:|Parse Error|Failed to load|Cannot open|Condition ".*" is true'

# Strict mode: a missing tool becomes a failure instead of a skip. CI runs with
# this on, otherwise the ladder can go green having checked almost nothing --
# rung 2 skips without gdlint, rungs 3 and 4 skip without dotnet.
STRICT="${CHECK_STRICT:-0}"

RUNGS=()
for arg in "$@"; do
	case "$arg" in
		--strict) STRICT=1 ;;
		*) RUNGS+=("$arg") ;;
	esac
done
[[ ${#RUNGS[@]} -eq 0 ]] && RUNGS=(1 2 3 4 5 6 7)

wants() { [[ " ${RUNGS[*]} " == *" $1 "* ]]; }
pass()  { printf '%-8s PASS  %s\n' "$1" "${2:-}"; }
fail()  { printf '%-8s FAIL  %s\n' "$1" "${2:-}"; }

# Exit code is the rung number, so a caller can tell which rung stopped it.
rung_num() {
	case "$1" in
		parse)  echo 1 ;;
		lint)   echo 2 ;;
		build)  echo 3 ;;
		cstest) echo 4 ;;
		import) echo 5 ;;
		smoke)  echo 6 ;;
		gdtest) echo 7 ;;
		*)      echo 99 ;;
	esac
}

# A SKIP is not a PASS.
skip() {
	if [[ "$STRICT" == "1" ]]; then
		fail "$1" "${2:-} [strict: a skip is not a pass]"
		exit "$(rung_num "$1")"
	fi
	printf '%-8s SKIP  %s\n' "$1" "${2:-}"
}

# Print at most 20 lines of captured detail, so a failure never dumps the
# whole engine log into an agent's context.
detail() { echo "$1" | grep -E "$ERR_RE" | head -20; }

scripts() {
	find "$ROOT" -name '*.gd' -not -path '*/.godot/*' -not -path '*/addons/*' \
		-printf 'res://%P\n' | sort
}

# --- rung 1: parse -----------------------------------------------------------
if wants 1; then
	bad=""
	while read -r s; do
		[[ -z "$s" ]] && continue
		out=$("$GODOT" --headless --path "$ROOT" --check-only --script "$s" 2>&1)
		if echo "$out" | grep -qE "$ERR_RE"; then
			bad+="$s"$'\n'"$out"$'\n'
		fi
	done < <(scripts)
	if [[ -n "$bad" ]]; then
		fail parse; detail "$bad"; exit 1
	fi
	pass parse "$(scripts | wc -l) scripts"
fi

# --- rung 2: lint ------------------------------------------------------------
if wants 2; then
	if ! command -v gdlint >/dev/null 2>&1; then
		skip lint "gdlint not installed (pip install gdtoolkit)"
	else
		out=$(cd "$ROOT" && gdlint $(scripts | sed 's|^res://||') 2>&1)
		if [[ $? -ne 0 ]]; then
			fail lint; echo "$out" | head -20; exit 2
		fi
		pass lint
	fi
fi

# --- rung 3: build (C#) ------------------------------------------------------
# The engine loads a prebuilt assembly; a C# compile error would otherwise stay
# invisible until runtime.
if wants 3; then
	if [[ ! -f "$ROOT/Void.csproj" ]]; then
		skip build "no Void.csproj"
	elif ! command -v dotnet >/dev/null 2>&1; then
		skip build "dotnet not installed"
	else
		out=$(dotnet build "$ROOT/Void.csproj" --nologo -v quiet 2>&1)
		if [[ $? -ne 0 ]]; then
			fail build; echo "$out" | grep -E 'error|Error' | head -20; exit 3
		fi
		pass build "$(find "$ROOT/src" -name '*.cs' 2>/dev/null | wc -l) sources"
	fi
fi

# --- rung 4: C# tests --------------------------------------------------------
# xunit. Runs without booting the engine, so it sits before the Godot rungs.
if wants 4; then
	if [[ ! -f "$ROOT/Void.Tests/Void.Tests.csproj" ]]; then
		skip cstest "no Void.Tests project"
	elif ! command -v dotnet >/dev/null 2>&1; then
		skip cstest "dotnet not installed"
	else
		out=$(dotnet test "$ROOT/Void.Tests/Void.Tests.csproj" --nologo -v quiet 2>&1)
		if [[ $? -ne 0 ]]; then
			fail cstest; echo "$out" | grep -E 'error|Failed|Assert' | head -20; exit 4
		fi
		pass cstest "$(echo "$out" | grep -oP 'Passed:\s*\K[0-9]+' | tail -1) passed"
	fi
fi

# --- rung 5: import ----------------------------------------------------------
# Catches broken script references, missing resources, bad UIDs in scenes.
if wants 5; then
	out=$("$GODOT" --headless --path "$ROOT" --import 2>&1)
	if echo "$out" | grep -qE "$ERR_RE"; then
		fail import; detail "$out"; exit 5
	fi
	pass import
fi

# --- rung 6: smoke -----------------------------------------------------------
# Boots the main scene headless for a few frames. Catches null refs, missing
# nodes and mis-wired signals — the things unit tests will never see.
if wants 6; then
	main_scene=$(grep -oP '^run/main_scene="\K[^"]+' "$ROOT/project.godot" 2>/dev/null)
	if [[ -z "$main_scene" ]]; then
		skip smoke "no run/main_scene set in project.godot"
	else
		out=$("$GODOT" --headless --path "$ROOT" --quit-after 120 2>&1)
		if echo "$out" | grep -qE "$ERR_RE"; then
			fail smoke "$main_scene"; detail "$out"; exit 6
		fi
		pass smoke "$main_scene, 120 frames"
	fi
fi

# --- rung 7: GDScript tests --------------------------------------------------
if wants 7; then
	out=$("$GODOT" --headless --path "$ROOT" --script res://tests/run_tests.gd 2>&1)
	summary=$(echo "$out" | grep -E '^[0-9]+ passed, [0-9]+ failed$' | tail -1)
	if [[ "$summary" != *"0 failed"* ]] || echo "$out" | grep -qE 'SCRIPT ERROR'; then
		fail gdtest "$summary"
		echo "$out" | grep -vE '^(Godot Engine|--|$)' | head -20
		exit 7
	fi
	pass gdtest "$summary"
fi
