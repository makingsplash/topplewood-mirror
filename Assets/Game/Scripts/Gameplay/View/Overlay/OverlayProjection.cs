using UnityEngine;

namespace Gameplay.View
{
    /// <summary>
    /// World → canvas-local projection for code-built Screen Space Overlay canvases (TASK-613 plan §3.9). Every point
    /// goes through <c>Camera.WorldToScreenPoint</c> and then <see cref="RectTransformUtility"/> into the canvas rect,
    /// so the result is right at ANY canvas scale factor. The former shortcut "raw screen delta = local delta" held
    /// only at scaleFactor 1 (ConstantPixelSize) and breaks on a ScaleWithScreenSize canvas.
    /// </summary>
    public static class OverlayProjection
    {
        /// <summary>Project <paramref name="world"/> into <paramref name="canvasRect"/>'s local space (origin = rect
        /// pivot). False when the point is behind the camera or the conversion fails — leave the overlay where it was.</summary>
        public static bool TryToCanvas(UnityEngine.Camera camera, RectTransform canvasRect, Vector3 world, out Vector2 local)
        {
            local = default;
            if (camera == null || canvasRect == null)
                return false;
            var screen = camera.WorldToScreenPoint(world);
            if (screen.z < 0f)
                return false;
            return RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRect, screen, null, out local);
        }
    }
}
