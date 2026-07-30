using System;

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
    }
}
