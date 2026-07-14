using System;
using System.Linq;
using SGS.Store.Purchases.Data;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace SGS.Store.Purchases.Ui.Components
{
    public abstract class StoreCardBaseController : MonoBehaviour
    {        
        [SerializeField]
        protected TMP_Text _priceText;
        [SerializeField]
        private Button _purchaseButton;
        [SerializeField]
        private GameObject[] _badges;
        [SerializeField]
        private GameObject _discountIcon;
        [SerializeField]
        private TMP_Text _discountPriceText;
        [SerializeField]
        private TMP_Text _priceBeforeDiscountText;
        
        private PriceTextWrapper _priceTextWrapper;
        private PriceTextWrapper _priceBeforeDiscountTextWrapper;
        private SellableItemData _sellableItem;
        private string _productId;
        
        public abstract int Width { get; }
        public string ProductId => _productId;
        public SellableItemData SellableItem => _sellableItem;
        
        public bool Enabled
        {
            get => gameObject.activeSelf;
            set => gameObject.SetActive(value);
        }


        public Action<StoreCardBaseController> OnClick;

        public virtual void Init(PurchaseService service, ProductData product, SellableItemData sellableItem)
        {
            _sellableItem = sellableItem;
            _productId = product.ProductId;
            if (product.PurchaseType != PurchaseType.Free)
            {
                bool hasDiscount = !string.IsNullOrEmpty(sellableItem.VisualSettings.IdBeforeDiscount);
                if (hasDiscount)
                {
                    _priceTextWrapper = new PriceTextWrapper(service, _discountPriceText, _productId);
                    _priceBeforeDiscountTextWrapper = new PriceTextWrapper(service, _priceBeforeDiscountText, sellableItem.VisualSettings.IdBeforeDiscount);
                }
                else
                {
                    _priceTextWrapper = new PriceTextWrapper(service, _priceText, _productId);
                }
                _discountPriceText?.gameObject.SetActive(hasDiscount);
                _priceText?.gameObject.SetActive(!hasDiscount);

                if (_discountIcon != null)
                {
                    _discountIcon.SetActive(hasDiscount);    
                }
                
            }

            InitBadges(sellableItem);
            SetupIcons(product);
            _purchaseButton.onClick.AddListener(OnPurchaseButton);
        }
        
        protected virtual void SetupIcons(ProductData productData)
        {
        }


        public void SetPosition(Vector2 localPosition)
        {
            ((RectTransform)transform).anchoredPosition = new Vector2(localPosition.x, -localPosition.y);
        }
        
        public virtual void ProductPurchased()
        {
            
        }

        [Obsolete]
        public virtual void UpdateCooldownState(bool stateActive, TimeSpan timeLeft)
        {
            UpdateCooldownState(stateActive, timeLeft, 0);
        }

        public virtual void UpdateCooldownState(bool stateActive, TimeSpan timeLeft, int adsLeft)
        {
            
        }
        
        public virtual void UpdateTimedState(bool stateActive, TimeSpan timeLeft)
        {
            
        }

        protected virtual void InitBadges(SellableItemData storeData)
        {
            foreach (var badge in _badges)
            {
                var badgeName = badge.name;
                var hasBadge = storeData.VisualSettings.Badges.Any(b => b.Equals(badgeName, StringComparison.InvariantCultureIgnoreCase));
                badge.SetActive(hasBadge);
            }
        }

        protected void SetInteractable(bool interactable)
        {
            _purchaseButton.interactable = interactable;
        }
        
        private void OnDestroy()
        {
            _priceTextWrapper?.DeInit();
            _priceBeforeDiscountTextWrapper?.DeInit();
            _purchaseButton.onClick.RemoveListener(OnPurchaseButton);
        }

        protected virtual void OnPurchaseButton()
        {
            OnClick?.Invoke(this);
        }
    }
}