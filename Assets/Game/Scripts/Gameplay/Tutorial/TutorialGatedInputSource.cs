using Gameplay.Core;
using R3;

namespace Gameplay.Tutorial
{
    /// <summary>
    /// Decorator over <see cref="IGameplayInputSource"/> (TASK-049 RFC §Q3): forwards domain input intents
    /// while the <see cref="ITutorialInputGate"/> is open and swallows them while a step blocks. One input
    /// mechanism in BOTH scopes (vs the prod-only <c>IInputBlockersHandler</c>) — the hosting scope passes the
    /// concrete source (prod <c>InputServiceGameplayInputSource</c> / bringup <c>PointerTapInputSource</c>) in.
    /// Context-independent and sim-clean: <c>SimBotInputSource</c> is used by the domain directly, so this
    /// decorator never enters the headless simulation graph (RFC §7 invariant 2).
    /// </summary>
    public sealed class TutorialGatedInputSource : IGameplayInputSource
    {
        private readonly IGameplayInputSource _inner;
        private readonly ITutorialInputGate _gate;

        public TutorialGatedInputSource(IGameplayInputSource inner, ITutorialInputGate gate)
        {
            _inner = inner;
            _gate = gate;
        }

        // Per-emission gate check: Block()/AllowOnly()/dispose mutate the gate live, so pass/suppress is
        // evaluated per intent (not captured at subscription time). Gate.Allows folds both rules — block wins,
        // else the optional allow-predicate decides (TASK-078 §4). Method group closes over the gate once per
        // subscription (two subscribers: GameController + TutorialRunner) — no per-frame alloc.
        public Observable<InputIntent> Intents => _inner.Intents.Where(_gate.Allows);
    }
}
