using System.Collections.Generic;

namespace Gameplay.Core.Sim
{
    /// <summary>
    /// One sweep run's outcome (RFC §5.2) — a single CSV row. Values are raw measurements; interpretation
    /// (solve-rate thresholds, tolerance headroom) is the balancer/QA's call, not the harness'.
    /// </summary>
    public readonly struct SimRunResult
    {
        public readonly string LevelId;
        public readonly int Seed;
        public readonly string PolicyId;
        public readonly string OrderKey;
        public readonly int RepeatIndex;
        public readonly bool Solved;
        public readonly string Outcome;
        public readonly int AttemptsUsed;
        public readonly int StepsToResolve;
        public readonly float MatchError;
        public readonly int SlotsFilled;
        public readonly int SlotsTotal;
        public readonly int LostPieces;
        public readonly long PoseHash;
        // TASK-024 §5: worst per-piece residual to the nearest same-Kind slot measured at the pre-glide physical
        // rest pose (the tick a piece first turns Placed, before the soft-settle glide moves it). Angular is
        // symmetry-reduced radians; positional is world units. Small angular ⇒ real contact torque did the turn (not
        // the glide); positional within SnapCaptureRadius ⇒ the topple did not carry the piece out of its window.
        // Zero on runs with no physical-settle pieces (flat assembly / balance) — interpretation is ⟨balancer⟩/⟨qa⟩.
        public readonly float PreGlideAngResidual;
        public readonly float PreGlidePosErr;
        // TASK-025 STEP-1 (observability): every piece's final resolved pose (kind + world x/y + rotation + state) at
        // the tick the run resolves (Won/Lost/Timeout — irrelevant). JSON-only (omitted from the CSV row: it is an
        // array, not a scalar). Lets the balancer author silhouette slots FROM where the deterministic physics
        // actually lands pieces, instead of blind trajectory-fitting (05 §9 request). Sim-only; production untouched.
        public readonly IReadOnlyList<SimPiecePose> FinalPoses;
        // TASK-026 STEP-1 (observability): each physical-settle piece's raw pose at its capture tick (the tick it
        // first turns Placed, BEFORE the soft-settle glide moves it) — the same snapshot the PreGlide* scalars are
        // read from, but the full (kind, x, y, rot) VECTOR instead of the worst scalar. Lets the owner diff rest − slot
        // per piece and precompensate the tilted-piece start so physics itself lands the slot. JSON-only (an array,
        // omitted from the CSV row like FinalPoses). Empty on runs with no physical-settle pieces. Sim-only.
        public readonly IReadOnlyList<SimPiecePose> PreGlidePoses;
        // TASK-042 honesty detector: physical-fidelity checks over the FINAL settled poses, the proof the owner's bug
        // (interpenetration + freeze-in-air) is gone in EVERY release order. IntersectingPairs = count of Placed piece
        // pairs whose SAT penetration exceeds 0.01 world units (touching/epsilon is not an intersection). MaxPenetration
        // = the worst such depth (0 ⇒ clean). UnsupportedPlaced = count of Placed pieces with nothing (floor or a lower
        // Placed piece) within a small gap beneath them — i.e. frozen on an "invisible support". Pass criterion (any
        // order): all three are 0. Sim-only measurement; the controller's honesty gate is what enforces it.
        public readonly int IntersectingPairs;
        public readonly float MaxPenetration;
        public readonly int UnsupportedPlaced;
        // TASK-042 reason-counters (owner: "добавь reason-счётчики в сим, не гадай"): how many snap candidates the
        // controller honesty gate rejected this run, split by cause — a slot pose overlapping a placed piece beyond
        // the authored baseline (ByOverlap) vs. one with no static/placed support beneath it (BySupport). Non-zero on
        // a CANONICAL run is the diagnostic that pins WHICH clamp mis-fired on which level (e.g. owl's ear).
        public readonly int SnapRejectsByOverlap;
        public readonly int SnapRejectsBySupport;
        // TASK-042 gate-v3 (owner 2026-07-26 20:55, pt.1): freeze-path counterpart of SnapRejectsBySupport — how many
        // freeze-at-rest attempts the controller's Place support-guard rejected this run. Non-zero in a wrong order is
        // the owner's proof the FREEZE path (not just the snap path) is support-gated; 0 on canon.
        public readonly int FreezeRejectsBySupport;
        // TASK-042 gate-v2 (owner 2026-07-26 20:42, pt.2 — "НЕ ГАДАЙ: добавь per-run детали худшей пары"): the pair of
        // Placed pieces driving MaxPenetration this run — kinds, raw SAT penetration, the authored baseline applied to
        // them, and both claimed slot indices. On a CANON run flagged by the detector this row answers WHY without a
        // second sweep: penetration ≈ baseline + a small slop ⇒ real physical-settle slop above baseline (calibrate ε);
        // baseline that belongs to a different slot pair (unexpected slotA/slotB for the two same-kind kinds) ⇒ a
        // cross-mapping to fix. Zeroed/-1 when no Placed pair overlaps at all (Worst == None).
        public readonly WorstOverlapPair WorstPair;

        public SimRunResult(string levelId, int seed, string policyId, string orderKey, int repeatIndex,
            bool solved, string outcome, int attemptsUsed, int stepsToResolve, float matchError,
            int slotsFilled, int slotsTotal, int lostPieces, long poseHash,
            float preGlideAngResidual, float preGlidePosErr, IReadOnlyList<SimPiecePose> finalPoses,
            IReadOnlyList<SimPiecePose> preGlidePoses,
            int intersectingPairs, float maxPenetration, int unsupportedPlaced,
            int snapRejectsByOverlap, int snapRejectsBySupport, int freezeRejectsBySupport,
            WorstOverlapPair worstPair)
        {
            LevelId = levelId;
            Seed = seed;
            PolicyId = policyId;
            OrderKey = orderKey;
            RepeatIndex = repeatIndex;
            Solved = solved;
            Outcome = outcome;
            AttemptsUsed = attemptsUsed;
            StepsToResolve = stepsToResolve;
            MatchError = matchError;
            SlotsFilled = slotsFilled;
            SlotsTotal = slotsTotal;
            LostPieces = lostPieces;
            PoseHash = poseHash;
            PreGlideAngResidual = preGlideAngResidual;
            PreGlidePosErr = preGlidePosErr;
            FinalPoses = finalPoses;
            PreGlidePoses = preGlidePoses;
            IntersectingPairs = intersectingPairs;
            MaxPenetration = maxPenetration;
            UnsupportedPlaced = unsupportedPlaced;
            SnapRejectsByOverlap = snapRejectsByOverlap;
            SnapRejectsBySupport = snapRejectsBySupport;
            FreezeRejectsBySupport = freezeRejectsBySupport;
            WorstPair = worstPair;
        }
    }

