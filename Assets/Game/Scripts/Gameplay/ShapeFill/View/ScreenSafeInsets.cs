using Gameplay.Core;
using UnityEngine;

namespace Gameplay.View
{
    /// <summary>
    /// This device's <c>Screen.safeArea</c> as viewport fractions of each screen edge (TASK-576). ONE reader for the
    /// camera framing and the target card: the band the frame reserves and the band the card is anchored in both hang
    /// under the same safe top, so the two consumers cannot drift apart — the defect of TASK-576 was exactly that the
    /// buttons and the frame read the safe area while the card did not. <c>safeArea</c>'s origin is bottom-left.
    /// </summary>
    internal static class ScreenSafeInsets
    {
        public static CameraFramingMath.SafeInsets Current()
        {
            var safe = Screen.safeArea;
            var safeLeft = Screen.width > 0 ? safe.xMin / Screen.width : 0f;
            var safeRight = Screen.width > 0 ? (Screen.width - safe.xMax) / Screen.width : 0f;
            var safeBottom = Screen.height > 0 ? safe.yMin / Screen.height : 0f;
            var safeTop = Screen.height > 0 ? (Screen.height - safe.yMax) / Screen.height : 0f;
            return new CameraFramingMath.SafeInsets(safeLeft, safeRight, safeBottom, safeTop);
        }
    }
}
