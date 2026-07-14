using System;
using System.Collections.Generic;

using UnityEngine;
using UnityEngine.UI;


namespace DynamicDebugMenu.Elements
{
    public class DDMDropdown : MonoBehaviour
    {
        [SerializeField]
        private Dropdown _dropdown;
        
        [SerializeField]
        private Text _label;

        private Action<int> _onChange;

        public void SetOptions(List<string> options, int selectedIndex)
        {
            _dropdown.ClearOptions();
            _dropdown.AddOptions(options);
            _dropdown.value = selectedIndex;
        }
        
        public int SelectedIndex
        {
            get => _dropdown.value;
            set => _dropdown.SetValueWithoutNotify(value);
        }

        public static DDMDropdown Create(DDMContext context, string label, Func<List<string>> optionsFunc, Func<int> initFunc, Action<int> onChange)
        {
            return Create(context, label, optionsFunc(), initFunc(), onChange);
        }
        
        public static DDMDropdown Create(DDMContext context, string label, List<string> optionsList, int selectedIndex, Action<int> onChange)
        {
            GameObject prefab = Instantiate(context.Config.DropdownPrefab, context.Parent);
            DDMDropdown component = prefab.GetComponent<DDMDropdown>();

            component._label.text = label;
            component._onChange = onChange;
            component._dropdown.ClearOptions();
            component._dropdown.AddOptions(optionsList);
            component._dropdown.value = selectedIndex;

            return component;
        }

        private void Start()
        {
            _dropdown.onValueChanged.AddListener(OnValueChanged);
        }

        private void OnValueChanged(int selectedValue)
        {
            _onChange.Invoke(selectedValue);
        }
    }
}