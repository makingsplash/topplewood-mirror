using System;
using System.Collections.Generic;

using UnityEngine;
using UnityEngine.UI;


namespace DynamicDebugMenu.Elements
{
    public class DDMDropdownSave : MonoBehaviour
    {
        [SerializeField]
        private Dropdown _dropdown;
        
        [SerializeField]
        private Text _label;
        
        [SerializeField]
        private Toggle _toggle;

        private Action<int> _onChange;
        private Action<bool> _onSave;

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

        public static DDMDropdownSave Create(DDMContext context, string label, Func<List<string>> optionsFunc, Func<int> initFunc, Action<int> onChange, bool initSave, Action<bool> onSave)
        {
            return Create(context, label, optionsFunc.Invoke(), initFunc.Invoke(), onChange, initSave, onSave);
        }

        public static DDMDropdownSave Create(DDMContext context, string label, List<string> optionsList, int selectedValue, Action<int> onChange, bool initSave, Action<bool> onSave)
        {
            GameObject prefab = Instantiate(context.Config.DropdownSavePrefab, context.Parent);
            DDMDropdownSave component = prefab.GetComponent<DDMDropdownSave>();

            component._label.text = label;
            component._onChange = onChange;
            component._onSave = onSave;
            component._dropdown.ClearOptions();
            component._dropdown.AddOptions(optionsList);
            component._dropdown.value = selectedValue;
            component._toggle.isOn = initSave;
            
            return component;
        }

        private void Start()
        {
            _dropdown.onValueChanged.AddListener(OnValueChanged);
            _toggle.onValueChanged.AddListener(OnSaveChanged);
        }

        private void OnSaveChanged(bool isSave)
        {
            _onSave.Invoke(isSave);
        }

        private void OnValueChanged(int selectedValue)
        {
            _onChange.Invoke(selectedValue);
        }
    }
}