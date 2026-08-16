using System;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Game.Editor.McpBridge.Handlers
{
    // Off-screen single-frame capture — the "eyes" for roles and the orchestrator (TASK-139).
    // Renders a camera (a URP base camera pulls in its whole cameraStack) into a RenderTexture
    // via SubmitRenderRequest and writes a PNG OUTSIDE the Assets tree (Logs/capture/), so no
    // asset reimport / domain reload is triggered. On-demand rendering means the frame is
    // produced regardless of editor focus or Game View visibility — the established gap from
    // TASK-134 (the tab may not repaint and the editor may not tick unfocused). Never writes an
    // empty PNG: if no renderable camera exists it throws with a named reason (silent degradation
    // — a popup rendering into nowhere — is exactly the defect that created this task, TASK-126).
    [InitializeOnLoad]
    public static class CaptureHandler
    {
        static CaptureHandler()
        {
            McpBridgeServer.RegisterHandler("capture_screenshot", CaptureScreenshot);
        }

        // params: camera (name, optional — default main/base), width, height (px, optional),
        // id, label (filename segments), frame (index in filename + echoed back).
        private static object CaptureScreenshot(JObject p)
        {
            var id = Sanitize(p["id"]?.ToString(), "capture");
            var label = Sanitize(p["label"]?.ToString(), "frame");
            var frame = p["frame"]?.ToObject<int>() ?? 0;

            var camera = ResolveCamera(p["camera"]?.ToString(), out var stack, out var renderType);

            var width = p["width"]?.ToObject<int>() ?? 0;
            var height = p["height"]?.ToObject<int>() ?? 0;
            if (width <= 0) width = camera.pixelWidth > 0 ? camera.pixelWidth : 1080;
            if (height <= 0) height = camera.pixelHeight > 0 ? camera.pixelHeight : 1920;

            var png = Render(camera, width, height);

            var dir = CaptureDir();
            Directory.CreateDirectory(dir);
            var fileName = $"{id}-{label}-{frame:D4}.png";
            var path = Path.Combine(dir, fileName);
            File.WriteAllBytes(path, png);

            return new
            {
                path,
                width,
                height,
                camera = camera.name,
                renderType,
                stack,
                frame,
                bytes = png.Length,
                isPlaying = EditorApplication.isPlaying,
            };
        }

        // Resolves which camera to render and describes it. Throws (never returns null) when
        // nothing renderable exists — the caller must surface an error, not an empty file.
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

        // Renders `camera` at width×height and returns the encoded PNG bytes. Uses the Unity 6
        // render-request API (renders on demand, independent of Game View repaint); falls back to
        // Camera.Render() for the built-in pipeline. Restores camera target + active RT on exit.
        private static byte[] Render(Camera camera, int width, int height)
        {
            var rt = RenderTexture.GetTemporary(width, height, 24, RenderTextureFormat.ARGB32);
            var prevActive = RenderTexture.active;
            var prevTarget = camera.targetTexture;
            try
            {
                var request = new RenderPipeline.StandardRequest { destination = rt };
                if (RenderPipeline.SupportsRenderRequest(camera, request))
                {
                    RenderPipeline.SubmitRenderRequest(camera, request);
                }
                else
                {
                    camera.targetTexture = rt;
                    camera.Render();
                }

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
