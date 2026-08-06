namespace Gameplay.Core
{
    /// <summary>
    /// Domain input intent (overview §Адаптерная граница): a plain float pair, no <c>UnityEngine.Vector2</c>.
    /// One began touch = one intent; a single frame may carry several (two fingers → two intents), which the
    /// balance coalescence group consumes (TASK-108). No touch identifier is needed: <c>OnIntent</c> buckets by
    /// the tapped piece's <c>InstanceId</c> and dedups a re-tap. Since the TASK-007 pivot, <see cref="X"/>/<see
    /// cref="Y"/> carry the tapped point in world/level space (the adapter projects the screen tap through the
    /// camera); the mechanic hit-tests which Idle piece contains that point and releases it (RFC §5).
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
