using MoreMountains.NiceVibrations;
using SGS.Services.Interfaces;

namespace SGS.Services.Haptic
{
    public enum HapticEffectType
    {
        None = 0,
        Selection = 1,
        Success = 2,
        Warning = 3,
        Failure = 4,
        LightImpact = 5,
        MediumImpact = 6,
        HeavyImpact = 7,
        RigidImpact = 8,
        SoftImpact = 9,
        Push = 10,
    }
    
    public class HapticService : IService
    {
        public void Init()
        {
            
        }

        public void DeInit()
        {
            
        }

        public void Play(float intensity, float sharpness, float duration)
        {
            MMVibrationManager.ContinuousHaptic(intensity, sharpness, duration);
        }
        
        public void Play(HapticEffectType effect)
        {
            switch (effect)
            {
                case HapticEffectType.Selection:
                    MMVibrationManager.Haptic(HapticTypes.Selection);
                    break;
                case HapticEffectType.Success:
                    MMVibrationManager.Haptic(HapticTypes.Success);
                    break;
                case HapticEffectType.Warning:
                    MMVibrationManager.Haptic(HapticTypes.Warning);
                    break;
                case HapticEffectType.Failure:
                    MMVibrationManager.Haptic(HapticTypes.Failure);
                    break;
                case HapticEffectType.LightImpact:
                    MMVibrationManager.Haptic(HapticTypes.LightImpact);
                    break;
                case HapticEffectType.MediumImpact:
                    MMVibrationManager.Haptic(HapticTypes.MediumImpact);
                    break;
                case HapticEffectType.HeavyImpact:
                    MMVibrationManager.Haptic(HapticTypes.HeavyImpact);
                    break;
                case HapticEffectType.RigidImpact:
                    MMVibrationManager.Haptic(HapticTypes.RigidImpact);
                    break;
                case HapticEffectType.SoftImpact:
                    MMVibrationManager.Haptic(HapticTypes.SoftImpact);
                    break;
                case HapticEffectType.Push:
                    MMVibrationManager.TransientHaptic(0.85f, 0.05f);
                    break;
            }
        }
        
        public bool HapticSupported()
        {
            return MMVibrationManager.HapticsSupported();
        }

        public void EnableHaptic(bool isEnable)
        {
            MMVibrationManager.SetHapticsActive(isEnable);
        }
        
    }
}