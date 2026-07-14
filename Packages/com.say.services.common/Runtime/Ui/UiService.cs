using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using JetBrains.Annotations;
using SGS.Data;
using SGS.Services.Interfaces;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.UI;

namespace SGS.Services.Ui
{
    public class UiService : IService
    {
        private readonly Dictionary<string, IScreenController> _shownScreens = new();
        private readonly Dictionary<string, UniTaskCompletionSource<IScreenController>> _screensInProgress = new();
        private readonly List<string> _screensStack = new();
        private readonly List<IScreenController> _shownWorldUi = new();

        private Canvas _canvas;
        private Transform _root;
        private GraphicRaycaster _raycaster;
        private AsyncLazy _initializeTask;

        public Canvas Canvas => _canvas;
        public GraphicRaycaster GraphicRaycaster => _raycaster;
        public Transform RootTransform => _root;
        public Camera UiCamera => _canvas.worldCamera;
        public string TopPanelName => _screensStack.Count == 0 ? "" : _screensStack[^1];
        
        public event Action<string> TopScreenChangedEvent;
        public event Action<string> ScreenShownEvent;
        public event Action<string> ScreenHiddenEvent;
        
        public void Init()
        {
            _initializeTask = InitializeAsync().ToAsyncLazy();
        }

        public void DeInit()
        {
            _shownScreens.Clear();
            _screensInProgress.Clear();
        }
        
        private async UniTask InitializeAsync()
        {
            _canvas = await SayData.Configs.Ui.CanvasAssetReference.InstantiateAsync();
            var camera = SayData.Configs.Ui.UseSeparateCanvasCamera
                ? await SayData.Configs.Ui.CameraAssetReference.InstantiateAsync()
                : Camera.main;
            _canvas.worldCamera = camera;
            _root = _canvas.transform;
            _raycaster = _canvas.GetComponent<GraphicRaycaster>();
        }

        public async void Show(string screenName)
        {
            await Show<IScreenController>(screenName);
        }
        
        public async UniTask<T> Show<T>(string screenName) where T : class, IScreenController
        {
            if (_initializeTask?.Task.Status.IsCompleted() == false)
                await _initializeTask;

            if (_shownScreens.TryGetValue(screenName, out var shownScreen))
            {
                return (T)shownScreen;
            }

            if (_screensInProgress.TryGetValue(screenName, out var shownInProgress))
            {
                return (T)await shownInProgress.Task;
            }

            var completion = new UniTaskCompletionSource<IScreenController>();
            _screensInProgress[screenName] = completion;

            try
            {
                GameObject panelObject = await Addressables.InstantiateAsync(screenName, _root);

                var controller = panelObject.GetComponent<T>();
                if (controller == null)
                {
                    throw new InvalidOperationException(
                        $"Screen '{screenName}' does not contain controller component '{typeof(T).Name}'.");
                }

                var animator = panelObject.GetComponent<IWindowAnimator>();
                if (animator is null)
                {
                    controller.Init();
                }
                else
                {
                    animator.Show(() => controller.Init());
                }
                
                _screensStack.Add(screenName);
                _shownScreens.Add(screenName, controller);
                ScreenShownEvent?.Invoke(screenName);
                TopScreenChangedEvent?.Invoke(screenName);
                completion.TrySetResult(controller);
                return controller;
            }
            catch (Exception exception)
            {
                completion.TrySetException(exception);
                throw;
            }
            finally
            {
                _screensInProgress.Remove(screenName);
            }
        }

        public void Hide(IScreenController screen)
        {
            string screenName = null;
            foreach (var screenKv in _shownScreens)
            {
                if (screenKv.Value != screen)
                {
                    continue;
                }

                screenName = screenKv.Key;
                break;
            }

            if (screenName == null)
            {
                return;
            }

            _shownScreens.Remove(screenName);
            RemoveScreenFromStack(screenName);
            DeInitScreen(screen);
        }
        
        public void Hide(string screenName)
        {
            if (_shownScreens.TryGetValue(screenName, out IScreenController screenController))
            {
                _shownScreens.Remove(screenName);
                RemoveScreenFromStack(screenName);
                DeInitScreen(screenController);
            }
        }

        private void RemoveScreenFromStack(string panelName)
        {
            var topPanel = _screensStack.Count > 0 ? _screensStack[^1] : null;
            _screensStack.Remove(panelName);
            if (topPanel == panelName)
            {
                var topPanelName = _screensStack.Count == 0 ? null : _screensStack[^1];
                TopScreenChangedEvent?.Invoke(topPanelName);
            }
            ScreenHiddenEvent?.Invoke(panelName);
        }
        public async UniTask<T> CreateSubpanel<T>(string subpanelName, Transform parent) where T : class, ISubpanelController, new()
        {
           
            GameObject panelObject = await Addressables.InstantiateAsync(subpanelName, parent);
            var controller = panelObject.GetComponent<T>();
            
            controller.Init();

            return controller;
        }
        
        public void DestroySubpanel(ISubpanelController subpanel)
        {
            subpanel.DeInit();
        }
        
        public void HideWorldUiScreen()
        {
            foreach (IScreenController screen in _shownWorldUi)
            {
                screen.GameObject.SetActive(false);
            }
        }
        
        public void HideAllScreens()
        {
            foreach (var (key, screen) in _shownScreens)
            {
                screen.GameObject.SetActive(false);
            }
        }
        
        public void DestroyWorldUiScreen()
        {
            foreach (IScreenController screen in _shownWorldUi)
            {
                DeInitScreen(screen);
            }
            _shownWorldUi.Clear();
        }
        
        public void DestroyAllScreens()
        {
            foreach (var (key, screen) in _shownScreens)
            {
                DeInitScreen(screen);
            }
                 
            _shownScreens.Clear();
        }

        [CanBeNull]
        public T TryGet<T>(string screenName) where T : class, IScreenController
        {
            if (_shownScreens.TryGetValue(screenName, out IScreenController screen))
            {
                return (T)screen;
            }
             
            return null;
        }
        
        public bool IsShown(string screenName)
        {
            return _shownScreens.ContainsKey(screenName);
        }
        
        public T ShowWorld<T>(string screenName, Transform parent, Vector3 localPosition) where T : class, IScreenController
        {
                
            GameObject screenObject = Addressables.InstantiateAsync(screenName, parent).WaitForCompletion();
            screenObject.transform.localPosition = localPosition;
            
            var controller = screenObject.GetComponent<T>();
            controller.Init();
            
            _shownWorldUi.Add(controller);
            
            return controller;
        }
        
        public void DeInitWorldPanel(IScreenController screen)
        {
            if (screen == null)
            {
                return;
            }
            
            if (!_shownWorldUi.Contains(screen))
            {
                Debug.LogError("Trying to deinit panel that was not shown");
            }
                 
            DeInitScreen(screen);
            _shownWorldUi.Remove(screen);
        }

        private void DeInitScreen(IScreenController screen)
        {
            if (screen == null)
            {
                return;
            }

            screen.DeInit();
            var animator = screen.GameObject.GetComponent<IWindowAnimator>();
            if (animator is null)
            {
                Addressables.ReleaseInstance(screen.GameObject);
            }
            else
            {
                animator.Hide(() => Addressables.ReleaseInstance(screen.GameObject));
            }
        }
    }
}
