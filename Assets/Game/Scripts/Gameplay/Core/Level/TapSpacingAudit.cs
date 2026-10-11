using System;
using System.Collections.Generic;

namespace Gameplay.Core
{
    /// <summary>TASK-087: which tap-spacing rule a violation breaks (metric v2, docs/design/level-authoring.md §D12).</summary>
    public enum TapSpacingRule
    {
        /// <summary>Rule 1, ANY two tapable pieces (co-release pair members included): the edge-to-edge gap of the
        /// start polygons is below the tap-zone diameter, so the two halo-inflated tap targets overlap.</summary>
        Gap,

        /// <summary>Rule 2, members of ONE co-release group only: their centres are closer than the two-finger spread,
        /// so two simultaneous fingers do not fit / the touchscreen may merge them into one touch.</summary>
        CoReleaseSpread,
    }

    /// <summary>
    /// TASK-181: one tapable start-pose pair that breaks a tap-spacing rule — the structural, machine-readable form of
    /// the load-time "Tap spacing" warning. Indices are into <c>LevelData.Pieces</c>; kinds are the piece
    /// <c>Type.Kind</c> (may be null, exactly as the warning string interpolates it). TASK-087 (metric v2):
    /// <see cref="Gap"/> is the EDGE-TO-EDGE world gap of the two start polygons (<see cref="Geometry.PolygonGap"/>),
    /// <see cref="CentreDistance"/> the centre-to-centre world distance of their start positions (the v1 metric, kept
    /// for comparison and as the measure of <see cref="TapSpacingRule.CoReleaseSpread"/>). The same pair can appear
    /// twice — once per rule — because the two rules are different causes.
    /// </summary>
    public readonly struct TapSpacingViolation
    {
        public readonly int IndexA;
        public readonly string KindA;
        public readonly int IndexB;
        public readonly string KindB;
        public readonly TapSpacingRule Rule;
        public readonly float Gap;
        public readonly float CentreDistance;

        /// <summary>Both pieces are members of the same <c>LevelData.CoReleaseGroups</c> group (tapped together by two
        /// fingers rather than one after another).</summary>
        public readonly bool SameGroup;

        public TapSpacingViolation(int indexA, string kindA, int indexB, string kindB, TapSpacingRule rule, float gap,
            float centreDistance, bool sameGroup)
        {
            IndexA = indexA;
            KindA = kindA;
            IndexB = indexB;
            KindB = kindB;
            Rule = rule;
            Gap = gap;
            CentreDistance = centreDistance;
            SameGroup = sameGroup;
        }
    }

    /// <summary>
    /// TASK-181: per-level result of the tap-spacing audit as DATA, so the sim sweep can lift it out of the log and
    /// into its JSON report (the <c>tapAudit</c> block) — an author reading the report inside their own run sees the
    /// spacing signal without grepping 10k log lines. Carries the predicted tap zone (<see cref="MinTapWorld"/> world
    /// units at the level's predicted <see cref="OrthoSize"/>), the predicted two-finger spread
    /// (<see cref="CoReleaseSpreadWorld"/>, TASK-087) and every tapable pair that breaks either rule. An EMPTY
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
        public readonly float CoReleaseSpreadWorld;
        public readonly IReadOnlyList<TapSpacingViolation> Violations;

        public TapSpacingAudit(string levelId, float minTapWorld, float orthoSize, float coReleaseSpreadWorld,
            IReadOnlyList<TapSpacingViolation> violations)
        {
            LevelId = levelId;
            MinTapWorld = minTapWorld;
            OrthoSize = orthoSize;
            CoReleaseSpreadWorld = coReleaseSpreadWorld;
            Violations = violations ?? Array.Empty<TapSpacingViolation>();
        }
    }
}
