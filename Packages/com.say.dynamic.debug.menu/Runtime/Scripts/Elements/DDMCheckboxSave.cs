using System;

using UnityEngine;
using UnityEngine.UI;


namespace DynamicDebugMenu.Elements
{
    public class DDMCheckboxSave : MonoBehaviour
    {
        [SerializeField]
        private Toggle _toggle;
        
        [SerializeField]
        private Toggle _toggleSave;
        
        [SerializeField]
        private Text _label;

        private Func<bool> _getCurrent;
        private Action<bool> _onChange;
        private Action<bool> _onSave;

        public static DDMCheckboxSave Create(DDMContext context, string label, Func<bool> valueFunc, Action<bool> onChange, bool initSave, Action<bool> onSave)
        {
            DDMCheckboxSave component = Create(context, label, valueFunc.Invoke(), onChange, initSave, onSave);
            component._getCurrent = valueFunc;
            return component;
        }
        
        public static DDMCheckboxSave Create(DDMContext context, string label, bool initValue, Action<bool> onChange, bool initSave, Action<bool> onSave)
        {
            GameObject prefab = Instantiate(context.Config.CheckboxSavePrefab, context.Parent);
            DDMCheckboxSave component = prefab.GetComponent<DDMCheckboxSave>();

            component._label.text = label;
            component._onChange = onChange;
            component._toggle.isOn = initValue;
            component._onSave = onSave;
            component._toggleSave.isOn = initSave;
            return component;
        }

        private void Start()
        {
            _toggle.onValueChanged.AddListener(OnValueChanged);
            _toggleSave.onValueChanged.AddListener(OnSaveChanged);
        }

        private void Update()
        {
            if (_getCurrent == null)
            {
                return;
            }
            _toggle.SetIsOnWithoutNotify(_getCurrent());
        }
        
        private void OnValueChanged(bool value)
        {
            _onChange.Invoke(value);
        }
        
        private void OnSaveChanged(bool isSave)
        {
            _onSave.Invoke(isSave);
        }
    }
}