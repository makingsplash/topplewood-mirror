using System;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace Game.Editor.McpBridge.Handlers
{
    // Single-frame capture — the "eyes" for roles and the orchestrator (TASK-139). Writes a PNG
    // OUTSIDE the Assets tree (Logs/capture/), so no asset reimport / domain reload is triggered.
    // Never writes an empty PNG: if nothing renderable exists it throws with a named reason (silent
    // degradation — a popup rendering into nowhere — is exactly the defect that created this task,
    // TASK-126).
    //
    // Two render methods (the first cut only had "rt", which lost the UI — owner comment on TASK-139):
    //  - "screen": ScreenCapture.CaptureScreenshotAsTexture — the final COMPOSITED game frame, i.e.
    //    every camera in the stack PLUS ScreenSpaceCamera/ScreenSpaceOverlay canvases. Play-mode
    //    only (it reads the live game backbuffer). This is the method that shows the popup: the UI
    //    canvas is ScreenSpaceCamera rendered by an overlay camera, and an off-screen render of just
    //    the base camera never composites the overlay stack (see below). Resolution follows the Game
    //    View — it is not arbitrary here.
    //  - "rt": renders the target camera into a RenderTexture via camera.targetTexture + Camera.Render,
    //    which makes URP composite the base camera's WHOLE cameraStack (overlay UI cameras included)
    //    into the RT. Honors arbitrary width/height and works in edit mode and without editor focus /
    //    a visible Game View (the established repaint gap from TASK-134). NOTE: this path deliberately
    //    does NOT use RenderPipeline.SubmitRenderRequest with a custom destination — that renders only
    //    the base pass and drops the stack, which is why the first cut captured the scene but not the
    //    popup.
    //
    // Default ("auto"): play-mode -> "screen" (guarantees the composited UI); edit-mode -> "rt".
    [InitializeOnLoad]
    public static class CaptureHandler
    {
        static CaptureHandler()
        {
            McpBridgeServer.RegisterHandler("capture_screenshot", CaptureScreenshot);
        }

        // params: method ("auto"|"screen"|"rt", default auto), camera (name, rt only — default
        // main/base), width, height (px, rt only), superSize (int >=1, screen only, resolution
        // multiplier), id, label (filename segments), frame (index in filename + echoed back).
        private static object CaptureScreenshot(JObject p)
        {
            var id = Sanitize(p["id"]?.ToString(), "capture");
            var label = Sanitize(p["label"]?.ToString(), "frame");
            var frame = p["frame"]?.ToObject<int>() ?? 0;
            var isPlaying = EditorApplication.isPlaying;

            var method = (p["method"]?.ToString() ?? "auto").Trim().ToLowerInvariant();
            if (string.IsNullOrEmpty(method) || method == "auto")
                method = isPlaying ? "screen" : "rt";
            if (method != "screen" && method != "rt")
                throw new ArgumentException($"Unknown method '{method}': expected 'auto', 'screen' or 'rt'.");

            byte[] png;
            int width, height;
            string cameraName;
            string renderType;
            string[] stack;
            string note = null;

            if (method == "screen")
            {
                if (!isPlaying)
                    throw new InvalidOperationException(
                        "method 'screen' captures the live composited game frame and requires play mode. " +
                        "Use method 'rt' (or omit it) in edit mode.");

                var superSize = Mathf.Max(1, p["superSize"]?.ToObject<int>() ?? 1);
                (png, width, height) = CaptureScreen(superSize);
                cameraName = "<screen composite>";
                renderType = "Composite";
                stack = Array.Empty<string>();

                var reqW = p["width"]?.ToObject<int>() ?? 0;
                var reqH = p["height"]?.ToObject<int>() ?? 0;
                if ((reqW > 0 && reqW != width) || (reqH > 0 && reqH != height))
                    note = $"method 'screen' captures the live Game View ({width}x{height}, superSize {superSize}); " +
                           "width/height are ignored here. Set the Game View resolution to the target size, or use " +
                           "method 'rt' with width/height, to capture at an exact resolution.";
            }
            else // rt
            {
                var camera = ResolveCamera(p["camera"]?.ToString(), out stack, out renderType);
                cameraName = camera.name;

                width = p["width"]?.ToObject<int>() ?? 0;
                height = p["height"]?.ToObject<int>() ?? 0;
                if (width <= 0) width = camera.pixelWidth > 0 ? camera.pixelWidth : 1080;
                if (height <= 0) height = camera.pixelHeight > 0 ? camera.pixelHeight : 1920;

                png = RenderToTexture(camera, width, height);

                // The runtime UI canvas is instantiated only in play mode, so an edit-mode rt capture
                // shows the scene without the screen-space UI even when it composites the stack.
                if (!isPlaying)
                    note = "edit-mode 'rt' render shows the scene without the runtime UI overlay — the UI canvas " +
                           "is only instantiated in play mode. For a frame WITH the popup use play mode (method 'screen').";
            }

            var dir = CaptureDir();
            Directory.CreateDirectory(dir);
            var fileName = $"{id}-{label}-{frame:D4}.png";
            var path = Path.Combine(dir, fileName);
            File.WriteAllBytes(path, png);

            return new
            {
                path,
                method,
                width,
                height,
                camera = cameraName,
                renderType,
                stack,
                frame,
                bytes = png.Length,
                isPlaying,
                note,
            };
        }

        // Grabs the final composited game frame (all cameras + all canvas render modes) at the live
        // Game View resolution times superSize. Throws rather than write an empty PNG.
        private static (byte[] png, int width, int height) CaptureScreen(int superSize)
        {
            var tex = ScreenCapture.CaptureScreenshotAsTexture(superSize);
            if (tex == null)
                throw new InvalidOperationException(
                    "ScreenCapture returned no texture: the game view produced no frame to read. Refusing to write an empty PNG.");
            try
            {
                var png = tex.EncodeToPNG();
                return (png, tex.width, tex.height);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(tex);
            }
        }

        // Renders `camera` into an off-screen RenderTexture at width×height and returns the PNG bytes.
        // Routes through camera.targetTexture + Camera.Render so URP composites the whole cameraStack
        // (overlay UI cameras included) into the RT — SubmitRenderRequest with a custom destination
        // would render only the base pass and drop the stack. Renders on demand (independent of Game
        // View repaint); restores camera target + active RT on exit.
        private static byte[] RenderToTexture(Camera camera, int width, int height)
        {
            var rt = RenderTexture.GetTemporary(width, height, 24, RenderTextureFormat.ARGB32);
            var prevActive = RenderTexture.active;
            var prevTarget = camera.targetTexture;
            try
            {
                camera.targetTexture = rt;
                camera.Render();

                RenderTexture.active = rt;
                var tex = new Texture2D(width, height, TextureFormat.RGBA32, false);
                tex.ReadPixels(new Rect(0, 0, width, height), 0, 0);
                tex.Apply();
                var png = tex.EncodeToPNG();
                UnityEngine.Object.DestroyImmediate(tex);
                return png;
            }
            finally
            {
                camera.targetTexture = prevTarget;
                RenderTexture.active = prevActive;
                RenderTexture.ReleaseTemporary(rt);
            }
        }

        // Resolves which camera to render (rt path) and describes it. Throws (never returns null)
        // when nothing renderable exists — the caller must surface an error, not an empty file.
        private static Camera ResolveCamera(string name, out string[] stack, out string renderType)
        {
            Camera camera;
            if (!string.IsNullOrEmpty(name))
            {
                camera = Camera.allCameras.FirstOrDefault(c => c != null && c.name == name);
                if (camera == null)
                {
                    var go = GameObject.Find(name);
                    camera = go != null ? go.GetComponent<Camera>() : null;
                }
                if (camera == null)
                    throw new ArgumentException($"Camera '{name}' not found: no active GameObject with that name carries a Camera.");
            }
            else
            {
                camera = Camera.main;
                if (camera == null)
                    camera = Camera.allCameras.FirstOrDefault(c => c != null && c.cameraType == CameraType.Game && c.isActiveAndEnabled);
                if (camera == null)
                    throw new InvalidOperationException("No renderable camera found: the scene has no active base/Game camera. Refusing to write an empty PNG.");
            }

            // If the target is a URP overlay, escalate to the base camera that stacks it — an
            // overlay rendered alone would drop the composite, i.e. the very thing we capture for.
            var data = camera.GetComponent<UniversalAdditionalCameraData>();
            if (data != null && data.renderType == CameraRenderType.Overlay)
            {
                var baseCam = FindBaseFor(camera);
                if (baseCam != null)
                {
                    camera = baseCam;
                    data = camera.GetComponent<UniversalAdditionalCameraData>();
                }
            }

            if (data != null && data.renderType == CameraRenderType.Base)
            {
                stack = data.cameraStack.Where(c => c != null).Select(c => c.name).ToArray();
                renderType = "Base";
            }
            else
            {
                stack = Array.Empty<string>();
                renderType = data != null ? data.renderType.ToString() : "Standalone";
            }
            return camera;
        }

        // The URP base camera whose stack contains `overlay`, or null if none does.
        private static Camera FindBaseFor(Camera overlay)
        {
            foreach (var c in Camera.allCameras)
            {
                if (c == null) continue;
                var d = c.GetComponent<UniversalAdditionalCameraData>();
                if (d != null && d.renderType == CameraRenderType.Base && d.cameraStack.Contains(overlay))
                    return c;
            }
            return null;
        }

        // Frames go to <projectRoot>/Logs/capture — outside Assets, so no reimport.
        private static string CaptureDir()
        {
            var root = Directory.GetParent(Application.dataPath)?.FullName ?? Application.dataPath;
            return Path.Combine(root, "Logs", "capture");
        }

        // Keeps filenames to [A-Za-z0-9_-]; empty/whitespace input falls back to `fallback`.
        private static string Sanitize(string value, string fallback)
        {
            if (string.IsNullOrWhiteSpace(value)) return fallback;
            var cleaned = new string(value
                .Select(ch => char.IsLetterOrDigit(ch) || ch == '-' || ch == '_' ? ch : '_')
                .ToArray());
            return string.IsNullOrEmpty(cleaned) ? fallback : cleaned;
        }
    }
}
