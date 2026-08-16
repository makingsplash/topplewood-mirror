using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Game.Editor.McpBridge.Handlers
{
    // Single-frame capture — the "eyes" for roles and the orchestrator (TASK-139). Writes a PNG
    // OUTSIDE the Assets tree (Logs/capture/), so no asset reimport / domain reload is triggered.
    // Never writes an empty PNG: if nothing renderable exists it throws with a named reason (silent
    // degradation — a popup rendering into nowhere — is exactly the defect that created this task,
    // TASK-126).
    //
    // Registered ASYNC (RegisterAsyncHandler): both real capture paths only produce a frame AFTER a
    // render, which cannot happen inside the same main-thread tick that started them. The handler
    // kicks off the work and resolves once the frame exists; the bridge keeps the main thread ticking
    // meanwhile (see McpBridgeServer).
    //
    // Two methods — chosen after two failed cuts (owner comments on TASK-139), each of which is now a
    // KNOWN dead end, not a guess:
    //  - "gameview": ScreenCapture.CaptureScreenshot(path) — the documented Editor capture of the
    //    GAME VIEW, i.e. the exact composited frame the human sees, including every camera in the
    //    stack and all canvas render modes (ScreenSpaceCamera/ScreenSpaceOverlay/WorldSpace). It is
    //    asynchronous (the shot is taken at end of frame and written on a worker), so we poll for the
    //    file. Play-mode only. Resolution follows the Game View (× superSize) — not arbitrary.
    //    NB: this replaces the first cut's ScreenCapture.CaptureScreenshotAsTexture, which grabbed the
    //    FOCUSED EditorWindow (the Scene View) rather than the Game View — owner confirmed a Scene-View
    //    frame at panel size.
    //    An exact width/height IS honored here: we temporarily force the Game View to that fixed
    //    resolution (editor-internal GameViewSizes, via reflection), capture, and restore the original
    //    size in every case (success, error, timeout). If the resize can't be applied we capture at the
    //    current size and say so in `note` — never silently return a different resolution.
    //  - "rt": renders the base (or named) camera into an off-screen RenderTexture at an exact
    //    width/height. In PLAY mode it sets camera.targetTexture and reads the texture back after the
    //    NORMAL URP render loop has run (endContextRendering) — the normal loop is the ONLY path that
    //    composites a base camera's cameraStack (overlay UI cameras included) into its target. The
    //    first cut used camera.targetTexture + Camera.Render(): that immediate call renders a single
    //    camera and URP ignores the stack, so the ScreenSpaceCamera UI was dropped — owner confirmed a
    //    byte-identical scene-only frame. In EDIT mode there is no runtime UI to composite, so we keep
    //    the cheap synchronous Camera.Render() and say so in `note`. Honors arbitrary width/height
    //    (e.g. 2340×1080 for safe-area) and works without editor focus / a visible Game View.
    //
    // Default ("auto"): play-mode -> "gameview" (the frame the human sees, guaranteed UI); edit-mode
    // -> "rt".
    [InitializeOnLoad]
    public static class CaptureHandler
    {
        // How long to wait for a real rendered frame before giving up (< the bridge's 25s listener
        // wait, so we reject with a clear reason rather than the generic timeout).
        private const double CaptureTimeoutSeconds = 10.0;

        static CaptureHandler()
        {
            McpBridgeServer.RegisterAsyncHandler("capture_screenshot", CaptureScreenshot);
        }

        // params: method ("auto"|"gameview"|"rt", default auto), camera (name, rt only — default
        // main/base), width, height (px — rt renders at exactly this; gameview temporarily forces the
        // Game View to this fixed size), superSize (int >=1, gameview only, resolution multiplier),
        // id, label (filename segments), frame (index in filename + echoed back).
        private static void CaptureScreenshot(JObject p, Action<object> resolve, Action<string> reject)
        {
            try
            {
                var id = Sanitize(p["id"]?.ToString(), "capture");
                var label = Sanitize(p["label"]?.ToString(), "frame");
                var frame = p["frame"]?.ToObject<int>() ?? 0;
                var isPlaying = EditorApplication.isPlaying;

                var method = (p["method"]?.ToString() ?? "auto").Trim().ToLowerInvariant();
                if (string.IsNullOrEmpty(method) || method == "auto")
                    method = isPlaying ? "gameview" : "rt";
                if (method != "gameview" && method != "rt")
                    throw new ArgumentException($"Unknown method '{method}': expected 'auto', 'gameview' or 'rt'.");

                var dir = CaptureDir();
                Directory.CreateDirectory(dir);
                var path = Path.Combine(dir, $"{id}-{label}-{frame:D4}.png");

                if (method == "gameview")
                {
                    if (!isPlaying)
                        throw new InvalidOperationException(
                            "method 'gameview' captures the live Game View and requires play mode. " +
                            "Use method 'rt' (or omit it) in edit mode.");

                    var superSize = Mathf.Max(1, p["superSize"]?.ToObject<int>() ?? 1);
                    var reqW = p["width"]?.ToObject<int>() ?? 0;
                    var reqH = p["height"]?.ToObject<int>() ?? 0;
                    string note = null;
                    Action restore = () => { };
                    var warmupTicks = 0;
                    if (reqW > 0 && reqH > 0)
                    {
                        // Temporarily force the Game View to the requested fixed resolution so the
                        // capture honors it (safe-area 2340x1080 / 2400x1080). Restore in every case.
                        if (TrySetGameViewSize(reqW, reqH, out restore, out var failReason))
                        {
                            // The resize applies on the Game View's next repaint; let a couple of
                            // frames settle before issuing the capture so we grab the NEW size.
                            warmupTicks = 3;
                            note = $"gameview captured at a temporary Game View size {reqW}x{reqH} (superSize x{superSize}); " +
                                   "original size restored afterwards. Actual pixels are in width/height.";
                        }
                        else
                        {
                            note = $"requested {reqW}x{reqH} but the Game View size could not be switched ({failReason}); " +
                                   "captured at the CURRENT Game View size instead. width/height report the actual pixels.";
                        }
                    }
                    else if (reqW > 0 || reqH > 0)
                    {
                        note = "gameview needs BOTH width and height to switch resolution; captured at the current " +
                               "Game View size. width/height report the actual pixels.";
                    }
                    CaptureGameView(path, superSize, frame, note, warmupTicks, restore, resolve, reject);
                    return;
                }

                // rt
                var camera = ResolveCamera(p["camera"]?.ToString(), out var stack, out var renderType);
                var width = p["width"]?.ToObject<int>() ?? 0;
                var height = p["height"]?.ToObject<int>() ?? 0;
                if (width <= 0) width = camera.pixelWidth > 0 ? camera.pixelWidth : 1080;
                if (height <= 0) height = camera.pixelHeight > 0 ? camera.pixelHeight : 1920;

                if (isPlaying)
                {
                    CaptureRtViaLoop(camera, width, height, path, frame, stack, renderType, resolve, reject);
                    return;
                }

                // Edit mode: no runtime UI to composite, so the cheap synchronous render is enough.
                var png = RenderImmediate(camera, width, height);
                File.WriteAllBytes(path, png);
                resolve(Result(path, "rt", width, height, camera.name, renderType, stack, frame, png.Length, false,
                    "edit-mode 'rt' render shows the scene without the runtime UI overlay — the UI canvas is only " +
                    "instantiated in play mode. For a frame WITH the popup use play mode (method 'gameview')."));
            }
            catch (Exception ex)
            {
                reject(ex.Message);
            }
        }

        // "gameview": (optionally after `warmupTicks` frames so a just-applied resize settles) schedule
        // ScreenCapture.CaptureScreenshot and poll for the written file. Forces a repaint every tick so
        // the end-of-frame capture completes even when the Game View tab is not focused / visible (the
        // established repaint gap, TASK-134). Reads PNG dimensions from the file header so the answer
        // reports the ACTUAL captured size (not the requested one). `restore` is called EXACTLY ONCE
        // when the poll ends — resolve, reject or timeout — so a temporary Game View size is always put
        // back, even on error.
        private static void CaptureGameView(string path, int superSize, int frame, string note,
            int warmupTicks, Action restore, Action<object> resolve, Action<string> reject)
        {
            var restored = 0;
            Action restoreOnce = () =>
            {
                if (Interlocked.CompareExchange(ref restored, 1, 0) == 0)
                {
                    try { restore(); } catch { /* best-effort restore; never mask the real outcome */ }
                }
            };

            try { if (File.Exists(path)) File.Delete(path); } catch { /* stale file; overwrite below */ }

            var start = EditorApplication.timeSinceStartup;
            var ticks = 0;
            var captureIssued = false;
            EditorApplication.CallbackFunction poll = null;
            poll = () =>
            {
                // Drive frames so both the resize and the queued end-of-frame capture happen without focus.
                InternalEditorUtility.RepaintAllViews();

                // Let the forced resolution take effect before issuing the shot.
                if (!captureIssued)
                {
                    if (ticks++ < warmupTicks) return;
                    ScreenCapture.CaptureScreenshot(path, superSize);
                    captureIssued = true;
                    return;
                }

                if (File.Exists(path))
                {
                    byte[] bytes;
                    try { bytes = File.ReadAllBytes(path); }
                    catch { return; } // still being written; try next tick
                    if (TryReadPngSize(bytes, out var w, out var h))
                    {
                        EditorApplication.update -= poll;
                        restoreOnce();
                        resolve(Result(path, "gameview", w, h, "<game view>", "Composite",
                            Array.Empty<string>(), frame, bytes.Length, true, note));
                        return;
                    }
                }

                if (EditorApplication.timeSinceStartup - start > CaptureTimeoutSeconds)
                {
                    EditorApplication.update -= poll;
                    restoreOnce();
                    reject($"gameview capture timed out after {CaptureTimeoutSeconds:0}s: " +
                           $"ScreenCapture.CaptureScreenshot wrote no readable PNG at {path}. " +
                           "The Game View may have produced no frame.");
                }
            };
            EditorApplication.update += poll;
        }

        // Temporarily forces the Game View to a fixed width x height so a "gameview" capture can honor an
        // exact resolution (safe-area checks need 2340x1080 / 2400x1080). Game View sizes live in the
        // editor-internal GameViewSizes / GameViewSizeGroup — reflected here (the documented community
        // technique; there is no public API). On ANY reflection failure this returns false and leaves
        // the environment untouched, so the caller degrades to the current size with a note rather than
        // silently returning a wrong resolution. On success `restore` (out) MUST be invoked exactly once
        // by the caller to reselect the original size and drop our temporary entry.
        private static bool TrySetGameViewSize(int width, int height, out Action restore, out string failReason)
        {
            restore = () => { };
            failReason = null;
            try
            {
                var editorAsm = typeof(UnityEditor.Editor).Assembly;
                var gameViewType = editorAsm.GetType("UnityEditor.GameView");
                var sizesType = editorAsm.GetType("UnityEditor.GameViewSizes");
                var sizeType = editorAsm.GetType("UnityEditor.GameViewSize");
                var sizeTypeEnum = editorAsm.GetType("UnityEditor.GameViewSizeType");
                var groupType = editorAsm.GetType("UnityEditor.GameViewSizeGroup");
                if (gameViewType == null || sizesType == null || sizeType == null || sizeTypeEnum == null || groupType == null)
                {
                    failReason = "editor GameView size types not found via reflection";
                    return false;
                }

                // Find an already-open Game View without stealing focus (capture must work unfocused).
                var windows = Resources.FindObjectsOfTypeAll(gameViewType);
                var gameView = windows != null && windows.Length > 0 ? windows[0] as EditorWindow : null;
                if (gameView == null)
                {
                    failReason = "no Game View window is open";
                    return false;
                }

                // `instance` is a static property inherited from ScriptableSingleton<GameViewSizes>;
                // FlattenHierarchy is required to surface an inherited static via the derived type.
                var sizesInstance = sizesType
                    .GetProperty("instance", BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy)
                    ?.GetValue(null);
                if (sizesInstance == null)
                {
                    failReason = "GameViewSizes.instance unavailable";
                    return false;
                }

                var currentGroupType = sizesType.GetProperty("currentGroupType")?.GetValue(sizesInstance);
                var getGroup = sizesType.GetMethod("GetGroup");
                var group = getGroup != null && currentGroupType != null
                    ? getGroup.Invoke(sizesInstance, new[] { currentGroupType })
                    : null;
                if (group == null)
                {
                    failReason = "GameViewSizeGroup unavailable";
                    return false;
                }

                var selectedProp = gameViewType.GetProperty("selectedSizeIndex",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (selectedProp == null)
                {
                    failReason = "GameView.selectedSizeIndex not found";
                    return false;
                }
                var originalIndex = (int)selectedProp.GetValue(gameView);

                var ctor = sizeType.GetConstructor(new[] { sizeTypeEnum, typeof(int), typeof(int), typeof(string) });
                var addCustom = groupType.GetMethod("AddCustomSize");
                var getTotal = groupType.GetMethod("GetTotalCount");
                var removeCustom = groupType.GetMethod("RemoveCustomSize");
                if (ctor == null || addCustom == null || getTotal == null || removeCustom == null)
                {
                    failReason = "GameViewSize ctor or GameViewSizeGroup add/remove/count methods not found";
                    return false;
                }

                var fixedRes = Enum.Parse(sizeTypeEnum, "FixedResolution");
                var newSize = ctor.Invoke(new object[] { fixedRes, width, height, "mcp-capture" });
                addCustom.Invoke(group, new[] { newSize });
                var newIndex = (int)getTotal.Invoke(group, null) - 1;

                selectedProp.SetValue(gameView, newIndex);
                gameView.Repaint();

                restore = () =>
                {
                    // Reselect the original size first (its index is below the appended custom entry,
                    // so removing the custom entry afterwards does not shift it).
                    try { selectedProp.SetValue(gameView, originalIndex); } catch { /* leave as-is */ }
                    try { removeCustom.Invoke(group, new object[] { newIndex }); } catch { /* leave temp entry */ }
                    try { gameView.Repaint(); } catch { /* cosmetic */ }
                };
                return true;
            }
            catch (Exception ex)
            {
                failReason = ex.GetType().Name + ": " + ex.Message;
                return false;
            }
        }

        // "rt" in play mode: point the base camera at an off-screen RenderTexture and read it back once
        // the NORMAL render loop has drawn it — that loop composites the whole cameraStack (overlay UI
        // cameras included) into the target, which immediate Camera.Render() does NOT. We read on the
        // frame whose camera list contains our target camera, then restore + free everything.
        private static void CaptureRtViaLoop(Camera camera, int width, int height, string path, int frame,
            string[] stack, string renderType, Action<object> resolve, Action<string> reject)
        {
            var rt = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32);
            rt.Create();
            var prevTarget = camera.targetTexture;
            camera.targetTexture = rt;

            var start = EditorApplication.timeSinceStartup;
            var fired = 0;
            var seen = 0;
            Action<ScriptableRenderContext, List<Camera>> endCb = null;
            EditorApplication.CallbackFunction timeoutPoll = null;

            Action teardown = () =>
            {
                RenderPipelineManager.endContextRendering -= endCb;
                EditorApplication.update -= timeoutPoll;
                camera.targetTexture = prevTarget;
                if (rt != null) UnityEngine.Object.DestroyImmediate(rt);
            };

            endCb = (ctx, cameras) =>
            {
                // Wait for the frame that actually rendered OUR camera (skip Scene View frames etc.);
                // by then its full stack has composited into rt.
                if (cameras == null || !cameras.Contains(camera)) return;
                // Read only on the SECOND qualifying render: the first may have started before our
                // targetTexture took effect (a stale/blank RT); by the second the loop is provably
                // drawing into rt. The target stays rt until teardown, so this is still a live frame.
                if (Interlocked.Increment(ref seen) < 2) return;
                if (Interlocked.CompareExchange(ref fired, 1, 0) != 0) return;

                try
                {
                    var prevActive = RenderTexture.active;
                    RenderTexture.active = rt;
                    var tex = new Texture2D(width, height, TextureFormat.RGBA32, false);
                    tex.ReadPixels(new Rect(0, 0, width, height), 0, 0);
                    tex.Apply();
                    var png = tex.EncodeToPNG();
                    UnityEngine.Object.DestroyImmediate(tex);
                    RenderTexture.active = prevActive;

                    File.WriteAllBytes(path, png);
                    var len = png.Length;
                    teardown();
                    resolve(Result(path, "rt", width, height, camera.name, renderType, stack, frame, len, true,
                        "rt play-mode capture: rendered the base camera's full URP stack (overlay UI cameras " +
                        "included) into the RenderTexture via the normal render loop."));
                }
                catch (Exception ex)
                {
                    teardown();
                    reject($"rt capture failed during readback: {ex.Message}");
                }
            };
            RenderPipelineManager.endContextRendering += endCb;

            timeoutPoll = () =>
            {
                InternalEditorUtility.RepaintAllViews();
                if (EditorApplication.timeSinceStartup - start <= CaptureTimeoutSeconds) return;
                if (Interlocked.CompareExchange(ref fired, 1, 0) != 0) return;
                teardown();
                reject($"rt capture timed out after {CaptureTimeoutSeconds:0}s: the render pipeline never " +
                       $"rendered camera '{camera.name}' into the target texture.");
            };
            EditorApplication.update += timeoutPoll;
        }

        // Synchronous single-camera render (edit mode only). URP ignores the cameraStack here, so this
        // is base-pass only — fine in edit mode, where the runtime UI overlay does not exist. Restores
        // camera target + active RT on exit.
        private static byte[] RenderImmediate(Camera camera, int width, int height)
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

        // Reads width/height from a PNG's IHDR (bytes 16..23, big-endian) without decoding the image.
        // Doubles as a "fully written" probe: a half-flushed file fails the signature/length check and
        // the caller retries next tick.
        private static bool TryReadPngSize(byte[] b, out int width, out int height)
        {
            width = 0;
            height = 0;
            if (b == null || b.Length < 24) return false;
            if (b[0] != 0x89 || b[1] != 0x50 || b[2] != 0x4E || b[3] != 0x47) return false; // \x89PNG
            width = (b[16] << 24) | (b[17] << 16) | (b[18] << 8) | b[19];
            height = (b[20] << 24) | (b[21] << 16) | (b[22] << 8) | b[23];
            return width > 0 && height > 0;
        }

        private static object Result(string path, string method, int width, int height, string camera,
            string renderType, string[] stack, int frame, int bytes, bool isPlaying, string note)
        {
            return new
            {
                path,
                method,
                width,
                height,
                camera,
                renderType,
                stack,
                frame,
                bytes,
                isPlaying,
                note,
            };
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
