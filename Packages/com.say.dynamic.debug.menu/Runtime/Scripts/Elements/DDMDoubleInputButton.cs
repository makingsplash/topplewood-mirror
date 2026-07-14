using System;
using UnityEngine;
using UnityEngine.UI;

namespace DynamicDebugMenu.Elements
{
    public class DDMDoubleInputButton : MonoBehaviour
    {
        [SerializeField]
        private InputField _input1;
        [SerializeField]
        private InputField _input2;
        
        [SerializeField]
        private Button _button;
        
        [SerializeField]
        private Text _label;
        
        [SerializeField]
        private Text _buttonLabel;

        private Action<string, string> _onClick;
        
        public string InputText1 { get => _input1.text; set => _input1.SetTextWithoutNotify(value); }

        public string InputText2 { get => _input2.text; set => _input2.SetTextWithoutNotify(value); }

        public static DDMDoubleInputButton Create(DDMContext context, string label, string buttonLabel, InputField.ContentType contentType, Func<Tuple<string, string>> initFunc, Action<string, string> onClick)
        {
            return Create(context, label, buttonLabel, contentType, initFunc.Invoke(), onClick);
        }
        
        public static DDMDoubleInputButton Create(DDMContext context, string label, string buttonLabel, InputField.ContentType contentType, Tuple<string, string> initText, Action<string, string> onClick)
        {
            GameObject prefab = Instantiate(context.Config.DoubleInputButtonPrefab, context.Parent);
            DDMDoubleInputButton component = prefab.GetComponent<DDMDoubleInputButton>();

            component._label.text = label;
            component._buttonLabel.text = buttonLabel;
            component._onClick = onClick;
            component._input1.text = initText.Item1;
            component._input1.contentType = contentType;
            component._input2.text = initText.Item2;
            component._input1.contentType = contentType;
            return component;
        }

        private void Start()
        {
            _button.onClick.AddListener(OnClick);
        }

        private void OnClick()
        {
            _onClick.Invoke(_input1.text, _input2.text);
        }
    }
}