using Cysharp.Threading.Tasks;
using Sirenix.OdinInspector;
using UnityEngine;
using UnityEngine.Scripting.APIUpdating;

namespace Ui.Components.Toggles
{
    [MovedFrom(sourceNamespace: "UI.Components.Toggles")]
    public abstract class BaseToggleViewElement : UIElement
    {
        [Space] 
        [SerializeField]
        [OnValueChanged("OnValueChangedValidate")]
        protected bool _value;
        

        public bool Value => _value;
        

        public async UniTask SetValueAsync(bool value, bool immediately = false, bool forceUpdate = false)
        {
            if (!forceUpdate && _value == value)
            {
                return;
            }

            _value = value;

            if (_value)
            {
                await SetEnabledViewAsync(immediately);
            }
            else
            {
                await SetDisabledViewAsync(immediately);
            }
        }

        protected abstract UniTask SetEnabledViewAsync(bool immediately);
        protected abstract UniTask SetDisabledViewAsync(bool immediately);

        protected abstract void SetEnabledView();
        protected abstract void SetDisabledView();

#if UNITY_EDITOR
        private void OnValueChangedValidate()
        {
            if (_value)
            {
                SetEnabledView();
            }
            else
            {
                SetDisabledView();
            }
        }
#endif
    }
}
