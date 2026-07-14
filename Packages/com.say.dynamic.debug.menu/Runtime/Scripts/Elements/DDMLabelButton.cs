using System;

using UnityEngine;
using UnityEngine.UI;


namespace DynamicDebugMenu.Elements
{
    public class DDMLabelButton : MonoBehaviour
    {
        [SerializeField]
        private Text _label;
        
        [SerializeField]
        private Button _button;
        
        [SerializeField]
        private Text _buttonLabel;

        private Action _onClick;

        public string Label { get => _label.text; set => _label.text = value; }

        public static DDMLabelButton Create(DDMContext context, string label, string buttonLabel, Action onClick)
        {
            GameObject prefab = Instantiate(context.Config.LabelButtonPrefab, context.Parent);
            DDMLabelButton component = prefab.GetComponent<DDMLabelButton>();

            component._label.text = label;
            component._buttonLabel.text = buttonLabel;
            component._onClick = onClick;
            return component;
        }

        private void Start()
        {
            _button.onClick.AddListener(OnClick);
        }

        private void OnClick()
        {
            _onClick.Invoke();
        }
    }
}