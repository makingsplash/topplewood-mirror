using UnityEngine;
using UnityEngine.Scripting.APIUpdating;

namespace Ui.Components
{
    [MovedFrom(sourceNamespace: "UI.Components")]
    public class AnimatorInitialUpdate : MonoBehaviour
    {
        [SerializeField]
        private Animator _animator;

        private void Start()
        {
            _animator?.Update(0f);
        }
#if UNITY_EDITOR
        
        private void OnValidate()
        {
            _animator ??= GetComponent<Animator>();
        }
#endif        
    }
}
