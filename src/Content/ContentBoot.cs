using System;
using Godot;

namespace Void;

/// <summary>
/// Autoload that loads the content registries once at game start and owns them
/// for the lifetime of the process (VOID-025).
///
/// <para>Deliberately thin, and — with <see cref="GodotContentSource"/> — one of
/// only two files in the content layer allowed to touch Godot. All it does is
/// build a source factory over <c>res://data/</c> and call
/// <see cref="ContentLoader.LoadAll"/>; every decision about order and
/// validation lives in that engine-free loader, where the xunit suite can reach
/// it. The xunit suite must never load this type: it needs an initialised
/// engine.</para>
///
/// <para>Registered in <c>project.godot</c> as the <c>ContentBoot</c> autoload,
/// so it runs before the main scene and before any system that reads content.
/// Rung 6 of the verification ladder boots the main scene headless, which makes
/// that rung the real coverage for this path.</para>
/// </summary>
public partial class ContentBoot : Node
{
    /// <summary>Godot path holding the shipped registries, one folder per registry.</summary>
    public const string DataRoot = "res://data";

    /// <summary>Set once by <see cref="_EnterTree"/>; null means the load failed.</summary>
    private GameContent? _content;

    /// <summary>
    /// The loaded registries.
    /// </summary>
    /// <remarks>
    /// Throws rather than returning null when loading failed, because
    /// <see cref="SceneTree.Quit(int)"/> is honoured only at the end of the
    /// current tree iteration — the main scene is still instantiated and its
    /// nodes still run their <c>_Ready</c> after an aborted boot. A null here
    /// would surface as a <see cref="NullReferenceException"/> in whichever
    /// system happened to read content first, burying the real message under an
    /// unrelated stack. Failing here keeps the cause attached to the symptom.
    /// </remarks>
    /// <exception cref="InvalidOperationException">
    /// If content failed to load. The underlying <see cref="ContentLoadException"/>
    /// is the inner exception, and its message names the referrer and the
    /// missing id.
    /// </exception>
    public GameContent Content => _content ?? throw new InvalidOperationException(
        "Content failed to load and boot was aborted; see the earlier error for the cause.",
        _failure);

    /// <summary>What <see cref="_EnterTree"/> caught, kept to explain a later access.</summary>
    private Exception? _failure;

    /// <summary>
    /// Loads all content before any other node's <c>_Ready</c>.
    /// </summary>
    /// <remarks>
    /// In <c>_EnterTree</c> rather than <c>_Ready</c> so the registries exist
    /// before the main scene's nodes initialise; an autoload's <c>_Ready</c>
    /// still runs first today, but that ordering is not something the rest of
    /// the game should have to know.
    ///
    /// <para>A content failure aborts boot: empty or partial registries produce
    /// a world of air and a stream of unrelated null errors a long way from the
    /// actual cause. The error text is pushed before quitting so the message
    /// naming the referrer and the missing id survives in the log. Note that
    /// <see cref="SceneTree.Quit(int)"/> is deferred to the end of the current
    /// tree iteration rather than immediate, so the main scene still loads and
    /// still runs; <see cref="Content"/> throws in that window rather than
    /// handing anyone a null.</para>
    /// </remarks>
    public override void _EnterTree()
    {
        try
        {
            _content = ContentLoader.LoadAll(static folder => new GodotContentSource($"{DataRoot}/{folder}"));
        }
        catch (Exception ex)
        {
            _failure = ex;
            GD.PushError($"Content load failed, aborting boot: {ex.Message}");
            GetTree().Quit(1);
        }
    }
}
