using System.Collections.Generic;
using UnityEngine;

namespace Game
{
    [CreateAssetMenu(fileName = "CaptureConfig", menuName = "Configs/CaptureConfig", order = 1)]
    public class CaptureConfig : ScriptableObject
    {
#pragma warning disable CS0649
        [SerializeField] private float _captureFreq = 0.3f;
        [SerializeField] private List<Preset> _presets;
#pragma warning restore CS0649

        [System.Serializable]
        public class Preset
        {
            public string Name;
            public Vector2Int Resolution;
        }

        public IReadOnlyList<Preset> Presets => _presets;

        public float CaptureFreq => _captureFreq;
    }
}
