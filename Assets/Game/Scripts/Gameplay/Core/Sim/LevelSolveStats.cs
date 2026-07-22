namespace Gameplay.Core.Sim
{
    /// <summary>
    /// Per-level solve statistics (RFC §5.4). A difficulty proxy: fewer reliable orders and/or more steps ⇒
    /// harder. Consumed by balancer/QA; the harness never judges pass/fail — there is no boolean "solvable".
    /// </summary>
    public readonly struct LevelSolveStats
    {
        public readonly string LevelId;
        public readonly int OrdersTried;
        public readonly int ReliableSolveOrders;
        public readonly float SolveRate;
        public readonly bool Deterministic;
        public readonly float AvgStepsToSolve;
        public readonly int MinStepsToSolve;
        public readonly float MedianMatchError;

        public LevelSolveStats(string levelId, int ordersTried, int reliableSolveOrders, float solveRate,
            bool deterministic, float avgStepsToSolve, int minStepsToSolve, float medianMatchError)
        {
            LevelId = levelId;
            OrdersTried = ordersTried;
            ReliableSolveOrders = reliableSolveOrders;
            SolveRate = solveRate;
            Deterministic = deterministic;
            AvgStepsToSolve = avgStepsToSolve;
            MinStepsToSolve = minStepsToSolve;
            MedianMatchError = medianMatchError;
        }
    }
}
