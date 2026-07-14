using System;
using R3;
using UnityEngine;
using Game.Services.LogService;

namespace Game.Scripts.Infrastructure.R3
{
    public static class UnityProviderInitializer
    {

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterAssembliesLoaded)]
        public static void SetDefaultObservableSystem()
        {
            SetDefaultObservableSystem(ex =>
            {
                Debug.unityLogger.Log(LogType.Error, "Unhandled R3 exception");
                Debug.unityLogger.LogException(ex);
            });
        }

        private static void SetDefaultObservableSystem(Action<Exception> unhandledExceptionHandler)
        {
            ObservableSystem.RegisterUnhandledExceptionHandler(unhandledExceptionHandler);
            ObservableSystem.DefaultTimeProvider = UnityTimeProvider.Update;
            ObservableSystem.DefaultFrameProvider = UnityFrameProvider.Update;
        }
    }
}
