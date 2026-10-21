using System;
using DataStorage;
using Gameplay.Configs;
using Gameplay.Core;
using Gameplay.PlayScreen;
using Services;
using Services.Gameplay;
using Tools.Constants;
using UnityEngine;

namespace Gameplay.Adapters
{
    /// <summary>
    /// Production <see cref="IBetweenLevelsAds"/> (TASK-658): the between-levels interstitial through the SayKit wrapper.
    /// The threshold is read from <c>Data.Configs.AdsConfig</c> on EVERY decision — never latched — so a new
    /// <c>FromLevel</c> from SayLauncher applies to existing installs too (why <c>RewardedStateService</c>, which latches
    /// its level in PlayerPrefs, is not used — plan §6). Gates in order: the rule (<see cref="InterstitialRules"/>), premium,
    /// SDK initialised (an interstitial has no readiness gate of its own — the caller gates, canon saykit.md §Ads),
    /// <c>IsInterstitialAvailable</c> (the publisher's frequency rules answer here), then <c>ShowInterstitial</c>.
    /// One <c>[ads] interstitial …</c> log line per decision — the editor acceptance and the on-device remote check.
    /// Fire-and-forget: <c>onClose</c> only logs, the caller never waits for it.
    /// </summary>
    public sealed class SayKitBetweenLevelsAds : IBetweenLevelsAds
    {
        private readonly ICampaignStore _store;

        public SayKitBetweenLevelsAds(ICampaignStore store)
        {
            _store = store;
        }

        public void TryShow(BetweenLevelsMoment moment)
        {
            // On a win the pointer is already advanced at resolve (durable WIN checkpoint, CampaignProgressionController),
            // so the level just played is one behind; on a loss the pointer has not moved. LevelNumber keeps growing past
            // the line-up (pool), so the threshold keeps working there.
            var current = _store.Read().LevelNumber;
            var playedLevel = moment == BetweenLevelsMoment.AfterWin ? current - 1 : current;
            var cfg = Data.Configs.AdsConfig?.Interstitial ?? new AdsConfig.InterstitialData();
            var head = $"[ads] interstitial {moment} level={playedLevel} from={cfg.FromLevel} →";

            if (!InterstitialRules.ShouldOffer(moment, playedLevel, cfg))
            {
                Debug.Log($"{head} skip(rule)");
                return;
            }

            var sdk = GlobalService.SayKit;
            if (sdk == null || !sdk.IsInitialized())
            {
                Debug.Log($"{head} skip(sdk-not-ready)");
                return;
            }

            if (sdk.IsPremium())
            {
                Debug.Log($"{head} skip(premium)");
                return;
            }

            var place = moment == BetweenLevelsMoment.AfterWin
                ? Placement.InterstitialWinLevel
                : Placement.InterstitialLoseLevel;

            if (!sdk.IsInterstitialAvailable(place))
            {
                Debug.Log($"{head} skip(not-available) place={place}");
                return;
            }

            if (!sdk.ShowInterstitial(place, () => Debug.Log($"[ads] interstitial closed place={place}")))
            {
                Debug.Log($"{head} skip(show-refused) place={place}");
                return;
            }

            Data.Meta.LastInterstitial = DateTime.Now;
            Debug.Log($"{head} SHOW place={place}");
        }
    }
}
