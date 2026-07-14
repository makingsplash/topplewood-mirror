using System;
using SGS.Configs;
using UnityEngine;

namespace Gameplay.Configs
{
    [CreateAssetMenu(fileName = "AnimationsConfig", menuName = "Configs/AnimationsConfig")]
    public class AnimationsConfig : AnimationsConfigBase, ISerializationCallbackReceiver
    {
        [SerializeField] private CameraSettings _camera;
        [SerializeField] private PopupAnimationsSettings _popupAnimations;

        public CameraSettings Camera => _camera;
        public PopupAnimationsSettings PopupAnimations => _popupAnimations;

        private void OnEnable()
        {
            EnsureDefaults();
        }

        public void OnAfterDeserialize()
        {
            EnsureDefaults();
        }

        public void OnBeforeSerialize()
        {
        }

        private void EnsureDefaults()
        {
            _camera ??= new CameraSettings();
            _popupAnimations ??= new PopupAnimationsSettings();
        }

        [Serializable]
        public class CameraSettings
        {
            public float TopLevelBoundOffset = 3f;
            public float BottomLevelBoundOffset = 3f;
            public float LevelStartDelay = 0f;
        }

        [Serializable]
        public class PopupAnimationsSettings
        {
            [Header("ScaleIn parameters")] public float ScaleInDelay = 0f;
            public float ScaleInDuration = 0.25f;
            public AnimationCurve ScaleInCurve = AnimationCurve.Linear(0f, 0f, 1f, 1f);
            public float ScaleInEndValue = 1f;

            [Header("ScaleOut parameters")] public float ScaleOutDelay = 0f;
            public float ScaleOutDuration = 0.25f;
            public AnimationCurve ScaleOutCurve = AnimationCurve.Linear(0f, 0f, 1f, 1f);
            public float ScaleOutEndValue = 0f;

            [Header("Fade in parameters")] public float FadeInDelay;
            public float FadeInDuration = 0.25f;
            public AnimationCurve FadeInCurve = AnimationCurve.Linear(0f, 0f, 1f, 1f);
            public float FadeInEndValue = 1f;

            [Header("Fade out parameters")] public float FadeOutDelay;
            public float FadeOutDuration = 0.25f;
            public AnimationCurve FadeOutCurve = AnimationCurve.Linear(0f, 0f, 1f, 1f);
            public float FadeOutEndValue = 0f;
        }
    }
}
