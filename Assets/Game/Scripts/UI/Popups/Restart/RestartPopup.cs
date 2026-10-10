using System;
using Ui.Screens;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Scripting.APIUpdating;

namespace Ui.Popups
{
    [MovedFrom(sourceNamespace: "UI.Popups")]
    public class RestartPopup : BaseScreen
    {
        [SerializeField] private Button _cancelButton;
        [SerializeField] private Button _submitButton;
        // Tap on the dim outside the window cancels too (owner decision, TASK-426).
        [SerializeField] private Button _dismissArea;

        private Action ClickCancel, ClickSubmit;

        public override void Init()
        {
            base.Init();
            _cancelButton.onClick.AddListener(OnClickCancel);
            _dismissArea.onClick.AddListener(OnClickCancel);
            _submitButton.onClick.AddListener(OnClickSubmit);
        }

        public override void DeInit()
        {
            base.DeInit();
            _cancelButton.onClick.RemoveListener(OnClickCancel);
            _dismissArea.onClick.RemoveListener(OnClickCancel);
            _submitButton.onClick.RemoveListener(OnClickSubmit);
        }

        public void Setup(Action onClickCancelRestart, Action onClickConfirmRestart)
        {
            ClickCancel = onClickCancelRestart;
            ClickSubmit = onClickConfirmRestart;
        }

        private void OnClickCancel()
        {
            ClickCancel?.Invoke();
            Close();
        }

        private void OnClickSubmit()
        {
            ClickSubmit?.Invoke();
            Close();
        }
    }
}
