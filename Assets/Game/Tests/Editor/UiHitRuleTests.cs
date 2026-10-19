using Game.Services.Input;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Gameplay.Tests
{
    /// <summary>
    /// TASK-638: <see cref="InputService.IsUiHit"/> — a tap that a uGUI graphic takes must not reach gameplay as well.
    /// The code-built HUD buttons live on layer Default (<c>new GameObject(...)</c>), so the old layer-only rule let one
    /// tap on the Hint button hit the button AND the field; during the hint tour the field half became the miss pulse.
    /// </summary>
    [TestFixture]
    public sealed class UiHitRuleTests
    {
        private const int UiLayer = 5;
        private GameObject _canvas;
        private GameObject _button;

        [SetUp]
        public void SetUp()
        {
            _canvas = new GameObject("canvas", typeof(Canvas), typeof(GraphicRaycaster));
            _button = new GameObject("button", typeof(Image));
            _button.transform.SetParent(_canvas.transform, false);
        }

        [TearDown]
        public void TearDown() => Object.DestroyImmediate(_canvas);

        [Test]
        public void CodeBuiltHudGraphic_OnLayerDefault_IsUi()
        {
            Assert.That(_button.layer, Is.EqualTo(0), "the HUD presenters build their buttons on layer Default");
            Assert.That(InputService.IsUiHit(_canvas.GetComponent<GraphicRaycaster>(), _button, UiLayer), Is.True);
        }

        [Test]
        public void UiLayerObject_IsUi_WhateverTheRaycaster()
        {
            _button.layer = UiLayer;
            Assert.That(InputService.IsUiHit(null, _button, UiLayer), Is.True);
        }

        [Test]
        public void NonGraphicHit_OffTheUiLayer_IsNotUi()
        {
            // Fully qualified: the enclosing Gameplay namespace has a sibling Gameplay.Camera (CS0118).
            var camera = new GameObject("camera", typeof(UnityEngine.Camera), typeof(PhysicsRaycaster));
            try
            {
                Assert.That(InputService.IsUiHit(camera.GetComponent<PhysicsRaycaster>(), _button, UiLayer), Is.False,
                    "a world object hit by a physics raycaster is the field's, not the UI's");
            }
            finally
            {
                Object.DestroyImmediate(camera);
            }
        }
    }
}
