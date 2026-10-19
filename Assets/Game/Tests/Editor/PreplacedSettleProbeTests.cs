using Gameplay.Core;
using Gameplay.Core.Sim;
using NUnit.Framework;

namespace Gameplay.Tests
{
    /// <summary>
    /// TASK-629: the measure behind the sweep's <c>Preplaced settle:</c> warning — the largest vertex shift between a
    /// preplaced piece's authored start and its rest pose — and the tolerance it is judged by. Scene-free: the probe's
    /// physics half runs in the sweep; these pin the geometry and the constants it relies on.
    /// </summary>
    public sealed class PreplacedSettleProbeTests
    {
        [Test]
        public void SolverRestLift_IsTwoPolygonSkinsMinusSlop()
        {
            // 2·0.01 − 0.005: what every flush polygon-on-polygon preplaced piece of the TASK-629 sweep rose by.
            Assert.That(PreplacedSettleProbe.SolverRestLift, Is.EqualTo(0.015f).Within(1e-6f));
            Assert.That(PreplacedSettleProbe.Tolerance,
                Is.EqualTo(LevelDataValidator.PreplacedRestTolerance + 0.015f).Within(1e-6f));
        }

        [Test]
        public void PureLift_ShiftsEveryVertexByTheLift()
        {
            var shift = PreplacedSettleProbe.MaxVertexShift(Shape.Plank(4f, 0.3f),
                new Vec2(1f, 1.1f), 0f, new Vec2(1f, 1.115f), 0f);
            Assert.That(shift, Is.EqualTo(0.015f).Within(1e-5f));
            Assert.That(shift, Is.LessThanOrEqualTo(PreplacedSettleProbe.Tolerance), "an honest flush rest is not a warning");
        }

        [Test]
        public void Tilt_IsMeasuredAtTheTip_NotAtTheCentre()
        {
            // lever_lid's lever turning 0.38 rad about a fixed centre: the centre does not move, the corner travels
            // 2·|v|·sin(θ/2) with |v| = √(2² + 0.15²).
            var shift = PreplacedSettleProbe.MaxVertexShift(Shape.Plank(4f, 0.3f),
                new Vec2(0f, 2f), 0f, new Vec2(0f, 2f), -0.38f);
            var corner = System.MathF.Sqrt(4f + 0.0225f);
            Assert.That(shift, Is.EqualTo(2f * corner * System.MathF.Sin(0.19f)).Within(1e-4f));
            Assert.That(shift, Is.GreaterThan(PreplacedSettleProbe.Tolerance));
        }
    }
}
