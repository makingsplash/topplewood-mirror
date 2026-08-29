using DataStorage;
using SGS.Infrastructure.Tools.PlayerPrefsWrapper;
using UnityEngine;

namespace Services.Gameplay
{
    /// <summary>
    /// Prod <see cref="IBoosterChargeStore"/> (architect §3.2): a booster charge is a <see cref="PlayerPrefsInt"/>
    /// in <c>Meta</c>, beside <c>GoldAmount</c>/<c>HeartsAmount</c> — the same proven mutate-then-<c>ForceSave</c>
    /// pattern as <c>HeartsService</c>. <see cref="Grant"/> clamps to the caller's cap; <see cref="TrySpend"/> is
    /// atomic (never drives the balance negative). Today only <see cref="BoosterId.Hint"/> has a key; a second
    /// booster turns <see cref="ChargesPref"/> into a per-id table (tech-debt).
    /// </summary>
    public sealed class MetaBoosterChargeStore : IBoosterChargeStore
    {
        public int Get(BoosterId id) => ChargesPref(id).Value;

        public bool TrySpend(BoosterId id, int amount)
        {
            if (amount <= 0)
                return false;

            var pref = ChargesPref(id);
            if (pref.Value < amount)
                return false;

            pref.Value -= amount;
            pref.ForceSave();
            return true;
        }

        public void Grant(BoosterId id, int amount, ChargeSource source, int cap)
        {
            if (amount <= 0)
                return;

            var pref = ChargesPref(id);
            pref.Value = Mathf.Clamp(pref.Value + amount, 0, cap);
            pref.ForceSave();
        }

        private static PlayerPrefsInt ChargesPref(BoosterId id) => Data.Meta.HintCharges;
    }
}
