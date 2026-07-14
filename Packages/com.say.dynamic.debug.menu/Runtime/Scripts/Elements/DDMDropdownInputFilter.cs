using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace DynamicDebugMenu.Elements
{
    public class DDMDropdownInputFilter : MonoBehaviour
    {
        [SerializeField]
        private Dropdown _dropdown;
        
        [SerializeField]
        private InputField _input;
        
        [SerializeField]
        private Text _label;
        
        [SerializeField]
        private Button _button;
        
        [SerializeField]
        private Text _buttonLabel;

        private const string _defaultButtonLabel = "Go"; 
        private Action<string> _onClick;
        private string _selectedValue;
        private Func<string, List<string>> _filterFunc;

        public static DDMDropdownInputFilter Create(DDMContext context, string label, Func<string, List<string>> filterFunc, Action<string> onClick)
        {
            return Create(context, label, "", filterFunc, onClick);
        }
        
        public static DDMDropdownInputFilter Create(DDMContext context, string label,string initValue, Func<string, List<string>> filterFunc, Action<string> onClick)
        {
            return Create(context, label, _defaultButtonLabel, initValue, filterFunc, onClick);
        }

        public static DDMDropdownInputFilter Create(DDMContext context, string label, string buttonLabel, string initValue, Func<string, List<string>> filterFunc, Action<string> onClick)
        {
            GameObject prefab = Instantiate(context.Config.DropdownInputFilter, context.Parent);
            DDMDropdownInputFilter component = prefab.GetComponent<DDMDropdownInputFilter>();
            
            component._label.text = label;
            component._buttonLabel.text = buttonLabel;
            component._input.text = initValue;
            component._dropdown.ClearOptions();
            component._dropdown.AddOptions(filterFunc.Invoke(initValue));
            component._dropdown.value = 0;
            component._selectedValue = component._dropdown.options[component._dropdown.value ].text;

            component._filterFunc = filterFunc;
            component._onClick = onClick;
            return component;
        }

        private void Start()
        {
            _button.onClick.AddListener(OnClick);
            _dropdown.onValueChanged.AddListener(OnValueChanged);
            _input.onValueChanged.AddListener(OnFilterValueChanged);
        }

        private void OnDestroy()
        {
            _button.onClick.RemoveListener(OnClick);
            _dropdown.onValueChanged.RemoveListener(OnValueChanged);
            _input.onValueChanged.RemoveListener(OnFilterValueChanged);
        }

        private void OnClick()
        {
            _onClick.Invoke(_selectedValue);
        }

        private void OnFilterValueChanged(string value)
        {
            _dropdown.ClearOptions();
            _dropdown.AddOptions(_filterFunc.Invoke(_input.text));
        }
        
        private void OnValueChanged(int selectedValue)
        {
            _selectedValue = _dropdown.options[selectedValue].text;
        }
    }
}