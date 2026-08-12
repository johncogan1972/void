extends "res://tests/test_case.gd"

## Template. Delete once you have real tests.
##
## Test pure logic only: damage and economy formulas, save/load round-trips,
## inventory rules, state machine transitions, seeded generation. If a test
## needs a physics step or a rendered frame, it belongs in the smoke rung.


func test_damage_is_reduced_by_armour() -> void:
	assert_eq(_damage(100, 0), 100)
	assert_eq(_damage(100, 50), 50)


func test_damage_never_goes_negative() -> void:
	assert_eq(_damage(10, 999), 0)


func _damage(raw: int, armour: int) -> int:
	return maxi(0, raw - armour)
