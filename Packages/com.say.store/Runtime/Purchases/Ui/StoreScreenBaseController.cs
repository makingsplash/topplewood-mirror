using System;
using System.Collections.Generic;
using System.Linq;
using SGS.Store.Purchases.Data;
using SGS.Store.Purchases.Ui.Components;
using UnityEngine;
using UnityEngine.Purchasing;
using UnityEngine.UI;

namespace SGS.Store.Purchases.Ui
{
    public abstract class StoreScreenBaseController : MonoBehaviour
    {
        [SerializeField]
        private Button _closeButton;

        [SerializeField]
        private RectTransform _blocksContainer;
        
        [SerializeField]
        private RectTransform _content;

        [SerializeField]
        private Vector2 _blocksStartOffset;
        
        [SerializeField]
        private float _normalTopOffset;
        
        [SerializeField]
        private float _increasedTopOffset;

        [SerializeField]
        private PurchaseWaitWindowController _waitWindow;

        protected List<StoreBlockBaseController> Blocks = new();
        protected Dictionary<string,StoreCardBaseController> Cards = new();
        protected int RunningAnimations => _runningAnimations;
        
        private PurchaseService _service;
        private StoreConfigData _storeData;
        
        private const float _stateUpdateInterval = 1f;
        private float _stateUpdateCooldown;
        private int _runningAnimations;
        private bool _animationsJustFinished;

        public GameObject GameObject => gameObject;
        protected Vector2 BlocksStartOffset => _blocksStartOffset;

        public void Setup(PurchaseService service)
        {
            _service = service;
            _storeData = service.StoreData;
            
            _waitWindow.gameObject.SetActive(false);
            _closeButton.onClick.AddListener(Close);

            _service.OnPurchaseCompleteEvent += ProductPurchased;
            CreateShopBlocks();
            CreateShopItems();
            UpdateState(false);
            LayoutBlocks();
            RecordSeenCards();
            StartAppearAnimation();
        }

        
        public virtual void Close()
        {
            _service.OnPurchaseCompleteEvent -= ProductPurchased;
            _closeButton.onClick.RemoveListener(Close);
        }
        
        public void UpdateItems()
        {
            UpdateState(true);

            if (_animationsJustFinished)
            {
                LayoutBlocks();
                RecordSeenCards();
            }
        }

        protected void SetContentOffset(bool isBannerActive = false)
        {
            _content.offsetMax = new Vector2(_content.offsetMax.x, isBannerActive ? -_increasedTopOffset : -_normalTopOffset);
        }

        protected abstract StoreBlockBaseController CreateBlock(string blockId);
        protected abstract StoreCardBaseController CreateCard(string visualId);
        
        protected virtual void CreateShopBlocks()
        {
            foreach (var blockData in _storeData.Blocks)
            {
                StoreBlockBaseController block = CreateBlock(blockData.VisualBlockId);
                if (block is null)
                {
                    continue;
                }

                block.BlockId = blockData.VisualBlockId;
                block.transform.SetParent(_blocksContainer, false);
                Blocks.Add(block);
            }
        }
        protected virtual void CreateShopItems()
        {
            foreach (var placementData in _service.GetStorePlacements())
            {
                CreateCardForPlacement(placementData);
            }
        }

        private void CreateCardForPlacement(StoreConfigPlacementData placement)
        {
            CreateCardForStoreItem(_service.GetCurrentStoreItem(placement), placement.PlacementId, placement.VisualBlockId);
        }
        
        protected void CreateCardForStoreItem(SellableItemData itemData, string placementId, string visualBlockId)
        {
            var block = Blocks.FirstOrDefault(b => b.BlockId == visualBlockId);
            if (block is null)
            {
                return;
            }
               
            var product = _service.FindProduct(itemData.ProductId);

            var card = CreateCard(itemData.VisualSettings.VisualId);
            card.Init(_service, product, itemData);
            card.OnClick += StartPurchase;
            Cards.Add(placementId, card);
            block.AddCard(card);
        }

        protected virtual void ChangeBlockState(StoreBlockBaseController block, bool isEnabled, Action onComplete)
        {
            block.Enabled = isEnabled;
            onComplete?.Invoke();
        }

        private void AnimationFinished()
        {
            _runningAnimations -= 1;
            if (_runningAnimations <= 0)
            {
                _animationsJustFinished = true;
                LayoutBlocks();
            }
        }

