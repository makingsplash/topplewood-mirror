using System;
using System.Collections.Generic;

namespace Ui.Components.Reveal
{
    /// <summary>
    /// The sound cues of one screen intro (TASK-667 plan §4.2): steps with a <see cref="RevealStep.SoundId"/> fire their
    /// cue when the intro reaches them. A tap that completes the intro must not fire every remaining cue in one frame
    /// (DOTween's <c>Complete(true)</c> runs ALL pending callbacks at once — three stars become a burst): between
    /// <see cref="BeginComplete"/> and <see cref="EndComplete"/> cues are held, and then only ONE plays — the latest by
    /// time (design TASK-666 §3.4: «at once — one WinStar with index 2»). Unity-free: the player is a delegate, so a
    /// test drives it without audio.
    /// </summary>
    public sealed class RevealSoundCues
    {
        private struct Cue
        {
            public float AtSeconds;
            public string Id;
            public float Semitones;
            public bool Played;
        }

        private readonly Action<string, float> _play;
        private readonly List<Cue> _cues = new();
        private bool _holding;

        /// <param name="play">(sound id, semitones) → play it.</param>
        public RevealSoundCues(Action<string, float> play)
        {
            _play = play;
        }

        public int Count => _cues.Count;

        public void Clear()
        {
            _cues.Clear();
            _holding = false;
        }

        /// <summary>Register a cue at <paramref name="atSeconds"/> of the intro; returns its index for <see cref="Fire"/>.</summary>
        public int Add(float atSeconds, string id, float semitones)
        {
            _cues.Add(new Cue { AtSeconds = atSeconds, Id = id, Semitones = semitones });
            return _cues.Count - 1;
        }

        /// <summary>The intro reached cue <paramref name="index"/>: play it, unless a completion is holding cues.</summary>
        public void Fire(int index)
        {
            if (_holding || index < 0 || index >= _cues.Count || _cues[index].Played)
                return;
            MarkPlayed(index);
            _play?.Invoke(_cues[index].Id, _cues[index].Semitones);
        }

        /// <summary>A completion of the intro starts: cues fired from here on are held.</summary>
        public void BeginComplete() => _holding = true;

        /// <summary>The completion ended: play the latest cue not yet heard (if any) and retire all the rest.</summary>
        public void EndComplete()
        {
            _holding = false;
            var latest = -1;
            for (var i = 0; i < _cues.Count; i++)
                if (!_cues[i].Played && (latest < 0 || _cues[i].AtSeconds >= _cues[latest].AtSeconds))
                    latest = i;
            for (var i = 0; i < _cues.Count; i++)
                MarkPlayed(i);
            if (latest >= 0)
                _play?.Invoke(_cues[latest].Id, _cues[latest].Semitones);
        }

        private void MarkPlayed(int index)
        {
            var cue = _cues[index];
            cue.Played = true;
            _cues[index] = cue;
        }
    }
}
