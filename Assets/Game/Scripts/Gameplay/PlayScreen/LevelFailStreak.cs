using System;
using Gameplay.Core;
using R3;
using VContainer.Unity;

namespace Gameplay.PlayScreen
{
    /// <summary>
    /// Runtime counter of consecutive losses on the CURRENT level (architect §5.2, balancer §4) — the proactive-
    /// offer trigger. Not saved (a fresh app run starts at 0, balancer §8): a Lost bumps it, and the NEXT level
    /// (a new <c>LevelId</c> arriving via <see cref="GameModel.LevelLoaded"/>) resets it. A Lost→retry reloads the
    /// SAME level (same id) so the streak is preserved; a Won→advance loads a NEW id so it resets — one rule on
    /// the id keeps both cases correct without a separate Won branch. Lives in the prod scope (Singleton entry
    /// point); the sim never builds it (it is not in <c>GameplayCoreInstaller</c>).
    /// </summary>
    public sealed class LevelFailStreak : IStartable, IDisposable
    {
        private readonly GameModel _model;
        private readonly ReactiveProperty<int> _current = new(0);
        private readonly CompositeDisposable _subs = new();
        private string _levelId;

        public LevelFailStreak(GameModel model) => _model = model;

        /// <summary>Consecutive losses on the current level (resets on advance).</summary>
        public ReadOnlyReactiveProperty<int> Current => _current;

        public void Start()
        {
            _model.LevelLoaded.Subscribe(_ =>
            {
                var id = _model.CurrentLevel?.LevelId;
                if (id == _levelId)
                    return; // Lost→retry reload of the same level: keep the streak.
                _levelId = id;
                _current.Value = 0; // advanced to a new level: reset.
            }).AddTo(_subs);

            _model.LevelResolved.Subscribe(_ =>
            {
                if (_model.Outcome.CurrentValue == GameOutcome.Lost)
                    _current.Value++;
                else if (_model.Outcome.CurrentValue == GameOutcome.Won)
                    _current.Value = 0;
            }).AddTo(_subs);
        }

        public void Dispose() => _subs.Dispose();
    }
}
