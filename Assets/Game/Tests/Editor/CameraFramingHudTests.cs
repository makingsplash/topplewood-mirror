using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Gameplay.Core;
using NUnit.Framework;

namespace Gameplay.Tests
{
    /// <summary>
    /// TASK-539: <see cref="CameraFramingMath.TryFrame"/> keeps the figure and the pieces out of the hint button's
    /// bottom-right corner (<see cref="HudCornerZone.HintButton"/>). Fixtures prove the three regimes — untouched,
    /// raised within the vertical slack, zoomed out — and the mirrored left corner; <see cref="RealLineup_NoLevelInHudCorner"/>
    /// runs the whole real line-up at three phone aspects and writes the before/after list to
    /// <c>Logs/TASK-539-hud-corner.txt</c>. The real-line-up test is the one that fails on the pre-TASK-539 framing
    /// (v_cup and others reach the corner on 20:9).
    /// </summary>
    [TestFixture]
    public sealed class CameraFramingHudTests
    {
        private const float Eps = 1e-4f;
        private const float Margin = 0.6f, Band = 0.2f, MinOrtho = 3.5f;
        private static readonly MaterialProfile Wood = new MaterialProfile(1.0f, 0.6f, 0.05f);
        private static readonly HudCornerZone Hint = HudCornerZone.HintButton;

        private static readonly (string name, float aspect)[] Phones =
        {
            ("20:9", 1080f / 2400f),
            ("19.5:9", 1080f / 2340f),
            ("16:9", 1080f / 1920f),
        };

        [Test]
        public void ContentAwayFromCorner_FrameUnchanged()
        {
            // A 3-wide row high up: nothing near the bottom-right corner.
            var level = Level(Pieces((-1f, 4f), (0f, 4f), (1f, 4f), (0f, 0f)));
            Old(level, 0.45f, out var oldSize, out var oldCentre);

            Assert.That(CameraFramingMath.TryFrame(level, 0.45f, Margin, Band, MinOrtho, Hint, 0f, 0f,
                out var size, out var centre), Is.True);
            Assert.That(size, Is.EqualTo(oldSize).Within(Eps));
            Assert.That(centre.X, Is.EqualTo(oldCentre.X).Within(Eps));
            Assert.That(centre.Y, Is.EqualTo(oldCentre.Y).Within(Eps));
        }

        [Test]
        public void WidthBound_PieceInCorner_RaisedWithoutZoom()
        {
            // 9 wide × 12 tall: at 0.45 the frame is width-bound (10.6 vs 8.25), so there is vertical slack to spend,
            // yet tall enough for the bottom-right box to reach the corner.
            var level = Level(Pieces((-4f, -5.5f), (4f, -5.5f), (0f, 5.5f)));
            const float aspect = 0.45f;
            Old(level, aspect, out var oldSize, out var oldCentre);
            Assert.That(CameraFramingMath.ContentInHudCorner(level, oldSize, oldCentre, aspect, Hint, 0f, 0f),
                Is.True, "fixture must start in the corner");

            CameraFramingMath.TryFrame(level, aspect, Margin, Band, MinOrtho, Hint, 0f, 0f, out var size, out var centre);
            Assert.That(size, Is.EqualTo(oldSize).Within(Eps), "slack is enough — no zoom-out");
            Assert.That(centre.Y, Is.LessThan(oldCentre.Y), "camera lowered = content raised");
            Assert.That(CameraFramingMath.ContentInHudCorner(level, size, centre, aspect, Hint, 0f, 0f), Is.False);
            AssertBelowPreviewBand(level, size, centre);
        }

