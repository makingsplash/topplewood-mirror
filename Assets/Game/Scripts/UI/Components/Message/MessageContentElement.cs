using System;
using Cysharp.Threading.Tasks;
using SGS.Services.Localization;
using UnityEngine;
using UnityEngine.Scripting.APIUpdating;

namespace Ui.Components.Message
{
    [MovedFrom(sourceNamespace: "UI.Components.Message")]
    public abstract class MessageContentElement : UIElement
    {
        [SerializeField] private LocaleComponent _title;

        public event Action OnCompleted;

        private MessageViewParameters _viewParameters;

        public virtual void SetParameters(MessageViewParameters viewParameters, IMessageContentInstanceParameters instanceParameters = null)
        {
            _viewParameters = viewParameters;
        }
        
        public virtual void DeInit() {}

        protected override async UniTask BeforeShowAsync()
        {
            await SetupViewAsync();
        }

        protected virtual UniTask SetupViewAsync()
        {
            if (_viewParameters == null)
            {
                Debug.unityLogger.Log(LogType.Warning, $"[{nameof(MessageContentElement)}] View parameters are null for {GetType().Name}.");
                return default;
            }

            SetTitle(_viewParameters.Title);
            
            return default;
        }
        
        private void SetTitle(string titleLocalizationId)
        {
            if (_title == null)
            {
                if (!string.IsNullOrWhiteSpace(titleLocalizationId))
                {
                    Debug.unityLogger.Log(
                        LogType.Warning,
                        $"[{nameof(MessageContentElement)}] Title component is not assigned in {GetType().Name}. Title id: '{titleLocalizationId}'.");
                }

                return;
            }

            if (string.IsNullOrWhiteSpace(titleLocalizationId))
            {
                return;
            }

            _title.SetId(titleLocalizationId);
        }

        protected void Complete()
        {
            OnCompleted?.Invoke();
        }
    }
}
