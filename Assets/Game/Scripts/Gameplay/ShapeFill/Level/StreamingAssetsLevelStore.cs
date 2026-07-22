using System.IO;
using Gameplay.Core;
using UnityEngine;

namespace Gameplay.Adapters
{
    /// <summary>
    /// File-backed <see cref="ILevelStore"/> (TASK-009): reads authored level content from
    /// <c>Application.streamingAssetsPath</c>. Direct <see cref="File"/> IO works in the editor, standalone,
    /// and the isolated bringup scene. On Android StreamingAssets lives inside the APK (needs UnityWebRequest)
    /// — a known follow-up before an Android build (TECH_DEBT), out of scope for the bringup slice.
    /// </summary>
    public sealed class StreamingAssetsLevelStore : ILevelStore
    {
        public string ReadText(string name)
        {
            var path = Path.Combine(Application.streamingAssetsPath, name);
            return File.Exists(path) ? File.ReadAllText(path) : null;
        }
    }
}
