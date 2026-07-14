using System;

using UnityEngine;
using UnityEngine.UI;


namespace DynamicDebugMenu.Elements
{
    public class DDMCheckbox : MonoBehaviour
    {
        [SerializeField]
        private Toggle _toggle;
        
        [SerializeField]
        private Text _label;

        private Func<bool> _getCurrent;
        private Action<bool> _onChange;

        public static DDMCheckbox Create(DDMContext context, string label, Func<bool> valueFunc, Action<bool> onChange)
        {
            DDMCheckbox component = Create(context, label, valueFunc(), onChange);
            component._getCurrent = valueFunc;
            return component;
        }
        
        public static DDMCheckbox Create(DDMContext context, string label, bool initValue, Action<bool> onChange)
        {
            GameObject prefab = Instantiate(context.Config.CheckboxPrefab, context.Parent);
            DDMCheckbox component = prefab.GetComponent<DDMCheckbox>();

            component._label.text = label;
            component._onChange = onChange;
            component._toggle.isOn = initValue;
            return component;
        }

        private void Start()
        {
            _toggle.onValueChanged.AddListener(OnValueChanged);
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
    }
}