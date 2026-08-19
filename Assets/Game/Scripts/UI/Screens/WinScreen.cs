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
    /// Two copy configurations, one prefab (ui-composer 03 §4): the normal state ("Nice!"/"Next") and the
    /// campaign-wrap state ("All done!"/"Play again"). Both title/label pairs are overlapping nodes in the same
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
