using System;
using SGS.Infrastructure.Tools.Rewards;
using SGS.Services;
using TMPro;
using UnityEngine;

namespace SGS.Store.Ui
{
    public class FreeCardController : StoreCardController
    {
        [SerializeField]
        private RewardItemPanel _rewardItem;
        
        [SerializeField]
        private GameObject[] _availableState;

        [SerializeField]
        private GameObject[] _waitState;

        [SerializeField]
        private TMP_Text _timerText;

        public override int Width => 1;

        protected override void SetupReward(ProductRewardData[] pack)
        {
            _rewardItem.Init(pack[0], ProductId);
        }
        
        public override void UpdateCooldownState(bool stateActive, TimeSpan timeLeft, int adsLeft)
        {
            foreach (var state in _availableState)
            {
                state.SetActive(!stateActive);
            }
            foreach (var state in _waitState)
            {
                state.SetActive(stateActive);
            }

            _timerText.text = GlobalService.Localization.GetLocalizedTimeInterval(timeLeft);
        }
    }
}