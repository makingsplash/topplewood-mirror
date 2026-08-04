using System;
using Gameplay.Core;

namespace Gameplay.Tutorial
{
    /// <summary>
    /// Stateful input gate consulted by <see cref="TutorialGatedInputSource"/> (TASK-049 RFC §Q3). A tutorial
    /// step calls <see cref="Block"/> to suppress gameplay input while it drives the player, and disposes the
    /// returned handle to release. Reentrant (nested blocks count) so overlapping steps compose. Context-
    /// independent: one instance lives in both playable scopes.
    /// </summary>
    public interface ITutorialInputGate
    {
        bool IsBlocked { get; }

        /// <summary>Suppress gameplay input until the returned handle is disposed.</summary>
        IDisposable Block();

        /// <summary>Pass ONLY intents for which <paramref name="predicate"/> is true until the returned handle is
        /// disposed (TASK-078 §4) — how the step "let only a tap on the highlighted piece through" while the
        /// finger points at the right one. Composes with <see cref="Block"/>: a block wins (nothing passes while
        /// blocked). One predicate is active at a time (steps run one highlight at a time); last-wins.</summary>
        IDisposable AllowOnly(Func<InputIntent, bool> predicate);

        /// <summary>Single decision the input decorator asks per emission: will this intent pass the gate now?</summary>
        bool Allows(InputIntent intent);
    }
}
