using DataStorage;
using DynamicDebugMenu.Elements;

namespace Services.DebugTools.Submenu
{
    public static class RewardedStateSubmenu
    {
        public static void Create(DDMContext context)
        {
            DDMSubmenu.Create(context, "Rewardeds", () => context.Main.CreatePage("Rewardeds", GenerateContent));
        }
        
        private static void GenerateContent(DDMContext context)
        {
            var rewardedStateService = Service.RewardedState;
            var state = Data.Meta.RewardedState;

            DDMLabel.Create(context, $"Purchases count: {rewardedStateService.DebugGetPurchasesCount()}");
            
            DDMCheckbox.Create(context, "Interstitials Enabled", state.InterstitialsEnabled.Value,
                v => state.InterstitialsEnabled.Value = v);
            DDMCheckbox.Create(context, "NoAdOffers Enabled", state.NoAdOffersEnabled.Value,
                v => state.NoAdOffersEnabled.Value = v);
            DDMCheckbox.Create(context, "Revive For Reward", state.ReviveForRewardEnabled.Value,
                v => state.ReviveForRewardEnabled.Value = v);
            DDMCheckbox.Create(context, "Ad Boosters Enabled", state.AdBoostersEnabled.Value,
                v => state.AdBoostersEnabled.Value = v);
            DDMCheckbox.Create(context, "Ad Hearts Enabled", state.AdHeartsEnabled.Value,
                v => state.AdHeartsEnabled.Value = v);
                
            DDMLabel.Create(context, $"Interstitial Was Shown: {state.InterstitialWasShown.Value}");
            DDMLabel.Create(context, $"Inters Enable Level: {state.IntersEnableLevel.Value}");

            DDMLabel.Create(context, $"Ad Boosters Was Shown: {state.AdBoostersWasShown.Value}");
            DDMLabel.Create(context, $"Ad Boosters Enable Level: {state.AdBoostersEnableLevel.Value}");
            DDMLabel.Create(context, $"Ad Boosters Disable Till Level: {state.AdBoostersDisableLevel.Value}");

            DDMLabel.Create(context, $"Ad Hearts Was Shown: {state.AdHeartsWasShown.Value}");
            DDMLabel.Create(context, $"Ad Hearts Enable Level: {state.AdHeartsEnableLevel.Value}");
            DDMLabel.Create(context, $"Ad Hearts Disable Till Level: {state.AdHeartsDisableLevel.Value}");

            DDMButton.Create(context, "Reset Rewarded State", () =>
            {
                rewardedStateService.DebugResetState();
                context.Main.CloseMenu();
            });
        }
    }
}
