using System.Collections.Generic;
using Gameplay.Core.Adapters;

namespace Gameplay.Audio
{
    /// <summary>
    /// Which impacts of one frame are heard, and how loud (TASK-667 plan §2.3; design TASK-666 §3.2–§3.4). Unity-free:
    /// time and numbers come in as arguments, so EditMode tests drive it deterministically. Order, per frame:
    /// <list type="number">
    /// <item>drop impacts slower than <see cref="ImpactSoundTuning.MinSpeed"/> (jitter, micro-bounces);</item>
    /// <item>collapse by body — its strongest impact of the frame (a compound collider reports several contacts);</item>
    /// <item>drop bodies still in their cooldown;</item>
    /// <item>keep the strongest <see cref="ImpactSoundTuning.MaxVoicesPerFrame"/> (ties by body id) — the rest are
    /// DROPPED, not queued;</item>
    /// <item>volume = lerp(MinVolume, MaxVolume, clamp01((speed − MinSpeed)/(FullSpeed − MinSpeed))).</item>
    /// </list>
    /// Only a body that actually sounds starts its cooldown: an impact cut by the voice limit was never heard, so it
    /// must not silence the same body's next knock.
    /// </summary>
    public sealed class ImpactSoundRules
    {
        // Cached: a method group passed to Sort would allocate a delegate every frame.
        private static readonly System.Comparison<PieceImpact> StrongerFirst = Stronger;

        private readonly ImpactSoundTuning _tuning;
        private readonly Dictionary<int, float> _lastHeard = new();
        private readonly List<PieceImpact> _strongest = new();

        public ImpactSoundRules(ImpactSoundTuning tuning)
        {
            _tuning = tuning;
        }

        /// <summary>Forget every cooldown — called on each level (re)load.</summary>
        public void Begin() => _lastHeard.Clear();

        /// <summary>Decide the voices of one frame into <paramref name="voices"/> (cleared first), strongest first;
        /// returns their count. <paramref name="now"/> is the frame time in seconds.</summary>
        public int Resolve(IReadOnlyList<PieceImpact> frame, float now, List<ImpactVoice> voices)
        {
            voices.Clear();
            _strongest.Clear();
            if (frame == null)
                return 0;

            for (var i = 0; i < frame.Count; i++)
            {
                var impact = frame[i];
                if (impact.Speed < _tuning.MinSpeed)
                    continue;
                var known = IndexOfBody(impact.BodyId);
                if (known < 0)
                    _strongest.Add(impact);
                else if (impact.Speed > _strongest[known].Speed)
                    _strongest[known] = impact;
            }

            for (var i = _strongest.Count - 1; i >= 0; i--)
                if (_lastHeard.TryGetValue(_strongest[i].BodyId, out var heard) && now - heard < _tuning.CooldownSeconds)
                    _strongest.RemoveAt(i);

            _strongest.Sort(StrongerFirst);

            var count = System.Math.Min(_strongest.Count, System.Math.Max(_tuning.MaxVoicesPerFrame, 0));
            for (var i = 0; i < count; i++)
            {
                var impact = _strongest[i];
                _lastHeard[impact.BodyId] = now;
                voices.Add(new ImpactVoice(impact.BodyId, Volume(impact.Speed)));
            }
            return count;
        }

        /// <summary>Volume scale for an audible impact speed (clamped to the tuning's range).</summary>
        public float Volume(float speed)
        {
            var span = _tuning.FullSpeed - _tuning.MinSpeed;
            var k = span > 0f ? (speed - _tuning.MinSpeed) / span : 1f;
            if (k < 0f) k = 0f;
            if (k > 1f) k = 1f;
            return _tuning.MinVolume + (_tuning.MaxVolume - _tuning.MinVolume) * k;
        }

        private int IndexOfBody(int bodyId)
        {
            for (var i = 0; i < _strongest.Count; i++)
                if (_strongest[i].BodyId == bodyId)
                    return i;
            return -1;
        }

        private static int Stronger(PieceImpact a, PieceImpact b)
        {
            var bySpeed = b.Speed.CompareTo(a.Speed);
            return bySpeed != 0 ? bySpeed : a.BodyId.CompareTo(b.BodyId);
        }
    }
}
