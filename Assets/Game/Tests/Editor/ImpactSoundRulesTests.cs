using System.Collections.Generic;
using Gameplay.Audio;
using Gameplay.Core.Adapters;
using NUnit.Framework;

namespace Gameplay.Tests
{
    /// <summary>
    /// TASK-667 plan §8 T1: which impacts of a frame sound and how loud (<see cref="ImpactSoundRules"/>) — threshold,
    /// one voice per body per frame, cooldown, the voice limit keeps the strongest, volume range and monotonicity,
    /// <see cref="ImpactSoundRules.Begin"/> resets. Time is a number, so every case is deterministic.
    /// </summary>
    [TestFixture]
    public sealed class ImpactSoundRulesTests
    {
        private static ImpactSoundTuning Tuning() => new()
        {
            MinSpeed = 1f, FullSpeed = 5f, MinVolume = 0.15f, MaxVolume = 0.45f, CooldownSeconds = 0.12f,
            MaxVoicesPerFrame = 3,
        };

        private static List<ImpactVoice> Resolve(ImpactSoundRules rules, float now, params PieceImpact[] frame)
        {
            var voices = new List<ImpactVoice>();
            var count = rules.Resolve(frame, now, voices);
            Assert.That(count, Is.EqualTo(voices.Count), "returned count must match the voices written");
            return voices;
        }

        [Test]
        public void BelowThreshold_IsSilent_AtThreshold_Sounds()
        {
            var rules = new ImpactSoundRules(Tuning());
            Assert.That(Resolve(rules, 0f, new PieceImpact(1, 0.99f)), Is.Empty);
            Assert.That(Resolve(rules, 1f, new PieceImpact(1, 1f)), Has.Count.EqualTo(1));
        }

        [Test]
        public void TwoImpactsOfOneBodyInAFrame_GiveOneVoice_AtTheStrongerVolume()
        {
            var rules = new ImpactSoundRules(Tuning());
            var voices = Resolve(rules, 0f, new PieceImpact(7, 2f), new PieceImpact(7, 4f));
            Assert.That(voices, Has.Count.EqualTo(1));
            Assert.That(voices[0].BodyId, Is.EqualTo(7));
            Assert.That(voices[0].VolumeScale, Is.EqualTo(rules.Volume(4f)).Within(1e-6f));
        }

        [Test]
        public void SameBody_InCooldown_IsSilent_AfterCooldown_Sounds()
        {
            var rules = new ImpactSoundRules(Tuning());
            Assert.That(Resolve(rules, 1f, new PieceImpact(3, 3f)), Has.Count.EqualTo(1));
            Assert.That(Resolve(rules, 1.05f, new PieceImpact(3, 3f)), Is.Empty, "inside the 0.12 s cooldown");
            Assert.That(Resolve(rules, 1.13f, new PieceImpact(3, 3f)), Has.Count.EqualTo(1), "after the cooldown");
        }

        [Test]
        public void Cooldown_IsPerBody()
        {
            var rules = new ImpactSoundRules(Tuning());
            Resolve(rules, 1f, new PieceImpact(3, 3f));
            Assert.That(Resolve(rules, 1.01f, new PieceImpact(4, 3f)), Has.Count.EqualTo(1));
        }

        [Test]
        public void FiveImpacts_KeepTheThreeStrongest_StrongestFirst()
        {
            var rules = new ImpactSoundRules(Tuning());
            var voices = Resolve(rules, 0f,
                new PieceImpact(1, 1.5f), new PieceImpact(2, 4.5f), new PieceImpact(3, 2.5f),
                new PieceImpact(4, 3.5f), new PieceImpact(5, 1.2f));
            Assert.That(voices, Has.Count.EqualTo(3));
            Assert.That(voices[0].BodyId, Is.EqualTo(2));
            Assert.That(voices[1].BodyId, Is.EqualTo(4));
            Assert.That(voices[2].BodyId, Is.EqualTo(3));
        }

        [Test]
        public void ImpactCutByTheVoiceLimit_DoesNotStartACooldown()
        {
            var rules = new ImpactSoundRules(Tuning());
            Resolve(rules, 0f, new PieceImpact(1, 4f), new PieceImpact(2, 4f), new PieceImpact(3, 4f),
                new PieceImpact(9, 2f));
            Assert.That(Resolve(rules, 0.02f, new PieceImpact(9, 2f)), Has.Count.EqualTo(1),
                "body 9 was never heard, so nothing silences its next knock");
        }

        [Test]
        public void Volume_StaysInRange_AndGrowsWithSpeed()
        {
            var rules = new ImpactSoundRules(Tuning());
            Assert.That(rules.Volume(1f), Is.EqualTo(0.15f).Within(1e-6f));
            Assert.That(rules.Volume(5f), Is.EqualTo(0.45f).Within(1e-6f));
            Assert.That(rules.Volume(50f), Is.EqualTo(0.45f).Within(1e-6f), "clamped above FullSpeed");
            var previous = 0f;
            for (var s = 1f; s <= 5f; s += 0.25f)
            {
                var v = rules.Volume(s);
                Assert.That(v, Is.InRange(0.15f, 0.45f));
                Assert.That(v, Is.GreaterThanOrEqualTo(previous));
                previous = v;
            }
            Assert.That(rules.Volume(3f), Is.GreaterThan(rules.Volume(2f)));
        }

        [Test]
        public void Begin_ResetsCooldowns()
        {
            var rules = new ImpactSoundRules(Tuning());
            Resolve(rules, 1f, new PieceImpact(3, 3f));
            rules.Begin();
            Assert.That(Resolve(rules, 1.01f, new PieceImpact(3, 3f)), Has.Count.EqualTo(1));
        }

        [Test]
        public void EmptyFrame_GivesNoVoices_AndClearsTheOutput()
        {
            var rules = new ImpactSoundRules(Tuning());
            var voices = new List<ImpactVoice> { new(1, 1f) };
            Assert.That(rules.Resolve(new List<PieceImpact>(), 0f, voices), Is.Zero);
            Assert.That(voices, Is.Empty);
        }
    }
}
