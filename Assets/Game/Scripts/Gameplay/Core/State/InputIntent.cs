namespace Gameplay.Core
{
    /// <summary>
    /// Domain input intent (overview §Адаптерная граница): a plain float pair, no <c>UnityEngine.Vector2</c>.
    /// One gesture = one intent (one-tap, GAME_BRIEF). Since the TASK-007 pivot, <see cref="X"/>/<see cref="Y"/>
    /// carry the tapped point in world/level space (the adapter projects the screen tap through the camera);
    /// the mechanic hit-tests which Idle piece contains that point and releases it (RFC §5).
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
