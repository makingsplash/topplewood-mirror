namespace Gameplay.Core
{
    /// <summary>Seeded, reproducible random stream (overview §Адаптерная граница). Not used by v0
    /// Fixed pool ordering, but the seed is threaded through for Bag ordering / QA sweeps later.</summary>
    public interface IGameplayRandom
    {
        int NextInt(int maxExclusive);
        float NextFloat();
    }
}
