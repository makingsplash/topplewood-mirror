using System.Collections.Generic;
using Gameplay.Audio;
using Gameplay.Core;
using Gameplay.Core.Adapters;
using NUnit.Framework;
using Scripts.Audio;

namespace Gameplay.Tests
{
    /// <summary>
    /// TASK-667 plan §3.3: the field's sound gates — impacts sound only while the level is running, the verdict
    /// silences the field (design TASK-666 §3.5) and plays «Lose» once, a player release clicks.
    /// </summary>
    [TestFixture]
    public sealed class GameSoundPresenterTests
    {
        private sealed class RecordingSounds : IGameSounds
        {
            public readonly List<string> Played = new();
            public void Play(string id, float volumeScale = 1f, float semitones = 0f) => Played.Add(id);
            public void Preload() { }
        }

        private GameModel _model;
        private RecordingSounds _sounds;
        private GameSoundPresenter _presenter;

        [SetUp]
        public void SetUp()
        {
            _model = new GameModel();
            _sounds = new RecordingSounds();
            _presenter = new GameSoundPresenter(_model, _sounds, new ImpactSoundTuning { MinSpeed = 1f, FullSpeed = 5f });
        }

        [TearDown]
        public void TearDown() => _presenter.Dispose();

        [Test]
        public void ImpactWhileRunning_Sounds()
        {
            _model.SetPhase(GamePhase.Running);
            _presenter.Report(new PieceImpact(1, 3f));
            _presenter.Tick();
            Assert.That(_sounds.Played, Is.EqualTo(new[] { Sounds.PieceLand }));
        }

        [Test]
        public void AfterTheLossVerdict_ImpactsAreSilent_AndLosePlaysOnce()
        {
            _model.SetPhase(GamePhase.Running);
            _model.Resolve(GameOutcome.Lost);
            _presenter.Report(new PieceImpact(1, 3f));
            _presenter.Report(new PieceImpact(2, 4f));
            _presenter.Tick();
            Assert.That(_sounds.Played, Is.EqualTo(new[] { Sounds.Lose }));
        }

        [Test]
        public void AfterAWin_ImpactsAreSilent_AndNoLose()
        {
            _model.SetPhase(GamePhase.Running);
            _model.Resolve(GameOutcome.Won);
            _presenter.Report(new PieceImpact(1, 3f));
            _presenter.Tick();
            Assert.That(_sounds.Played, Is.Empty);
        }

        [Test]
        public void ImpactsBufferedBeforeTheVerdict_AreDroppedNotReplayedLater()
        {
            _model.SetPhase(GamePhase.Running);
            _presenter.Report(new PieceImpact(1, 3f));
            _model.Resolve(GameOutcome.Lost);
            _presenter.Tick();
            _model.SetPhase(GamePhase.Running);
            _presenter.Tick();
            Assert.That(_sounds.Played, Is.EqualTo(new[] { Sounds.Lose }));
        }

        [Test]
        public void PlayerRelease_Clicks()
        {
            _model.RegisterAttempt(42);
            Assert.That(_sounds.Played, Is.EqualTo(new[] { Sounds.PieceRelease }));
        }
    }
}
