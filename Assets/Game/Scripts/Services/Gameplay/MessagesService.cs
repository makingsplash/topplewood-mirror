using Cysharp.Threading.Tasks;
using DataStorage;
using SGS.Services;
using SGS.Services.Interfaces;
using Ui;
using Ui.Components.Message;
using Ui.Screens;
using UnityEngine;
using Game.Services.LogService;

namespace Services
{
    public sealed class MessagesService : IService
    {
        private MessagesViewConfig _config;
        
        public async UniTask ShowAsync(string viewParametersName,
            IMessageContentInstanceParameters instanceParameters = null)
        {
            var screen = await Service.UI.Show<MessageScreen>(ScreenNames.MessageScreen);
            
            bool isClosed = false;
            screen.OnClose += () =>
            {
                isClosed = true;
            };

            await screen.ShowMessageAsync(viewParametersName, instanceParameters);
            
            await UniTask.WaitUntil(() => isClosed);
        }
        
        public void Init()
        {
            _config = Data.Configs.MessagesConfig;
        }

        public void DeInit()
        {
        }

        public MessageViewParameters GetParameters(string name)
        {
            if (_config.TryGetParameters(name, out var parameters))
            {
                return parameters;
            }

            if (_config.TryGetParameters(MessageNames.LoseLevelTemp, out parameters))
            {
                Debug.unityLogger.Log(LogType.Warning,
                    $"[{LogCategory.UI}] Message '{name}' not found. Fallback to '{MessageNames.LoseLevelTemp}'.");
                return parameters;
            }

            Debug.unityLogger.Log(LogType.Error,
                $"[{LogCategory.UI}] Message '{name}' not found and fallback '{MessageNames.LoseLevelTemp}' is missing.");
            return _config.GetParameters(name);
        }
    }
}
