namespace Gameplay.Core
{
    /// <summary>
    /// Pure seeded xorshift RNG — engine-independent, so the same stream is reproducible in Unity and
    /// headless. (Reproducibility of the *physics* path is a separate, deferred concern — ADR-0001/M0.)
    /// </summary>
    public sealed class DeterministicRandom : IGameplayRandom
    {
        private uint _state;

        public DeterministicRandom(int seed)
        {
            _state = seed == 0 ? 0x9E3779B9u : unchecked((uint)seed);
        }

        private uint NextBits()
        {
            _state ^= _state << 13;
            _state ^= _state >> 17;
            _state ^= _state << 5;
            return _state;
        }

        public int NextInt(int maxExclusive)
        {
            if (maxExclusive <= 0)
                return 0;
            return (int)(NextBits() % (uint)maxExclusive);
        }

        public float NextFloat() => (NextBits() & 0xFFFFFF) / (float)0x1000000;
    }
}
