using System.Collections.Generic;
using Infrastructure.Loading;
using NUnit.Framework;
using UnityEngine;

namespace Gameplay.Tests
{
    /// <summary>
    /// TASK-408: the boot step that hides the Loading scene's static ScreenSpaceOverlay canvas must not hide the
    /// UiService root canvas. That canvas is ScreenSpaceCamera, but until UiService assigns its camera (after an
    /// awaited instantiation) Canvas.renderMode reads ScreenSpaceOverlay — the race seen in TASK-401. The step decides
    /// by origin: only canvases captured BEFORE the boot instantiated anything are candidates.
    /// </summary>
    [TestFixture]
    public sealed class SceneCanvasSnapshotTests
    {
        private readonly List<GameObject> _created = new();

        [TearDown]
        public void TearDown()
        {
            foreach (var go in _created)
            {
                if (go != null)
                    Object.DestroyImmediate(go);
            }

            _created.Clear();
        }

        [Test]
        public void UiRootCanvas_CreatedAfterCapture_WithoutCamera_SurvivesTheStep_AndKeepsCameraLater()
        {
            var staticOverlay = CreateCanvas("Canvas", RenderMode.ScreenSpaceOverlay);
            var snapshot = SceneCanvasSnapshot.Capture(new[] { staticOverlay.gameObject });

            // UiService: Canvas(Clone) is instantiated after the snapshot, camera not assigned yet.
            var uiRoot = CreateCanvas("Canvas(Clone)", RenderMode.ScreenSpaceCamera);
            Assert.That(uiRoot.renderMode, Is.EqualTo(RenderMode.ScreenSpaceOverlay),
                "precondition of the race: a camera canvas without worldCamera reads as Overlay");

            var hidden = new List<string>();
            snapshot.HideOverlayCanvases(hidden);

            Assert.That(hidden, Is.EqualTo(new[] { "Canvas" }), "only the scene-authored overlay is hidden");
            Assert.That(staticOverlay.gameObject.activeSelf, Is.False);
            Assert.That(uiRoot.gameObject.activeSelf, Is.True, "UiService root canvas must survive the boot step");

            // UiService assigns the camera later — the canvas is live and camera-space.
            var camera = new GameObject("UiCamera").AddComponent<UnityEngine.Camera>();
            _created.Add(camera.gameObject);
            uiRoot.worldCamera = camera;
            Assert.That(uiRoot.renderMode, Is.EqualTo(RenderMode.ScreenSpaceCamera));
            Assert.That(uiRoot.isActiveAndEnabled, Is.True);
        }

        [Test]
        public void CapturedCameraCanvasWithCamera_IsNotHidden()
        {
            var camera = new GameObject("SceneCamera").AddComponent<UnityEngine.Camera>();
            _created.Add(camera.gameObject);
            var sceneCameraCanvas = CreateCanvas("SceneCameraCanvas", RenderMode.ScreenSpaceCamera);
            sceneCameraCanvas.worldCamera = camera;

            var snapshot = SceneCanvasSnapshot.Capture(new[] { sceneCameraCanvas.gameObject });
            var hidden = new List<string>();
            snapshot.HideOverlayCanvases(hidden);

            Assert.That(hidden, Is.Empty);
            Assert.That(sceneCameraCanvas.gameObject.activeSelf, Is.True);
        }

        [Test]
        public void NestedAndInactiveCanvases_AreCaptured_DestroyedOnesAreSkipped()
        {
            var root = CreateCanvas("Root", RenderMode.ScreenSpaceOverlay);
            var inactive = CreateCanvas("Inactive", RenderMode.ScreenSpaceOverlay);
            inactive.transform.SetParent(root.transform);
            inactive.gameObject.SetActive(false);
            var doomed = CreateCanvas("Doomed", RenderMode.ScreenSpaceOverlay);

            var snapshot = SceneCanvasSnapshot.Capture(new[] { root.gameObject, doomed.gameObject, null });
            Assert.That(snapshot.Count, Is.EqualTo(3));

            Object.DestroyImmediate(doomed.gameObject);
            var hidden = new List<string>();
            snapshot.HideOverlayCanvases(hidden);

            Assert.That(hidden, Is.EquivalentTo(new[] { "Root", "Inactive" }));
            Assert.That(root.gameObject.activeSelf, Is.False);
        }

        private Canvas CreateCanvas(string name, RenderMode mode)
        {
            var go = new GameObject(name);
            _created.Add(go);
            var canvas = go.AddComponent<Canvas>();
            canvas.renderMode = mode;
            return canvas;
        }
    }
}
