using SGS.Infrastructure.Tools.Rewards;
using SGS.Services;
using TMPro;
using UnityEngine;

namespace SGS.Store.Ui
{
    public class NoAdsPackCardController : StoreCardController
    {
        [SerializeField]
        private TMP_Text _nameText;
   
        [SerializeField]
        private RewardItemPanel[] _rewards;
        
        [SerializeField]
        private int _width;

        public override int Width => _width;

        protected override void SetupReward(ProductRewardData[] pack)
        {
            if (!string.IsNullOrEmpty(SellableItem.VisualSettings.LocalizationId) && _nameText != null)
            {
                _nameText.text = GlobalService.Localization.GetLocalizedString(SellableItem.VisualSettings.LocalizationId);
            }

            for (int i = 0; i < _rewards.Length; i++)
            {
                if (i >= pack.Length - 1)
                {
                    _rewards[i].gameObject.SetActive(false);
                    continue;
                }

                _rewards[i].InitForPack(pack[i+1], ProductId);
            }
        }
    }
}