        protected virtual void UpdateState(bool animated)
        {
            foreach (var blockData in _storeData.Blocks)
            {
                if (string.IsNullOrEmpty(blockData.ShowCondition))
                {
                    continue;
                }

                var block = Blocks.First(b => b.BlockId == blockData.VisualBlockId);

                var enable = _service.ConditionChecker.IsConditionMet(new IConditionChecker.Context
                {
                    ProductId = null,
                    BlockId = block.BlockId
                }, blockData.ShowCondition);
                if (enable == block.Enabled)
                {
                    continue;
                }

                if (animated)
                {
                    _runningAnimations += 1;
                    ChangeBlockState(block, enable, AnimationFinished);
                }
                else
                {
                    block.Enabled = enable;
                }
            }

            foreach (var placementData in _storeData.Placements)
            {
                var block = Blocks.FirstOrDefault(b => b.BlockId == placementData.VisualBlockId);
                if (block is null || !block.Enabled)
                {
                    continue;
                }

                var card = Cards[placementData.PlacementId];
                var cardState = _service.GetCardsState(card.SellableItem).State;

                var enable = cardState != StoreCardsState.Expired &&
                    _service.ConditionChecker.IsConditionMet(new IConditionChecker.Context
                    {
                        ProductId = card.ProductId,
                        ItemId = card.SellableItem.ItemId,
                        BlockId = block.BlockId
                    }, card.SellableItem.ShowCondition);
                if (enable == card.Enabled)
                {
                    continue;
                }

                if (animated)
                {
                    _runningAnimations += 1;
                    block.ChangeCardState(card, enable, AnimationFinished);
                }
                else
                {
                    card.Enabled = enable;
                }
            }
            
            LayoutBlocks();
        }

        protected virtual void LayoutBlocks()
        {
            Vector2 nextPosition = _blocksStartOffset;
            foreach (var block in Blocks)
            {
                if (!block.Enabled)
                {
                    continue;
                }
                var rectTransform = (RectTransform)block.transform;
                rectTransform.anchoredPosition = nextPosition;
                block.LayoutCards();
    
                nextPosition.y -= rectTransform.sizeDelta.y;
            }

            var sizeDelta = _blocksContainer.sizeDelta;
            sizeDelta.y = Mathf.Abs(nextPosition.y);
            _blocksContainer.sizeDelta = sizeDelta;
        }
        
        private void RecordSeenCards()
        {
            foreach (var block in Blocks)
            {
                if ( !block.Enabled)
                {
                    continue;
                } 
                foreach (var card in block.Cards)
                {
                    if ( !card.Enabled)
                    {
                        continue;
                    }
                    _service.RecordCardSeen(card.ProductId);
                    OnCardSeen(card.ProductId);
                }
            }
        }

        protected virtual void OnCardSeen(string productId)
        {
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

        protected virtual void Update()
        {
            if (Cards.Count <= 0)
            {
                return;
            } 
            
            _stateUpdateCooldown -= Time.deltaTime;
            if (_stateUpdateCooldown > 0)
            {
                return;
            }

            _stateUpdateCooldown = _stateUpdateInterval;

            var haveExpired = false;
            foreach (var card in Cards.Values)
            {
                var cardStateData = _service.GetCardsState(card.SellableItem);

                switch (cardStateData.State)
                {
                    case StoreCardsState.Permanent:
                        card.UpdateTimedState(false, TimeSpan.Zero);
                        card.UpdateCooldownState(false, TimeSpan.Zero, 0);
                        break;
                    case StoreCardsState.Timed:
                        card.UpdateTimedState(true, cardStateData.Timer);
                        break;
                    case StoreCardsState.Cooldown:
                        card.UpdateCooldownState(true, cardStateData.Timer, cardStateData.AdsLeft);
                        break;
                    case StoreCardsState.Expired:
                        if (card.Enabled)
                        {
                            haveExpired = true;
                        }
                        break;
                }
            }

            if (haveExpired)
            {
                UpdateItems();
            }
        }
        
        protected virtual void ProductPurchased(string productId, int quantity, PurchaseService.PurchaseCompleteStatus status)
        {
            if (Cards.TryGetValue(productId, out var card))
            {
                card.ProductPurchased();
            }

            UpdateItems();
        }
    }
}
