using DataStorage;
using Ui;
using UnityEngine.Scripting.APIUpdating;

namespace Ui.Components.Counters
{
    [MovedFrom(sourceNamespace: "UI.Components.Counters")]
    public sealed class CoinsCounterComponent : BaseCounterComponent
    {
        private void OnClickAddButton()
        {
            // TODO: open the new game's store/shop here.
        }

        protected override bool IsAddButtonAvailable() => false;

        protected override void Awake()
        {
            base.Awake();
            Button.onClick.AddListener(OnClickAddButton);
        }

        private void OnEnable()
        {
            var coinsField =Data.Meta.GoldAmount;
            coinsField.OnChanged += OnChangedCoinsAmount;
            Setup(coinsField.Value);
        }

        private void OnChangedCoinsAmount(int count)
        {
            Setup(count);
        }

        private void OnDisable()
        {
            Data.Meta.GoldAmount.OnChanged -= OnChangedCoinsAmount;
        }

        private void Setup(int coinsCount)
        {
            SetCountText(coinsCount);
        }
    }
}
