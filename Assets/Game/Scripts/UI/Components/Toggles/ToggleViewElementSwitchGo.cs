using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.Scripting.APIUpdating;

namespace Ui.Components.Toggles
{
    [MovedFrom(sourceNamespace: "UI.Components.Toggles")]
    internal sealed class ToggleViewElementSwitchGo : BaseToggleViewElement
    {
        [SerializeField] private GameObject _enabledView;
        [SerializeField] private GameObject _disabledView;

        protected override UniTask SetEnabledViewAsync(bool immediately)
        {
            SetEnabledView();
            return default;
        }

        protected override UniTask SetDisabledViewAsync(bool immediately)
        {
            SetDisabledView();
            return default;
        }

        protected override void SetEnabledView()
        {
            _enabledView.SetActive(true);
            _disabledView.SetActive(false);
        }

        protected override void SetDisabledView()
        {
            _enabledView.SetActive(false);
            _disabledView.SetActive(true);
        }
    }
}
