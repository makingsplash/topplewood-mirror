using Cysharp.Threading.Tasks;
using JetBrains.Annotations;
using Services;
using Ui;
using Ui.Popups;
using VContainer.Unity;

namespace Gameplay.PlayScreen
{
    /// <summary>
    /// Hands the HUD gear (<see cref="SettingsButtonPresenter"/>, TASK-562) its action: open the settings popup
    /// (TASK-566). Like RESTART, a tap during the WIN/LOSE hold (<see cref="PlayScreenInteraction.IsBusy"/>) is ignored —
    /// no popup over a running transition. A second tap while the popup is up cannot stack another: UiService returns the
    /// already-shown controller, and the gear is hidden under any screen anyway (HudCover). Prod scope only.
    /// </summary>
    [UsedImplicitly]
    public sealed class SettingsGearBinder : IStartable
    {
        private readonly SettingsButtonPresenter _gear;
        private readonly PlayScreenInteraction _interaction;

        public SettingsGearBinder(SettingsButtonPresenter gear, PlayScreenInteraction interaction)
        {
            _gear = gear;
            _interaction = interaction;
        }

        public void Start() => _gear.Bind(OnGear);

        private void OnGear()
        {
            if (_interaction.IsBusy.Value)
                return;
            Service.UI.Show<SettingsPopup>(ScreenNames.SettingsScreen).Forget();
        }
    }
}
