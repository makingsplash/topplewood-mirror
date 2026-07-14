using SGS.Data;
using UnityEngine;
using UnityEngine.EventSystems;

namespace SGS.Services.DebugTools.Ui
{
    public class DebugMenuButtonUi : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler, IPointerClickHandler
    {
        [SerializeField] private RectTransform _contentRectTransform;
        [SerializeField] private RectTransform _buttonTransform;
        
        private bool _startDrag;
        private Vector2 _prevTouchPos;

        private void Awake()
        {
            _contentRectTransform.gameObject.SetActive(false);
            SayData.Debug.ShowMenuButton.OnChanged += RefreshButton;
            
            RefreshButton(SayData.Debug.ShowMenuButton.Value);
        }

        private void OnDestroy()
        {
            SayData.Debug.ShowMenuButton.OnChanged -= RefreshButton;
        }

        private void RefreshButton(bool value)
        {
            if (value)
            {
                ShowButton();
            }
            else
            {
                HideButton();
            }
        }

        private void ShowButton()
        {
            _contentRectTransform.gameObject.SetActive(true);
            _buttonTransform.position = new Vector3(SayData.Debug.MenuButtonX.Value, SayData.Debug.MenuButtonY.Value);
        }

        private void HideButton()
        {
            _contentRectTransform.gameObject.SetActive(false);
        }

        public void OnBeginDrag(PointerEventData eventData)
        {
            _startDrag = false;
            if (!SayData.Debug.DebugMenuShown.Value)
            {
                _startDrag = true;
            }
        }

        public void OnDrag(PointerEventData eventData)
        {
            if (_startDrag)
            {
                _buttonTransform.position = eventData.position;
            }
        }

        public void OnEndDrag(PointerEventData eventData)
        {
            if (_startDrag)
            {
                _buttonTransform.position = eventData.position;
                SayData.Debug.MenuButtonX.Value = (int)eventData.position.x;
                SayData.Debug.MenuButtonY.Value = (int)eventData.position.y;
                _startDrag = false;
            }
        }

        public void OnPointerClick(PointerEventData eventData)
        {
            if (!_startDrag)
            {
                SayData.Debug.DebugMenuShown.Value = true;
            }
        }
    }
}
