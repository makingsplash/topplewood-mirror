using Gameplay.PlayScreen;
using NUnit.Framework;

namespace Gameplay.Tests
{
    /// <summary>
    /// TASK-398 scene-free tests for <see cref="HudScreenCoverBridge"/>'s bookkeeping: the HUD is covered while ANY
    /// UiService screen is up, and uncovered only when the LAST one hides. Drives the public handlers directly
    /// (UiService events cannot be raised from outside the class and Show needs Addressables), so Initialize is not
    /// called and the UiService argument is null.
    /// </summary>
    [TestFixture]
    public sealed class HudScreenCoverBridgeTests
    {
        private PlayScreenInteraction _interaction;
        private HudScreenCoverBridge _bridge;

        [SetUp]
        public void SetUp()
        {
            _interaction = new PlayScreenInteraction();
            _bridge = new HudScreenCoverBridge(null, _interaction);
        }

        [Test]
        public void NoScreen_IsNotCovered()
        {
            Assert.That(_interaction.IsCoveredByScreen.Value, Is.False);
        }

        [Test]
        public void WinScreenShown_Covers_Hidden_Uncovers()
        {
            _bridge.OnScreenShown("Screens/WinScreen.prefab");
            Assert.That(_interaction.IsCoveredByScreen.Value, Is.True, "win screen up → HUD hidden");

            _bridge.OnScreenHidden("Screens/WinScreen.prefab");
            Assert.That(_interaction.IsCoveredByScreen.Value, Is.False, "win screen closed → HUD back");
        }

        [Test]
        public void PopupClosingOverWinScreen_KeepsCovered()
        {
            _bridge.OnScreenShown("Screens/WinScreen.prefab");
            _bridge.OnScreenShown("Screens/MessageScreen.prefab");
            _bridge.OnScreenHidden("Screens/MessageScreen.prefab");
            Assert.That(_interaction.IsCoveredByScreen.Value, Is.True,
                "the win screen is still up under the closed popup — the HUD must stay hidden");

            _bridge.OnScreenHidden("Screens/WinScreen.prefab");
            Assert.That(_interaction.IsCoveredByScreen.Value, Is.False);
        }

        [Test]
        public void HidingScreenThatWasNeverShown_DoesNotUncoverOthers()
        {
            _bridge.OnScreenShown("Screens/LoseScreen.prefab");
            _bridge.OnScreenHidden("Screens/RestartPopup.prefab");
            Assert.That(_interaction.IsCoveredByScreen.Value, Is.True);
        }
    }
}
