using System;
using System.Collections.Generic;
using Gameplay.Core;
using Gameplay.Core.Adapters;
using R3;
using Scripts.Audio;
using UnityEngine;
using VContainer.Unity;

namespace Gameplay.Audio
{
    /// <summary>
    /// Field sounds of a level (TASK-667 plan §3.3): «PieceRelease» on a player release, «PieceLand» from physics
    /// impacts, «Lose» at the verdict. Listener only — it subscribes to the model and receives impacts; it never
    /// writes to the model or the simulation.
    ///
    /// <see cref="Report"/> runs inside <c>Physics2D.Simulate</c>, mid domain tick, so it only buffers. <see cref="Tick"/>
    /// decides once per frame (<see cref="ImpactSoundRules"/>) and ALWAYS clears the buffer: outside Running/Resolving
    /// the frame is dropped unheard. That is the whole of «after the verdict the field is silent» (design TASK-666
    /// §3.5): on an instant loss (a required piece left the bounds mid-tick) the other pieces are still falling, but
    /// the phase is already Lost.
    ///
    /// The tour has no sounds of its own by construction: it only filters input (TutorialGatedInputSource), and its
    /// taps are the player's real releases. Pre-placed pieces never emit <see cref="GameModel.PieceReleased"/>, so they
    /// make no release click — only their impacts, like any other body. A won level is voiced by the win beat
    /// (<c>WinBeatView</c>), not here.
    /// </summary>
    public sealed class GameSoundPresenter : IPieceImpactSink, IStartable, ITickable, IDisposable
    {
        private readonly GameModel _model;
        private readonly IGameSounds _sounds;
        private readonly ImpactSoundRules _rules;
        private readonly List<PieceImpact> _frame = new();
        private readonly List<ImpactVoice> _voices = new();
        private readonly CompositeDisposable _subs = new();

        public GameSoundPresenter(GameModel model, IGameSounds sounds, ImpactSoundTuning tuning)
        {
            _model = model;
            _sounds = sounds;
            _rules = new ImpactSoundRules(tuning);

            // In the constructor, not in Start: the simulation resolves this presenter as its impact sink while the graph
            // is being built, and the first level may load before this entry point's Start runs.
            _model.LevelLoaded.Subscribe(_ => OnLevelLoaded()).AddTo(_subs);
            _model.PieceReleased.Subscribe(_ => _sounds.Play(Sounds.PieceRelease)).AddTo(_subs);
            _model.Outcome.Subscribe(OnOutcome).AddTo(_subs);
        }

        public void Start() => _sounds.Preload();

        public void Report(PieceImpact impact)
        {
#if GPIE_DEVELOPMENT
            Debug.Log(string.Format(System.Globalization.CultureInfo.InvariantCulture,
                "[sfx] impact body={0} speed={1:0.000} phase={2}", impact.BodyId, impact.Speed, _model.CurrentPhase));
#endif
            _frame.Add(impact);
        }

        public void Tick()
        {
            if (_frame.Count == 0)
                return;

            var phase = _model.CurrentPhase;
            if (phase == GamePhase.Running || phase == GamePhase.Resolving)
            {
                _rules.Resolve(_frame, Time.time, _voices);
                foreach (var voice in _voices)
                    _sounds.Play(Sounds.PieceLand, voice.VolumeScale);
            }
            _frame.Clear();
        }

        private void OnLevelLoaded()
        {
            // Impacts of the previous level's destroyed bodies are never played on the new one.
            _frame.Clear();
            _rules.Begin();
        }

        private void OnOutcome(GameOutcome outcome)
        {
            if (outcome == GameOutcome.None)
                return;
#if GPIE_DEVELOPMENT
            Debug.Log($"[sfx] verdict {outcome} — field impacts silent from here");
#endif
            if (outcome != GameOutcome.Lost)
                return;
            // Synchronously with Resolve, before the lose flow's settle pause: the sting marks the moment of the verdict.
            _sounds.Play(Sounds.Lose);
        }

        public void Dispose() => _subs.Dispose();
    }
}
