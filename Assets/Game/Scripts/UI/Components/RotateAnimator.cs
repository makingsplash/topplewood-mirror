using UnityEngine;
using UnityEngine.Scripting.APIUpdating;

namespace Ui.Components
{
    [MovedFrom(sourceNamespace: "UI.Components")]
    public class RotateAnimator : MonoBehaviour
    {
        [SerializeField]
        private float _degreesPerSecond = 15;

        [SerializeField]
        private bool _ignoreTimeScale = false;

        private float _rotation;

        private void Update()
        {
            _rotation += _degreesPerSecond * GetDelta();
            
            if (_rotation < -360f)
            {
                _rotation += 360f;
            }
            else if (_rotation > 360f)
            {
                _rotation -= 360f;
            }
            
            transform.rotation = Quaternion.AngleAxis(_rotation, Vector3.forward);
        }

        private float GetDelta()
        {
            return _ignoreTimeScale ? Time.unscaledDeltaTime : Time.deltaTime;
        }
    }
}