    /// <summary>
    /// TASK-042 gate-v2 (owner 2026-07-26 20:42, pt.2): the Placed pair with the largest overshoot above its authored
    /// baseline in a sim run — the diagnostic the owner asked for so a detector hit on CANON is explained by data, not
    /// a guess. <see cref="Penetration"/> is the raw SAT depth; <see cref="Baseline"/> is the authored overlap the
    /// controller applied to this pair (via <c>OverlapAllowance</c>); <see cref="SlotA"/>/<see cref="SlotB"/> are the
    /// slots each piece claimed (-1 if none). A canon hit with Penetration ≈ Baseline + small residual ⇒ physical-
    /// settle slop (calibrate ε); an unexpected slot pair for two same-kind kinds ⇒ a cross-mapping to fix. Sim-only.
    /// </summary>
    public readonly struct WorstOverlapPair
    {
        public readonly string KindA;
        public readonly string KindB;
        public readonly float Penetration;
        public readonly float Baseline;
        public readonly int SlotA;
        public readonly int SlotB;

        public WorstOverlapPair(string kindA, string kindB, float penetration, float baseline, int slotA, int slotB)
        {
            KindA = kindA;
            KindB = kindB;
            Penetration = penetration;
            Baseline = baseline;
            SlotA = slotA;
            SlotB = slotB;
        }

        /// <summary>Sentinel for a run with no overlapping Placed pair — empty kinds, zero depths, -1 slots.</summary>
        public static WorstOverlapPair None => new("", "", 0f, 0f, -1, -1);
    }

    /// <summary>
    /// TASK-025 STEP-1: one piece's final pose in a sim run (kind + world position + rotation + life-cycle state).
    /// Emitted per run so a human/balancer can read where the deterministic physics deposits each piece and author
    /// slots to those observed rest points. Rotation is raw radians (NOT symmetry-reduced — this is a physical
    /// reading, not a match residual). Sim-report data only; not part of the domain/production path.
    /// </summary>
    public readonly struct SimPiecePose
    {
        public readonly string Kind;
        public readonly float X;
        public readonly float Y;
        public readonly float Rot;
        public readonly string State;

        public SimPiecePose(string kind, float x, float y, float rot, string state)
        {
            Kind = kind;
            X = x;
            Y = y;
            Rot = rot;
            State = state;
        }
    }
}
