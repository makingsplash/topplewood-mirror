using SGS.Services.DebugLog;

namespace Services.AssetPreload
{
    public class AssetPreloadServiceDebugger
    {
        private AssetPreloadService _service;

        public void Init(AssetPreloadService service)
        {
            _service = service;
            
            _service.OnStarted += OnStarted;
            _service.OnProgress += OnProgress;
            _service.OnCompleted += OnCompleted;
            _service.OnFailed += OnFailed;
        }

        public void DeInit()
        {
            if (_service != null)
            {
                _service.OnStarted -= OnStarted;
                _service.OnProgress -= OnProgress;
                _service.OnCompleted -= OnCompleted;
                _service.OnFailed -= OnFailed;
            }
        }

        private void OnStarted(string label)
        {
            GlobalService.DebugLog.Log($"Started: {label}", DebugLogType.AssetPreload);
        }

        private void OnProgress(string label, float progress)
        {
            GlobalService.DebugLog.Log($"Progress: {label} = {progress:P2}", DebugLogType.AssetPreload);
        }

        private void OnCompleted(string label)
        {
            GlobalService.DebugLog.Log($"✓ Completed: {label}", DebugLogType.AssetPreload);
        }

        private void OnFailed(string label, string error)
        {
            GlobalService.DebugLog.Log($"✗ Failed: {label} - {error}", DebugLogType.AssetPreload);
        }
    }
}
