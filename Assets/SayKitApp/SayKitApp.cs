
public class SayKitApp
{
    /* Features */
    public const bool notificationsEnabled = true;

    /* App settings */
    public const string APP_NAME_CHINA_IOS = "";
    // Mirrors PlayerSettings.productName: the SayKit "[iOS] Application config" menu writes this back into it.
    public const string APP_NAME_IOS = "Topplewood";

    public const string APP_BUNDLE_CHINA_IOS = "";
    public const string APP_BUNDLE_IOS = "com.game.dropcolorblocks";

#if SAYKIT_PURCHASING
    public static bool purchasesEnabled = true;
#else
    public static bool purchasesEnabled = false;
#endif

    public const string APP_KEY_IOS = "";
    public const string APP_SECRET_IOS = "";

    // Topplewood (Android) in SayLauncher, bundle com.game.dropcolorblocks (TASK-064, 2026-10-21).
    // iOS keys stay empty on purpose: the publisher registered the Android app only.
    public const string APP_KEY_ANDROID = "tplwoda";
    public const string APP_SECRET_ANDROID = "tmOydPtySPlSrZMThip7a8iNBB0ZXzeA";

    public const string PROMO_KEY_IOS = "";

    /* App constants */
    public const string AD_INTERSTITIAL = "ad_interstitial";
    public const string AD_REWARDED = "ad_rewarded";
}
