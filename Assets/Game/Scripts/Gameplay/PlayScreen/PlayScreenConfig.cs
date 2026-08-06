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

        /// <summary>Banner intro (pop-in + fade) duration.</summary>
        public float BannerIntroSeconds = 0.45f;

        public Color WinColor = new(0.22f, 0.83f, 0.33f, 1f);
        public Color LoseColor = new(0.90f, 0.28f, 0.24f, 1f);
    }
}
