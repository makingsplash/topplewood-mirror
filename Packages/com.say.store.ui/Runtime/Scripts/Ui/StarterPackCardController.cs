using SGS.Infrastructure.Tools.Rewards;
using SGS.Services;
using TMPro;
using UnityEngine;

namespace SGS.Store.Ui
{
    public class StarterPackCardController : StoreCardController
    {
        [SerializeField]
        private TMP_Text _nameText;

        [SerializeField]
        private RewardItemPanel[] _addRewards;

        [SerializeField]
        private int _width;

        public override int Width => _width;

        protected override void SetupReward(ProductRewardData[] pack)
        {
            if (!string.IsNullOrEmpty(SellableItem.VisualSettings.LocalizationId))
            {
                _nameText.text = GlobalService.Localization.GetLocalizedString(SellableItem.VisualSettings.LocalizationId);
            }

            for (var i = 0; i < _addRewards.Length; i++)
            {
                _addRewards[i].InitForPack(pack[i], ProductId);
            }
        }
    }
}