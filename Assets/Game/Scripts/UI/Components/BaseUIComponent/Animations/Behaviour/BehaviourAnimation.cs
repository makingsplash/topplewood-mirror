using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.Scripting.APIUpdating;

namespace Ui.Components.Animations.Behaviour
{
    [MovedFrom(sourceNamespace: "UI.Components.Animations.Behaviour")]
    public abstract class BehaviourAnimation : UIShowHideAnimation
    {
        [SerializeField]
        private UIShowHideAnimation _showHideAnimation;

        protected abstract void ClearBehaviourAnimation();
        
        public override async UniTask PlayShowAnimationAsync()
        {
            ClearBehaviourAnimation();
            if (_showHideAnimation != null)
            {
                await _showHideAnimation.PlayShowAnimationAsync();
            }
        }

        public override async UniTask PlayHideAnimationAsync()
        {
            ClearBehaviourAnimation();
            if (_showHideAnimation != null)
            {
                await _showHideAnimation.PlayHideAnimationAsync();
            }
        }

        public override void ResetToShown()
        {
            ClearBehaviourAnimation();
            if (_showHideAnimation != null)
            {
                _showHideAnimation.ResetToShown();
            }
        }

        public override void ResetToHidden()
        {
            ClearBehaviourAnimation();
            if (_showHideAnimation != null)
            {
                _showHideAnimation.ResetToHidden();
            }
        }
    }
}
