namespace Gameplay.Core
{
    /// <summary>
    /// Domain input intent (overview §Адаптерная граница): normalised screen coordinates in 0..1,
    /// no <c>UnityEngine.Vector2</c>. One gesture = one intent (one-tap, GAME_BRIEF). In v0 shape_fill
    /// the coordinates are unused (fixed drop spawn) — the intent simply means "release the block".
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
