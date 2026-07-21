namespace Gameplay.Core
{
    /// <summary>
    /// Pure fixed-step accumulator. Has no engine dependency, so one implementation serves both the
    /// Unity tick (Advance(Time.deltaTime)) and headless QA (manual Advance(dt)) — see overview.
    /// </summary>
    public sealed class GameplayClock : IGameplayClock
    {
        private float _accumulator;

        public float FixedStep { get; }

        public GameplayClock(float fixedStep)
        {
            FixedStep = fixedStep;
        }

        public void Advance(float dt)
        {
            if (dt > 0f)
                _accumulator += dt;
        }

        public bool TryConsumeStep()
        {
            if (_accumulator < FixedStep)
                return false;

            _accumulator -= FixedStep;
            return true;
        }
    }
}
