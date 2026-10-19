using System;
using DataStorage;
using Gameplay.Configs;
using R3;
using SGS.Infrastructure.Tools.PlayerPrefsWrapper.Storage;

namespace Services.Gameplay
{
    /// <summary>
    /// Today's charge faucet for the Hint booster, without a currency (architect §3.3, balancer §1). Since TASK-635 the
    /// booster is LOCKED until the hint tour's level: no install seed any more — <see cref="Unlock"/> (driven by
    /// <c>HintUnlockController</c>) sets the persisted flag and, on the tour's level, tops the stock up to
    /// <c>UnlockGrantTo</c>. After the unlock it tops the stock up to the daily-refill target on a new day, and spends
    /// one charge per accepted activation. All numbers come from <c>Data.Configs.BoostersConfig.Hint</c> (config-over-
    /// hardcode). The reactive <see cref="Charges"/>/<see cref="Unlocked"/> mirror the persisted state so the presenter's
    /// badge, enabled-state and visibility stay in sync (R3, the project's reactive idiom). No other source is wired:
    /// rewarded plumbing is dead (owner 20:47), and the TASK-240 coin purchase was removed with the currency (TASK-612).
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
            // TASK-635: no install seed (Meta.HintSeeded is dormant) and no refill while locked — otherwise the badge
            // would read "3" on the tour instead of the owner's "2".
            _unlocked = new ReactiveProperty<bool>(Data.Meta.HintUnlocked.Value);
            if (_unlocked.Value)
                ApplyDailyRefill();
            _charges = new ReactiveProperty<int>(_store.Get(BoosterId.Hint));
        }

        /// <summary>Current Hint charges, reactive (badge + enabled-state).</summary>
        public ReadOnlyReactiveProperty<int> Charges => _charges;

        /// <summary>The button exists for the player (TASK-635): false until the hint tour's level or a silent unlock.</summary>
        public ReadOnlyReactiveProperty<bool> Unlocked => _unlocked;

        /// <summary>
        /// Unlock the booster (TASK-635 plan §3.1). <paramref name="introGrant"/> = the tour's level: top the stock up TO
        /// <c>UnlockGrantTo</c> (repeatable — a tour replayed after a kill tops up again, never above the grant). On the
        /// FIRST unlock the daily-refill stamp is set: with the intro grant to now (the first refill comes the next
        /// calendar day, design 01 §4); silently, the refill runs now (a legacy player past the place keeps the faucet).
        /// The flag is flushed durably (TASK-537).
        /// </summary>
        public void Unlock(bool introGrant)
        {
            var first = !_unlocked.Value;
            if (introGrant)
            {
                var missing = HintUnlockRules.TopUpAmount(_store.Get(BoosterId.Hint), Cfg.UnlockGrantTo);
                if (missing > 0)
                    _store.Grant(BoosterId.Hint, missing, ChargeSource.FreeGrant, Cfg.StockCap);
            }

            if (first)
            {
                Data.Meta.HintUnlocked.Value = true;
                if (introGrant)
                    Data.Meta.HintLastDailyRefill.Value = DateTime.Now;
                else
                    ApplyDailyRefill();
            }

            PlayerPrefsCollection.Instance?.SaveDataIfChanged();
            _charges.Value = _store.Get(BoosterId.Hint);
            _unlocked.Value = true;
        }

        // The BoostersConfig asset is editor-assigned; until it is (or in a build without it), fall back to the C#
        // defaults — which ARE the shipping values (balancer §5). Remote-config A/B overrides apply once the asset
        // is assigned, with no code change (RemoteConfigMap already maps it to config_boosters).
        private static readonly BoostersConfig.HintData Fallback = new();

        private static BoostersConfig.HintData Cfg
        {
            get
            {
                var cfg = Data.Configs.BoostersConfig;
                return cfg != null && cfg.Hint != null ? cfg.Hint : Fallback;
            }
        }

        /// <summary>Resolved Hint knobs (asset if present, else C# defaults) — shared with the presenter so both
        /// read one source.</summary>
        public BoostersConfig.HintData HintConfig => Cfg;

        /// <summary>Bounded free faucet (balancer §1.3), only once unlocked (TASK-635): once per calendar day, top the stock UP TO
        /// <c>DailyRefillTo</c> (not +n) — a player at 2 becomes 3, a player at 4 (hoarded) stays 4. 0 disables it
        /// (A/B control arm). Uses <see cref="DateTime.Now"/> like <c>HeartsService</c>.</summary>
        private void ApplyDailyRefill()
        {
            var target = Cfg.DailyRefillTo;
            if (target <= 0)
                return;

            var now = DateTime.Now;
            if (now.Date <= Data.Meta.HintLastDailyRefill.Value.Date)
                return;

            var missing = target - _store.Get(BoosterId.Hint);
            if (missing > 0)
                _store.Grant(BoosterId.Hint, missing, ChargeSource.FreeGrant, Cfg.StockCap);

            Data.Meta.HintLastDailyRefill.Value = now;
            PlayerPrefsCollection.Instance?.SaveDataIfChanged();   // stamp durable with the grant (TASK-537)
        }

        public bool HasCharge => _charges.Value > 0;

        /// <summary>Spend for one accepted activation (balancer §1.2: one activation = one revealed piece). Call
        /// ONLY after the resolver found a real piece to reveal — a no-op must never spend (that stays the
        /// presenter's contract, §5). Returns false when short.</summary>
        public bool TrySpendActivation()
        {
            if (!_store.TrySpend(BoosterId.Hint, Cfg.SpendPerActivation))
                return false;

            _charges.Value = _store.Get(BoosterId.Hint);
            return true;
        }

        /// <summary>Dev/QA lever (architect §3.3): grant arbitrary charges for playtest. Not shipped in prod UI.</summary>
        public void GrantDev(int amount)
        {
            _store.Grant(BoosterId.Hint, amount, ChargeSource.FreeGrant, Cfg.StockCap);
            _charges.Value = _store.Get(BoosterId.Hint);
        }
    }
}
