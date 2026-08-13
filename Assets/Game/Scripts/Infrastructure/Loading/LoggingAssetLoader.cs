using System;
using Cysharp.Threading.Tasks;
using Game.Services.LogService;
using SGS.Infrastructure.Loading;

namespace Infrastructure.Loading
{
    // Проектная обёртка над пакетным IAssetUniTaskAsyncLoader (TASK-134). Пакет com.say.infrastructure
    // не трогаем, поэтому точечная инструментация загрузчиков конфигов живёт здесь: по завершении Load()
    // пишем строку с _assetPath в тот же канал _logService/LogCategory.LoadLevel, что и маркеры бута.
    //
    // Диагностический контракт: висящий загрузчик определяется по ОТСУТСТВИЮ своей строки "config loaded".
    // Все загрузчики стартуют синхронно и одновременно (LoadingGame.LoadResources), поэтому старт логировать
    // незачем — сигналом является только завершение. Тот единственный, чья строка так и не появилась между
    // "Load Configs start" и (не пришедшим) "Load Configs finished", и есть виновник вечного WhenAll.
    internal sealed class LoggingAssetLoader : IAssetUniTaskAsyncLoader
    {
        private readonly IAssetUniTaskAsyncLoader _inner;
        private readonly string _assetPath;
        private readonly ILogService _logService;

        public LoggingAssetLoader(IAssetUniTaskAsyncLoader inner, string assetPath, ILogService logService)
        {
            _inner = inner ?? throw new ArgumentNullException(nameof(inner));
            _assetPath = assetPath;
            _logService = logService ?? throw new ArgumentNullException(nameof(logService));
        }

        public float PercentComplete => _inner.PercentComplete;

        public async UniTask Load()
        {
            await _inner.Load();

            var time = DateTime.Now;
            _logService.Log(
                LogLevel.Info,
                $"[{time:T}.{time.Millisecond.ToString()}] config loaded: {_assetPath}",
                LogCategory.LoadLevel);
        }
    }
}
