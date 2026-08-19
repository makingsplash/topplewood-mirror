using UnityEngine;

namespace Gameplay.PlayScreen
{
    /// <summary>
    /// Plain-C# config for the play screen (TASK-116, RFC §6). Registered as an instance by the hosting scope so
    /// tuning lives in one place (no serialized prefab in bringup — RFC §4). <see cref="OutcomeHoldSeconds"/> is
    /// the owner-mandated "+ 2 seconds of static hold" after the banner animation, before any transition.
    /// </summary>
    public sealed class PlayScreenConfig
    {
        /// <summary>Static hold after the banner intro, before the level advances/reloads (owner requirement §3).</summary>
        public float OutcomeHoldSeconds = 2f;

        /// <summary>
        /// Settle-beat before the prod lose panel appears (TASK-154): a pause so the player sees WHAT they missed
        /// before <see cref="GatedResolutionFlow"/> shows <c>LoseScreen</c>. v1 = 1.2s, the midpoint of the
        /// designer's 1.0–1.5s range (01-game-designer.md §96); config, not a constant, so game-balancer can tune
        /// it against the working screen without a code change.
        /// </summary>
        public float LoseSettleSeconds = 1.2f;

        /// <summary>
        /// Settle-beat on the ASSEMBLED figure before the prod win panel appears (TASK-157): a pause to savour the
        /// win (the figure reveal is the reward, design §2) before <see cref="GatedResolutionFlow"/> shows
        /// <c>WinScreen</c>. v1 = 1.4s (owner decision: midpoint of the designer's 1.2–1.6s range, a touch longer
        /// than <see cref="LoseSettleSeconds"/>); config, not a constant, so game-balancer can tune it against the
        /// working screen without a code change.
        /// </summary>
        public float WinSettleSeconds = 1.4f;

        /// <summary>Banner intro (pop-in + fade) duration.</summary>
        public float BannerIntroSeconds = 0.45f;

        public Color WinColor = new(0.22f, 0.83f, 0.33f, 1f);
        public Color LoseColor = new(0.90f, 0.28f, 0.24f, 1f);
    }
}
