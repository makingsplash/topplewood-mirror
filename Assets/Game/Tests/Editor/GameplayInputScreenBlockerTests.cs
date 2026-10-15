using Game.Infrastructure;
using Game.Services.Input;
using Gameplay.Core;
using NUnit.Framework;
using Ui;

namespace Gameplay.Tests
{
    /// <summary>
    /// TASK-566: the settings popup is modal — input blocked AND simulation paused, both released on hide. Drives the
    /// public handlers directly (UiService events are not raisable from outside, Show needs Addressables), so
    /// Initialize is not called and the UiService argument is null — the HudScreenCoverBridgeTests seam.
    /// Mutations these catch: blocker not taking the pause token (first test red), taking it on any screen (second).
    /// </summary>
    [TestFixture]
    public sealed class GameplayInputScreenBlockerTests
    {
        private InputBlockersHandler _input;
        private GameplayPause _pause;
        private GameplayInputScreenBlocker _blocker;

        [SetUp]
        public void SetUp()
        {
            _input = new InputBlockersHandler();
            _pause = new GameplayPause();
            _blocker = new GameplayInputScreenBlocker(_input, null, _pause);
        }

        [Test]
        public void SettingsShown_Pauses_HiddenResumes()
        {
            _blocker.OnScreenShown(ScreenNames.SettingsScreen);
            Assert.That(_pause.IsPaused, Is.True, "settings up → simulation stands");
            Assert.That(_input.HasBlockers(InputActionTypeFlags.All), Is.True, "settings up → gameplay input off");

            _blocker.OnScreenHidden(ScreenNames.SettingsScreen);
            Assert.That(_pause.IsPaused, Is.False, "settings closed → simulation resumes");
            Assert.That(_input.HasBlockers(InputActionTypeFlags.All), Is.False);
        }

        [Test]
        public void OtherScreen_DoesNotPause()
        {
            _blocker.OnScreenShown(ScreenNames.RestartPopup);
            Assert.That(_pause.IsPaused, Is.False, "restart popup is not modal for the simulation (TASK-153)");
            Assert.That(_input.HasBlockers(InputActionTypeFlags.All), Is.False);
        }

        [Test]
        public void SettingsShownTwice_HoldsOneToken()
        {
            _blocker.OnScreenShown(ScreenNames.SettingsScreen);
            _blocker.OnScreenShown(ScreenNames.SettingsScreen);
            _blocker.OnScreenHidden(ScreenNames.SettingsScreen);
            Assert.That(_pause.IsPaused, Is.False, "a repeated show must not leak a second pause token");
        }

        [Test]
        public void Dispose_WhileShown_ReleasesPause()
        {
            _blocker.OnScreenShown(ScreenNames.SettingsScreen);
            _blocker.Dispose();
            Assert.That(_pause.IsPaused, Is.False, "scope teardown with the popup up must not leave the game paused");
        }
    }
}
