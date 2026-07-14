using SGS.Infrastructure.Tools.PlayerPrefsWrapper.Storage;

namespace SGS.Infrastructure.Tools.PlayerPrefsWrapper
{
    public sealed class PlayerPrefsInt : PlayerPrefsValue<int>
    {
        private readonly int _defaultValue;
        
        public PlayerPrefsInt(string key, int defaultValue, PlayerPrefTags info = default) : base(key, info)
        {
            _defaultValue = defaultValue;
            Refresh();
        }

        protected override void SaveToStorage(int value)
        {
            PlayerPrefsCollection.SetInt(Key, value);
        }

        public override void Refresh()
        {
            RealtimeValue = PlayerPrefsCollection.GetInt(Key, _defaultValue);
        }
    }
}