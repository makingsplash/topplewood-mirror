using System;

using UnityEngine;
using UnityEngine.UI;


namespace DynamicDebugMenu.Elements
{
    public class DDMInputButton : MonoBehaviour
    {
        [SerializeField]
        private InputField _input;
        
        [SerializeField]
        private Button _button;
        
        [SerializeField]
        private Text _label;
        
        [SerializeField]
        private Text _buttonLabel;

        private Action<string> _onClickText;
        private Action<int> _onClickInt;

        public string InputText { get => _input.text; set => _input.SetTextWithoutNotify(value); }

        public static DDMInputButton Create(DDMContext context, string label, string buttonLabel, InputField.ContentType contentType, Func<string> initFunc, Action<string> onClick)
        {
            return Create(context, label, buttonLabel, contentType, initFunc.Invoke(), onClick);
        }
        
        public static DDMInputButton Create(DDMContext context, string label, string buttonLabel, InputField.ContentType contentType, string initText, Action<string> onClick)
        {
            GameObject prefab = Instantiate(context.Config.InputButtonPrefab, context.Parent);
            DDMInputButton component = prefab.GetComponent<DDMInputButton>();

            component._label.text = label;
            component._buttonLabel.text = buttonLabel;
            component._onClickText = onClick;
            component._input.text = initText;
            component._input.contentType = contentType;
            return component;
        }
        
        public static DDMInputButton Create(DDMContext context, string label, string buttonLabel, int initValue, Action<int> onClick)
        {
            GameObject prefab = Instantiate(context.Config.InputButtonPrefab, context.Parent);
            DDMInputButton component = prefab.GetComponent<DDMInputButton>();

            component._label.text = label;
            component._buttonLabel.text = buttonLabel;
            component._onClickInt = onClick;
            component._input.text = initValue.ToString();
            component._input.contentType = InputField.ContentType.IntegerNumber;
            return component;
        }
        
        private void Start()
        {
            _button.onClick.AddListener(OnClick);
        }

        private void OnClick()
        {
            if (_onClickText != null)
            {
                _onClickText(_input.text);
            }

            if (_onClickInt != null)
            {
                if (!int.TryParse(_input.text, out var value))
                {
                    value = 0;
                   _input.SetTextWithoutNotify(value.ToString());
                }

                _onClickInt(value);
            }
        }
    }
}