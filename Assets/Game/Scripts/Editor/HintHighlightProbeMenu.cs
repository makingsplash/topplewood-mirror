using Gameplay.PlayScreen;
using UnityEditor;
using UnityEngine;

namespace Game.Editor
{
    /// <summary>
    /// TASK-309 render-isolation menu. Fires the Hint booster's PulseOutline highlight WITHOUT a tap so the contour
    /// render can be verified/screenshotted in play mode — the orchestrator's bridge can capture a frame but cannot
    /// inject the tap that would normally trigger the hint. Delegates to the presenter-registered probe
    /// (<see cref="HintHighlightProbe.Force"/>, set only while a gameplay level is live); logs a no-op otherwise.
    /// If this shows a blinking green contour but a real tap shows nothing, the defect is in a tap-path gate; if this
    /// shows nothing either, the defect is in the contour render itself (pair with the [Tutorial] Highlight logs).
    /// </summary>
    public static class HintHighlightProbeMenu
    {
        [MenuItem("Tools/Hint Booster/Force Highlight (Play Mode)")]
        private static void ForceHighlight()
        {
            if (!Application.isPlaying)
            {
                Debug.Log("[booster] probe: enter Play Mode and load a gameplay level first, then run this menu.");
                return;
            }
            if (HintHighlightProbe.Force == null)
            {
                Debug.Log("[booster] probe: HintBoosterPresenter is not alive (no gameplay scope). Load a level first.");
                return;
            }
            HintHighlightProbe.Force.Invoke();
        }
    }
}
