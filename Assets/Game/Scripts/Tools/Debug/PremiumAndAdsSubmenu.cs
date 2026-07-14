using DynamicDebugMenu.Elements;
using SGS.Services.DebugTools;

namespace Tools.Debug
{
    public static class PremiumAndAdsSubmenu
    {
        public static void Create(DDMContext context)
        {
            DDMSubmenu.Create(context, "Premium & Ads", () => context.Main.CreatePage("Premium & Ads", GenerateContent));
        }

        private static void GenerateContent(DDMContext context)
        {
            DefaultDebugMenu.CreatePremium(context);
            DefaultDebugMenu.CreateAd(context);            
        }
    }
}
