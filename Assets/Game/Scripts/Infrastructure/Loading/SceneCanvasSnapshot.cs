using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Infrastructure.Loading
{
    // TASK-408: the canvases a scene was AUTHORED with, captured before the boot instantiates anything into it.
    //
    // Why a snapshot and not a renderMode scan at hide time: Canvas.renderMode reports the EFFECTIVE mode, not the
    // serialized one — a Screen Space Camera canvas whose worldCamera is still null reads as ScreenSpaceOverlay
    // (docs/platform/unity-gotchas.md). UiService instantiates its root Canvas(Clone) (ScreenSpaceCamera) and only
    // then awaits the camera instantiation, so a late scan could catch it in that window and hide it as an "Overlay
    // remnant" — the win/lose screen then opened inside a disabled canvas (TASK-401, 1 of 4 runs). Deciding by origin
    // closes the race by construction: an instance created after Capture is simply not in the list. The serialized
    // m_RenderMode alternative needs SerializedObject — editor-only, so it can't guard a player build.
    public sealed class SceneCanvasSnapshot
    {
        private readonly List<Canvas> _canvases = new();

        private SceneCanvasSnapshot()
        {
        }

        public int Count => _canvases.Count;

        public static SceneCanvasSnapshot Capture(Scene scene)
        {
            return Capture(scene.GetRootGameObjects());
        }

        // Roots overload: lets EditMode tests drive the snapshot without creating an additive scene.
        public static SceneCanvasSnapshot Capture(IEnumerable<GameObject> roots)
        {
            var snapshot = new SceneCanvasSnapshot();
            foreach (var root in roots)
            {
                if (root == null)
                    continue;

                snapshot._canvases.AddRange(root.GetComponentsInChildren<Canvas>(true));
            }

            return snapshot;
        }

        // Hides the captured canvases that render as ScreenSpaceOverlay NOW. The renderMode filter stays, but only
        // over scene-authored objects: for them the effective mode is exactly what draws on top of the camera-space
        // UI. Destroyed entries are skipped. Names of the hidden ones are appended to `hidden`.
        public void HideOverlayCanvases(List<string> hidden)
        {
            foreach (var canvas in _canvases)
            {
                if (canvas == null)
                    continue;

                if (canvas.renderMode != RenderMode.ScreenSpaceOverlay)
                    continue;

                hidden.Add(canvas.name);
                canvas.gameObject.SetActive(false);
            }
        }
    }
}
