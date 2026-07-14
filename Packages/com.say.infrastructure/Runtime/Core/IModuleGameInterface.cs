using SGS.Infrastructure.Tools.Rewards;

namespace SGS.Infrastructure.Core
{
    public interface IModuleGameInterface
    {
        void StartLevel(string eventId);
        
        int GetCurrentLevel();

        int GetCurrentPlayTime();

        public void GiveReward(string productId, ProductRewardData[] reward);
    }
}