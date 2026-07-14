using Cysharp.Threading.Tasks;
using DataStorage;
using Ui.Components.Toggles;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Scripting.APIUpdating;

namespace Ui.Screens.RateUs
{
    [MovedFrom(sourceNamespace: "UI.Screens.RateUs")]
    internal sealed class RateUsPopUp : BaseScreen
    {
        [SerializeField] private int _initialActiveStars = 3;
        [SerializeField] private BaseToggleViewElement[] _stars;
        [SerializeField] private Button[] _starButtons;

        [SerializeField] private Button _submitButton;
        [SerializeField] private Button _closeButton;

        private int _currentRate;
        
        private void OnClickSubmit()
        {
            SayKit.showCustomRateAppPopup(_currentRate);
            if (_currentRate >= Data.Configs.RateUsConfig.MinimalPositiveRate)
            {
                OnUserMakePositiveRate();
            }
            Close();
        }

        private void OnUserMakePositiveRate()
        {
#if UNITY_ANDROID
            Application.OpenURL("market://details?id=" + Application.identifier);
#endif
        }

        private void OnClickClose()
        {
            SayKit.showCustomRateAppPopup(0);
            Close();
        }
        
        public override void Init()
        {
            base.Init();
            
            for (int index = 0; index < _starButtons.Length; index++)
            {
                var starIndex = index;
                
                _starButtons[index].onClick.AddListener(()=>OnClickTo(starIndex));
            }
            
            _submitButton.onClick.AddListener(OnClickSubmit);
            _closeButton.onClick.AddListener(OnClickClose);

            if (_initialActiveStars > 0)
            {
                SetTogglesView(_initialActiveStars - 1, forceUpdate: true);
            }
            else
            {
                SetTogglesView(forceUpdate: true);
            }
            _submitButton.interactable = false;
            Data.Meta.RateUsShown.Value = true;
        }

        private void OnClickTo(int starIndex)
        {
            _submitButton.interactable = true;
            SetTogglesView(starIndex);
        }

        private void SetTogglesView(int activeIndex = -1, bool forceUpdate = false)
        {
            for (var index = 0; index < _stars.Length; index++)
            {
                var starToggle = _stars[index];
                starToggle.SetValueAsync(index <= activeIndex, forceUpdate: forceUpdate).Forget();
            }
            _currentRate = activeIndex+1;
        }
    }
}
