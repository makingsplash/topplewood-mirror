namespace Gameplay.Core
{
    /// <summary>Outcome of pose-match evaluation (RFC §4.1). <see cref="TotalError"/> feeds analytics/hint.</summary>
    public readonly struct MatchResult
    {
        public readonly bool AllSlotsFilled;
        public readonly int SlotsFilled;
        public readonly int SlotsTotal;
        public readonly float TotalError;

        public MatchResult(bool allSlotsFilled, int slotsFilled, int slotsTotal, float totalError)
        {
            AllSlotsFilled = allSlotsFilled;
            SlotsFilled = slotsFilled;
            SlotsTotal = slotsTotal;
            TotalError = totalError;
        }
    }
}
