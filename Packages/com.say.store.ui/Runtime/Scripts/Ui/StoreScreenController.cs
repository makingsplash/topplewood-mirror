using System;
using DG.Tweening;
using SGS.Services;
using SGS.Services.Containers;
using SGS.Services.Ui;
using SGS.Store.Purchases;
using SGS.Store.Purchases.Ui;
using SGS.Store.Purchases.Ui.Components;
using UnityEngine;
using UnityEngine.UI;

namespace SGS.Store.Ui
{
    public class StoreScreenController : StoreScreenBaseController, IScreenController, IWindowAnimator
    {
        [SerializeField]
        private Image _background;
        
      
        private Tween _activeShowTween;
        private bool _isLuckyCouponActive;
        private StoreVisualConfig _visualConfig;

        public event Action OnClose;

        public void Init()
        {
            GlobalService.SayKit.TrackScreen("Store");
            _visualConfig = GlobalServiceContainer.Get<GamePurchaseService>().VisualConfig;
            Setup(GlobalServiceContainer.Get<GamePurchaseService>());
        }

        public void DeInit()
        {
            foreach (var pair in Cards)
            {
                ((ISubpanelController)pair.Value).DeInit();
            }

            foreach (var block in Blocks)
            {
                ((ISubpanelController)block).DeInit();
            }

            OnClose?.Invoke();
        }
        
        public void Show(Action onFinished)
        {
            if (_activeShowTween != null)
            {
                return;
            }

            _background.gameObject.SetActive(true);
            var showSequence = DOTween.Sequence();
            showSequence.SetLink(gameObject, LinkBehaviour.KillOnDestroy);
            showSequence.Append(DOVirtual.Float(0f, _background.color.a, 0.16f, AnimateBackgroundFade).SetEase(Ease.Linear));
            showSequence.AppendCallback(() =>
            {
                _activeShowTween = null;
                onFinished?.Invoke();
            });
            _activeShowTween = showSequence;
        }
        
        public override void Close()
        {
            base.Close();
            Service.UI.Hide(this);
        }

        protected override StoreBlockBaseController CreateBlock(string blockId)
        {
            var block = _visualConfig.GetBlockPrefab(blockId);
            ((ISubpanelController)block)?.Init();
            return block;
        }

        protected override StoreCardBaseController CreateCard(string visualId)
        {
            var card = _visualConfig.GetStoreCardPrefab(visualId);
            ((ISubpanelController)card)?.Init();
            return card;
        }
        protected override void CreateShopItems()
        {
            base.CreateShopItems();
        }

        public void Hide(Action onFinished)
        {
            if (_activeShowTween != null)
            {
                _activeShowTween.Kill();
                _activeShowTween = null;
            }
            
            var showSequence = DOTween.Sequence();
            showSequence.SetLink(gameObject, LinkBehaviour.KillOnDestroy);
            showSequence.Append(DOVirtual.Float(_background.color.a, 0f, 0.16f, AnimateBackgroundFade).SetEase(Ease.Linear));
            showSequence.AppendCallback(() =>
            {
                _activeShowTween = null;
                onFinished?.Invoke();
            });
            _activeShowTween = showSequence;
        }
        
        protected override void OnCardSeen(string productId)
        {
            LocalServiceContainer.Get<PurchaseAnalytics>().SendIapOfferEvent(productId);
        }

        protected override void StartAppearAnimation()
        {
            float delay = 0f;
            foreach (var block in Blocks)
            {
                foreach (var card in block.Cards)
                {
                    ((StoreCardController)card).AppearAnimation(delay);
                    delay += 0.05f;
                }
            }
        }

        protected override void Update()
        {
            base.Update();
            if (RunningAnimations > 0)
            {
                Vector2 nextPosition = BlocksStartOffset;
                foreach (var block in Blocks)
                {
                    if (!block.Enabled)
                    {
                        continue;
                    }

                    var rectTransform = (RectTransform)block.transform;
                    rectTransform.anchoredPosition = nextPosition;

                    nextPosition.y -= rectTransform.sizeDelta.y;
                }
            }
        }

        protected override void ChangeBlockState(StoreBlockBaseController block, bool enabled, Action onComplete)
        {
            var blockImpl = (StoreBlockController)block;

            if (enabled)
            {
                blockImpl.ShowAnimation(onComplete);
            }
            else
            {
                blockImpl.HideAnimation(onComplete);
            }
        }

        private void AnimateBackgroundFade(float value)
        {
            var color = _background.color;
            color.a = value;
            _background.color = color;
        }

    }
}
