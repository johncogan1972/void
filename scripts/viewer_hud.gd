extends PanelContainer

## Status readout for the world viewer (VOID-057).
##
## Presentation only. `WorldViewer` emits raw numbers on `ViewChanged` and this
## decides how they are worded and laid out, which is the language split in
## CLAUDE.md: the C# side owns generation and the window, GDScript owns what the
## screen says about it.
##
## Readable at 1280x800 (Steam Deck, per CLAUDE.md), which is why the font size
## is set explicitly rather than inherited -- the default theme size is chosen
## for a desktop monitor and is marginal at that resolution.

## Control hints. Constant because they describe the `[input]` actions in
## project.godot, not anything the viewer computes -- if a binding changes there,
## this string changes with it or it becomes a lie.
const CONTROLS := (
	"arrows/stick pan   Q/E or shoulders jump window   "
	+ "+/- or d-pad zoom   enter/A recentre   esc/start quit"
)

## The readout itself. Resolved by unique name so it can be re-parented inside
## the panel without breaking this script.
@onready var _label: Label = %StatusLabel


## Connects to the viewer.
##
## `owner` is the scene root, which is the `WorldViewer` node. Connecting by
## string name rather than through the signal object because the signal is
## declared in C#, where it registers under its PascalCase name.
func _ready() -> void:
	owner.connect("ViewChanged", _on_view_changed)


## Renders one status update.
##
## Takes a Dictionary rather than a long argument list so that adding a field on
## the C# side does not break this signature. A missing key shows as "?" rather
## than erroring: this is a diagnostic overlay, and it failing loudly would
## obscure the very thing it is there to help read.
func _on_view_changed(status: Dictionary) -> void:
	var column: int = status.get("column", 0)
	var width: int = status.get("world_width", 1)

	_label.text = "\n".join([
		"seed %s   %s   %s (%d wide)" % [
			status.get("seed", "?"),
			status.get("world_type", "?"),
			status.get("size_preset", "?"),
			width,
		],
		"column %d (%.1f%% across)   surface row %d" % [
			column,
			100.0 * float(column) / float(max(width, 1)),
			status.get("surface_row", 0),
		],
		"biome %s" % status.get("biome", "?"),
		"window %d chunks   zoom %.2fx" % [status.get("chunks", 0), status.get("zoom", 1.0)],
		CONTROLS,
	])
