namespace SGS.Services.DebugLog
{
    public readonly partial struct DebugLogType
    {
        public static readonly DebugLogType AssetPreload = new(1 << 20);
    }
}
