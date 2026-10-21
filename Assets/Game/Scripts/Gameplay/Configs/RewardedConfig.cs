using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using SGS.Infrastructure.Tools.Common;
using SGS.Infrastructure.Tools.Configs;
using UnityEngine;

namespace Gameplay.Configs
{
    [Serializable]
    public class SteppedValuesList
    {
        private struct SteppedValue
        {
            public int Idx;
            public int Value;
        }
        
        private SteppedValue[] _steps;

        public SteppedValuesList(int startStep, string stepsString)
        {
            // TASK-660: a partial remote config_rewarded leaves the delay strings null — no steps, not an NRE.
            if (string.IsNullOrEmpty(stepsString))
            {
                _steps = Array.Empty<SteppedValue>();
                return;
            }

            var splitSteps = stepsString.Split(";", StringSplitOptions.RemoveEmptyEntries);

            _steps = new SteppedValue[splitSteps.Length];
            var step = startStep;
            for (int i = 0; i < splitSteps.Length; i++)
            {
                var stepValues = splitSteps[i].Trim().Split(",", 2);
                // TASK-660: an element without a comma ("5") used to throw IndexOutOfRange on stepValues[1].
                if (stepValues.Length == 2 && int.TryParse(stepValues[0], out int value) && int.TryParse(stepValues[1], out int len))
                {
                    _steps[i] = new SteppedValue{ Idx = step, Value = value };
                    step += len;
                }
            }
        }

        public int this[int idx]
        {
            get
            {
                int v = 0; 
                foreach (var step in _steps)
                {
                    if (idx < step.Idx)
                    {
                        break;
                    }
                    v = step.Value;
                }

                return v;
            }
        }
    }
    
    [CreateAssetMenu(fileName = "RewardedConfig", menuName = "Configs/RewardedConfig")]
    public class RewardedConfig : ConfigWithJson<RewardedConfig.Data>
    {
        /// <summary>
        /// Template leftover — NOT read by the game. The between-levels interstitial threshold is
        /// <c>AdsConfig.Interstitial.FromLevel</c> (TASK-658, remote section <c>config_ads</c>); do not add a second source
        /// of truth here. A partial remote <c>config_rewarded</c> nulls the sections it omits (ConfigWithJson replaces);
        /// <see cref="InitWithRemote"/> re-creates them empty (TASK-660), it does not restore the asset's values.
        /// </summary>
        [Serializable]
        [JsonObject]
        public class InterstitialData
        {
            public int StartAfterLevel;
            public int PurchaseDelayBeforeStart;
            public int PurchaseDelayAfterStart;
        }
        
        [Serializable]
        [JsonObject(MemberSerialization.OptIn)]
        public class ReviveForRewardedData
        {
            [JsonProperty]
            public int StartAfterLevel;
            [JsonProperty]
            public string DelaysForPayers;
            [JsonProperty]
            public string DelaysForNonPayers;
            /// <summary>
            /// Maximum number of rewarded revives allowed per level attempt. -1 = unlimited.
            /// </summary>
            [JsonProperty]
            public int MaxRevivesForRewarded = -1;

            [NonSerialized]
            public SteppedValuesList ParsedDelaysForPayers;
            [NonSerialized]
            public SteppedValuesList ParsedDelaysForNonPayers;
        }
        
        [Serializable]
        [JsonObject]
        public class AdBoostersData
        {
            public int StartAfterLevel;

            public int PurchaseDelayBeforeStart;
            public int PurchaseDelayAfterStart;
        }
        
        [Serializable]
        [JsonObject]
        public class RewardedPlacementTimeout
        {
            public string Placement;
            public TimeSpanValue Timeout;
        }
        
        [Serializable]
        [JsonObject]
        public class Data
        {
            public InterstitialData Inters;
            public ReviveForRewardedData Revives;
            public AdBoostersData AdBoosters;
            public AdBoostersData AdHearts;

            public RewardedPlacementTimeout[] RewardTimeouts;
        }
        
        public InterstitialData Inters => Config.Inters;
        public ReviveForRewardedData Revives => Config.Revives;
        public AdBoostersData AdBoosters => Config.AdBoosters;
        public AdBoostersData AdHearts => Config.AdHearts;

        public override void InitWithRemote(string remoteConfig)
        {
            base.InitWithRemote(remoteConfig);
            Config ??= new();

            // TASK-660: a partial remote payload (ConfigWithJson replaces, never merges) nulls the sections it omits.
            Config.Inters ??= new();
            Config.Revives ??= new();
            Config.AdBoosters ??= new();
            Config.AdHearts ??= new();

            Config.Revives.ParsedDelaysForPayers = new(Config.Revives.StartAfterLevel, Config.Revives.DelaysForPayers);
            Config.Revives.ParsedDelaysForNonPayers = new(Config.Revives.StartAfterLevel, Config.Revives.DelaysForNonPayers);
        }

        protected override List<JsonConverter> InitJsonConverters()
        {
            List<JsonConverter> converters = base.InitJsonConverters();
            converters.Add(new TimespanValueConverter());
            return converters;
        }

        public TimeSpan GetRewardedTimeout(string placement)
        {
            if (Config.RewardTimeouts == null)
            {
                return TimeSpan.Zero;
            }
            var data = Config.RewardTimeouts.FirstOrDefault(p =>
                p.Placement.Equals(placement, StringComparison.InvariantCultureIgnoreCase));
            return data == null ? TimeSpan.Zero : data.Timeout;
        }
    }
}
