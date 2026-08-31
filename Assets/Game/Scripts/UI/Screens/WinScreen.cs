using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Ui.Screens
{
    /// <summary>
    /// Win screen (TASK-157): shown by <see cref="Gameplay.PlayScreen.GatedResolutionFlow"/> when a level resolves
    /// Won, after a settle-beat on the assembled figure. One warm message + one primary button. Mirror of
    /// <see cref="LoseScreen"/> by mechanic, opposite by tone. The screen decides *when* to transition, never
    /// *what*: the button runs the campaign controller's own transition callback (the flow's <c>onNext</c>, i.e.
    /// the won-advance path), it does NOT reload the level itself — the "won ? advance : reload" logic stays the
    /// single source of truth in <c>CampaignProgressionController.ApplyResolution</c> (seam docstring on
    /// <see cref="Gameplay.PlayScreen.ILevelResolutionFlow"/>). Tap-gated, closes itself on tap. No auto-dismiss.
    ///
    /// Two copy configurations, one prefab (ui-composer 03 §4): the normal state ("VICTORY!"/"Continue") and the
    /// campaign-wrap state ("ALL DONE!"/"Play again"). Titles are baked lettering sprites (owner-sanctioned by the TASK-272 reference; loc-debt TASK-160), labels stay text nodes. Both title/label pairs are overlapping nodes in the same
    /// position (so the compiler measures BOTH for localization overflow); <see cref="Setup"/> toggles the
    /// visible pair by the presentational <c>isFinalLevel</c> flag — the flag never changes the transition itself.
    /// </summary>
    public class WinScreen : BaseScreen
    {
        [SerializeField] private Button _nextButton;
        [SerializeField] private GameObject _titleDefault;
        [SerializeField] private GameObject _titleFinal;
        [SerializeField] private GameObject _labelDefault;
        [SerializeField] private GameObject _labelFinal;

        // TASK-240 (ui-composer 03 §5): net-new reward + ×2 nodes. Null-guarded everywhere below so the screen keeps
        // working on the not-yet-rebuilt prefab (the placeholder-first build) instead of NRE-ing — until ui-build
        // regenerates the prefab from WinScreen.json these bind to nothing and reward/×2 are simply inert.
        [SerializeField] private GameObject _rewardBlock;
        [SerializeField] private TextMeshProUGUI _rewardAmount;
        [SerializeField] private Button _x2Button;

        private Action _onNext;
        private Action _onX2;

        public override void Init()
        {
            _nextButton.onClick.AddListener(OnClickNext);
            if (_x2Button != null)
                _x2Button.onClick.AddListener(OnClickX2);
        }

        public override void DeInit()
        {
            base.DeInit();
            _nextButton.onClick.RemoveListener(OnClickNext);
            if (_x2Button != null)
                _x2Button.onClick.RemoveListener(OnClickX2);
        }

        /// <param name="reward">Coins credited for this win. 0 (a replay) hides the reward row instead of "+0".</param>
        /// <param name="x2Available">Show the ×2 button (owner: visible stub from <c>WinScreenX2ButtonStartLevel</c>).</param>
        /// <param name="onX2">Invoked on a ×2 tap — the reward-doubler stub; does NOT close the screen (the player
        /// still taps Next).</param>
        public void Setup(Action onNext, bool isFinalLevel, int reward, bool x2Available, Action onX2)
        {
            _onNext = onNext;
            _onX2 = onX2;

            _titleDefault.SetActive(!isFinalLevel);
            _titleFinal.SetActive(isFinalLevel);
            _labelDefault.SetActive(!isFinalLevel);
            _labelFinal.SetActive(isFinalLevel);

            var hasReward = reward > 0;
            if (_rewardBlock != null)
                _rewardBlock.SetActive(hasReward);
            if (hasReward && _rewardAmount != null)
                _rewardAmount.text = "+" + reward;

            if (_x2Button != null)
                _x2Button.gameObject.SetActive(x2Available);
        }

        private void OnClickNext()
        {
            _onNext?.Invoke();
            Close();
        }

        private void OnClickX2()
        {
            // Stub tap: run the doubler seam (debug-log today) and leave the screen up — Next still transitions.
            _onX2?.Invoke();
        }
    }
}
