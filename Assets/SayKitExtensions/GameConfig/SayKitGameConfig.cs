using System;

#region ReSharper

// ReSharper disable InconsistentNaming
// ReSharper disable CheckNamespace
// ReSharper disable RedundantDefaultMemberInitializer

#endregion

[System.Serializable]
public class SayKitGameConfig
{
    public string config_boosters;
    public string config_core;
    public string config_levels;
    public string config_rewarded;
    public string config_start;
    public string config_rate_us;
    public string config_input;
    public string config_store;
    public string config_store_items;
    public string config_no_ads;
    public string config_heart;
    public string config_asset_preload;
    public string config_levels_palettes;
    public string config_visual_presets;
    public string config_support;

#if SAYKIT_PURCHASING
    public string config_purchases;
#endif
}
