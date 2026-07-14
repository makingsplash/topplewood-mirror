using Services;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Scripting.APIUpdating;

namespace Ui.Screens
{
    [MovedFrom(sourceNamespace: "UI.Screens")]
    public class PopupSound : MonoBehaviour
    {
        [SerializeField]
        private string _soundId;
        
        private Selectable _button;

        private void Awake()
        {
            GlobalService.AudioService.Play(_soundId);
        }
    }
}
