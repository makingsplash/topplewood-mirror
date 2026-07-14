using System;
using UnityEngine;
using UnityEngine.Purchasing;
using UnityEngine.UI;

namespace SGS.Store.Purchases.Ui.Components
{
    public class PurchaseWaitWindowController : MonoBehaviour
    {
        [SerializeField]
        private RectTransform _waitPanel;
        [SerializeField]
        private GameObject _failedPanel;
        [SerializeField]
        private GameObject _purchaseFailedMessage;
        [SerializeField]
        private GameObject _noConnectionMessage;
        [SerializeField]
        private Button[] _closeButtons;

        public event Action OnClose;
        
        public GameObject GameObject => gameObject;
        
        public void Init()
        {
            foreach (Button closeButton in _closeButtons)
            {
                closeButton.onClick.AddListener(OnCloseButton);
            }
            _waitPanel.gameObject.SetActive(true);
            _failedPanel.gameObject.SetActive(false);
        }

        public void DeInit()
        {
            foreach (Button closeButton in _closeButtons)
            {
                closeButton.onClick.RemoveListener(OnCloseButton);
            }
        }
        
        
        public virtual void ShowError(PurchaseService.PurchaseResult result, PurchaseFailureReason reason)
        {
            _waitPanel.gameObject.SetActive(false);
            _failedPanel.gameObject.SetActive(true);
            _purchaseFailedMessage.gameObject.SetActive(result == PurchaseService.PurchaseResult.Failed);
            _noConnectionMessage.gameObject.SetActive(result == PurchaseService.PurchaseResult.NoConnection);
        }

        private void OnCloseButton()
        {
            OnClose?.Invoke();
        }
    }
}