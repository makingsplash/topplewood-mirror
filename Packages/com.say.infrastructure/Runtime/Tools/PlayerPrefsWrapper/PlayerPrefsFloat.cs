using SGS.Infrastructure.Tools.PlayerPrefsWrapper.Storage;

namespace SGS.Infrastructure.Tools.PlayerPrefsWrapper
{
    public sealed class PlayerPrefsFloat : PlayerPrefsValue<float>
    {
        private readonly float _defaultValue;
        
        public PlayerPrefsFloat(string key, float defaultValue, PlayerPrefTags info = default) : base(key, info)
        {
            _defaultValue = defaultValue;
            Refresh();
        }

        protected override void SaveToStorage(float value)
        {
            PlayerPrefsCollection.SetFloat(Key, value);
        }

        public override void Refresh()
        {
            RealtimeValue = PlayerPrefsCollection.GetFloat(Key, _defaultValue);
        }
    }
}