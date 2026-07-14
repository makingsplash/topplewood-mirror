using SGS.Infrastructure.Tools.Rewards;
using SGS.Services;
using TMPro;
using UnityEngine;

namespace SGS.Store.Ui
{
    public class PackCardController : StoreCardController
    {
        [SerializeField]
        private TMP_Text _nameText;
   
        [SerializeField]
        private RewardItemPanel _mainReward;

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

            _mainReward?.Init(pack[0], ProductId);

            for (int i = 0; i < _addRewards.Length; i++)
            {
                if (pack.Length <= i + 1)
                {
                    _addRewards[i].gameObject.SetActive(false);
                    continue;
                }
                
                _addRewards[i].InitForPack(pack[i + 1], ProductId);
            }
        }
    }
}