using System;
using DynamicDebugMenu;
using DynamicDebugMenu.Elements;
using SGS.Configs;
using SGS.Data;
using SGS.Infrastructure.Tools.Platform;
using SGS.Services.DebugLog;
using SGS.Services.Interfaces;
using UnityEngine;
using Object = UnityEngine.Object;


namespace SGS.Services.DebugTools
{
    public class DebugService : IService
    {
        public Action<bool> OnDebugMenuClose;
        public Action<DDMContext> OnDebugMenuShown;
            
        private readonly Vector2 _touchZone;

        private const float _shakeMeasureTime = 0.5f;
        private const float _shakeTriggerValue = 1f;
        private const float _shakeCompareValue = 2f;
        private const float _shakeCompareValueTablet = 1.1f;

        private bool _isDebugMenuOpened;
        private GameObject[] _debugUis;

        private float _shakeThresholdValue;
        private float _shakeTimeLeft;
        private float _shakeMin;
        private float _shakeMax;

        public void Init()
        {
            SayData.Debug.DebugMenuShown.OnChanged += ShowDebugMenu;
            if (PlatformUtils.IsMobile())
            {
                _shakeThresholdValue = IsTablet() ? _shakeCompareValueTablet : _shakeCompareValue;
                GlobalService.Timer.Update.AddListener(UpdateAcceleration);
            }
            else
            {
#if ENABLE_LEGACY_INPUT_MANAGER
                GlobalService.Timer.Update.AddListener(UpdateKeyboard);
#endif
            }

            CreateDebugUIs();
        }

        public void DeInit()
        {
            SayData.Debug.DebugMenuShown.OnChanged -= ShowDebugMenu;
            
            foreach (var ui in _debugUis)
            {
                Object.Destroy(ui);
            }

            _debugUis = null;
#if ENABLE_LEGACY_INPUT_MANAGER
            GlobalService.Timer.Update.RemoveListener(UpdateKeyboard);
#endif
            GlobalService.Timer.Update.RemoveListener(UpdateAcceleration);
        }

        private void UpdateAcceleration()
        {
            float shakeValue = 0f;
            try
            {
#if ENABLE_LEGACY_INPUT_MANAGER
                shakeValue = Input.acceleration.x;
#endif
            }
            catch (Exception exc)
            {
                GlobalService.DebugLog.LogError("DebugService.UpdateAcceleration exception: " + exc.Message);
            }
            
            if (_shakeTimeLeft < 0f)
            {
                if (Mathf.Abs(shakeValue) > _shakeTriggerValue)
                {
                    _shakeTimeLeft = _shakeMeasureTime;
                }

                _shakeMin = _shakeMax = shakeValue;
            }
            else
            {
                _shakeMin = Mathf.Min(_shakeMin, shakeValue);
                _shakeMax = Mathf.Max(_shakeMax, shakeValue);
                _shakeTimeLeft -= Time.unscaledDeltaTime;

                if (_shakeTimeLeft <= 0f)
                {
                    if (_shakeMin < -_shakeThresholdValue && _shakeMax > _shakeThresholdValue)
                    {
                        SayData.Debug.DebugMenuShown.Value = true;
                    }
                }
            }
        }

        private static bool IsTablet()
        {
#if UNITY_IOS   
            return  PlatformUtils.IsIpadResolution();
#elif UNITY_ANDROID
            float diagonalInches = Mathf.Sqrt (Screen.width * Screen.width + Screen.height * Screen.height) / Screen.dpi;
            GlobalService.DebugLog.Log ("Getting device inches: " + diagonalInches, DebugLogType.Profiling);

            return PlatformUtils.IsIpadResolution() || diagonalInches > 7f;
#else 
            return false;
#endif
        }

#if ENABLE_LEGACY_INPUT_MANAGER
        private void UpdateKeyboard()
        {
            if (Input.GetKeyDown(KeyCode.Escape))
            {
                SayData.Debug.DebugMenuShown.Value = true;
            }
        }   
#endif

        private void CreateDebugUIs()
        {
            _debugUis = new GameObject[SayData.Configs.Ui.DebugUIPrefabs.Length];
            for (var i = 0; i < SayData.Configs.Ui.DebugUIPrefabs.Length; i++)
            {
                _debugUis[i] = Object.Instantiate(SayData.Configs.Ui.DebugUIPrefabs[i]);
                GameObject.DontDestroyOnLoad(_debugUis[i]);
            }
        }
                
        private void ShowDebugMenu(bool show)
        {
            if (!show)
            {
                return;
            }

            DebugItemsConfig config = SayData.Configs.DebugItems;
            GameObject debugObject = Object.Instantiate(config.MainPrefab);
            debugObject.GetComponent<DDMMain>().Init(config, OnDebugMenuShown, HandleOnDebugMenuClosed);
        }

        private void HandleOnDebugMenuClosed(bool isRestart)
        {
            OnDebugMenuClose?.Invoke(isRestart);

            SayData.Debug.DebugMenuShown.Value = false;
        }
    }
}
