using SGS.Infrastructure.Tools.Rewards;
using SGS.Services.Containers;
using SGS.Store.Purchases;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace SGS.Store.Ui
{
    public class RewardItemPanel : MonoBehaviour
    {
        public Image Icon;
        public TMP_Text CountText;
        public GameObject CoinIcon;

        public void Init(ProductRewardData reward, string productId)
        {
            var config = GlobalServiceContainer.Get<GamePurchaseService>().VisualConfig;

            if (CountText is not null)
            {
                CountText.text = config.GetRewardText(reward);
                CountText.gameObject.SetActive(true);
            }

            if (CoinIcon != null)
            {
                CoinIcon.SetActive(true);
            }

            // Icon sprite intentionally left as prefab default (runtime substitution disabled)
        }

        public void InitForPack(ProductRewardData reward, string productId = null)
        {
            var config = GlobalServiceContainer.Get<GamePurchaseService>().VisualConfig;

                if (CountText is not null)
                {
                    CountText.text = config.GetRewardText(reward);
                    CountText.gameObject.SetActive(true);
                }

            // Icon sprite intentionally left as prefab default (runtime substitution disabled)
        }

    }
}