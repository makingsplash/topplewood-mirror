using System;
using System.Collections.Generic;
using DG.Tweening;
using SGS.Services.Containers;
using SGS.Services.Ui;
using SGS.Store.Purchases;
using SGS.Store.Purchases.Ui.Components;
using UnityEngine;

namespace SGS.Store.Ui
{
    public class StoreBlockController: StoreBlockBaseController, ISubpanelController
    {
        [SerializeField]
        private CanvasGroup _canvasGroup;

        protected readonly List<Tween> RunningTweens = new();

        void ISubpanelController.Init()
        {
        }

        void ISubpanelController.DeInit()
        {
            foreach (var tween in RunningTweens)
            {
                tween.Kill();
            }
        }

        public void SetVisible(bool value)
        {
        }

        public void ShowAnimation(Action onComplete)
        {
            foreach (var tween in RunningTweens)
            {
                tween.Kill();
            }

            var config = GlobalServiceContainer.Get<GamePurchaseService>().VisualConfig.AnimationSettings;
            
            LayoutCards();
            var sequence = DOTween.Sequence();
            sequence.SetLink(gameObject, LinkBehaviour.KillOnDestroy);
            var sizeDelta = CardsContainer.sizeDelta;
            Enabled = true;
            sequence.AppendCallback(() => Cards.ForEach(c => c.Enabled = false));
            sequence.Append( DOVirtual.Float(0f, sizeDelta.y, config.BlockShrinkDuration, h =>
            {
                sizeDelta.y = h;
                CardsContainer.sizeDelta = sizeDelta;
            }).SetEase(Ease.OutBack));
            sequence.AppendCallback(() =>
            {
                _canvasGroup.alpha = 0f;
                Cards.ForEach(c => c.Enabled = true);
            });
            sequence.Append(DOTween.To(() => _canvasGroup.alpha, x => _canvasGroup.alpha = x, 1f, config.CardHideDuration));
            sequence.AppendCallback(() =>
            {
                RunningTweens.Remove(sequence);
                onComplete?.Invoke();
            });

            RunningTweens.Add(sequence);
        }

        public void HideAnimation(Action onComplete)
        {
            foreach (var tween in RunningTweens)
            {
                tween.Kill();
            }

            var config = GlobalServiceContainer.Get<GamePurchaseService>().VisualConfig.AnimationSettings;
            
            var sequence = DOTween.Sequence();
            sequence.SetLink(gameObject, LinkBehaviour.KillOnDestroy);
            sequence.AppendInterval(config.CardClickDuration);
            sequence.Append(DOTween.To(() => _canvasGroup.alpha, x => _canvasGroup.alpha = x, 0f, config.CardHideDuration));
            sequence.AppendCallback(() => Cards.ForEach(c => c.Enabled = false));

            var sizeDelta =  ((RectTransform)transform).sizeDelta;
            sequence.Append( DOVirtual.Float(sizeDelta.y, 0f, config.BlockShrinkDuration, h =>
            {
                sizeDelta.y = h;
                ((RectTransform)transform).sizeDelta = sizeDelta;
            }).SetEase(Ease.OutBack));

            sequence.AppendCallback(() =>
            {
                Enabled = false;
                RunningTweens.Remove(sequence);
                onComplete?.Invoke();
                LayoutCards();
            });

            RunningTweens.Add(sequence);
        }
    }
}
