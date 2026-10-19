using Game.Services.LogService;
using UnityEngine;

namespace Gameplay.Tutorial
{
    /// <summary>
    /// The tour's prefab channel (TASK-613 plan 03 §1/§3.10): a serialized reference to the compiled UI component
    /// <c>TourFinger</c> (in <c>Assets/Game/Prefabs/UI/Components</c>), in an asset under <c>Resources</c> so the tour
    /// loads it SYNCHRONOUSLY — it is armed inside the <c>LevelLoaded</c> delivery, where an async load would create a
    /// "step armed, view not there yet" state. A reference (not a path string) also pulls the prefab and its sprites into
    /// the player build by construction. Missing asset or reference → one warning and the code-built fallback
    /// (<see cref="TourFingerRig.BuildFallback"/>): the tour never breaks silently. (The second component, the ✓
    /// <c>TourCheck</c>, left with the "Совпало" beat in TASK-634.)
    /// </summary>
    public sealed class TourAssets : ScriptableObject
    {
        /// <summary><c>Resources.Load</c> path: <c>Assets/Resources/Tutorial/TourAssets.asset</c>.</summary>
        public const string ResourcePath = "Tutorial/TourAssets";

        [SerializeField] private GameObject finger;

        public GameObject Finger => finger;

        private static TourAssets _instance;
        private static bool _loaded;

        /// <summary>The asset, or null (logged once).</summary>
        public static TourAssets Load()
        {
            if (_loaded)
                return _instance;
            _loaded = true;
            _instance = Resources.Load<TourAssets>(ResourcePath);
            if (_instance == null)
                Debug.unityLogger.Log(LogType.Warning,
                    $"[{LogCategory.Tutorial}] no Resources/{ResourcePath} asset — the tour finger falls back to a code-built one");
            return _instance;
        }
    }
}
