using DataStorage;
using Gameplay.Configs;
using R3;
using SGS.Infrastructure.Tools.PlayerPrefsWrapper.Storage;

namespace Services.Gameplay
{
    /// <summary>
    /// The Hint booster's charges, without a currency (architect §3.3, balancer §1). Since TASK-635 the booster is
    /// LOCKED until the hint tour's level: no install seed — <see cref="Unlock"/> (driven by <c>HintUnlockController</c>)
    /// sets the persisted flag and, on the tour's level, sets the stock to exactly <c>UnlockGrantTo</c> (TASK-638: assign,
    /// not top up). That is the ONLY source of charges: there is no daily refill (owner 2026-10-19, TASK-638 — «У нас не
    /// должно быть никаких ежедневных пополнений бустера»), rewarded plumbing is dead (owner 20:47), and the TASK-240
    /// coin purchase went with the currency (TASK-612). It spends one charge per accepted activation. Numbers come from
    /// <c>Data.Configs.BoostersConfig.Hint</c> (config-over-hardcode).
    ///
    /// <see cref="Charges"/>/<see cref="Unlocked"/> are a reactive MIRROR of the store (badge, enabled-state, visibility —
    /// R3, the project's reactive idiom). The store is the truth: <see cref="ResyncFromStore"/> re-reads it, and
    /// <c>HintUnlockController</c> calls it on every level load, so a write behind the service's back (the DEV "Reset
    /// Progress" reloads the level IN PLACE, the scope and this singleton live on) never leaves a stale badge. TASK-638:
    /// the stale "3" over an empty store made the tour's tap a silent no-op on the owner's phone.
    /// A future source adds a <see cref="IBoosterChargeStore.Grant"/> call with the right <see cref="ChargeSource"/>.
    /// </summary>
    public sealed class HintBoosterService
    {
        private readonly IBoosterChargeStore _store;
        private readonly ReactiveProperty<int> _charges;
        private readonly ReactiveProperty<bool> _unlocked;

        public HintBoosterService(IBoosterChargeStore store)
        {
            _store = store;
            _unlocked = new ReactiveProperty<bool>(_store.IsUnlocked(BoosterId.Hint));
            _charges = new ReactiveProperty<int>(_store.Get(BoosterId.Hint));
        }

        /// <summary>Current Hint charges, reactive (badge + enabled-state).</summary>
        public ReadOnlyReactiveProperty<int> Charges => _charges;

        /// <summary>The button exists for the player (TASK-635): false until the hint tour's level or a silent unlock.</summary>
        public ReadOnlyReactiveProperty<bool> Unlocked => _unlocked;

        /// <summary>Re-read the mirror from the store (TASK-638). Cheap (two pref reads); setting an unchanged value
        /// notifies nobody, so calling it on every level load is free for the subscribers.</summary>
        public void ResyncFromStore()
        {
            _unlocked.Value = _store.IsUnlocked(BoosterId.Hint);
            _charges.Value = _store.Get(BoosterId.Hint);
        }

        /// <summary>
        /// Unlock the booster (TASK-635 plan §3.1). <paramref name="introGrant"/> = the tour's level: the stock becomes
        /// EXACTLY <c>UnlockGrantTo</c> (TASK-638) — before the unlock the player can neither spend nor earn, so anything
        /// else in the store is a leftover of an older build's install seed, and the owner's word is "2". Repeatable: a
        /// tour replayed after a kill sets 2 again. A silent unlock (a player already past the tour's place) leaves the
        /// charges alone. Both writes are flushed durably by the store (TASK-537).
        /// </summary>
        public void Unlock(bool introGrant)
        {
            if (introGrant)
                _store.Set(BoosterId.Hint, Cfg.UnlockGrantTo, ChargeSource.FreeGrant);
            if (!_store.IsUnlocked(BoosterId.Hint))
                _store.MarkUnlocked(BoosterId.Hint);

            PlayerPrefsCollection.Instance?.SaveDataIfChanged();
            _charges.Value = _store.Get(BoosterId.Hint);
            _unlocked.Value = true;
        }

        // The BoostersConfig asset is editor-assigned; until it is (or in a build without it, or in an EditMode test with
        // no Data), fall back to the C# defaults — which ARE the shipping values (balancer §5). Remote-config overrides
        // apply once the asset is assigned, with no code change (RemoteConfigMap already maps it to config_boosters).
        private static readonly BoostersConfig.HintData Fallback = new();

        private static BoostersConfig.HintData Cfg
        {
            get
            {
                var cfg = Data.IsReady() ? Data.Configs.BoostersConfig : null;
                return cfg != null && cfg.Hint != null ? cfg.Hint : Fallback;
            }
        }

        /// <summary>Resolved Hint knobs (asset if present, else C# defaults) — shared with the presenter so both
        /// read one source.</summary>
        public BoostersConfig.HintData HintConfig => Cfg;

        public bool HasCharge => _charges.Value > 0;

        /// <summary>Spend for one accepted activation (balancer §1.2: one activation = one revealed piece). Call
        /// ONLY after the resolver found a real piece to reveal — a no-op must never spend (that stays the
        /// presenter's contract, §5). Returns false when short — and then the mirror is re-read, so a badge that
        /// disagreed with the store shows the truth instead of offering a tap that can never spend (TASK-638).</summary>
        public bool TrySpendActivation()
        {
            var spent = _store.TrySpend(BoosterId.Hint, Cfg.SpendPerActivation);
            _charges.Value = _store.Get(BoosterId.Hint);
            return spent;
        }

        /// <summary>Dev/QA lever (architect §3.3): grant arbitrary charges for playtest. Not shipped in prod UI.</summary>
        public void GrantDev(int amount)
        {
            _store.Grant(BoosterId.Hint, amount, ChargeSource.FreeGrant, Cfg.StockCap);
            _charges.Value = _store.Get(BoosterId.Hint);
        }
    }
}
