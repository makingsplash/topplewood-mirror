using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using SGS.Infrastructure.Tools.Configs;
using UnityEngine;

namespace Gameplay.Configs
{
    [CreateAssetMenu(fileName = "CoreConfig", menuName = "Configs/Core Config", order = 0)]
    public class CoreConfig : ConfigWithJson<CoreConfig.Data>
    {
        private const int DefaultAdditionalMovesOnRevive = 10;

        [Serializable]
        [JsonObject]
        public class Data
        {
            [JsonProperty]
            public int LevelReward;
            // TASK-240: per-tier reward curve (balancer 40/50/60/70/80 over bands ≤2/3-4/5-6/7-8/≥9). Format
            // "reward:maxTierInclusive", ascending, the last band is the ceiling. Empty/null ⇒ fall back to the flat
            // LevelReward (back-compat). An explicit table over an int+multiplier because the curve is authored, not
            // arithmetic (architect §2.3); A/B-able through the already-mapped config_core, no new asset.
            [JsonProperty]
            public string LevelRewardByTierBand = "40:2,50:4,60:6,70:8,80:999";
            // TASK-240: coins for RE-clearing an already-rewarded level (campaign wraps at the end, architect §5).
            // Default 0 keeps the faucet single-pass (holds the balancer ledger); raise via config for a grind loop,
            // no code change.
            [JsonProperty]
            public int ReplayReward;
            [JsonProperty]
            public int AdditionalTimeSeconds = 20;
            [JsonProperty]
            public int AdditionalMovesOnRevive = DefaultAdditionalMovesOnRevive;
            [JsonProperty]
            public int WinScreenX2ButtonStartLevel;
            // TASK-240: master switch for the net-new win-screen ×2 button (owner: visible stub). Default true.
            [JsonProperty]
            public bool WinScreenX2Enabled = true;
            [JsonProperty]
            public string RevivePricesList = "1200";


            [JsonProperty]
            public int LoseScreenOfferStartLevel = 2;
            [JsonProperty]
            public bool EnableFreeTutorialRevive;
            [JsonProperty]
            public bool EnableSingleTileColumnHelper = true;

            [NonSerialized]
            public int[] ParsedRevivePrices;
            [NonSerialized]
            public (int maxTier, int reward)[] ParsedRewardBands;
        }

        public int LevelReward => Config.LevelReward;
        public int ReplayReward => Config.ReplayReward;
        public bool WinScreenX2Enabled => Config.WinScreenX2Enabled;

        public int AdditionalTimeSeconds => Config.AdditionalTimeSeconds;
        public int AdditionalMovesOnRevive => Config.AdditionalMovesOnRevive > 0
            ? Config.AdditionalMovesOnRevive
            : DefaultAdditionalMovesOnRevive;
        public int WinScreenX2ButtonStartLevel => Config.WinScreenX2ButtonStartLevel;
        public int LoseScreenOfferStartLevel => Config.LoseScreenOfferStartLevel;
        public bool EnableFreeTutorialRevive => Config.EnableFreeTutorialRevive;
        public bool EnableSingleTileColumnHelper => Config.EnableSingleTileColumnHelper;

        public int GetRevivePrice(int restartsCount)
        {
            if (Config.ParsedRevivePrices.Length == 0)
            {
                return 99;
            }

            return Config.ParsedRevivePrices[Mathf.Min(restartsCount, Config.ParsedRevivePrices.Length - 1)];
        }

        /// <summary>TASK-240 (architect §2.3): coins for winning a level of the given difficulty tier. Linear scan
        /// for the first band whose <c>maxTier</c> covers the tier; an empty/unparsable table falls back to the flat
        /// <see cref="LevelReward"/> (defensive, mirrors <see cref="GetRevivePrice"/>).</summary>
        public int GetLevelReward(int tier)
        {
            var bands = Config.ParsedRewardBands;
            if (bands == null || bands.Length == 0)
            {
                return Config.LevelReward;
            }

            foreach (var band in bands)
            {
                if (tier <= band.maxTier)
                {
                    return band.reward;
                }
            }

            // Tier above the last band's ceiling → the last (highest) band's reward.
            return bands[bands.Length - 1].reward;
        }

        public override void InitWithRemote(string remoteConfig)
        {
            base.InitWithRemote(remoteConfig);
            Config.ParsedRevivePrices = ParseSimpleList(Config.RevivePricesList);
            Config.ParsedRewardBands = ParseRewardBands(Config.LevelRewardByTierBand);
        }

        // "reward:maxTier,reward:maxTier,…" → ascending (maxTier, reward) bands. Null/empty ⇒ null (GetLevelReward
        // then falls back to the flat LevelReward). Malformed entries are skipped rather than throwing, so a bad
        // remote value degrades to the flat reward instead of crashing config init.
        private static (int maxTier, int reward)[] ParseRewardBands(string bandsString)
        {
            if (string.IsNullOrWhiteSpace(bandsString))
            {
                return null;
            }

            var entries = bandsString.Split(",", StringSplitOptions.RemoveEmptyEntries);
            var parsed = new List<(int maxTier, int reward)>(entries.Length);
            foreach (var entry in entries)
            {
                var pair = entry.Split(":", StringSplitOptions.RemoveEmptyEntries);
                if (pair.Length == 2
                    && int.TryParse(pair[0].Trim(), out var reward)
                    && int.TryParse(pair[1].Trim(), out var maxTier))
                {
                    parsed.Add((maxTier, reward));
                }
            }

            return parsed.Count == 0 ? null : parsed.ToArray();
        }

        private int[] ParseSimpleList(string countsString)
        {
            if ( string.IsNullOrWhiteSpace(countsString))
            {
                return null;
            }

            var splitCounts = countsString.Split(",", StringSplitOptions.RemoveEmptyEntries);
            return splitCounts.Select(int.Parse).ToArray();
        }
    }
}
