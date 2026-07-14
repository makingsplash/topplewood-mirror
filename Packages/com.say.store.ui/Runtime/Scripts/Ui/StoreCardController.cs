using System;
using DG.Tweening;
using SGS.Infrastructure.Tools.Rewards;
using SGS.Services;
using SGS.Services.Containers;
using SGS.Services.Ui;
using SGS.Store.Purchases;
using SGS.Store.Purchases.Data;
using SGS.Store.Purchases.Ui.Components;
using TMPro;
using UnityEngine;

namespace SGS.Store.Ui
{
    public abstract class StoreCardController : StoreCardBaseController, ISubpanelController
    {
        [SerializeField] private RectTransform _animationRoot;
        [SerializeField] private CanvasGroup _canvasGroup;
        [SerializeField] private RectTransform _rewardFlyPoint;
        [SerializeField] private GameObject _timedOffer;
        [SerializeField] private TMP_Text _timedOfferText;
        
       
        protected ProductRewardData[] Reward;
        
        private Tween _runningTween;

        public CanvasGroup CanvasGroup => _canvasGroup;
        
        [NonSerialized]
        public float AnimatedSize = 1f;

        void ISubpanelController.Init()
        {
        }

        void ISubpanelController.DeInit()
        {
            _runningTween?.Kill();
            _runningTween = null;
        }

        public void SetVisible(bool value)
        {
        }

        public override void Init(PurchaseService service, ProductData product, SellableItemData storeData)
        {
            Reward = GlobalServiceContainer.Get<GamePurchaseService>().PurchaseProcessor.GetActualReward(product.ProductId, product.Rewards);
            
            base.Init(service, product, storeData);
            SetupReward(Reward);
        }

        protected abstract void SetupReward(ProductRewardData[] pack);

        public override void ProductPurchased()
        {
            var config = GlobalServiceContainer.Get<GamePurchaseService>().VisualConfig.AnimationSettings;

            var sequence = DOTween.Sequence();
            sequence.SetLink(gameObject, LinkBehaviour.KillOnDestroy);
            sequence.Append(DOVirtual.Float(1f, config.CardClickScale, config.CardClickDuration / 2f, v => AnimatedScale = v).SetEase(Ease.InBack));
            sequence.Append(DOVirtual.Float(config.CardClickScale, 1f, config.CardClickDuration / 2f, v => AnimatedScale = v).SetEase(Ease.OutBack));
            sequence.AppendCallback(() => _runningTween = null);

            _runningTween = sequence;
        }
        public override void UpdateTimedState(bool stateActive, TimeSpan timeLeft)
        {
            if (_timedOffer != null)
            {
                _timedOffer.SetActive(stateActive);
            }

            if (_timedOfferText != null)
            {
                _timedOfferText.text =  GlobalService.Localization.GetLocalizedTimeInterval(timeLeft);
            }
        }

        public float AnimatedScale
        {
            get => _animationRoot.localScale.x;
            set => _animationRoot.localScale = Vector3.one * value;
        }

        public void AppearAnimation(float delay)
        {
            _animationRoot.localScale = Vector3.zero;
            var sequence = DOTween.Sequence();
            sequence.SetLink(gameObject, LinkBehaviour.KillOnDestroy);
            sequence.AppendInterval(delay);
            sequence.Append(DOVirtual.Float(0, 1, 0.3f, v => AnimatedScale = v).SetEase(Ease.OutBack));
            sequence.AppendCallback(() => _runningTween = null);

            _runningTween = sequence;
        }
        
        public void DisappearAnimation(float delay, Action callback)
        {
            _animationRoot.localScale = Vector3.one;
            var sequence = DOTween.Sequence();
            sequence.SetLink(gameObject, LinkBehaviour.KillOnDestroy);
            sequence.AppendInterval(delay);
            sequence.Append(DOVirtual.Float(1, 0, 0.3f, v => AnimatedScale = v).SetEase(Ease.OutBack));
            sequence.AppendCallback(() =>
            {
                _runningTween = null;
                Refresh();
                callback?.Invoke();
            });
            _runningTween = sequence;
        }

        private void Refresh()
        {
            Reward = GlobalServiceContainer.Get<GamePurchaseService>()
                .PurchaseProcessor.GetActualReward(ProductId,
                    GlobalServiceContainer.Get<GamePurchaseService>().FindProduct(ProductId).Rewards);
            SetupReward(Reward);
        }
    }
}
