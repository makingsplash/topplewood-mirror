using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.Scripting.APIUpdating;

namespace Ui.Components
{
    [MovedFrom(sourceNamespace: "UI.Components")]
    public abstract class UIShowHideAnimation : MonoBehaviour
    {
        public abstract UniTask PlayShowAnimationAsync();
        public abstract UniTask PlayHideAnimationAsync();

        public abstract void ResetToShown();
        public abstract void ResetToHidden();
    }
}
