using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace SGS.Services.Audio
{
    [RequireComponent(typeof(Selectable))]
    public class ButtonSound : MonoBehaviour, IPointerDownHandler
    {
        [SerializeField]
        private string _soundId;
        
        private Selectable _button;

        private void Awake()
        {
            _button = GetComponent<Selectable>();
        }

        public void OnPointerDown(PointerEventData eventData)
        {
            if (_button.interactable)
            {
                GlobalService.AudioService.Play(_soundId).Forget();
            }
        }
    }
}