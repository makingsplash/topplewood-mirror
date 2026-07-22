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

        public SimRunResult(string levelId, int seed, string policyId, string orderKey, int repeatIndex,
            bool solved, string outcome, int attemptsUsed, int stepsToResolve, float matchError,
            int slotsFilled, int slotsTotal, int lostPieces, long poseHash)
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
        }
    }
}
