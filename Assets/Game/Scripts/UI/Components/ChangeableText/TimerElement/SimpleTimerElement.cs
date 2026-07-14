using System;
using Cysharp.Threading.Tasks;
using Ui.Components.ChangeableText;
using UnityEngine.Scripting.APIUpdating;

namespace Ui.Components
{
    [MovedFrom(sourceNamespace: "UI.Components")]
    public class SimpleTimerElement : ChangeableTextElement
    {
        public void SetTimerValue(TimeSpan timeLeft)
        {
            if (_timerAnimations != null)
            {
                _timerAnimations.UpdateValueAnimationForTimersAsync(timeLeft, ()=>SetValue(GetTimerString(timeLeft))).Forget();
            }
            else
            {
                SetValue(GetTimerString(timeLeft));
            }
        }

        public void SetTimerValueImmediately(TimeSpan timeLeft)
        {
            SetValue(GetTimerString(timeLeft));
        }

        protected virtual string GetTimerString(TimeSpan span)
        {
            if (span.TotalSeconds > 0)
            {
                return $"{span.TotalSeconds}";
            }
            
            return "0";
        }
    }
}
