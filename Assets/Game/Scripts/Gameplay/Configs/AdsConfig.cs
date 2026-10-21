using System;
using Newtonsoft.Json;
using SGS.Infrastructure.Tools.Configs;
using UnityEngine;

namespace Gameplay.Configs
{
    /// <summary>
    /// The game's remote-drivable ad knobs (TASK-658), section <c>SayKitGameConfig.config_ads</c>. Today only the
    /// between-levels interstitial lives here; future rewarded knobs go next to it. All defaults are C# field
    /// initialisers (the <see cref="BoostersConfig"/> pattern): Newtonsoft builds the object through its constructor and
    /// overwrites only the fields a payload carries, so a partial remote JSON keeps the rest at the defaults. This is why
    /// the threshold is NOT <c>RewardedConfig.Inters</c>: <c>ConfigWithJson</c> REPLACES the asset data with the remote
    /// object, and a partial <c>config_rewarded</c> payload nulls <c>Revives</c> → NRE at boot (plan §2).
    /// Frequency and pauses between shows are NOT here — the native SDK owns them per ad group (publisher side).
    /// </summary>
    [CreateAssetMenu(fileName = "AdsConfig", menuName = "Configs/AdsConfig")]
    public sealed class AdsConfig : ConfigWithJson<AdsConfig.Data>
    {
        [Serializable]
        [JsonObject]
        public class InterstitialData
        {
            public bool Enabled = true;     // kill-switch for the whole between-levels interstitial
            public int FromLevel = 10;      // first interstitial AFTER finishing this level (1-based, the shown number)
            public bool OnLose = false;     // also on "Try again" after a loss on a level ≥ FromLevel
        }

        [Serializable]
        [JsonObject]
        public class Data
        {
            public InterstitialData Interstitial;
        }

        public InterstitialData Interstitial => Config.Interstitial;

        public override void InitWithRemote(string remoteConfig)
        {
            base.InitWithRemote(remoteConfig);
            Config ??= new();
            Config.Interstitial ??= new();

            // One receiver line per boot (canon saykit.md §Remote config: the proof is the receiver's own log line).
            // remoteLen is the incoming string (0 = remote sent nothing); the values are what is applied. In the
            // editor's local mode the base serves the asset whatever the remote says; an empty or unreadable remote
            // section also serves the asset (TASK-064), so remoteLen=0 means "asset values".
            var i = Config.Interstitial;
            Debug.Log($"[ads] config_ads applied: remoteLen={remoteConfig?.Length ?? 0} " +
                      $"Enabled={i.Enabled} FromLevel={i.FromLevel} OnLose={i.OnLose}");
        }
    }
}
