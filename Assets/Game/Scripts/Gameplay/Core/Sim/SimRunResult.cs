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
        // TASK-042 honesty detector (semantics rewritten by TASK-043; docstring corrected TASK-056): physical-fidelity
        // checks over the FINAL settled poses, the proof the owner's interpenetration bug is gone in EVERY release
        // order. IntersectingPairs = count of Placed piece pairs whose SAT penetration OVERSHOOTS their baseline by
        // more than DetectorEpsilon (= 0.05 world units; see SimRun.cs) — the overshoot, NOT the raw depth, so authored
        // micro-overlaps (owl's ear in the head) are not false-flagged. TASK-077: baseline = max(authored slot overlap,
        // the solver's honest-rest allowance for the pair type — ContactRestAllowance). MaxPenetration = the worst such
        // overshoot (max(penetration − baseline), clamped ≥0; baseline already subtracted in SimRun — 0 ⇒ every
        // contact within baseline). The sim-gate fails a win above PEN_EPS. TASK-343: the freeze-in-air
        // UnsupportedPlaced field was REMOVED — a directional rudiment after FreezeAt's removal (TASK-305); "not moving"
        // is proven by the rest-gated Won verdict itself. Sim-only measurement; the sim-gate is what enforces it.
        public readonly int IntersectingPairs;
        public readonly float MaxPenetration;
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
            int intersectingPairs, float maxPenetration,
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
            WorstPair = worstPair;
        }
    }

    /// <summary>
    /// TASK-042 gate-v2 (owner 2026-07-26 20:42, pt.2): the Placed pair with the largest overshoot above its authored
    /// baseline in a sim run — the diagnostic the owner asked for so a detector hit on CANON is explained by data, not
    /// a guess. <see cref="Penetration"/> is the raw SAT depth; <see cref="Baseline"/> is the baseline SimRun applied to
    /// this pair (TASK-077: <see cref="ContactRestAllowance.Baseline"/> — 0.01 for circle–circle unless the authored
    /// overlap is larger, the authored overlap otherwise); <see cref="SlotA"/>/<see cref="SlotB"/> are the
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
