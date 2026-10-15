using Game.Infrastructure;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

namespace Gameplay.Tests
{
    /// <summary>
    /// TASK-566 QA defect 1: the first system "back" after a cold start was lost on the phone. Android back (adb keyevent,
    /// gesture navigation) delivers DOWN and UP within ONE input update. The first-ever
    /// <c>escapeKey.wasPressedThisFrame</c> query judges only the update's end state, so that press reads as "up";
    /// <see cref="ScreenBackButtonHandler"/> now reads an <see cref="InputAction"/>, which is driven per state event.
    ///
    /// Headless: Input System runs player updates in edit mode under a temporary settings object (manual update mode +
    /// the RUN_PLAYER_UPDATES_IN_EDIT_MODE feature flag); each test gets a fresh keyboard, so every key query is "first".
    /// Mutation these catch: a wrong binding (any key but Escape) or a frame-state read instead of the action — the
    /// sub-frame test goes red; an action that stays "performed" — the next-update test goes red.
    /// </summary>
    [TestFixture]
    public sealed class ScreenBackButtonInputTests
    {
        private InputSettings _savedSettings;
        private InputSettings _testSettings;
        private Keyboard _keyboard;
        private InputAction _back;

        [SetUp]
        public void SetUp()
        {
            _savedSettings = InputSystem.settings;
            _testSettings = ScriptableObject.CreateInstance<InputSettings>();
            _testSettings.updateMode = InputSettings.UpdateMode.ProcessEventsManually;
            _testSettings.SetInternalFeatureFlag("RUN_PLAYER_UPDATES_IN_EDIT_MODE", true);
            InputSystem.settings = _testSettings;
        }

        [TearDown]
        public void TearDown()
        {
            _back?.Dispose();
            _back = null;
            if (_keyboard != null && _keyboard.added)
                InputSystem.RemoveDevice(_keyboard);
            _keyboard = null;
            InputSystem.settings = _savedSettings;
            Object.DestroyImmediate(_testSettings);
        }

        [Test]
        public void Premise_FirstKeyPoll_MissesPressThatWentDownAndUpInOneUpdate()
        {
            _keyboard = InputSystem.AddDevice<Keyboard>();
            InputSystem.Update();

            PressAndReleaseInOneUpdate();

            Assert.That(_keyboard.escapeKey.wasPressedThisFrame, Is.False,
                "engine premise of the fix: the first frame-state query sees only the final 'up'. If this fails after an " +
                "Input System upgrade, the package changed that path — the action-based handler stays correct; update the premise");
        }

        [Test]
        public void BackAction_CatchesPressThatWentDownAndUpInOneUpdate()
        {
            _keyboard = InputSystem.AddDevice<Keyboard>();
            _back = ScreenBackButtonHandler.CreateBackAction();
            _back.Enable();
            InputSystem.Update();

            PressAndReleaseInOneUpdate();

            Assert.That(_back.WasPerformedThisFrame(), Is.True, "the very first back of the session must count");
        }

        [Test]
        public void BackAction_KeyboardAddedAfterEnable_CatchesFirstPress()
        {
            _back = ScreenBackButtonHandler.CreateBackAction();
            _back.Enable();
            _keyboard = InputSystem.AddDevice<Keyboard>();
            InputSystem.Update();

            PressAndReleaseInOneUpdate();

            Assert.That(_back.WasPerformedThisFrame(), Is.True,
                "the action binds every keyboard, including one that appears after the handler was built");
        }

        [Test]
        public void BackAction_NextUpdateWithoutPress_IsNotPerformed()
        {
            _keyboard = InputSystem.AddDevice<Keyboard>();
            _back = ScreenBackButtonHandler.CreateBackAction();
            _back.Enable();
            InputSystem.Update();

            PressAndReleaseInOneUpdate();
            Assume.That(_back.WasPerformedThisFrame(), Is.True);

            InputSystem.Update();

            Assert.That(_back.WasPerformedThisFrame(), Is.False, "one press must close one popup, not one per frame");
        }

        private void PressAndReleaseInOneUpdate()
        {
            InputSystem.QueueStateEvent(_keyboard, new KeyboardState(Key.Escape));
            InputSystem.QueueStateEvent(_keyboard, new KeyboardState());
            InputSystem.Update();
        }
    }
}
