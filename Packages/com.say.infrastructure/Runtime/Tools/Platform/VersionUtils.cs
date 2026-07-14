using SGS.Infrastructure.Tools.Configs;

using UnityEngine;

namespace SGS.Infrastructure.Tools.Platform
{
    public static class VersionUtils
    {
        public static string GetVersionName()
        {
            string appVersion = "v.";

#if UNITY_EDITOR
            appVersion += Application.version;
            if (ConfigSwitcher.UseLocalConfig)
            {
                appVersion += " (Local)";
            }
#elif SAYKIT
            // Progress should be in state CONSENT or more. This corresponds to 0.40f;
            if ( SayKit.initializedProgress >= 0.40f)
            {
                appVersion += SayKit.BuildVersion;
            }
            else
            {
                appVersion += "0.0.0.0";
            }
#endif

            // ReSharper disable once Unity.UnknownResource
            // build_number is injected during build phase
            TextAsset buildnumberText = Resources.Load("build_number") as TextAsset;
            string buildnumber = string.Empty;
            if (buildnumberText != null)
            {
                buildnumber = buildnumberText.text.Replace(System.Environment.NewLine, "");
            }
            
            if (!string.IsNullOrEmpty(buildnumber))
            {
                appVersion = appVersion + "." + buildnumber;
            }

            return appVersion;
        }
    }
}