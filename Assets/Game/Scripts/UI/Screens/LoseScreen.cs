using System;
using UnityEngine;
using UnityEngine.UI;

namespace Ui.Screens
{
    /// <summary>
    /// Lose screen (TASK-154): shown by <see cref="Gameplay.PlayScreen.GatedResolutionFlow"/> when a level resolves
    /// Lost. One blameless message + one "Try again" button. The screen decides *when* to transition, never *what*:
    /// the button runs the campaign controller's own transition callback (the flow's <c>applyTransition</c>, i.e.
    /// the Lost-retry path), it does NOT reload the level itself — the "won ? advance : reload" logic stays the
    /// single source of truth in <c>CampaignProgressionController.ApplyResolution</c> (seam docstring on
    /// <see cref="Gameplay.PlayScreen.ILevelResolutionFlow"/>). Mirrors <see cref="Ui.Popups.RestartPopup"/>:
    /// tap-gated, closes itself on tap. No auto-dismiss.
    /// </summary>
    public class LoseScreen : BaseScreen
    {
        [SerializeField] private Button _tryAgainButton;

        private Action _onTryAgain;

        public override void Init()
        {
            _tryAgainButton.onClick.AddListener(OnClickTryAgain);
        }

        public override void DeInit()
        {
            base.DeInit();
            _tryAgainButton.onClick.RemoveListener(OnClickTryAgain);
        }

        public void Setup(Action onTryAgain)
        {
            _onTryAgain = onTryAgain;
        }

        private void OnClickTryAgain()
        {
            _onTryAgain?.Invoke();
            Close();
        }
    }
}
