using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace SGS.Services.Haptic
{
    [RequireComponent(typeof(Selectable))]
    public class ButtonHaptic : MonoBehaviour, IPointerDownHandler
    {
        [SerializeField]
        private HapticEffectType _haptic;
        private Selectable _button;

        private void Awake()
        {
            _button = GetComponent<Selectable>();
        }

        public void OnPointerDown(PointerEventData eventData)
        {
            if (_button.interactable)
            {
                GlobalService.HapticService.Play(_haptic);
            }
        }
    }
}