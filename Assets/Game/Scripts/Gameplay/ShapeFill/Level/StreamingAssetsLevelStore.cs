using System.IO;
using Gameplay.Core;
using UnityEngine;
#if UNITY_ANDROID && !UNITY_EDITOR
using UnityEngine.Networking;
#endif

namespace Gameplay.Adapters
{
    /// <summary>
    /// File-backed <see cref="ILevelStore"/> (TASK-009): reads authored level content from
    /// <c>Application.streamingAssetsPath</c>. On the editor, standalone, and the isolated bringup scene
    /// StreamingAssets is a real directory on disk, so direct <see cref="File"/> IO is used. On Android the
    /// StreamingAssets folder is packed inside the APK (<c>jar:file://…</c>) where <see cref="File"/> cannot
    /// reach it (<see cref="File.Exists"/> is always false) — there the same path is read through a
    /// <see cref="UnityWebRequest"/> (TASK-063). The <see cref="ReadText"/> contract stays synchronous on every
    /// platform: the manifest is a small, one-shot read pulled eagerly during
    /// <see cref="Gameplay.Core.JsonLevelSource"/> construction, so the Android branch blocks on the local
    /// (non-network) request exactly as <see cref="File.ReadAllText"/> blocks elsewhere — no caller changes.
    /// </summary>
    public sealed class StreamingAssetsLevelStore : ILevelStore
    {
        public string ReadText(string name)
        {
            var path = Path.Combine(Application.streamingAssetsPath, name);
#if UNITY_ANDROID && !UNITY_EDITOR
            using var request = UnityWebRequest.Get(path);
            request.SendWebRequest();
            // StreamingAssets on Android is a local (jar:file://) read, not a network fetch, so the download
            // handler completes on a worker thread — this busy-wait resolves without pumping the player loop,
            // keeping the synchronous ReadText contract the JsonLevelSource constructor relies on.
            while (!request.isDone)
            {
            }

            if (request.result != UnityWebRequest.Result.Success)
                return null;

            var text = request.downloadHandler.text;
            return string.IsNullOrEmpty(text) ? null : text;
#else
            return File.Exists(path) ? File.ReadAllText(path) : null;
#endif
        }
    }
}
