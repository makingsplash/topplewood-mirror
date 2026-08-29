using System;
using DataStorage;
using Gameplay.Configs;
using R3;

namespace Services.Gameplay
{
    /// <summary>
    /// Today's charge faucet for the Hint booster, without a currency yet (architect §3.3, balancer §1). It seeds
    /// the one-time grant on first run, tops the stock up to the daily-refill target on a new day, and spends one
    /// charge per accepted activation. All numbers come from <c>Data.Configs.BoostersConfig.Hint</c> (config-over-
    /// hardcode). The reactive <see cref="Charges"/> mirrors the persisted balance so the presenter's badge and
    /// enabled-state stay in sync (R3, the project's reactive idiom). Rewarded/coin sources are NOT wired here
    /// today: rewarded plumbing is dead (owner 20:47), currency is the next ticket (TASK-240) — both add a
    /// <see cref="IBoosterChargeStore.Grant"/> call with the right <see cref="ChargeSource"/>, no re-form.
    /// </summary>
    public sealed class HintBoosterService
    {
        private readonly IBoosterChargeStore _store;
        private readonly ReactiveProperty<int> _charges;

        public HintBoosterService(IBoosterChargeStore store)
        {
            _store = store;
            SeedIfFirstRun();
            ApplyDailyRefill();
            _charges = new ReactiveProperty<int>(_store.Get(BoosterId.Hint));
        }

        /// <summary>Current Hint charges, reactive (badge + enabled-state).</summary>
        public ReadOnlyReactiveProperty<int> Charges => _charges;

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

        /// <summary>One-time seed on first install (balancer §1.1). Guarded by a persisted flag so it fires once,
        /// not every launch — a missing PlayerPref defaults to false, so an upgrading install still seeds.</summary>
        private void SeedIfFirstRun()
        {
            if (Data.Meta.HintSeeded.Value)
                return;

            _store.Grant(BoosterId.Hint, Cfg.SeedGrant, ChargeSource.FreeGrant, Cfg.StockCap);
            Data.Meta.HintSeeded.Value = true;
            Data.Meta.HintSeeded.ForceSave();
        }

        /// <summary>Bounded free faucet (balancer §1.3): once per calendar day, top the stock UP TO
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
            Data.Meta.HintLastDailyRefill.ForceSave();
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
