using Gameplay.PlayScreen;
using NUnit.Framework;
using UnityEngine;

namespace Gameplay.Tests
{
    /// <summary>TASK-613 plan §8 T9: the HUD cover rule with the tour focus — a covering screen wins (hidden), the tour
    /// dims to the focused alpha and takes the taps away, neither leaves it fully open.</summary>
    [TestFixture]
    public sealed class HudCoverTests
    {
        private GameObject _go;
        private CanvasGroup _group;
        private PlayScreenInteraction _interaction;

        [SetUp]
        public void SetUp()
        {
            _go = new GameObject("hud", typeof(CanvasGroup));
            _group = _go.GetComponent<CanvasGroup>();
            _interaction = new PlayScreenInteraction();
        }

        [TearDown]
        public void TearDown() => Object.DestroyImmediate(_go);

        [Test]
        public void Covered_WinsOverFocus()
        {
            HudCover.Apply(_group, covered: true, focused: true, focusedAlpha: 0.35f);

            Assert.That(_group.alpha, Is.EqualTo(0f));
            Assert.That(_group.blocksRaycasts, Is.False);
        }

        [Test]
        public void Focused_DimsAndBlocksTaps()
        {
            HudCover.Apply(_group, covered: false, focused: true, focusedAlpha: 0.35f);

            Assert.That(_group.alpha, Is.EqualTo(0.35f));
            Assert.That(_group.blocksRaycasts, Is.False);
            Assert.That(_group.interactable, Is.False);
        }

        [Test]
        public void Neither_IsFullyOpen()
        {
            HudCover.Apply(_group, covered: false, focused: false, focusedAlpha: 0.35f);

            Assert.That(_group.alpha, Is.EqualTo(1f));
            Assert.That(_group.blocksRaycasts, Is.True);
            Assert.That(_group.interactable, Is.True);
        }

        [Test]
        public void Bind_FollowsTheTourFlag()
        {
            using var _ = HudCover.Bind(_group, _interaction, 0.35f);
            Assert.That(_group.alpha, Is.EqualTo(1f));

            _interaction.IsTourFocused.Value = true;
            Assert.That(_group.alpha, Is.EqualTo(0.35f));
            Assert.That(_group.blocksRaycasts, Is.False);

            _interaction.IsTourFocused.Value = false;
            Assert.That(_group.alpha, Is.EqualTo(1f));
            Assert.That(_group.blocksRaycasts, Is.True);
        }
    }
}
