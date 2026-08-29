using Godot;

namespace Void;

/// <summary>
/// Smallest possible proof that the C# assembly builds, loads, and can talk to
/// the engine. Rung 6 (smoke) instantiates this via the main scene.
///
/// Delete once real simulation code exists in src/.
/// </summary>
public partial class BuildInfo : Node
{
    /// <summary>
    /// Prints the loaded assembly name so the smoke rung has observable proof the
    /// C# side is alive. Runs on scene entry, before the first frame.
    /// </summary>
    public override void _Ready()
    {
        GD.Print($"C# assembly loaded: {typeof(BuildInfo).Assembly.GetName().Name}");
    }
}
