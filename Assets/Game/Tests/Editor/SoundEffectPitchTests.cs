using NUnit.Framework;
using SGS.Services.Audio;
using UnityEditor;
using UnityEngine;

namespace Gameplay.Tests
{
    /// <summary>
    /// TASK-667 plan §8 T2: <see cref="SoundEffect"/> pitch per play — the semitone ladder from one clip, the jitter
    /// range, no pitch leak between plays of a pooled AudioSource, and an empty clip bag that plays nothing instead of
    /// throwing.
    /// </summary>
    [TestFixture]
    public sealed class SoundEffectPitchTests
    {
        private GameObject _host;
        private AudioSource _source;
        private AudioClip _clip;
        private SoundEffect _effect;

        [SetUp]
        public void SetUp()
        {
            _host = new GameObject("SoundEffectPitchTests");
            _source = _host.AddComponent<AudioSource>();
            _clip = AudioClip.Create("tick", 441, 1, 44100, false);
            _effect = ScriptableObject.CreateInstance<SoundEffect>();
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(_effect);
            Object.DestroyImmediate(_clip);
            Object.DestroyImmediate(_host);
        }

        private void Configure(float jitter, params AudioClip[] clips)
        {
            var so = new SerializedObject(_effect);
            so.FindProperty("_pitchJitter").floatValue = jitter;
            var bag = so.FindProperty("_clips");
            bag.arraySize = clips.Length;
            for (var i = 0; i < clips.Length; i++)
                bag.GetArrayElementAtIndex(i).objectReferenceValue = clips[i];
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        [Test]
        public void Semitones_SetThePitch_WithoutJitter()
        {
            Configure(0f, _clip);
            _effect.Play(_source, 1f, 4f);
            Assert.That(_source.pitch, Is.EqualTo(1.259921f).Within(1e-4f));
            _effect.Play(_source, 1f, 2f);
            Assert.That(_source.pitch, Is.EqualTo(1.122462f).Within(1e-4f));
        }

        [Test]
        public void PitchDoesNotLeak_BetweenPlaysOfOneSource()
        {
            Configure(0f, _clip);
            _effect.Play(_source, 1f, 4f);
            _effect.Play(_source, 1f, 0f);
            Assert.That(_source.pitch, Is.EqualTo(1f).Within(1e-6f));
            _effect.Play(_source, 1f, 4f);
            _effect.Play(_source); // the legacy overload resets it too
            Assert.That(_source.pitch, Is.EqualTo(1f).Within(1e-6f));
        }

        [Test]
        public void Jitter_StaysWithinItsFraction_AndActuallyVaries()
        {
            Configure(0.06f, _clip);
            var min = float.MaxValue;
            var max = float.MinValue;
            for (var i = 0; i < 200; i++)
            {
                _effect.Play(_source, 1f, 0f);
                min = Mathf.Min(min, _source.pitch);
                max = Mathf.Max(max, _source.pitch);
            }
            Assert.That(min, Is.GreaterThanOrEqualTo(0.94f - 1e-5f));
            Assert.That(max, Is.LessThanOrEqualTo(1.06f + 1e-5f));
            Assert.That(max - min, Is.GreaterThan(0.01f), "a jitter of 6 % must move the pitch");
        }

        [Test]
        public void VolumeScale_MultipliesTheAssetVolume()
        {
            Configure(0f, _clip);
            var so = new SerializedObject(_effect);
            so.FindProperty("_volume").floatValue = 0.5f;
            so.ApplyModifiedPropertiesWithoutUndo();
            _effect.Play(_source, 0.3f, 0f);
            Assert.That(_source.volume, Is.EqualTo(0.15f).Within(1e-5f));
        }

        [Test]
        public void EmptyBag_PlaysNothing_AndDoesNotThrow()
        {
            Configure(0f);
            Assert.DoesNotThrow(() => _effect.Play(_source, 1f, 0f));
            Assert.DoesNotThrow(() => _effect.Play(_source, 0));
            Assert.That(_source.clip, Is.Null);
        }

        [Test]
        public void PitchFor_IsTheEqualTemperedRatio()
        {
            Assert.That(SoundEffect.PitchFor(12f, 0f), Is.EqualTo(2f).Within(1e-5f));
            Assert.That(SoundEffect.PitchFor(0f, 0.05f), Is.EqualTo(1.05f).Within(1e-6f));
        }
    }
}