        [Test]
        public void HeightBound_PieceInCorner_ZoomsOutJustEnough()
        {
            // Tall column with a foot to the right: height-bound, no slack, only zoom can free the corner.
            var level = Level(Pieces((0f, 0f), (0f, 1f), (0f, 2f), (0f, 3f), (0f, 4f), (0f, 5f), (2f, 0f)));
            const float aspect = 0.45f;
            Old(level, aspect, out var oldSize, out var oldCentre);
            Assert.That(CameraFramingMath.ContentInHudCorner(level, oldSize, oldCentre, aspect, Hint, 0f, 0f),
                Is.True, "fixture must start in the corner");

            CameraFramingMath.TryFrame(level, aspect, Margin, Band, MinOrtho, Hint, 0f, 0f, out var size, out var centre);
            Assert.That(size, Is.GreaterThan(oldSize));
            Assert.That(CameraFramingMath.ContentInHudCorner(level, size, centre, aspect, Hint, 0f, 0f), Is.False);
            AssertBelowPreviewBand(level, size, centre);

            // Minimal: 1% less zoom (at the most raised frame that size allows) is back in the corner.
            var smaller = size * 0.99f;
            CameraFramingMath.TryContentBounds(level, out var min, out var max);
            var raised = new Vec2(centre.X, (min.Y + max.Y) * 0.5f + smaller * Band - MaxSlack(level, smaller));
            Assert.That(CameraFramingMath.ContentInHudCorner(level, smaller, raised, aspect, Hint, 0f, 0f), Is.True);
        }

        [Test]
        public void SafeAreaInset_GrowsTheCorner()
        {
            Hint.ToViewport(0.45f, 0f, 0f, out var side0, out var top0);
            Hint.ToViewport(0.45f, 0.02f, 0.05f, out var side1, out var top1);
            Assert.That(side1, Is.EqualTo(side0 + 0.02f).Within(Eps));
            Assert.That(top1, Is.EqualTo(top0 + 0.05f).Within(Eps));
        }

        [Test]
        public void LeftCorner_IsTheMirror()
        {
            var left = new HudCornerZone(false, false, Hint.SideInset, Hint.EdgeInset, Hint.Width, Hint.Height, Hint.Pad,
                Hint.RefWidth, Hint.RefHeight, Hint.Match);
            var level = Level(Pieces((0f, 0f), (0f, 1f), (0f, 2f), (0f, 3f), (0f, 4f), (0f, 5f), (2f, 0f)));
            var mirrored = Level(Pieces((0f, 0f), (0f, 1f), (0f, 2f), (0f, 3f), (0f, 4f), (0f, 5f), (-2f, 0f)));

            CameraFramingMath.TryFrame(level, 0.45f, Margin, Band, MinOrtho, Hint, 0f, 0f, out var rSize, out var rCentre);
            CameraFramingMath.TryFrame(mirrored, 0.45f, Margin, Band, MinOrtho, left, 0f, 0f, out var lSize, out var lCentre);
            Assert.That(lSize, Is.EqualTo(rSize).Within(Eps));
            Assert.That(lCentre.X, Is.EqualTo(-rCentre.X).Within(Eps));
            Assert.That(lCentre.Y, Is.EqualTo(rCentre.Y).Within(Eps));
        }

        [Test]
        public void RealLineup_NoLevelInHudCorner()
        {
            var source = new JsonLevelSource(new FileStore(), new LevelDataValidator());
            var report = new StringBuilder();
            report.AppendLine("TASK-539 — фигура/детали в углу кнопки подсказки, расчёт CameraFramingMath, safe-area 0");
            var after = new List<string>();

            foreach (var (name, aspect) in Phones)
            {
                report.AppendLine($"\n## {name} (aspect {aspect:0.####})");
                var before = 0;
                for (var i = 0; i < source.Count; i++)
                {
                    var level = source.Load(i);
                    if (level == null)
                        continue;
                    Old(level, aspect, out var oldSize, out var oldCentre);
                    if (!CameraFramingMath.ContentInHudCorner(level, oldSize, oldCentre, aspect, Hint, 0f, 0f))
                        continue;

                    before++;
                    CameraFramingMath.TryFrame(level, aspect, Margin, Band, MinOrtho, Hint, 0f, 0f,
                        out var size, out var centre);
                    var still = CameraFramingMath.ContentInHudCorner(level, size, centre, aspect, Hint, 0f, 0f);
                    if (still)
                        after.Add($"{name} #{i + 1} {level.LevelId}");
                    report.AppendLine($"#{i + 1} {level.LevelId}: до — в углу; после — {(still ? "В УГЛУ" : "чисто")}; " +
                                      $"orthoSize {oldSize:0.###} → {size:0.###} (×{size / oldSize:0.###}), " +
                                      $"подъём {(oldCentre.Y + (size - oldSize) * Band - centre.Y):0.###}");
                }
                report.AppendLine($"итого до: {before} из {source.Count}");
            }
            report.AppendLine($"\nпосле правки в углу: {after.Count}");

            Directory.CreateDirectory("Logs");
            File.WriteAllText("Logs/TASK-539-hud-corner.txt", report.ToString());
            TestContext.Out.WriteLine(report.ToString());

            Assert.That(source.Count, Is.GreaterThan(0));
            Assert.That(after, Is.Empty, string.Join("\n", after));
        }

