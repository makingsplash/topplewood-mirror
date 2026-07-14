#if SAYKIT_PURCHASING

using System;
using SGS.Store.Purchases.PersistentStorage;
using UnityEngine;
using Object = UnityEngine.Object;

namespace SGS.Store.Purchases.Modules
{
    public class UpdateModule
    {
        private readonly IPurchasesPersistentStorage _persistentStorage;
        private readonly GameObject _timerObject;
        private bool _storageIsDirty;
        private int _saveWaitUpdates;
        private Action _afterSaveAction;

        public IPurchasesPersistentStorage PersistentStorage => _persistentStorage;
        public event Action OnUpdate;

        public UpdateModule(IPurchasesPersistentStorage persistentStorage)
        {
            _persistentStorage = persistentStorage;
                        
            _timerObject = new GameObject("PurchaseServiceUpdater");
            var timerMonoBehaviour = _timerObject.AddComponent<TimerMonoBehaviour>();
            timerMonoBehaviour.Init(Update);
            
            Object.DontDestroyOnLoad(_timerObject);
        }

        public void DeInit()
        {
            Object.Destroy(_timerObject);
        }

        public void SaveStorageAndContinue(Action continueAction)
        {
            _persistentStorage.SaveState();
            _afterSaveAction = continueAction;
            _saveWaitUpdates = 1;
        }

        public void SetStorageDirty()
        {
            _storageIsDirty = true;
        }
        
        private void Update()
        {
            OnUpdate?.Invoke();

            if (_storageIsDirty)
            {
                _persistentStorage.SaveState();
                _storageIsDirty = false;
            }

            if (_afterSaveAction != null)
            {
                if (_saveWaitUpdates == 0)
                {
                    _afterSaveAction();
                    _afterSaveAction = null;
                }

                _saveWaitUpdates -= 1;
            }
        }
    }
        
    public class TimerMonoBehaviour : MonoBehaviour
    {
        private Action _onUpdate;

        public void Init(Action onUpdate)
        {
            _onUpdate = onUpdate;
        }

        private void Update()
        {
            _onUpdate();
        }
    }
}
#endif