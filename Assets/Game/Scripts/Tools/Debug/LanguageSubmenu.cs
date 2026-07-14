using System;
using DynamicDebugMenu.Elements;
using Services;
using SGS.Services.DebugTools;
using SGS.Services.Localization;

namespace Tools.Debug
{
    public static class LanguageSubmenu
    {
        public static void Create(DDMContext context)
        {
            DDMSubmenu.Create(context, "Language", () => context.Main.CreatePage("Language", GenerateContent));
        }

        private static void GenerateContent(DDMContext context)
        {
            var current = GlobalService.SayKit.GetCurrentLanguage();
            DDMLabel.Create(context, $"Current: {current}");

            foreach (AppLanguage language in Enum.GetValues(typeof(AppLanguage)))
            {
                var lang = language;
                DDMButton.Create(context, lang.ToString(), () =>
                {
                    GlobalService.SayKit.OverrideSystemLanguage(lang);
                    GlobalService.Localization.Init();
                    context.Main.CloseMenu();
                });
            }
        }
    }
}
