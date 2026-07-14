using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace DynamicDebugMenu.Elements
{
    public class DDMDropdownButton: MonoBehaviour
    {
        [SerializeField]
        private Dropdown _dropdown;
        
        [SerializeField]
        private Text _label;
        
        [SerializeField]
        private Button _button;
        
        [SerializeField]
        private Text _buttonLabel;

        private const string _defaultButtonLabel = "Go"; 
        private Action<string> _onClick;
        private string _selectedValue;

        public void SetOptions(List<string> options, int selectedIndex)
        {
            _dropdown.ClearOptions();
            _dropdown.AddOptions(options);
            _dropdown.value = selectedIndex;
        }
        
        public int SelectedIndex
        {
            get => _dropdown.value;
            set => _dropdown.value = value;
        }

        public static DDMDropdownButton Create(DDMContext context, string label, Func<List<string>> optionsFunc, Action<string> onClick)
        {
            return Create(context, label, _defaultButtonLabel, optionsFunc.Invoke(), onClick);
        }

        public static DDMDropdownButton Create(DDMContext context, string label, List<string> optionsList, Action<string> onClick)
        {
            return Create(context, label, _defaultButtonLabel, optionsList, onClick);
        }

        public static DDMDropdownButton Create(DDMContext context, string label, string buttonLabel, List<string> optionsList, Action<string> onClick)
        {
            GameObject prefab = Instantiate(context.Config.DropdownButton, context.Parent);
            DDMDropdownButton component = prefab.GetComponent<DDMDropdownButton>();

            component._label.text = label;
            component._buttonLabel.text = buttonLabel;
            component._dropdown.ClearOptions();
            component._dropdown.AddOptions(optionsList);
            component._dropdown.value = 0;
            component._selectedValue = component._dropdown.options[component._dropdown.value ].text;

            component._onClick = onClick;

            return component;
        }

        private void Start()
        {
            _button.onClick.AddListener(OnClick);
            _dropdown.onValueChanged.AddListener(OnValueChanged);
        }
        
        private void OnClick()
        {
            _onClick.Invoke(_selectedValue);
        }

        private void OnValueChanged(int selectedValue)
        {
            _selectedValue = _dropdown.options[selectedValue].text;
        }
    }
}