using Gameplay.Core;

namespace Gameplay.Tutorial
{
    /// <summary>
    /// Highlight seam for the tutorial system (TASK-049 RFC §Q3). The tutorial reads piece poses from
    /// <see cref="GameModel"/> (read-only) and asks the view to draw attention to a world-space point; the
    /// domain never imports the view. Bound to <c>TutorialView</c> (TASK-078) — a screen-space ring + finger
    /// overlay that re-projects the target each frame; <see cref="LoggingTutorialView"/> is kept as a headless/
    /// fallback stub. Art is placeholder (fixed-radius ring + white-square finger) pending a content pass.
    /// <see cref="Vec2"/> is the Unity-free domain vector; the view adapter converts to <c>Vector3</c> on its side.
    /// </summary>
    public interface ITutorialView
    {
        void Highlight(Vec2 worldTarget);

        void Clear();
    }
}
