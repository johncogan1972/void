extends SceneTree
## Proves that `data/**/*.json` survives export and is readable from inside the
## built pack (VOID-013).
##
## This script is meant to run against an exported .pck, not against the loose
## project:
##
##     godot --headless --main-pack <pck> --script res://tests/export_check.gd
##
## Run from the project directory it would pass trivially — the editor reads the
## loose files off disk whether or not the export preset carries them, so an
## editor-only check proves nothing about a build. `tests/check.sh` rung 8 does
## the export first and runs this against the result.
##
## Output is one `EXPORT_JSON_FOUND=<n>` line plus a line per file. The caller
## compares the count against the number of .json files in `data/` on disk;
## this script deliberately does not know the expected number, so adding a
## registry cannot quietly lower the bar.

const DATA_ROOT := "res://data"


func _init() -> void:
	var found := _scan(DATA_ROOT)
	print("EXPORT_JSON_FOUND=%d" % found)
	quit()


## Recursively reads every .json under [param dir], returning how many were
## readable. Reads the contents rather than just listing names: a file can be
## present in the pack's directory listing and still fail to open, which is the
## failure mode that matters to the registries.
func _scan(dir_path: String) -> int:
	var dir := DirAccess.open(dir_path)
	if dir == null:
		printerr("EXPORT_CHECK: cannot open %s (%d)" % [dir_path, DirAccess.get_open_error()])
		return 0

	var count := 0

	for file in dir.get_files():
		# Exported resources are served under a ".remap" alias; the logical name
		# is what the loader asks for, so strip it before reading.
		var logical := file.trim_suffix(".remap")
		if not logical.ends_with(".json"):
			continue

		var path := "%s/%s" % [dir_path, logical]
		var text := FileAccess.get_file_as_string(path)
		if text.is_empty():
			printerr("EXPORT_CHECK: %s is empty or unreadable (%d)"
				% [path, FileAccess.get_open_error()])
			continue

		print("EXPORT_CHECK: %s (%d bytes)" % [path, text.length()])
		count += 1

	for sub in dir.get_directories():
		count += _scan("%s/%s" % [dir_path, sub])

	return count
