namespace Gameplay.Core
{
    /// <summary>
    /// Domain input intent (overview §Адаптерная граница): a plain float pair, no <c>UnityEngine.Vector2</c>.
    /// One gesture = one intent (one-tap, GAME_BRIEF). In v0 shape_fill <see cref="X"/> carries the tapped
    /// drop column in world/level space (the adapter projects the screen tap through the camera); the block
    /// is dropped there, clamped to the silhouette span. <see cref="Y"/> is unused in v0.
    /// </summary>
    public readonly struct InputIntent
    {
        public readonly float X;
        public readonly float Y;

        public InputIntent(float x, float y)
        {
            X = x;
            Y = y;
        }
    }
}
