using SGS.Infrastructure.Tools.Addressables;
using SGS.Infrastructure.Tools.Configs;
using UnityEngine;

namespace SGS.Configs
{
    [CreateAssetMenu(fileName = "UiPrefabs", menuName = "Configs/UiPrefabs")]
    [GameConfig(ConfigName = "UiPrefabs", ConfigDir = "Configs")]
    public class UiPrefabs : ScriptableObject
    {
        [SerializeField] 
        private bool _useSeparateCanvasCamera;
        [SerializeField]
        private AssetReferenceCanvas _canvasAssetReference;
        [SerializeField]
        private AssetReferenceCamera _cameraAssetReference;
        [SerializeField]
        private GameObject[] _debugUIPrefabs;

        public bool UseSeparateCanvasCamera => _useSeparateCanvasCamera;
        public AssetReferenceCanvas CanvasAssetReference => _canvasAssetReference;
        public AssetReferenceCamera CameraAssetReference => _cameraAssetReference;
        public GameObject[] DebugUIPrefabs => _debugUIPrefabs;

    }
}