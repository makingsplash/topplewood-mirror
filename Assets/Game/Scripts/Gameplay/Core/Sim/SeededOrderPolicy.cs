using System.Collections.Generic;

namespace Gameplay.Core.Sim
{
    /// <summary>
    /// Fallback sampler (RFC §4): when a level has too many pieces to enumerate every permutation, this builds
    /// ONE random release order from a seeded stream (lazily, once it can read the piece count) and then
    /// behaves exactly like <see cref="ReleaseOrderPolicy"/>. The host runs many of these with distinct seeds
    /// to sample the order space.
    /// </summary>
    public sealed class SeededOrderPolicy : ISimBotPolicy
    {
        private readonly IGameplayRandom _rng;
        private ReleaseOrderPolicy _inner;

        public SeededOrderPolicy(IGameplayRandom rng) => _rng = rng;

        // Patient: wraps ReleaseOrderPolicy, which is rest-gated (TASK-060). Unchanged behaviour.
        public bool GatedByRest => true;

        public bool TryNextRelease(GameModel model, List<Vec2> taps)
        {
            _inner ??= new ReleaseOrderPolicy(Permutations.Shuffled(model.Pieces.Count, _rng));
            return _inner.TryNextRelease(model, taps);
        }
    }
}
