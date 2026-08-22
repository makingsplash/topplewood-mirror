using System;
using System.Collections.Generic;

namespace Gameplay.Core
{
    /// <summary>
    /// TASK-181: one tapable start-pose pair whose centres sit closer than the predicted tap zone — the structural,
    /// machine-readable form of the load-time "Tap spacing" warning. Indices are into <c>LevelData.Pieces</c>; kinds
    /// are the piece <c>Type.Kind</c> (may be null, exactly as the warning string interpolates it); distance is the
    /// centre-to-centre world distance of the two start poses (metric v1 — see <see cref="LevelDataValidator"/>).
    /// </summary>
    public readonly struct TapSpacingViolation
    {
        public readonly int IndexA;
        public readonly string KindA;
        public readonly int IndexB;
        public readonly string KindB;
        public readonly float Distance;

        public TapSpacingViolation(int indexA, string kindA, int indexB, string kindB, float distance)
        {
            IndexA = indexA;
            KindA = kindA;
            IndexB = indexB;
            KindB = kindB;
            Distance = distance;
        }
    }

    /// <summary>
    /// TASK-181: per-level result of the tap-spacing audit as DATA, so the sim sweep can lift it out of the log and
    /// into its JSON report (the <c>tapAudit</c> block) — an author reading the report inside their own run sees the
    /// spacing signal without grepping 10k log lines. Carries the predicted tap zone (<see cref="MinTapWorld"/> world
    /// units at the level's predicted <see cref="OrthoSize"/>) and every tapable pair closer than it. An EMPTY
    /// <see cref="Violations"/> list means the level is clean; that is distinct from an audit that early-exits
    /// (&lt;2 tapable pieces or unframable content), which reports <see cref="MinTapWorld"/>=0 and no violations.
    /// Diagnostic only: <c>sim-gate.py</c> judges just <c>runs</c>, so this block never gates — gating tight spacing
    /// stays the validator's job at load (phase 2, <see cref="TapZoneConfig.EnforceMinDistanceAsError"/>). See
    /// docs/design/level-authoring.md §D12 and docs/architecture/sim-harness-metrics.md.
    /// </summary>
    public readonly struct TapSpacingAudit
    {
        public readonly string LevelId;
        public readonly float MinTapWorld;
        public readonly float OrthoSize;
        public readonly IReadOnlyList<TapSpacingViolation> Violations;

        public TapSpacingAudit(string levelId, float minTapWorld, float orthoSize,
            IReadOnlyList<TapSpacingViolation> violations)
        {
            LevelId = levelId;
            MinTapWorld = minTapWorld;
            OrthoSize = orthoSize;
            Violations = violations ?? Array.Empty<TapSpacingViolation>();
        }
    }
}
