using TMPro;
using UnityEngine;

namespace Ui.Components
{
    public class ButtonCounterBadge : MonoBehaviour
    {
        [SerializeField] private TMP_Text _countText;

        [Tooltip("Optional format string. Use {0} for the count value, e.g. \"+{0}\" or \"×{0}\".")]
        [SerializeField] private string _format;

        public void Setup(int count)
        {
            gameObject.SetActive(count > 0);

            if (_countText == null)
                return;

            _countText.text = string.IsNullOrEmpty(_format)
                ? count.ToString()
                : string.Format(_format, count);
        }
    }
}
