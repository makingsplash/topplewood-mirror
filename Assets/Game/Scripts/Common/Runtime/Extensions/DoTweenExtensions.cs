using DG.Tweening;
using DG.Tweening.Core;
using DG.Tweening.Plugins.Options;
using TMPro;
using UnityEngine;

namespace Common.Extensions
{
    public static class DoTweenExtensions
    {
        public static TweenerCore<float, float, FloatOptions> DOText(this TMP_Text target, float startValue, float endValue, float duration, string format = "{0}")
        {
            return DOTween.To(() => startValue, x => target.text = string.Format(format, x), endValue, duration);
        }
        
        public static TweenerCore<int, int, NoOptions> DOText(this TMP_Text target, int startValue, int endValue, float duration, string format = "{0}")
        {
            return DOTween.To(() => startValue, x => target.text = string.Format(format, x), endValue, duration);
        }
        
        public static TweenerCore<float, float, FloatOptions> DOFade(this CanvasGroup canvasGroup, float endValue, float duration)
        {
            return DOTween.To(() => canvasGroup.alpha, x => canvasGroup.alpha = x, endValue, duration);
        }
    }
}
