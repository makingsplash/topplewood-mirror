using System;
using Cysharp.Threading.Tasks;
using SGS.Services;
using UnityEngine;
using UnityEngine.UI;
using Game.Services.LogService;
using TMPro;
using UnityEngine.Scripting.APIUpdating;

namespace Ui.Components.SliderToggle
{
    [MovedFrom(sourceNamespace: "UI.Components.SliderToggle")]
    public sealed class SliderToggle : UIElement
    {
        [SerializeField] private RectTransform _handle;
        [SerializeField] private bool _value;

        [SerializeField] private GameObject _enabledView;
        [SerializeField] private GameObject _disabledView;

        [SerializeField] private Button _toggleButton;
        [SerializeField] private TMP_Text _toggleText;
        [SerializeField] private Material _toggleTextMaterial;
        [SerializeField] private string _toggleOnLocaleId;
        [SerializeField] private string _toggleOffLocaleId;


        public event Action<bool> OnChangedValue;

        protected override UniTask InitializeProcessAsync()
        {
            _toggleButton.onClick.AddListener(OnClickToggle);
            return base.InitializeProcessAsync();
        }

        protected override UniTask BeforeShowAsync()
        {
            ApplyToggleTextMaterial();
            UpdateToggleText(_value);
            return base.BeforeShowAsync();
        }

        private void ApplyToggleTextMaterial()
        {
            if (_toggleTextMaterial == null || _toggleText == null) return;

            var mat = _toggleText.fontMaterial;
            mat.CopyPropertiesFromMaterial(_toggleTextMaterial);
            mat.SetTexture(ShaderUtilities.ID_MainTex, _toggleText.font.atlasTexture);
            _toggleText.UpdateMeshPadding();
        }

        private void OnClickToggle()
        {
            SetValue(!_value);
        }

        public void SetValue(bool value, bool triggerCallback = true, bool immediately = true)
        {
            if (_value == value)
            {
                return;
            }

            _value = value;
            UpdateHandlePosition(_value ? 1f : 0f);
            UpdateView(_value);

            if (triggerCallback)
            {
                InvokeOnChangedValue();
            }
        }

        private void UpdateView(bool enabled)
        {
            _enabledView.SetActive(enabled);
            _disabledView.SetActive(!enabled);
            UpdateToggleText(enabled);
        }

        private void UpdateToggleText(bool enabled)
        {
            if (_toggleText == null) return;

            var localeId = enabled ? _toggleOnLocaleId : _toggleOffLocaleId;
            if (string.IsNullOrEmpty(localeId)) return;

            _toggleText.text = GlobalService.Localization.GetLocalizedString(localeId);
        }

        private void InvokeOnChangedValue()
        {
            OnChangedValue?.Invoke(_value);
        }

        private void UpdateHandlePosition(float normalizedPosition)
        {
            if (_handle == null || _handle.parent == null)
            {
                Debug.unityLogger.Log(LogType.Warning, $"[{LogCategory.UI}] RectTransform или его родитель равен null");
                return;
            }

            RectTransform parentRect = _handle.parent as RectTransform;

            float parentWidth = parentRect.rect.width;

            float localX = Mathf.Lerp(0f, parentWidth, normalizedPosition);

            Vector2 anchoredPosition = _handle.anchoredPosition;
            anchoredPosition.x = localX;
            _handle.anchoredPosition = anchoredPosition;
        }

        private void OnValidate()
        {
            if (_handle != null)
            {
                UpdateHandlePosition(_value ? 1f : 0f);
            }

            if (_enabledView != null && _disabledView != null)
            {
                _enabledView.SetActive(_value);
                _disabledView.SetActive(!_value);
            }
        }
    }
}
