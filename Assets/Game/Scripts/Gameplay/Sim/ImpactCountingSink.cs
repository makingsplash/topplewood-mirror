using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Gameplay.Core.Adapters;

namespace Gameplay.Sim
{
    /// <summary>
    /// The sim harness's impact receiver (TASK-667 plan §8 T6). Handing it to the physics adapter makes every body carry
    /// the same read-only <c>PieceImpactReporter</c> prod carries, so a sweep of this build vs a sweep of a build without
    /// reporters must give byte-identical poseHash — that is the proof the sound listener does not touch physics. It also
    /// collects the approach speeds, whose percentiles set <c>ImpactSoundTuning.MinSpeed/FullSpeed</c> from data.
    /// Records only; never reads or writes the simulation.
    /// </summary>
    public sealed class ImpactCountingSink : IPieceImpactSink
    {
        private readonly List<float> _speeds = new();

        public int Count => _speeds.Count;

        public void Report(PieceImpact impact) => _speeds.Add(impact.Speed);

        /// <summary>
        /// <c>count=N p5=… p20=… p50=… p80=… p95=… p99=… max=…</c> over the impacts recorded so far (nearest-rank
        /// percentiles, units/s). «count=0» on a non-empty sweep means the reporter is not working.
        /// </summary>
        public string Summary()
        {
            var ic = CultureInfo.InvariantCulture;
            var sb = new StringBuilder();
            sb.Append("count=").Append(_speeds.Count.ToString(ic));
            if (_speeds.Count == 0)
                return sb.ToString();

            var sorted = new List<float>(_speeds);
            sorted.Sort();
            foreach (var p in new[] { 5, 20, 50, 80, 95, 99 })
                sb.Append(" p").Append(p.ToString(ic)).Append('=').Append(Percentile(sorted, p).ToString("0.000", ic));
            sb.Append(" max=").Append(sorted[sorted.Count - 1].ToString("0.000", ic));
            return sb.ToString();
        }

        private static float Percentile(List<float> sorted, int p)
        {
            var rank = (int)System.Math.Ceiling(p / 100.0 * sorted.Count) - 1;
            if (rank < 0) rank = 0;
            if (rank >= sorted.Count) rank = sorted.Count - 1;
            return sorted[rank];
        }
    }
}
