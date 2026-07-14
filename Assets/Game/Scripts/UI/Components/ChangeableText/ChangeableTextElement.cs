using Cysharp.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.Scripting.APIUpdating;

namespace Ui.Components.ChangeableText
{
    [MovedFrom(sourceNamespace: "UI.Components.ChangeableText")]
    public class ChangeableTextElement : UIElement
    {
        [SerializeField] private TMP_Text _timerText;

        [SerializeField] protected TextElementAnimations _timerAnimations;


        public void SetStringValue(string value)
        {
#if UNITY_EDITOR
            if (!Application.isPlaying)
            {
                SetValue(value);
            }
#endif
            
            if (_timerAnimations != null)
            {
                _timerAnimations.UpdateValueAnimationAsync(() => SetValue(value)).Forget();
            }
            else
            {
                SetValue(value);
            }
        }

        protected void SetValue(string value)
        {
            if (value == null)
            {
                _timerText.text = string.Empty;
            }
            else
            {
                _timerText.text = value;
            }
        }
    }
}
