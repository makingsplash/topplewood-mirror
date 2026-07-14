using System;
using System.Collections.Generic;
using UnityEngine;

namespace SGS.Store.Purchases.Ui.Components
{
    public class StoreBlockBaseController : MonoBehaviour
    {

        [SerializeField]
        protected RectTransform CardsContainer;

        private List<StoreCardBaseController> _cards = new();

        [NonSerialized]
        public string BlockId;
        public List<StoreCardBaseController> Cards => _cards;
        public virtual bool Enabled
        {
            get => gameObject.activeSelf;
            set => gameObject.SetActive(value);
        }

        public void AddCard(StoreCardBaseController card)
        {
            card.transform.SetParent(CardsContainer, false);
            _cards.Add(card);
        }

        public virtual void LayoutCards()
        {
        }

        public virtual void ChangeCardState(StoreCardBaseController card, bool enabled, Action onComplete)
        {
            card.Enabled = enabled;
            onComplete?.Invoke();
        }
    }

}