using System;
using Newtonsoft.Json;
using SGS.Infrastructure.Tools.Configs;
using UnityEngine;

namespace Gameplay.Configs
{
    /// <summary>
    /// Remote-drivable booster knobs (TASK-237), plugged into the pre-existing, unused
    /// <c>SayKitGameConfig.config_boosters</c> field (architect §4) — no SDK-config change. All defaults live in C#
    /// (balancer §5 "config-over-hardcode"); the A/B lever of this ticket (<see cref="HintData.ProactiveEnabled"/>) is
    /// remote-drivable at once. The daily-refill lever (<c>DailyRefillTo</c>) is GONE with the mechanic (owner 2026-10-19,
    /// TASK-638): a remote payload still carrying the key is ignored by Newtonsoft. Registered in <c>RemoteConfigMap</c> +
    /// <c>Configs</c> exactly like <see cref="RewardedConfig"/>; read through <c>Data.Configs.BoostersConfig.Hint</c>.
    /// </summary>
    [CreateAssetMenu(fileName = "BoostersConfig", menuName = "Configs/BoostersConfig")]
    public sealed class BoostersConfig : ConfigWithJson<BoostersConfig.Data>
    {
        [Serializable]
        [JsonObject]
        public class HintData
        {
            public int UnlockGrantTo = 2;                   // charges set to EXACTLY n on the hint tour level (owner, TASK-635/638)
            public int SpendPerActivation = 1;              // one activation reveals ONE next piece (balancer §1.2)
            public int StockCap = 5;                        // anti-hoard stock ceiling (balancer §1.3)
            public int RewardedGrantPerView = 1;            // charge per rewarded view — source not live today
            public float ReactivationCooldownSec = 2f;      // anti-misclick between activations (balancer §1.2)
            public int MinTapablePieces = 3;                // below this a hint is a no-op (balancer §2.2)
            public bool ProactiveEnabled = false;           // control(false) / treatment(true); A/B (balancer §4)
            public int ProactiveFailThresholdDefault = 3;   // fails before an offer, tiers ≤ HardTierFloor-1
            public int ProactiveFailThresholdHardTier = 2;  // fails before an offer, tiers ≥ HardTierFloor
            public int ProactiveHardTierFloor = 6;          // boundary of a "hard" tier
            public int ProactiveReofferSuppressFails = 2;   // extra fails suppressed after a declined offer
        }

        [Serializable]
        [JsonObject]
        public class Data
        {
            public HintData Hint;
        }

        public HintData Hint => Config.Hint;

        public override void InitWithRemote(string remoteConfig)
        {
            base.InitWithRemote(remoteConfig);
            Config ??= new();
            Config.Hint ??= new();
        }
    }
}
