using System;

using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;


namespace DynamicDebugMenu.Elements
{
    public class DDMHoldButton : MonoBehaviour
    {
        [SerializeField]
        private Image _button;

        [SerializeField]
        private Image _fillImage;

        [SerializeField]
        private Text _label;

        private Action _onClick;
        private float _longPressDuration;

        public static void Create(DDMContext context, string label, Action onClick)
        {
            Create(context, label, 2f, onClick);
        }

        public static void Create(DDMContext context, string label, float longPressDuration, Action onClick)
        {
            GameObject prefab = Instantiate(context.Config.HoldButtonPrefab, context.Parent);
            DDMHoldButton component = prefab.GetComponent<DDMHoldButton>();

            component._label.text = label;
            component._onClick = onClick;
            component._longPressDuration = longPressDuration;
        }

        private void Start()
        {
            var button = _button.gameObject.AddComponent<LongPressButton>();
            button.OnClick += OnClick;
            button.ProgressImage = _fillImage;
            button.LongPressDuration = _longPressDuration;
        }

        private void OnClick()
        {
            _onClick.Invoke();
        }

    }
    public class LongPressButton : Selectable, IDragHandler
    {
        public event Action OnClick;
        public Image ProgressImage;
        public float LongPressDuration;
        
        private bool _pressed;
        private float _pressedTimeLeft;

        void Update()
        {
            if (!IsPressed())
            {
                _pressed = false;
                _pressedTimeLeft = 0;
                ProgressImage.fillAmount = 0f;
                return;
            }

            if (_pressedTimeLeft < 0f)
            {
                return;
            }

            if (!_pressed)
            {
                _pressed = true;
                _pressedTimeLeft = LongPressDuration;
            }

            ProgressImage.fillAmount = (LongPressDuration - _pressedTimeLeft) / LongPressDuration;
            _pressedTimeLeft -= Time.deltaTime;
            if (_pressedTimeLeft < 0f)
            {
                _pressed = false;
                ProgressImage.fillAmount = 0f;
                OnClick?.Invoke();
            }
        }

        public void OnDrag(PointerEventData eventData)
        {
            // Need to disable scroll list of Debug Menu while holding button
        }
    }
}