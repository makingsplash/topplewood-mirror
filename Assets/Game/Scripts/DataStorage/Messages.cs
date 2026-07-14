using SGS.Infrastructure.Tools.PlayerPrefsWrapper.Collections;

namespace DataStorage
{
    public sealed class Messages
    {
        private readonly PlayerPrefsList<string> _data = new PlayerPrefsList<string>("Meta.Messages");

        public bool IsUserSawMessage(string messageName)
        {
            return _data.Value.Contains(messageName);
        }

        public void AddSeenMessage(string messageName)
        {
            _data.Value.Add(messageName);
            _data.ForceSave();
        }

        public void ClearSeenMessages()
        {
            _data.Value.Clear();
            _data.ForceSave();
        }
    }
}
