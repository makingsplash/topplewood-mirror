using UnityEngine;

namespace SGS.Store.Ui
{
    public class RotateAnimator : MonoBehaviour
    {
        [SerializeField]
        private float _degreesPerSecond = 15;

        private float _rotation = 0;

        void Update()
        {
            _rotation += _degreesPerSecond * Time.deltaTime;
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
    }
}