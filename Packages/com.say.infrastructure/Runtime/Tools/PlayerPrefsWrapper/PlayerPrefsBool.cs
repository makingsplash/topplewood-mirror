using SGS.Infrastructure.Tools.PlayerPrefsWrapper.Storage;

namespace SGS.Infrastructure.Tools.PlayerPrefsWrapper
{
    public sealed class PlayerPrefsBool : PlayerPrefsValue<bool>
    {
        private readonly bool _defaultValue;
        
        public PlayerPrefsBool(string key, bool defaultValue, PlayerPrefTags info = default) : base(key, info)
        {
            _defaultValue = defaultValue;
            Refresh();
        }

        protected override void SaveToStorage(bool value)
        {
            PlayerPrefsCollection.SetInt(Key, value ? 1 : 0);
        }

        public override void Refresh()
        {
            RealtimeValue = PlayerPrefsCollection.GetInt(Key, _defaultValue ? 1 : 0) == 1;
        }
    }
}