        // ---------------------------------------------------------------- TASK-563: top corners

        private static readonly HudCornerZone[] Top = HudCornerZone.TopButtons;

        [Test]
        public void TopZone_ReachingBelowBand_ZoomsOutUntilClear()
        {
            // A top-right zone deep enough to leave the preview band (930 units ≈ 43 % of a 20:9 screen) over a column
            // with a block at its top-right: only zoom can free it. The real buttons never reach this deep — the
            // fixture is what proves the pass is not a no-op.
            var deep = new HudCornerZone(true, true, 28f, 30f, 300f, 900f, 0f, 1080f, 1920f, 0.5f);
            var level = Level(Pieces((0f, 0f), (0f, 1f), (0f, 2f), (0f, 3f), (0f, 4f), (0f, 5f), (2f, 5f)));
            const float aspect = 0.45f;
            Old(level, aspect, out var oldSize, out var oldCentre);
            Assert.That(CameraFramingMath.ContentInHudCorner(level, oldSize, oldCentre, aspect, deep, 0f, 0f),
                Is.True, "fixture must start in the top corner");

            Assert.That(CameraFramingMath.TryFrame(level, aspect, Margin, Band, MinOrtho, Hint, 0f, 0f,
                new[] { deep }, default, out var size, out var centre), Is.True);
            Assert.That(size, Is.GreaterThan(oldSize));
            Assert.That(CameraFramingMath.ContentInHudCorner(level, size, centre, aspect, deep, 0f, 0f), Is.False);
            Assert.That(CameraFramingMath.ContentInHudCorner(level, size, centre, aspect, Hint, 0f, 0f), Is.False,
                "the bottom corner stays clear while the top one is freed");
            AssertBelowPreviewBand(level, size, centre);

            // Minimal: 1 % less zoom is back in the top corner.
            CameraFramingMath.TryFrame(level, aspect, Margin, Band, size * 0.99f, Hint, 0f, 0f, out var s, out var c);
            Assert.That(CameraFramingMath.ContentInHudCorner(level, s, c, aspect, deep, 0f, 0f), Is.True);
        }

        [Test]
        public void TopZone_SafeTopInset_GrowsTheDepth()
        {
            var zone = HudCornerZone.RestartButton;
            zone.ToViewport(0.45f, 0f, 0f, out var side0, out var depth0);
            zone.ToViewport(0.45f, 0.02f, 0.05f, out var side1, out var depth1);
            Assert.That(side1, Is.EqualTo(side0 + 0.02f).Within(Eps));
            Assert.That(depth1, Is.EqualTo(depth0 + 0.05f).Within(Eps));
        }

