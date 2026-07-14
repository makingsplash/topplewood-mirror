using System;
using System.Globalization;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;


namespace DynamicDebugMenu.Elements
{
    public class DDMSlider : MonoBehaviour
    {
        [SerializeField]
        private Text _label;

        [SerializeField]
        private Slider _slider;

        [SerializeField]
        private Button _prevButton;

        [SerializeField]
        private Button _nextButton;

        [SerializeField]
        private InputField _input;

        private Action<int> _onChangeInt;
        private Action<float> _onChangeFloat;
        private float _buttonDelta;

        public int IntValue
        {
            get => Mathf.RoundToInt(_slider.value);
            set => _slider.value = value;
        }

        public float FloatValue
        {
            get => _slider.value;
            set => _slider.value = value;
        }
        public static DDMSlider Create(DDMContext context, string label, int initValue, int min, int max, int buttonDelta, Action<int> onChange)
        {
            GameObject prefab = Instantiate(context.Config.SliderPrefab, context.Parent);
            DDMSlider component = prefab.GetComponent<DDMSlider>();

            component._label.text = label;
            component._onChangeInt = onChange;

            component._slider.wholeNumbers = true;
            component._slider.minValue = min;
            component._slider.maxValue = max;
            component._slider.value = initValue;
            component._buttonDelta = buttonDelta;
            component._input.contentType = InputField.ContentType.IntegerNumber;
            component._input.SetTextWithoutNotify(initValue.ToString());
            return component;
        }
        public static DDMSlider Create(DDMContext context, string label, float initValue, float min, float max, float buttonDelta, Action<float> onChange)
        {
            GameObject inputObject = Instantiate(context.Config.SliderPrefab, context.Parent);
            DDMSlider slider = inputObject.GetComponent<DDMSlider>();

            slider._label.text = label;
            slider._onChangeFloat = onChange;

            slider._slider.wholeNumbers = false;
            slider._slider.minValue = min;
            slider._slider.maxValue = max;
            slider._slider.value = initValue;
            slider._buttonDelta = buttonDelta;
            slider._input.contentType = InputField.ContentType.DecimalNumber;
            slider._input.SetTextWithoutNotify(initValue.ToString(CultureInfo.InvariantCulture));
            return slider;
        }

        private void Start()
        {
            _slider.onValueChanged.AddListener(OnSliderValue);
            _input.onValueChanged.AddListener(OnInputValue);
            _prevButton.onClick.AddListener(() => ChangeValue(-_buttonDelta));
            _nextButton.onClick.AddListener(() => ChangeValue(_buttonDelta));
        }


        private void ChangeValue(float delta)
        {
            var newValue = Mathf.Clamp(_slider.value + delta, _slider.minValue, _slider.maxValue);
            
            // ReSharper disable once CompareOfFloatsByEqualityOperator
            if(newValue != _slider.value)
            {
                _slider.value = newValue;
            }
        }

        private void OnSliderValue(float value)
        {
            _onChangeFloat?.Invoke(value);
            _onChangeInt?.Invoke(Mathf.RoundToInt(value));

            if (_input.contentType == InputField.ContentType.IntegerNumber)
            {
                _input.SetTextWithoutNotify(Mathf.RoundToInt(value).ToString());
            }
            else
            {
                _input.SetTextWithoutNotify(value.ToString("F3"));
            }
        }

        private void OnInputValue(string strValue)
        {
            var cleanValue = new string(strValue.Where(char.IsDigit).ToArray());
            if (!float.TryParse(cleanValue, out var value))
            {
                return;
            }
            value = Mathf.Clamp(value, _slider.minValue, _slider.maxValue);
            _input.SetTextWithoutNotify(value.ToString(CultureInfo.InvariantCulture));
            _slider.value = value;
        }
    }
}