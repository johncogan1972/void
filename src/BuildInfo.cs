using Godot;

namespace Void;

/// <summary>
/// Smallest possible proof that the C# assembly builds, loads, and can talk to
/// the engine. Rung 5 (smoke) instantiates this via the main scene.
///
/// Delete once real simulation code exists in src/.
/// </summary>
public partial class BuildInfo : Node
{
    public override void _Ready()
    {
        GD.Print($"C# assembly loaded: {typeof(BuildInfo).Assembly.GetName().Name}");
    }
}
