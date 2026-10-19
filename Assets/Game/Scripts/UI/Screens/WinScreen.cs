using System;
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
    /// end-of-line-up state ("ALL DONE!"/"Next" — shown once per game, on the last line-up level; the tap enters the
    /// replay pool, TASK-528). Titles are baked lettering sprites (owner-sanctioned by the TASK-272 reference; loc-debt TASK-160), labels stay text nodes. Both title/label pairs are overlapping nodes in the same
    /// position (so the compiler measures BOTH for localization overflow); <see cref="Setup"/> toggles the
    /// visible pair by the presentational <c>isFinalLevel</c> flag — the flag never changes the transition itself.
    ///
    /// The hero of the screen is three stars for any win (TASK-612, owner 2026-10-19): decoration, not a grade and
    /// not a currency — always three, never stored. They are static prefab nodes with their own show-animation
    /// (<c>design/screens/WinScreen.json</c> → StarsBlock), so the controller has nothing to bind for them. The
    /// TASK-240 coin row and ×2 button went with the currency.
    /// </summary>
    public class WinScreen : BaseScreen
    {
        [SerializeField] private Button _nextButton;
        [SerializeField] private GameObject _titleDefault;
        [SerializeField] private GameObject _titleFinal;
        [SerializeField] private GameObject _labelDefault;
        [SerializeField] private GameObject _labelFinal;

        private Action _onNext;

        public override void Init()
        {
            _nextButton.onClick.AddListener(OnClickNext);
        }

        public override void DeInit()
        {
            base.DeInit();
            _nextButton.onClick.RemoveListener(OnClickNext);
        }

        public void Setup(Action onNext, bool isFinalLevel)
        {
            _onNext = onNext;

            _titleDefault.SetActive(!isFinalLevel);
            _titleFinal.SetActive(isFinalLevel);
            _labelDefault.SetActive(!isFinalLevel);
            _labelFinal.SetActive(isFinalLevel);
        }

        private void OnClickNext()
        {
            _onNext?.Invoke();
            Close();
        }
    }
}
