using System;
using UnityEngine;

namespace SGS.Configs
{
    [CreateAssetMenu(fileName = "AnimationsConfig", menuName = "Configs/AnimationsConfig")]
    public class AnimationsConfigBase : ScriptableObject
    {
        [Serializable]
        public class FlyingResourceAnimationSettings
        {
            public float SpawnDelay = 0.1f;
            public float SoundDelay = 0.15f;
            public float SpawnMoveTime = 0.1f;
            public float TotalDuration = 1.4f;
            public float SpawnRadius = 0.5f;
            [Range(0, 1)] public float DelayAndFlyTime = 0.2f;
            [Range(0, 1)] public float ScaleTime = 0.8f;
            public float TargetScale = 0.5f;
            public AnimationCurve ChildCurve;
        }
     
        [SerializeField]
        private FlyingResourceAnimationSettings _flyingResource;

        public FlyingResourceAnimationSettings FlyingResource => _flyingResource;
    }
}