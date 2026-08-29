namespace Services.Gameplay
{
    /// <summary>
    /// Booster identity (TASK-237). Only <see cref="Hint"/> exists today; the enum is the seam a future booster
    /// (B "Undo" already has a backlog ticket) plugs into without touching the charge/faucet machinery. The
    /// per-<see cref="BoosterId"/> PlayerPrefs key is resolved in <see cref="MetaBoosterChargeStore"/> — today it
    /// hardcodes the single Hint key (YAGNI, one booster); a second booster turns that into a key table (tech-debt).
    /// </summary>
    public enum BoosterId
    {
        Hint,
    }

    /// <summary>
    /// Where a booster charge came from — the "заряд ← источник" seam (designer §5 / architect §3.2). Today only
    /// <see cref="FreeGrant"/> is live (seed + daily refill); <see cref="RewardedAd"/> is a source string but the ad
    /// plumbing is dead (owner 20:47, debt TASK-127), and <see cref="CoinPurchase"/>/<see cref="LevelReward"/> are
    /// the currency hooks the NEXT ticket (TASK-240) wires in — it adds the CALL, not the type, so the charge/cap
    /// machinery never re-forms.
    /// </summary>
    public enum ChargeSource
    {
        FreeGrant,
        RewardedAd,
        CoinPurchase,
        LevelReward,
    }

    /// <summary>
    /// Charge store abstraction (architect §3.2): a booster's charge is meta-state (survives an app restart, but is
    /// NOT in-level — GAME_RULES §5). Grant clamps to a per-call cap; TrySpend is atomic (false when short). The
    /// prod impl is <see cref="MetaBoosterChargeStore"/> (PlayerPrefs in <c>Meta</c>, beside Gold/Hearts).
    /// </summary>
    public interface IBoosterChargeStore
    {
        int Get(BoosterId id);
        bool TrySpend(BoosterId id, int amount);
        void Grant(BoosterId id, int amount, ChargeSource source, int cap);
    }
}
