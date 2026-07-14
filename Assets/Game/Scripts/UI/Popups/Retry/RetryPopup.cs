using System;
using Ui.Screens;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Scripting.APIUpdating;

namespace Ui.Popups
{
    [MovedFrom(sourceNamespace: "UI.Popups")]
    public class Retry : BaseScreen
    {
        [SerializeField] private Button _submitButton;

        private Action _clickSubmit;

        public override void Init()
        {
            _submitButton.onClick.AddListener(OnClickSubmit);
        }

        public override void DeInit()
        {
            base.DeInit();
            _submitButton.onClick.RemoveListener(OnClickSubmit);
        }

        public void Setup(Action onClickConfirmRestart)
        {
            _clickSubmit = onClickConfirmRestart;
        }

        private void OnClickSubmit()
        {
            _clickSubmit?.Invoke();
            Close();
        }
    }
}
