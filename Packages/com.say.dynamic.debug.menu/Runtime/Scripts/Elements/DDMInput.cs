using System;

using UnityEngine;
using UnityEngine.UI;


namespace DynamicDebugMenu.Elements
{
    public class DDMInput : MonoBehaviour
    {
        [SerializeField]
        private InputField _input;
        
        [SerializeField]
        private Text _label;

        private Action<string> _onChangeText;
        private Action<int> _onChangeInt;

        public string InputText { get => _input.text; set => _input.SetTextWithoutNotify(value); }

        public static DDMInput Create(DDMContext context, string label, InputField.ContentType contentType, Func<string> initFunc, Action<string> onChange)
        {
            return Create(context, label, contentType, initFunc.Invoke(), onChange);
        }
        
        public static DDMInput Create(DDMContext context, string label, InputField.ContentType contentType, string initText, Action<string> onChange)
        {
            GameObject prefab = Instantiate(context.Config.InputPrefab, context.Parent);
            DDMInput component = prefab.GetComponent<DDMInput>();

            component._label.text = label;
            component._onChangeText = onChange;
            component._input.text = initText;
            component._input.contentType = contentType;
            return component;
        }
        
        public static DDMInput Create(DDMContext context, string label, int initValue, Action<int> onChange)
        {
            GameObject prefab = Instantiate(context.Config.InputPrefab, context.Parent);
            DDMInput component = prefab.GetComponent<DDMInput>();

            component._label.text = label;
            component._onChangeInt = onChange;
            component._input.text = initValue.ToString();
            component._input.contentType = InputField.ContentType.IntegerNumber;
            return component;
        }

        private void Start()
        {
            _input.onValueChanged.AddListener(OnValueChanged);
        }

        private void OnValueChanged(string newText)
        {
            if (_onChangeText != null)
            {
                _onChangeText(newText);
            }

            if (_onChangeInt != null)
            {
                if (int.TryParse(newText, out int value))
                {
                    _onChangeInt(value);
                }
            }
        }
        
    }
}