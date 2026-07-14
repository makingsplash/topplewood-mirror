using System;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace DynamicDebugMenu.Elements
{
    public class DDMPage : MonoBehaviour
    {
        [SerializeField]
        private Button _backButton;
        
        [SerializeField]
        private Button _closeButton;
        
        [SerializeField]
        private Text _header;

        [SerializeField]
        private Transform _contentParent;

        private Action _onClose;
        private Action _backAction;
        private Action _closeAction;
        private Action<DDMContext> _contentGenerator;
        private DDMContext _context;

        public Action OnClose => _onClose;

        public static DDMPage Create(DDMContext context, string label, Action<DDMContext> contentGenerator, Action onClose,
            Action backAction, Action closeAction)
        {
            GameObject prefab = Instantiate(context.Config.PagePrefab, context.Parent);
            DDMPage component = prefab.GetComponent<DDMPage>();
            component._context = context;

            component._header.text = label;

            component._backAction = backAction;
            component._closeAction = closeAction;
            component._onClose = onClose;
            component._contentGenerator = contentGenerator;

            return component;
        }

        public void GenerateContent()
        {
            DDMContext context = new DDMContext
            {
                Config = _context.Config,
                Main = _context.Main,
                Parent = _contentParent
            };
            foreach (Transform child in _contentParent)
            {
                Destroy(child.gameObject);
            }

            _contentGenerator?.Invoke(context);
        }

        private void Start()
        {
            if (_backAction != null)
            {
                _backButton.onClick.AddListener(OnBackClick);
            }
            else
            {
                _backButton.gameObject.SetActive(false);
            }

            _closeButton.onClick.AddListener(OnCloseClick);
        }

        private void OnCloseClick()
        {
            _closeAction.Invoke();
        }

        private void OnBackClick()
        {
            _backAction.Invoke();
        }
    }
}