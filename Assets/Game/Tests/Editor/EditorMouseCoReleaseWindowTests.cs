using System.Collections.Generic;
using Gameplay.Adapters;
using Gameplay.Core;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Gameplay.Tests
{
    /// <summary>
    /// TASK-482: the editor-mouse override of <see cref="ShapeFillTuning.CoReleaseWindowSteps"/>. With no touchscreen
    /// the window is the wide editor value (a pair is clickable member by member); with a touchscreen it stays the
    /// shipped device value (4). The probe is injected, so both branches run headless; the level is a bare
    /// <see cref="LevelData"/> (no pieces) — only its <see cref="LevelData.CoReleaseGroups"/> matter for the log.
    /// </summary>
    [TestFixture]
    public sealed class EditorMouseCoReleaseWindowTests
    {
        [Test]
        public void NoTouchscreen_WidensWindowToEditorValue()
        {
            var tuning = new ShapeFillTuning();
            var window = new EditorMouseCoReleaseWindow(new GameModel(), tuning, () => false);

            window.Start();

            Assert.That(tuning.CoReleaseWindowSteps, Is.EqualTo(EditorMouseCoReleaseWindow.EditorWindowSteps));
            Assert.That(EditorMouseCoReleaseWindow.EditorWindowSteps, Is.EqualTo(36));
        }

        [Test]
        public void Touchscreen_KeepsDeviceWindow()
        {
            var tuning = new ShapeFillTuning();
            var window = new EditorMouseCoReleaseWindow(new GameModel(), tuning, () => true);

            window.Start();

            Assert.That(tuning.CoReleaseWindowSteps, Is.EqualTo(4));
        }

        [Test]
        public void TouchscreenAppears_NextLevelLoadRestoresDeviceWindow()
        {
            var tuning = new ShapeFillTuning();
            var model = new GameModel();
            var hasTouch = false;
            var window = new EditorMouseCoReleaseWindow(model, tuning, () => hasTouch);
            window.Start();
            Assert.That(tuning.CoReleaseWindowSteps, Is.EqualTo(36));

            hasTouch = true; // e.g. the Device Simulator was switched on
            model.LoadLevel(Level("solo", null));

            Assert.That(tuning.CoReleaseWindowSteps, Is.EqualTo(4));
            window.Dispose();
        }

        [Test]
        public void LevelWithPair_LogsEditorWindow()
        {
            var model = new GameModel();
            var window = new EditorMouseCoReleaseWindow(model, new ShapeFillTuning(), () => false);
            window.Start();

            LogAssert.Expect(LogType.Log, new System.Text.RegularExpressions.Regex("пара: окно редактора 36 шагов"));
            model.LoadLevel(Level("heart_halves", new List<IReadOnlyList<int>> { new[] { 0, 1 } }));

            window.Dispose();
        }

        private static LevelData Level(string id, IReadOnlyList<IReadOnlyList<int>> groups) => new LevelData
        {
            LevelId = id,
            Pieces = new List<PiecePlacement>(),
            CoReleaseGroups = groups,
        };
    }
}
