using UnityEngine;
using UnityEngine.Scripting.APIUpdating;

namespace Ui.Components.Message
{
    [DisallowMultipleComponent]
    [MovedFrom(sourceNamespace: "UI.Components.Message")]
    public sealed class FadeBackgroundHoleTarget : MonoBehaviour
    {
        private RectTransform _rectTransform;

        public RectTransform RectTransform => _rectTransform ? _rectTransform : (_rectTransform = (RectTransform)transform);
    }
}
