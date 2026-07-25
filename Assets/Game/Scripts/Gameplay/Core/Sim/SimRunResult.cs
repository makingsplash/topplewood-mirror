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

        public SimRunResult(string levelId, int seed, string policyId, string orderKey, int repeatIndex,
            bool solved, string outcome, int attemptsUsed, int stepsToResolve, float matchError,
            int slotsFilled, int slotsTotal, int lostPieces, long poseHash,
            float preGlideAngResidual, float preGlidePosErr, IReadOnlyList<SimPiecePose> finalPoses)
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
        }
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
