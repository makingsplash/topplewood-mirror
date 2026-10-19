using Gameplay.Core;
using Newtonsoft.Json;
using NUnit.Framework;

namespace Gameplay.Tests
{
    /// <summary>
    /// TASK-617: the optional <c>fan</c> flag of an authored air zone. Absent must keep every existing level drawing
    /// its fan; <c>false</c> must reach the domain as <see cref="AirZone.HasFan"/>=false without touching the force.
    /// </summary>
    [TestFixture]
    public sealed class AirZoneDtoTests
    {
        private const string Zone =
            "\"region\": { \"min\": { \"x\": -5.0, \"y\": 0.05 }, \"max\": { \"x\": -0.3, \"y\": 4.9 } }, " +
            "\"direction\": { \"x\": -1, \"y\": 0 }, \"acceleration\": 600";

        [Test]
        public void FanFlag_Absent_DrawsFan()
        {
            var zone = Parse("{" + Zone + "}");
            Assert.That(zone.HasFan, Is.True, "pre-TASK-617 levels must keep their fan");
        }

        [Test]
        public void FanFlag_False_ReachesDomain_ForceUnchanged()
        {
            var withFan = Parse("{" + Zone + "}");
            var fanless = Parse("{" + Zone + ", \"fan\": false}");

            Assert.That(fanless.HasFan, Is.False);
            Assert.That(fanless.Region.Min.X, Is.EqualTo(withFan.Region.Min.X));
            Assert.That(fanless.Region.Max.Y, Is.EqualTo(withFan.Region.Max.Y));
            Assert.That(fanless.Direction.X, Is.EqualTo(withFan.Direction.X));
            Assert.That(fanless.Acceleration, Is.EqualTo(withFan.Acceleration));
        }

        [Test]
        public void FanFlag_True_DrawsFan()
        {
            Assert.That(Parse("{" + Zone + ", \"fan\": true}").HasFan, Is.True);
        }

        private static AirZone Parse(string json) => JsonConvert.DeserializeObject<AirZoneDto>(json).ToDomain();
    }
}
