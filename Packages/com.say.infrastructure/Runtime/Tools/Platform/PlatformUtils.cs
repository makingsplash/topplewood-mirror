using UnityEngine;

namespace SGS.Infrastructure.Tools.Platform
{
    public static class PlatformUtils
    {
        public static bool IsIpadResolution()
        {
            float resolution = (float) Screen.height / Screen.width;
            return resolution < 1.6f;
        }

        public static bool IsPortraitOrientation()
        {
            return Screen.height >= Screen.width;
        }

        public static bool IsMobile()
        {
            return IsAndroid() || IsIos();
        }

        public static bool IsIos()
        {
            return Application.platform == RuntimePlatform.IPhonePlayer;
        }

        public static bool IsAndroid()
        {
            return Application.platform == RuntimePlatform.Android;
        }
    }
}