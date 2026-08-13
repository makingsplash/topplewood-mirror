using System;
using Cysharp.Threading.Tasks;
using Game.Services.LogService;
using SGS.Infrastructure.Loading;

namespace Infrastructure.Loading
{
    // Проектная обёртка над пакетным IAssetUniTaskAsyncLoader (TASK-134). Пакет com.say.infrastructure
    // не трогаем, поэтому точечная инструментация загрузчиков конфигов живёт здесь.
    //
    // Диагностический контракт (уточнён по замеру 12:47:33): логируем И старт, И завершение каждого Load().
    // Только пары «config load start» без «config loaded» отличают «Load() был вызван и не завершился» от
    // «Load() вообще не вызван» — без старт-строки эти два случая в логе неразличимы. Плюс обёртка держит
    // флаг IsCompleted, по которому call-site watchdog (LoadingGame) поimённо перечисляет зависшие загрузчики.
    internal sealed class LoggingAssetLoader : IAssetUniTaskAsyncLoader
    {
        private readonly IAssetUniTaskAsyncLoader _inner;
        private readonly ILogService _logService;

        public LoggingAssetLoader(IAssetUniTaskAsyncLoader inner, string assetPath, ILogService logService)
        {
            _inner = inner ?? throw new ArgumentNullException(nameof(inner));
            AssetPath = assetPath;
            _logService = logService ?? throw new ArgumentNullException(nameof(logService));
        }

        public string AssetPath { get; }

        // Выставляется ПОСЛЕ завершения _inner.Load(). Watchdog читает его, чтобы назвать висящих поимённо.
        public bool IsCompleted { get; private set; }

        public float PercentComplete => _inner.PercentComplete;

        public async UniTask Load()
        {
            Log("config load start");

            await _inner.Load();

            IsCompleted = true;
            Log("config loaded");
        }

        private void Log(string stage)
        {
            var time = DateTime.Now;
            _logService.Log(
                LogLevel.Info,
                $"[{time:T}.{time.Millisecond.ToString()}] {stage}: {AssetPath}",
                LogCategory.LoadLevel);
        }
    }
}
