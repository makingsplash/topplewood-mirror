using System;
using DG.Tweening;
using SGS.Services.Containers;
using SGS.Store.Purchases;
using SGS.Store.Purchases.Ui.Components;
using UnityEngine;

namespace SGS.Store.Ui
{
    public class RibbonStoreBlockController: StoreBlockController
    {
        [SerializeField]
        protected Vector2 GridStart = new(0, 0);
        [SerializeField]
        protected float ColumnSize = 100f;
        [SerializeField]
        protected float BlockHeight;

        private bool _animated;

        public override void LayoutCards()
        {
            Vector2 nextPosition = GridStart;
            foreach (var baseCard in Cards)
            {
                var card = (StoreCardController)baseCard;
                if (!card.Enabled)
                {
                    continue;
                }
                card.SetPosition(nextPosition);
                nextPosition.x += ColumnSize * card.Width * card.AnimatedSize;
            }
            
            var rectTransform = (RectTransform)transform;

            Vector2 sizeDelta = rectTransform.sizeDelta;
            if (Cards.Count == 0)
            {
                sizeDelta.x = 0;
                sizeDelta.y = 0;
            }
            else
            {
                sizeDelta.x = nextPosition.x;
                sizeDelta.y = BlockHeight;
            }

            CardsContainer.sizeDelta = sizeDelta;
            rectTransform.sizeDelta = sizeDelta;
        }

        private void Update()
        {
            if (RunningTweens.Count > 0)
            {
                LayoutCards();
            }
        }

        public override void ChangeCardState(StoreCardBaseController baseCard, bool enabled, Action onComplete)
        {
            var card = (StoreCardController)baseCard;

            if (enabled)
            {
                card.Enabled = enabled;
                card.AnimatedSize = 1f;
                LayoutCards();
                onComplete?.Invoke();
                return;
            }

            var config = GlobalServiceContainer.Get<GamePurchaseService>().VisualConfig.AnimationSettings;

            var sequence = DOTween.Sequence();
            sequence.SetLink(gameObject, LinkBehaviour.KillOnDestroy);
            sequence.AppendInterval(config.CardClickDuration);
            sequence.Append(DOTween.To(() => card.CanvasGroup.alpha, x => card.CanvasGroup.alpha = x, 1f, config.CardHideDuration));
            sequence.Append(DOVirtual.Float(1f, 0f, config.BlockShrinkDuration, h =>
                {
                    card.AnimatedSize = h;
                })
                .SetEase(Ease.OutBack));

            sequence.AppendCallback(() => card.Enabled = false);
            sequence.AppendCallback(() =>
            {
                RunningTweens.Remove(sequence);
                onComplete?.Invoke();
            });

            RunningTweens.Add(sequence);
        }
    }
}
