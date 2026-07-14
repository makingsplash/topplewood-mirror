
public class SayKitApp
{
    /* Features */
    public const bool notificationsEnabled = true;

    /* App settings */
    public const string APP_NAME_CHINA_IOS = "";
    public const string APP_NAME_IOS = "";

    public const string APP_BUNDLE_CHINA_IOS = "";
    public const string APP_BUNDLE_IOS = "com.game.tilesort";

#if SAYKIT_PURCHASING
    public static bool purchasesEnabled = true;
#else
    public static bool purchasesEnabled = false;
#endif

    public const string APP_KEY_IOS = "";
    public const string APP_SECRET_IOS = "";

    public const string APP_KEY_ANDROID = "ctlsrta";
    public const string APP_SECRET_ANDROID = "WTnQWMoOzkEBxTlbNPx6kHpkklai1tdf";

    public const string PROMO_KEY_IOS = "";

    /* App constants */
    public const string AD_INTERSTITIAL = "ad_interstitial";
    public const string AD_REWARDED = "ad_rewarded";
}
