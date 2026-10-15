#if GPIE_DEVELOPMENT
using System.Collections.Generic;
using UnityEngine;

namespace Gameplay.Dev
{
    /// <summary>TASK-557 ВРЕМЕННЫЙ пробник: циклит глобальный _WoodProbe и пишет в logcat FPS/время GPU окна.</summary>
    public sealed class WoodPerfProbe : MonoBehaviour
    {
        private static readonly int ProbeId = Shader.PropertyToID("_WoodProbe");
        private static readonly int[] Modes = { 0, 3, 2, 1, 4, 5, 6, 0, 3, 2, 1, 4, 5, 6 };
        private const float Warmup = 2f, Window = 7f;

        private readonly List<float> _dt = new List<float>(1024);
        private readonly FrameTiming[] _ft = new FrameTiming[1];
        private double _gpuSum; private int _gpuN;
        private int _idx = -1; private float _t;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Boot()
        {
            var go = new GameObject("WoodPerfProbe");
            DontDestroyOnLoad(go);
            go.AddComponent<WoodPerfProbe>();
        }

        private void Start() { _t = -20f; Shader.SetGlobalFloat(ProbeId, 0f); }

        private void Update()
        {
            FrameTimingManager.CaptureFrameTimings();
            _t += Time.unscaledDeltaTime;
            if (_idx < 0) { if (_t >= 0f) Next(); return; }
            if (_t > Warmup)
            {
                _dt.Add(Time.unscaledDeltaTime);
                if (FrameTimingManager.GetLatestTimings(1, _ft) > 0 && _ft[0].gpuFrameTime > 0) { _gpuSum += _ft[0].gpuFrameTime; _gpuN++; }
            }
            if (_t >= Warmup + Window) { Report(); Next(); }
        }

        private void Next()
        {
            _idx++;
            if (_idx >= Modes.Length) { Shader.SetGlobalFloat(ProbeId, 0f); Debug.Log("[WOODPROBE] done"); enabled = false; return; }
            Shader.SetGlobalFloat(ProbeId, Modes[_idx]);
            _t = 0f; _dt.Clear(); _gpuSum = 0; _gpuN = 0;
        }

        private void Report()
        {
            float sum = 0; int slow = 0;
            foreach (var d in _dt) { sum += d; if (d >= 0.0300f) slow++; }
            var n = Mathf.Max(1, _dt.Count);
            Debug.Log($"[WOODPROBE] mode={Modes[_idx]} frames={_dt.Count} fps={n / Mathf.Max(sum, 1e-3f):F1} slow33={100f * slow / n:F0}% gpuMs={(_gpuN > 0 ? _gpuSum / _gpuN : -1):F2} gpuN={_gpuN} w={Screen.width}x{Screen.height}");
        }
    }
}
#endif
