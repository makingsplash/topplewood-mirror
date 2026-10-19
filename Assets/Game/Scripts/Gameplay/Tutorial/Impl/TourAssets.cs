using Game.Services.LogService;
using UnityEngine;

namespace Gameplay.Tutorial
{
    /// <summary>
    /// The tour's prefab channel (TASK-613 plan 03 §1/§3.10): serialized references to the two compiled UI components
    /// (<c>TourFinger</c>, <c>TourCheck</c> in <c>Assets/Game/Prefabs/UI/Components</c>), in an asset under
    /// <c>Resources</c> so the tour loads them SYNCHRONOUSLY — it is armed inside the <c>LevelLoaded</c> delivery, where an
    /// async load would create a "step armed, view not there yet" state. A reference (not a path string) also pulls the
    /// prefabs and their sprites into the player build by construction. Missing asset or reference → one warning and the
    /// code-built fallback (<see cref="TourFingerRig.BuildFallback"/>): the tour never breaks silently.
    /// </summary>
    public sealed class TourAssets : ScriptableObject
    {
        /// <summary><c>Resources.Load</c> path: <c>Assets/Resources/Tutorial/TourAssets.asset</c>.</summary>
        public const string ResourcePath = "Tutorial/TourAssets";

        [SerializeField] private GameObject finger;
        [SerializeField] private GameObject check;

        public GameObject Finger => finger;
        public GameObject Check => check;

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
                    $"[{LogCategory.Tutorial}] no Resources/{ResourcePath} asset — tour finger/✓ fall back to code-built ones");
            return _instance;
        }
    }
}
