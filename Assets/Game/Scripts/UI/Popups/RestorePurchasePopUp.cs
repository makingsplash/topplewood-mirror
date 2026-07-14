using Cysharp.Threading.Tasks;
using DataStorage;
using TMPro;
using Ui.Components.Toggles;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Scripting.APIUpdating;

namespace Ui.Screens.RateUs
{
    [MovedFrom(sourceNamespace: "UI.Screens.RateUs")]
    internal sealed class RestorePurchasePopUp : BaseScreen
    {
        [SerializeField] private Button[] _closeButtons;
        [SerializeField] private TMP_Text _messageText;

        public override void Init()
        {
            base.Init();

            for (var index = 0; index < _closeButtons.Length; index++)
            {
                _closeButtons[index].onClick.AddListener(Close);
            }

            SetCloseButtonsInteractable(false);
        }

        public override void DeInit()
        {
            for (var index = 0; index < _closeButtons.Length; index++)
            {
                _closeButtons[index].onClick.RemoveListener(Close);
            }

            base.DeInit();
        }

        public void SetCloseButtonsInteractable(bool interactable)
        {
            for (var index = 0; index < _closeButtons.Length; index++)
            {
                _closeButtons[index].interactable = interactable;
            }
        }

        public void SetMessage(string message)
        {
            _messageText.text = message;
        }
    }
}
