using SGS.Infrastructure.Tools.PlayerPrefsWrapper.Storage;

namespace SGS.Infrastructure.Tools.PlayerPrefsWrapper
{
    public sealed class PlayerPrefsString : PlayerPrefsValue<string>
    {
        private readonly string _defaultValue;
        
        public PlayerPrefsString(string key, string defaultValue, PlayerPrefTags info = default) : base(key, info)
        {
            _defaultValue = defaultValue;
            Refresh();
        }

        protected override void SaveToStorage(string value)
        {
            PlayerPrefsCollection.SetString(Key, value);
        }

        public override void Refresh()
        {
            RealtimeValue = PlayerPrefsCollection.GetString(Key, _defaultValue);
        }
    }
}