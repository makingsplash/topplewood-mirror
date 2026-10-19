using DataStorage;
using SGS.Infrastructure.Tools.PlayerPrefsWrapper;
using SGS.Infrastructure.Tools.PlayerPrefsWrapper.Storage;
using UnityEngine;

namespace Services.Gameplay
{
    /// <summary>
    /// Prod <see cref="IBoosterChargeStore"/> (architect §3.2): a booster charge is a <see cref="PlayerPrefsInt"/>
    /// in <c>Meta</c>, beside <c>GoldAmount</c>/<c>HeartsAmount</c>, and the unlock is a <see cref="PlayerPrefsBool"/>
    /// beside it (TASK-635 key, behind this store since TASK-638). Every mutation flushes to disk on the spot
    /// (<c>SaveDataIfChanged</c>, TASK-537): the <c>Value</c> setter only stages <c>PlayerPrefs.SetInt</c>
    /// (<c>ForceSave</c> is the same <c>SetInt</c> again, NOT durable), so without the flush a kill in focus handed a
    /// spent charge back. <see cref="Grant"/> clamps to the caller's cap; <see cref="TrySpend"/> is atomic (never
    /// drives the balance negative). Today only <see cref="BoosterId.Hint"/> has keys; a second booster turns
    /// <see cref="ChargesPref"/>/<see cref="UnlockedPref"/> into a per-id table (tech-debt).
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
            PlayerPrefsCollection.Instance?.SaveDataIfChanged();
            return true;
        }

        public void Grant(BoosterId id, int amount, ChargeSource source, int cap)
        {
            if (amount <= 0)
                return;

            var pref = ChargesPref(id);
            pref.Value = Mathf.Clamp(pref.Value + amount, 0, cap);
            PlayerPrefsCollection.Instance?.SaveDataIfChanged();
        }

        public void Set(BoosterId id, int amount, ChargeSource source)
        {
            ChargesPref(id).Value = Mathf.Max(0, amount);
            PlayerPrefsCollection.Instance?.SaveDataIfChanged();
        }

        public bool IsUnlocked(BoosterId id) => UnlockedPref(id).Value;

        public void MarkUnlocked(BoosterId id)
        {
            UnlockedPref(id).Value = true;
            PlayerPrefsCollection.Instance?.SaveDataIfChanged();
        }

        private static PlayerPrefsInt ChargesPref(BoosterId id) => Data.Meta.HintCharges;

        private static PlayerPrefsBool UnlockedPref(BoosterId id) => Data.Meta.HintUnlocked;
    }
}
