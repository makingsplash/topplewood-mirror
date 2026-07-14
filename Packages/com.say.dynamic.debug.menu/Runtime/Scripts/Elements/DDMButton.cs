using System;

using UnityEngine;
using UnityEngine.UI;


namespace DynamicDebugMenu.Elements
{
    public class DDMButton : MonoBehaviour
    {
        [SerializeField]
        private Button _button;
        
        [SerializeField]
        private Text _label;

        public string Label
        {
            get => _label.text;
            set => _label.text = value;
        }

        private Action _onClick;

        public static DDMButton Create(DDMContext context, string label, Action onClick)
        {
            GameObject prefab = Instantiate(context.Config.ButtonPrefab, context.Parent);
            DDMButton component = prefab.GetComponent<DDMButton>();

            component._label.text = label;
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