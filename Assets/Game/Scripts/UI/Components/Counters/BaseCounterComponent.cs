using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Scripting.APIUpdating;

namespace Ui.Components.Counters
{
    [MovedFrom(sourceNamespace: "UI.Components.Counters")]
    public class BaseCounterComponent : MonoBehaviour
    {
        [SerializeField] protected TMP_Text _countTxt;
        [SerializeField] private string _format;
        [SerializeField] private Button _button;
        [SerializeField] private GameObject _addRoot;
        [SerializeField] private bool _enableAddButton;

        protected Button Button => _button;

        protected virtual bool IsAddButtonAvailable() => true;

        protected void RefreshAddButton()
        {
            bool show = _enableAddButton && IsAddButtonAvailable();
            if (_addRoot != null)
                _addRoot.SetActive(show);
            if (_button != null)
                _button.interactable = show;
        }

        protected virtual void Awake()
        {
            RefreshAddButton();
        }

        public void SetCountText(int count)
        {
            if(string.IsNullOrEmpty(_format))
                _countTxt.text = count.ToString();
            else
                _countTxt.text = string.Format(_format, count.ToString());
        }
    }
}