        /// <summary>
        /// The before/after proof for TASK-563: on every level of the real line-up, at phone and tablet aspects and
        /// with and without a cutout's safe top, the frame WITH the top-corner reserve is bit-identical to the frame
        /// without it — the buttons live inside the preview band, which content never enters. Writes the comparison
        /// to <c>Logs/TASK-563-top-corners.txt</c>.
        /// </summary>
        [Test]
        public void RealLineup_TopCornerReserve_LeavesFrameUnchanged()
        {
            var source = new JsonLevelSource(new FileStore(), new LevelDataValidator());
            var aspects = Phones.Concat(new[] { ("21:9", 1080f / 2520f), ("3:4", 0.75f) }).ToArray();
            var safeTops = new[] { 0f, 0.05f };
            var report = new StringBuilder();
            report.AppendLine("TASK-563 — кадр с резервом верхних углов против кадра без него, CameraFramingMath");
            var changed = new List<string>();
            var frames = 0;

            foreach (var (name, aspect) in aspects)
            foreach (var safeTop in safeTops)
            {
                var same = 0;
                for (var i = 0; i < source.Count; i++)
                {
                    var level = source.Load(i);
                    if (level == null)
                        continue;
                    CameraFramingMath.TryFrame(level, aspect, Margin, Band, MinOrtho, Hint, 0f, 0f,
                        out var oldSize, out var oldCentre);
                    CameraFramingMath.TryFrame(level, aspect, Margin, Band, MinOrtho, Hint, 0f, 0f, Top,
                        new CameraFramingMath.SafeInsets(0f, 0f, 0f, safeTop), out var size, out var centre);
                    frames++;
                    // Exact equality on purpose: the claim is "unchanged", not "close".
                    if (size == oldSize && centre.X == oldCentre.X && centre.Y == oldCentre.Y)
                    {
                        same++;
                        continue;
                    }
                    changed.Add($"{name} safeTop {safeTop} #{i + 1} {level.LevelId}: orthoSize {oldSize} → {size}, " +
                                $"centre ({oldCentre.X}, {oldCentre.Y}) → ({centre.X}, {centre.Y})");
                }
                HudCornerZone.RestartButton.ToViewport(aspect, 0f, safeTop, out var side, out var depth);
                report.AppendLine($"{name} (aspect {aspect:0.####}), safeTop {safeTop:0.##}: угол {side:P1} ширины × " +
                                  $"{depth:P1} высоты (полоса превью {Band:P0}); кадр совпал у {same} из {source.Count}");
            }
            report.AppendLine($"\nвсего кадров: {frames}, изменилось: {changed.Count}");
            foreach (var line in changed)
                report.AppendLine(line);

            Directory.CreateDirectory("Logs");
            File.WriteAllText("Logs/TASK-563-top-corners.txt", report.ToString());
            TestContext.Out.WriteLine(report.ToString());

            Assert.That(source.Count, Is.GreaterThan(0));
            Assert.That(changed, Is.Empty, string.Join("\n", changed));
        }

        // ---------------------------------------------------------------- helpers

        // The pre-TASK-539 frame: content-centred, raised by size·band, no HUD reserve.
        private static void Old(LevelData level, float aspect, out float size, out Vec2 centre)
        {
            Assert.That(CameraFramingMath.TryFrame(level, aspect, Margin, Band, MinOrtho, null, 0f, 0f,
                out size, out centre), Is.True);
        }

        private static float MaxSlack(LevelData level, float size)
        {
            CameraFramingMath.TryContentBounds(level, out var min, out var max);
            return System.MathF.Max(0f, size * (1f - Band) - (max.Y - min.Y) * 0.5f - Margin);
        }

        // The content top stays under the preview band with the margin — raising never eats the preview.
        private static void AssertBelowPreviewBand(LevelData level, float size, Vec2 centre)
        {
            CameraFramingMath.TryContentBounds(level, out _, out var max);
            var usableTop = centre.Y + size - 2f * size * Band;
            Assert.That(max.Y + Margin, Is.LessThanOrEqualTo(usableTop + Eps));
        }

        private static LevelData Level(IReadOnlyList<PiecePlacement> pieces) => new LevelData { Pieces = pieces };

        // 1 × 1 boxes at the given centres.
        private static IReadOnlyList<PiecePlacement> Pieces(params (float x, float y)[] centres)
        {
            var type = new PieceType("box", Shape.Box(1f, 1f), Wood);
            return centres.Select(c => new PiecePlacement(type, new Vec2(c.x, c.y))).ToArray();
        }

        private sealed class FileStore : ILevelStore
        {
            public string ReadText(string name)
            {
                var path = Path.Combine("Assets/StreamingAssets", name);
                return File.Exists(path) ? File.ReadAllText(path) : null;
            }
        }
    }
}
