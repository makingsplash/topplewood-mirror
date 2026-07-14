using System.Collections.Generic;
using Common.Extensions;
using Cysharp.Threading.Tasks;
using DataStorage;
using DG.Tweening;
using UnityEngine;
using UnityEngine.Scripting.APIUpdating;


namespace Ui.Components.Animations
{
    [MovedFrom(sourceNamespace: "UI.Components.Animations")]
    public class DefaultShowHidePopupAnimation : UIShowHideAnimation
    {
        [SerializeField] private Transform _content;
        [SerializeField] private CanvasGroup _contentCanvasGroup;
        [SerializeField] private CanvasGroup _backgroundCanvasGroup;

        public override async UniTask PlayShowAnimationAsync()
        {
            List<UniTask> tasks = new List<UniTask>();
            var config = Data.Configs.Animations.PopupAnimations;
            var linkTarget = gameObject;

            tasks.Add(_content.DOScale(config.ScaleInEndValue, config.ScaleInDuration).SetUpdate(true).SetDelay(config.ScaleInDelay)
                .SetEase(config.ScaleInCurve)
                .SetLink(linkTarget, LinkBehaviour.KillOnDestroy)
                .SetAutoKill().ToUniTask());
            tasks.Add(DOTween.To(() => _contentCanvasGroup.alpha, x => _contentCanvasGroup.alpha = x, config.FadeInEndValue, config.FadeInDuration)
                .SetUpdate(true).SetDelay(config.FadeInDelay).SetEase(config.FadeInCurve)
                .SetLink(linkTarget, LinkBehaviour.KillOnDestroy)
                .SetAutoKill().ToUniTask());

            if (_backgroundCanvasGroup)
            {
                tasks.Add(DOTween.To(() => _backgroundCanvasGroup.alpha, x => _backgroundCanvasGroup.alpha = x, config.FadeInEndValue, config.FadeInDuration)
                    .SetUpdate(true).SetDelay(config.FadeInDelay).SetEase(config.FadeInCurve)
                    .SetLink(linkTarget, LinkBehaviour.KillOnDestroy)
                    .SetAutoKill().ToUniTask());
            }

            await tasks.WhenAll();
        }

        public override async UniTask PlayHideAnimationAsync()
        {
            List<UniTask> tasks = new List<UniTask>();
            var config = Data.Configs.Animations.PopupAnimations;
            var linkTarget = gameObject;

            tasks.Add(_content.DOScale(config.ScaleOutEndValue, config.ScaleOutDuration).SetUpdate(true).SetDelay(config.ScaleOutDelay)
                .SetEase(config.ScaleOutCurve)
                .SetLink(linkTarget, LinkBehaviour.KillOnDestroy)
                .SetAutoKill().ToUniTask());
            tasks.Add(DOTween.To(() => _contentCanvasGroup.alpha, x => _contentCanvasGroup.alpha = x, config.FadeOutEndValue, config.FadeOutDuration)
                .SetUpdate(true).SetDelay(config.FadeOutDelay).SetEase(config.FadeOutCurve)
                .SetLink(linkTarget, LinkBehaviour.KillOnDestroy)
                .SetAutoKill().ToUniTask());

            if (_backgroundCanvasGroup)
            {
                tasks.Add(DOTween.To(() => _backgroundCanvasGroup.alpha, x => _backgroundCanvasGroup.alpha = x, config.FadeOutEndValue, config.FadeOutDuration)
                    .SetUpdate(true).SetDelay(config.FadeOutDelay).SetEase(config.FadeOutCurve)
                    .SetLink(linkTarget, LinkBehaviour.KillOnDestroy)
                    .SetAutoKill().ToUniTask());
            }

            await tasks.WhenAll();
        }

        public override void ResetToShown()
        {
            var config = Data.Configs.Animations.PopupAnimations;
            _content.localScale = Vector3.one * config.ScaleInEndValue;
            _contentCanvasGroup.alpha = config.FadeInEndValue;
            if (_backgroundCanvasGroup) _backgroundCanvasGroup.alpha = config.FadeInEndValue;
        }

        public override void ResetToHidden()
        {
            var config = Data.Configs.Animations.PopupAnimations;
            _content.localScale = Vector3.one * config.ScaleOutEndValue;
            _contentCanvasGroup.alpha = config.FadeOutEndValue;
            if (_backgroundCanvasGroup) _backgroundCanvasGroup.alpha = config.FadeOutEndValue;
        }
    }
}
