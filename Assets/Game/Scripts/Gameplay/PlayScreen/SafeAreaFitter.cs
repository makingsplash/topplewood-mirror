using UnityEngine;

namespace Gameplay.PlayScreen
{
    /// <summary>
    /// Stretches its RectTransform over <see cref="Screen.safeArea"/> (TASK-539, closes debt TASK-246): code-built HUD
    /// buttons are parented under it, so their corner offsets count from the safe edges — on a 20:9 gesture phone the
    /// bottom-right hint button no longer sits on the home indicator. The standard anchor-from-safe-area pattern
    /// (uGUI 2.6 ships it as a <c>SafeArea</c> component; this project's uGUI 2.0 does not have it). The safe area can
    /// change after start (cutout settles, multi-window), so it is re-read every frame — a Rect compare, no allocation.
    /// Camera framing reads the same <see cref="Screen.safeArea"/> to reserve the button's corner.
    /// </summary>
    public sealed class SafeAreaFitter : MonoBehaviour
    {
        private Rect _applied;
        private Vector2Int _appliedScreen;

        /// <summary>Creates a full-safe-area container under <paramref name="canvas"/>.</summary>
        public static RectTransform Create(Transform canvas)
        {
            var go = new GameObject("SafeArea", typeof(RectTransform), typeof(SafeAreaFitter));
            go.transform.SetParent(canvas, false);
            var rect = (RectTransform)go.transform;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            go.GetComponent<SafeAreaFitter>().Apply();
            return rect;
        }

        private void LateUpdate() => Apply();

        private void Apply()
        {
            var safe = Screen.safeArea;
            var screen = new Vector2Int(Screen.width, Screen.height);
            if (safe == _applied && screen == _appliedScreen)
                return;
            if (screen.x <= 0 || screen.y <= 0)
                return;

            var rect = (RectTransform)transform;
            rect.anchorMin = new Vector2(safe.xMin / screen.x, safe.yMin / screen.y);
            rect.anchorMax = new Vector2(safe.xMax / screen.x, safe.yMax / screen.y);
            _applied = safe;
            _appliedScreen = screen;
        }
    }
}
