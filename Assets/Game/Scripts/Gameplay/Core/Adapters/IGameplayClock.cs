namespace Gameplay.Core
{
    /// <summary>
    /// Deterministic fixed-step clock (overview §Тик-луп). Ускорение прогона — числом шагов, НЕ
    /// увеличением FixedStep (ADR-0001). Pure C#: the same accumulator serves Unity play and headless QA.
    /// </summary>
    public interface IGameplayClock
    {
        float FixedStep { get; }

        /// <summary>Accumulate wall/real delta.</summary>
        void Advance(float dt);

        /// <summary>Consume one fixed step if a full step has accumulated; returns false otherwise.</summary>
        bool TryConsumeStep();
    }
}
