using System;

namespace Gameplay.Core
{
    /// <summary>
    /// Gameplay pause gate (TASK-566): while at least one holder keeps a token, <c>GameControllerRunner</c> skips the
    /// whole tick — the scripted physics step, the fixed-step accumulator and the mechanic all stand still. A counter,
    /// not a bool: two modal screens (settings now, a shop or a daily bonus later) each take their own token, and closing
    /// one must not resume the game under the other.
    ///
    /// Deliberately not <c>Time.timeScale</c> and not the SGS <c>PauseService</c>: the first freezes everything driven by
    /// game time, the second also stops the shared timer service the asset preloader runs on. Physics here advances only
    /// from <c>GameController.Tick</c> (SimulationMode2D.Script), so not calling it IS the pause. The headless sim never
    /// builds the runner, so this class never reaches it.
    /// </summary>
    public sealed class GameplayPause
    {
        private int _holders;

        public bool IsPaused => _holders > 0;

        /// <summary>Takes one pause token. Disposing it releases exactly one hold; a second Dispose is a no-op.</summary>
        public IDisposable Acquire()
        {
            _holders++;
            return new Token(this);
        }

        private void Release() => _holders--;

        private sealed class Token : IDisposable
        {
            private GameplayPause _owner;

            public Token(GameplayPause owner)
            {
                _owner = owner;
            }

            public void Dispose()
            {
                if (_owner == null)
                    return;
                _owner.Release();
                _owner = null;
            }
        }
    }
}
