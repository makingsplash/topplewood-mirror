using Gameplay.Core;
using UnityEngine;
using Game.Services.LogService;

namespace Gameplay.Tutorial
{
    /// <summary>
    /// Logging <see cref="ITutorialView"/> (TASK-049 RFC §Q3/§6): logs highlight/clear under the existing
    /// <c>LogCategory.Tutorial</c> so the view seam can be exercised without any art. NO LONGER the bound view —
    /// TASK-078 swapped the installer to the real <c>TutorialView</c> overlay; this is retained as a headless/
    /// fallback stub (e.g. for a scope with no camera), a one-line scope change away.
    /// </summary>
    public sealed class LoggingTutorialView : ITutorialView
    {
        public void Highlight(Vec2 origin, float rotation, Shape shape, HighlightStyle style)
        {
            Debug.unityLogger.Log(LogType.Log,
                $"[{LogCategory.Tutorial}] highlight ({style}) {shape.Primitive} at ({origin.X:0.##}, {origin.Y:0.##}) rot {rotation:0.##}");
        }

        public void Clear()
        {
            Debug.unityLogger.Log(LogType.Log, $"[{LogCategory.Tutorial}] highlight cleared");
        }
    }
}
