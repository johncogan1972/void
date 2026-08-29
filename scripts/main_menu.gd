extends Control

## Placeholder main menu, and the scene rung 6 (smoke) boots headless.
##
## It exists so the smoke rung always has something real to instantiate:
## `run/main_scene` points here, so deleting this scene breaks verification
## rather than the menu. Replace it with the real menu; do not delete it.
##
## Steam Deck rule (CLAUDE.md): every action must be reachable from a
## controller, which is why `_ready` grabs focus rather than relying on a
## mouse click — with nothing focused, a gamepad cannot reach the button
## at all.

## Exit button, resolved by unique name so moving it in the scene tree does
## not break this script.
@onready var _exit_button: Button = %ExitButton


## Wires the button and seeds keyboard/controller focus.
##
## The `grab_focus` call is load-bearing, not cosmetic: it is what makes the
## menu operable on a gamepad. See the Steam Deck note above.
func _ready() -> void:
	_exit_button.pressed.connect(_on_exit_pressed)
	_exit_button.grab_focus()


## Quits the game. Placeholder — real shutdown will need to flush saves first
## (save-format-spec), so this is not the final implementation.
func _on_exit_pressed() -> void:
	get_tree().quit()
