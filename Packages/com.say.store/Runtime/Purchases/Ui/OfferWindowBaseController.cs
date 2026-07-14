using System;
using System.Linq;
using SGS.Store.Purchases.Data;
using SGS.Store.Purchases.Ui.Components;
using UnityEngine;
using UnityEngine.Purchasing;

namespace SGS.Store.Purchases.Ui
{
    public abstract class OfferWindowBaseController : MonoBehaviour
    {
        [SerializeField]
        private RectTransform _cardContainer;

        [SerializeField]
        private PurchaseWaitWindowController _waitWindow;

        protected StoreCardBaseController Card;
        
        private PurchaseService _service;
        
        private const float _stateUpdateInterval = 1f;
        private float _stateUpdateCooldown;

        public void Setup(PurchaseService service, string itemId)
        {
            if (string.IsNullOrEmpty(itemId))
            {
                return;
            }
            _service = service;
            var item = _service.GetSellableItem(itemId);
            if (item == null)
            {
                return;
            }
            
            _waitWindow.gameObject.SetActive(false);

            _service.OnPurchaseCompleteEvent += ProductPurchased;
            
            CreateShopItem(item);
            CheckConditions();
            _service.RecordCardSeen(item.ProductId);
            StartAppearAnimation();
        }

        
        public virtual void Close()
        {
            if (_service != null)
            {
                _service.OnPurchaseCompleteEvent -= ProductPurchased;
            }
        }
        
        public void UpdateItems()
        {
            CheckConditions();
        }
        
        protected abstract StoreCardBaseController CreateCard(string visualId);
        protected abstract bool IsConditionMet(string productId, string condition);
        
        protected virtual void CreateShopItem(SellableItemData itemData)
        {
            var product = _service.FindProduct(itemData.ProductId);
            if (product == null)
            {
                return;
            }

            Card = CreateCard(GetVisualId(itemData));
            Card.Init(_service, product, itemData);
            Card.OnClick += StartPurchase;
            Card.transform.SetParent(_cardContainer, false);
        }

        protected virtual string GetVisualId(SellableItemData itemData)
        {
            return itemData.VisualSettings.VisualId;
        }
        
        protected virtual void CheckConditions()
        {
            if (Card is null)
            {
                return;
            }

            var itemData = Card.SellableItem;
            var cardState = _service.GetCardsState(Card.SellableItem);

            var enable = cardState.State != StoreCardsState.Expired && IsConditionMet(Card.ProductId, itemData.ShowCondition);
            if (enable != Card.Enabled)
            {
                Card.Enabled = enable;
            }
        }

        protected virtual void StartAppearAnimation()
        {
        }

        protected virtual void StartPurchase(StoreCardBaseController card)
        {
            ShowWaitWindow();
            bool purchaseStarted = _service.PurchaseProduct(card.SellableItem, OnPurchaseResult);
            if (!purchaseStarted)
            {
                HideWaitWindow();
            }
        }

        private void OnPurchaseResult(PurchaseService.PurchaseResult result, PurchaseFailureReason reason)
        {
            if (_waitWindow == null || !_waitWindow.gameObject.activeSelf)
            {
                return;
            }

            if (result == PurchaseService.PurchaseResult.Failed || result == PurchaseService.PurchaseResult.NoConnection)
            {
                _waitWindow.ShowError(result, reason);
                _waitWindow.OnClose += HideWaitWindow;
            }
            else
            {
                HideWaitWindow();
            }
        }

        protected virtual void ShowWaitWindow()
        {
            _waitWindow.gameObject.SetActive(true);
            _waitWindow.Init();
        }

        protected virtual void HideWaitWindow()
        {
            _waitWindow.OnClose -= HideWaitWindow;
            _waitWindow.DeInit();
            _waitWindow.gameObject.SetActive(false);
        }

        protected void Update()
        {
            if (Card is null)
            {
                return;
            }

            _stateUpdateCooldown -= Time.deltaTime;
            if (_stateUpdateCooldown > 0)
            {
                return;
            }

            _stateUpdateCooldown = _stateUpdateInterval;

            StoreCardStateData cardStateData = _service.GetCardsState(Card.SellableItem);

            switch (cardStateData.State)
            {
                case StoreCardsState.Permanent:
                    Card.UpdateTimedState(false, TimeSpan.Zero);
                    Card.UpdateCooldownState(false, TimeSpan.Zero, 0);
                    break;
                case StoreCardsState.Timed:
                    Card.UpdateTimedState(true, cardStateData.Timer);
                    break;
                case StoreCardsState.Cooldown:
                    Card.UpdateCooldownState(true, cardStateData.Timer, cardStateData.AdsLeft);
                    break;
                case StoreCardsState.Expired:
                    if (Card.Enabled)
                    {
                        UpdateItems();
                    }
                    break;
            }
        }
        
        private void ProductPurchased(string productId, int quantity, PurchaseService.PurchaseCompleteStatus status)
        {
            if (Card != null && Card.ProductId == productId)
            {
                Card.ProductPurchased();
            }

            UpdateItems();
        }

    }
}