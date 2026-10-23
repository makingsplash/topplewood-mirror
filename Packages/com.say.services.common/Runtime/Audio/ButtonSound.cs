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

        // TASK-667: settable so the UI compiler's `Sound` behaviour and code-built HUD buttons can assign it.
        public string SoundId
        {
            get => _soundId;
            set => _soundId = value;
        }

        private void Awake()
        {
            _button = GetComponent<Selectable>();
        }

        public void OnPointerDown(PointerEventData eventData)
        {
            if (_button == null || !_button.interactable || string.IsNullOrEmpty(_soundId))
            {
                return;
            }

            // No service (a scene without the LoadingGame bootstrap, e.g. bringup) → silent, not a NullReferenceException.
            AudioService audio = GlobalService.AudioService;
            if (audio != null)
            {
                audio.Play(_soundId).Forget();
            }
        }
    }
}
