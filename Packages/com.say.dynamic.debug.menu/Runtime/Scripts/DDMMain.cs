using System;
using System.Collections.Generic;
using DynamicDebugMenu.Elements;
using SRDebugger;
using UnityEngine;


namespace DynamicDebugMenu
{
    public class DDMMain : MonoBehaviour
    {
        private DebugItemsConfig _config;
        private Action<bool> _closeCallback;

        private readonly Stack<DDMPage> _pagesStack = new();
        private DDMPage _currentPage;

        private bool _isNeedRestart;
        private Action<DDMContext> _contentGenerator;

        public event Action OnMenuClosed;

        public void Init(DebugItemsConfig config, Action<DDMContext> contentGenerator, Action<bool> closeCallback)
        {
            _config = config;
            _closeCallback = closeCallback;
            _contentGenerator = contentGenerator;

            CreatePage("Main menu", MainMenuContent, null, true);
        }

        private void OnDestroy()
        {
            _currentPage.OnClose?.Invoke();
            foreach (var page in _pagesStack)
            {
                page.OnClose?.Invoke();
            }

            OnMenuClosed?.Invoke();
            _closeCallback?.Invoke(_isNeedRestart);
        }

        public void CreatePage(string title, Action<DDMContext> contentGenerator, Action pageCloseAction = null)
        {
            CreatePage(title, contentGenerator, pageCloseAction, false);
        }

        private void MainMenuContent(DDMContext context)
        {
            DDMButton.Create(context, "Console", () =>
            {
                SRDebug.Instance.ShowDebugPanel(DefaultTabs.Console);
            });

            _contentGenerator?.Invoke(context);
        }

        private void CreatePage(string title, Action<DDMContext> contentGenerator, Action pageCloseAction, bool isRoot)
        {
            if (_currentPage)
            {
                _pagesStack.Push(_currentPage);
                _currentPage.gameObject.SetActive(false);
            }

            Action backAction = isRoot ? null : BackMenu;

            DDMContext context = new DDMContext
            {
                Config = _config,
                Main = this,
                Parent = transform
            };

            DDMPage page = DDMPage.Create(context, title, contentGenerator, pageCloseAction, backAction, CloseMenu);
            _currentPage = page;
            _currentPage.GenerateContent();
        }

        private void BackMenu()
        {
            _currentPage.OnClose?.Invoke();
            Destroy(_currentPage.gameObject);
            _currentPage = _pagesStack.Pop();
            _currentPage.gameObject.SetActive(true);
        }

        public void CloseMenu()
        {
            Destroy(gameObject);
        }

        public void CloseMenuAndRestart()
        {
            _isNeedRestart = true;
            Destroy(gameObject);
        }

        public void ReloadCurrentPage()
        {
            _currentPage.GenerateContent();
        }

        public void SetRestart(bool restart = true)
        {
            _isNeedRestart = restart;
        }
    }
}