extends SceneTree

## Runs every tests/test_*.gd. Invoked by tests/check.sh (rung 5), or directly:
##   godot --headless --path <root> --script res://tests/run_tests.gd
## Exits non-zero if any test fails.

const TESTS_DIR := "res://tests"


func _initialize() -> void:
	var paths := _discover()
	if paths.is_empty():
		print("no tests found in %s" % TESTS_DIR)
		quit(0)
		return

	var passed := 0
	var failed := 0
	var report: Array[String] = []

	for path in paths:
		var script: GDScript = load(path)
		if script == null:
			failed += 1
			report.append("%s: could not load script" % path)
			continue

		for method in _test_methods(script):
			var case: RefCounted = script.new()
			case.before_each()
			case.call(method)
			case.after_each()

			var failures: Array = case._failures
			if failures.is_empty():
				passed += 1
			else:
				failed += 1
				for f in failures:
					report.append("%s::%s: %s" % [path.get_file(), method, f])

	for line in report:
		print(line)
	print("%d passed, %d failed" % [passed, failed])
	quit(1 if failed > 0 else 0)


func _discover() -> PackedStringArray:
	var found := PackedStringArray()
	var dir := DirAccess.open(TESTS_DIR)
	if dir == null:
		return found
	for file in dir.get_files():
		if file.begins_with("test_") and file.ends_with(".gd") and file != "test_case.gd":
			found.append(TESTS_DIR.path_join(file))
	found.sort()
	return found


func _test_methods(script: GDScript) -> PackedStringArray:
	var names := PackedStringArray()
	for method in script.get_script_method_list():
		var name: String = method.name
		if name.begins_with("test_") and not names.has(name):
			names.append(name)
	names.sort()
	return names
