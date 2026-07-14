using Cysharp.Threading.Tasks;
using UnityEngine;
using VContainer;
using Game.Services.LogService;
using UnityEngine.Scripting.APIUpdating;

namespace Ui.Components
{
    [MovedFrom(sourceNamespace: "UI.Components")]
    public abstract class UIShowHide : MonoBehaviour
    {

        [MovedFrom(sourceNamespace: "UI.Components")]
        internal enum EState
        {
            Hidden,
            Show,
            Shown,
            Hide
        }
        
        [SerializeField] private UIShowHideAnimation _animation;
        private ILogService _logService;
        
        private bool _isInitialized;
        internal EState _state = EState.Hidden;

        public bool IsInitialized => _isInitialized;
        public bool IsShown => gameObject.activeSelf;
        
        protected virtual UniTask InitializeProcessAsync() => default;
        
        protected virtual void OnInitialized() { }

        protected virtual UniTask BeforeShowAsync() { return default; }
        protected virtual void OnShown(){}
        protected virtual void BeforeHide(){}
        protected virtual void OnHidden(){}
        protected virtual void SubscribeToEvents(){}
        protected virtual void UnsubscribeFromEvents(){}

        [Inject]
        public void InjectLogService(ILogService logService)
        {
            _logService = logService;
        }
        
        public async UniTask ShowAsync(bool immediately = false)
        {
            _state = EState.Show;

            if (this == null)
            {
                return;
            }
            
            if (_animation != null)
            {
                _animation.ResetToHidden();
            }

            if (!_isInitialized)
            {
                await InitializeAsync();

                if (this == null)
                {
                    return;
                }
            }

            await BeforeShowAsync();

            if (this == null)
            {
                return;
            }
            
            gameObject.SetActive(true);
            
            if (!immediately)
            {
                if (_animation != null)
                {
                    await _animation.PlayShowAnimationAsync();

                    if (this == null)
                    {
                        return;
                    }
                }
            }

            SubscribeToEvents();
            OnShown();
            _state = EState.Shown;
        }

        public async UniTask HideAsync(bool immediately = false)
        {
            _state = EState.Hide;

            if (this == null)
            {
                return;
            }

            if (_animation != null)
            {
                _animation.ResetToShown();

                if (!immediately)
                {
                    await _animation.PlayHideAnimationAsync();

                    if (this == null)
                    {
                        return;
                    }
                }
            }

            BeforeHide();

            if (this == null)
            {
                return;
            }

            gameObject.SetActive(false);
            
            if(_isInitialized)
                UnsubscribeFromEvents();
            
            OnHidden();
            _state = EState.Hidden;
        }
        

        private async UniTask InitializeAsync()
        {
            if (_isInitialized)
            {
                if (this)
                {
                    _logService?.Log(LogLevel.Warning, $"you are trying to call object({gameObject.name}) initialization for the second time", LogCategory.UI);
                }

                return;
            }

            await InitializeProcessAsync();

            if (this == null)
            {
                return;
            }

            _isInitialized = true;
            OnInitialized();
        }
    }
}
