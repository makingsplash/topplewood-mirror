using DataStorage;
using VContainer.Unity;

namespace Services.Gameplay
{
    /// <summary>
    /// One-time start-gold seed (TASK-240, architect §3). <c>StartConfig.StartGoldAmount</c> shipped as a dead
    /// declaration (zero consumers, architect §0.2) — a fresh install started with 0 gold. This grants the bank
    /// once, guarded by a persisted flag, exactly like <c>HintBoosterService.SeedIfFirstRun</c>. Prod-only
    /// (<c>GameplayLifetimeScope</c>), so the sim never seeds — the economy stays sim-invisible.
    ///
    /// Uses <c>+=</c>, not <c>=</c>: an upgrading install with an already-earned balance is never clobbered; a
    /// clean install is <c>0 += StartGoldAmount</c>. A missing <c>StartGoldSeeded</c> pref defaults to false, so
    /// the seed still fires once on an upgrading install (additive key, no migration — architect §9).
    /// </summary>
    public sealed class EconomySeedService : IStartable
    {
        public void Start() => SeedIfNeeded();

        /// <summary>Idempotent start-gold seed (architect §7). Static so the dev "Reset Progress" can re-run it
        /// after <c>RefreshAll()</c> clears the guard, giving a true fresh-install balance without an app relaunch.
        /// The guard makes a second call a no-op, so boot + reset never double-seed.</summary>
        public static void SeedIfNeeded()
        {
            if (Data.Meta.StartGoldSeeded.Value)
                return;

            var start = Data.Configs.Start;
            var seed = start != null ? start.StartGoldAmount : 0;

            Data.Meta.GoldAmount.Value += seed;
            Data.Meta.GoldAmount.ForceSave();
            Data.Meta.StartGoldSeeded.Value = true;
            Data.Meta.StartGoldSeeded.ForceSave();
        }
    }
}
