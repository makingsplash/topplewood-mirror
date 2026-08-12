using Cysharp.Threading.Tasks;
using DynamicDebugMenu.Elements;
using Services;
using Ui;
using Ui.Popups;
using Ui.Screens.RateUs;

namespace Tools.Debug
{
    public class PopupsSubmenu
    {
        public static void Create(DDMContext context)
        {
            DDMSubmenu.Create(context, "Popups", () => context.Main.CreatePage("Popups", GenerateContent));
        }

        private static void GenerateContent(DDMContext context)
        {
            DDMButton.Create(context, "Show RateUs", () => { Service.UI.Show<RateUsPopUp>(ScreenNames.RateUsPopUp).Forget(); });
            DDMButton.Create(context, "Show Restart", () => { Service.UI.Show<RestartPopup>(ScreenNames.RestartPopup).Forget(); });
        }
    }
}
