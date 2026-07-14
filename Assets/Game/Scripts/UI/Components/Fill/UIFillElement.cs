using UnityEngine;
using UnityEngine.Scripting.APIUpdating;

namespace Ui.Components.Fill
{
    [MovedFrom(sourceNamespace: "UI.Components.Fill")]
    public sealed class UIFillElement : MonoBehaviour
    {
        [Range(0f, 1f)]
        [SerializeField] private float _fill = 1f;
        [SerializeField] private RectTransform _fillRect;

        public void Fill(float percent)
        {
            _fill = percent;
            UpdateView();
        }

        private void UpdateView()
        {
            _fillRect.anchorMax = new Vector2(_fill, 1f);
        }

        private void OnValidate()
        {
            UpdateView();
        }
    }
}
