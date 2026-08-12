extends RefCounted

## Base class for tests. Extend it, name methods `test_*`.
## Only assert on pure logic — formulas, serialization, state machines.
## Anything needing physics, animation or input feel belongs in the smoke
## rung or in your hands, not here.

var _failures: Array[String] = []
var _assertions := 0

# Override in subclasses if you need setup/teardown.
func before_each() -> void:
	pass


func after_each() -> void:
	pass


func fail(msg: String) -> void:
	_failures.append(msg)


func assert_true(value: bool, msg := "") -> void:
	_assertions += 1
	if not value:
		fail("expected true, got false" + _suffix(msg))


func assert_false(value: bool, msg := "") -> void:
	_assertions += 1
	if value:
		fail("expected false, got true" + _suffix(msg))


func assert_eq(actual: Variant, expected: Variant, msg := "") -> void:
	_assertions += 1
	if actual != expected:
		fail("expected %s, got %s%s" % [_show(expected), _show(actual), _suffix(msg)])


func assert_ne(actual: Variant, other: Variant, msg := "") -> void:
	_assertions += 1
	if actual == other:
		fail("expected value other than %s%s" % [_show(other), _suffix(msg)])


func assert_almost_eq(actual: float, expected: float, tolerance := 0.0001, msg := "") -> void:
	_assertions += 1
	if absf(actual - expected) > tolerance:
		fail("expected %f +/- %f, got %f%s" % [expected, tolerance, actual, _suffix(msg)])


func assert_null(value: Variant, msg := "") -> void:
	_assertions += 1
	if value != null:
		fail("expected null, got %s%s" % [_show(value), _suffix(msg)])


func assert_not_null(value: Variant, msg := "") -> void:
	_assertions += 1
	if value == null:
		fail("expected non-null" + _suffix(msg))


func assert_in_range(value: float, low: float, high: float, msg := "") -> void:
	_assertions += 1
	if value < low or value > high:
		fail("expected %f in [%f, %f]%s" % [value, low, high, _suffix(msg)])


func assert_has(collection: Variant, item: Variant, msg := "") -> void:
	_assertions += 1
	if not (item in collection):
		fail("expected collection to contain %s%s" % [_show(item), _suffix(msg)])


func _suffix(msg: String) -> String:
	return "" if msg.is_empty() else " -- " + msg


func _show(value: Variant) -> String:
	if value is String:
		return '"%s"' % value
	return str(value)
