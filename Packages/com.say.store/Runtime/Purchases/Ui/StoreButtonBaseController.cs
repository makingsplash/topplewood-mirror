using SGS.Store.Purchases.PersistentStorage;
using UnityEngine;
using UnityEngine.UI;

namespace SGS.Store.Purchases.Ui
{
    public abstract class StoreButtonBaseController : MonoBehaviour
    {
        [SerializeField]
        private Button _button;
        [SerializeField]
        private GameObject _badge;
        private IPurchasesPersistentStorage _persistentStorage;
        private const float _stateUpdateInterval = 1f;
        private float _stateUpdateCooldown;
        private bool _badgeShown;

        public void Setup(PurchaseService service)
        {
            _persistentStorage = service.PersistentStorage;
            _badgeShown = false;
            ShowBadge(_badgeShown);
            _button.onClick.AddListener(OpenStore);
        }

        protected abstract void OpenStore();

        private void Update()
        {
            if (_persistentStorage == null)
            {
                return;
            }
                        
            _stateUpdateCooldown -= Time.deltaTime;
            if (_stateUpdateCooldown > 0)
            {
                return;
            }

            _stateUpdateCooldown = _stateUpdateInterval;

            if (_badgeShown != _persistentStorage.HasUpdates)
            {
                _badgeShown = _persistentStorage.HasUpdates;
                ShowBadge(_badgeShown);
            }
        }

        protected virtual void ShowBadge(bool show)
        {
            _badge.SetActive(show);
        }
        

    }
}