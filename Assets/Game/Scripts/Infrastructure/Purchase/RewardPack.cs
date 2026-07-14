#if SAYKIT_PURCHASING
using System;
using System.Linq;

namespace Gameplay.Sequence.StartElement
{
    [Serializable]
    public class RewardPack
    {
        public Reward[] Rewards;
        
        public Reward GetRewardByType(RewardType rewardType)
        {
            return Rewards.FirstOrDefault(r => r.Type == rewardType);
        }

        public RewardPack Clone()
        {
            RewardPack rewardPack = new();
            rewardPack.Rewards = new Reward[Rewards.Length];
            for (int i = 0; i < Rewards.Length; i++)
            {
                rewardPack.Rewards[i] = new Reward
                {
                    Type = Rewards[i].Type,
                    Amount = Rewards[i].Amount,
                    Id = Rewards[i].Id
                };
            }
            return rewardPack;
        }
    }
}
#endif
