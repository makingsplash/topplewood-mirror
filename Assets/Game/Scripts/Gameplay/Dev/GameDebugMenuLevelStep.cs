#if GPIE_DEVELOPMENT
using System;

namespace Gameplay.Dev
{
    /// <summary>
    /// Project-owned dev signal (TASK-174): the prod static <c>GameDebugMenu</c> (app layer) fires a level-step
    /// intent that the in-scope <c>GameDebugMenuRestartBridge</c> applies. It is deliberately SEPARATE from the
    /// package seam <c>DebugService.OnDebugMenuClose</c> for two reasons: (1) the ◀/▶ arrows must apply the jump
    /// WITHOUT closing the menu (that seam is close-driven), and (2) <c>DebugService</c> lives in
    /// <c>Packages/com.say.services.common</c> — foreign code we do not touch (portability).
    ///
    /// The static menu can only EMIT intent: it is DI-free and does not know N (the level count lives only in the
    /// in-scope <c>ILevelSource.Count</c>). Clamp, persist and reload happen in-scope on the bridge's side.
    /// <paramref name="delta"/> is −1 (prev / ◀) or +1 (next / ▶). If no bridge is subscribed (menu open outside a
    /// gameplay scene), <see cref="Request"/> is a harmless no-op. Editor/dev builds only — the whole file is
    /// guarded and never compiles into player builds.
    /// </summary>
    public static class GameDebugMenuLevelStep
    {
        public static event Action<int> Requested;

        public static void Request(int delta) => Requested?.Invoke(delta);
    }
}
#endif
