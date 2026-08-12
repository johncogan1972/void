extends Control

@onready var _exit_button: Button = %ExitButton


func _ready() -> void:
	_exit_button.pressed.connect(_on_exit_pressed)
	_exit_button.grab_focus()


func _on_exit_pressed() -> void:
	get_tree().quit()
