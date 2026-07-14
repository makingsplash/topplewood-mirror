using System;
using System.Linq;
using DynamicDebugMenu.Elements;
using SGS.Data;
using SGS.Infrastructure.Tools.Platform;
using SGS.Services.DebugTools.Submenu;
using UnityEngine;

namespace SGS.Services.DebugTools
{
    public static class DefaultDebugMenu
    {
        public static void CreateMenuFooter(DDMContext context)
        {
            DDMHoldButton.Create(context, "Reset Progress", () =>
            {
                PlayerPrefs.DeleteAll();
                context.Main.SetRestart();
                context.Main.CloseMenu();
            });

            LogFilterSubmenu.Create(context);
            
            CreatePremium(context);
            CreateAd(context);

            CreateShortMenuFooter(context);
        }
        
        public static void CreateShortMenuFooter(DDMContext context)
        {
            DDMLabel.Create(context, $"Version: {VersionUtils.GetVersionName()}");
            DDMLabel.Create(context, $"IDFA: {GetIDFA()}");
        }

        public static void CreatePremium(DDMContext context)
        {
            if (GlobalService.SayKit.IsPremium())
            {
                DDMButton.Create(context, "Disable Premium", GlobalService.SayKit.DisablePremium);    
            }
            else
            {
                DDMButton.Create(context, "Enable Premium", GlobalService.SayKit.EnablePremium);
            }
        }

        public static void CreateAd(DDMContext context)
        {
            var adModes = Enum.GetNames(typeof(DebugData.AdModes)).ToList();
            DDMDropdown.Create(context, "Ad mode:", () => adModes, () => (int)SayData.Debug.AdMode.Value,
                i => { SayData.Debug.AdMode.Value = (DebugData.AdModes)i; });
        }

        public static string GetBuildVersion()
        {
            string version = GlobalService.SayKit.GetAppVersion();
            string buildnumber = "";
            TextAsset buildnumberText = Resources.Load("build_number") as TextAsset;
            if (buildnumberText != null)
            {
                buildnumber = buildnumberText.text.Replace(Environment.NewLine, "");
            }

            if (!String.IsNullOrEmpty(buildnumber))
            {
                version = version + "(" + buildnumber + ")";
            }

            if (GlobalService.SayKit.IsPremium())
            {
                version += "p";
            }
        
            return version;
        }
        
        public static string GetIDFA()
        {
            string idfa = string.Empty;
#if SAYKIT
            idfa = GlobalService.SayKit.GetIDFA();
#endif
            if (string.IsNullOrEmpty(idfa))
            {
                idfa = "00000000-0000-0000-0000-000000000000";
            }

            return idfa;
        }

    }
}