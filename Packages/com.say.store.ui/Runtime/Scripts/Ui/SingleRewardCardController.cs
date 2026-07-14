using SGS.Infrastructure.Tools.Rewards;
using UnityEngine;

namespace SGS.Store.Ui
{
    public class SingleRewardCardController : StoreCardController
    {
        [SerializeField]
        private RewardItemPanel _rewardItem;
        
        public override int Width => 1;

        protected override void SetupReward(ProductRewardData[] pack)
        {
                _rewardItem.Init(pack[0], ProductId);
        }
    }
}