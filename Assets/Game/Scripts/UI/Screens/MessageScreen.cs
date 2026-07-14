using Coffee.UIExtensions;
using Cysharp.Threading.Tasks;
using Services;
using SGS.Services.Containers;
using Sirenix.OdinInspector;
using Ui.Components.Message;
using UnityEngine;
using UnityEngine.Scripting.APIUpdating;

namespace Ui.Screens
{
    [MovedFrom(sourceNamespace: "UI.Screens")]
    public sealed class MessageScreen : BaseScreen
    {
        [SerializeField] private RectTransform _contentParent;
        [SerializeField] private Unmask _fadeBackgroundHole;

        private MessageContentElement _contentInstance;
        
        private MessagesService _service;

        public override void Init()
        {
            base.Init();
            
            _service = LocalServiceContainer.Get<MessagesService>();
        }

        public async UniTask ShowMessageAsync(string messageName, IMessageContentInstanceParameters instanceParameters = null)
        {
            await CreateContentAsync(GetParameters(messageName), instanceParameters);

            await _contentInstance.ShowAsync();
        }

        
        private MessageViewParameters GetParameters(string messageName)
        {
            return _service.GetParameters(messageName);
        }

        private async UniTask CreateContentAsync(MessageViewParameters viewParameters, IMessageContentInstanceParameters instanceParameters)
        {
            if(_contentInstance != null)
                ClearContent();
            
            var instance = (await viewParameters.Prefab.InstantiateAsync(_contentParent)).GetComponent<MessageContentElement>();
            instance.SetParameters(viewParameters, instanceParameters);
            _contentInstance = instance;
            _contentInstance.OnCompleted += Close;
            UpdateFadeBackgroundHole();
        }

        public override void DeInit()
        {
            ClearContent();
            base.DeInit();
        }

        private void ClearContent()
        {
            if (_contentInstance == null)
            {
                return;
            }

            _contentInstance.DeInit();
            Destroy(_contentInstance.gameObject);
            _contentInstance = null;
            UpdateFadeBackgroundHole();
        }

        private void UpdateFadeBackgroundHole()
        {
            if (_fadeBackgroundHole == null)
            {
                return;
            }

            var holeTarget = _contentInstance != null
                ? _contentInstance.GetComponentInChildren<FadeBackgroundHoleTarget>(true)
                : null;

            if (holeTarget != null && holeTarget.RectTransform != null)
            {
                _fadeBackgroundHole.fitTarget = holeTarget.RectTransform;
                if (!_fadeBackgroundHole.gameObject.activeSelf)
                {
                    _fadeBackgroundHole.gameObject.SetActive(true);
                }
            }
            else if (_fadeBackgroundHole.gameObject.activeSelf)
            {
                _fadeBackgroundHole.gameObject.SetActive(false);
            }
        }
        
#if UNITY_EDITOR
        [SerializeField] private string _testMessageName;
        
        [Button]
        public void ShowTestMessage()
        {
            ShowMessageAsync(_testMessageName).Forget();
        }
#endif
    }
}
