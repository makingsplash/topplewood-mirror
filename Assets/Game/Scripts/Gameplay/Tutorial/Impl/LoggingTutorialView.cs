using Gameplay.Core;
using UnityEngine;
using Game.Services.LogService;

namespace Gameplay.Tutorial
{
    /// <summary>
    /// v1 stub <see cref="ITutorialView"/> (TASK-049 RFC §Q3/§6): logs highlight/clear under the existing
    /// <c>LogCategory.Tutorial</c> so the view seam is exercised end-to-end without any art. Shared by both
    /// scopes (context-independent). The real ring/finger presenter reading <c>GameModel.Pieces</c> is a content
    /// follow-up — swapping this for it is a one-line scope change, no seam change.
    /// </summary>
    public sealed class LoggingTutorialView : ITutorialView
    {
        public void Highlight(Vec2 worldTarget)
        {
            Debug.unityLogger.Log(LogType.Log,
                $"[{LogCategory.Tutorial}] highlight ({worldTarget.X:0.##}, {worldTarget.Y:0.##})");
        }

        public void Clear()
        {
            Debug.unityLogger.Log(LogType.Log, $"[{LogCategory.Tutorial}] highlight cleared");
        }
    }
}
