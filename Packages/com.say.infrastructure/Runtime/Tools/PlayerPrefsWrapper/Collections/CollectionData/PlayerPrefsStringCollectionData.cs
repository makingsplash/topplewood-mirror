namespace SGS.Infrastructure.Tools.PlayerPrefsWrapper.Collections.CollectionData
{
    public class PlayerPrefsStringCollectionData : BasePlayerPrefsCollectionData<string>
    {
        public PlayerPrefsStringCollectionData()
        {
            Value = string.Empty;
        }
        
        public override void TryParseValue(string valueString)
        {
            Value = valueString;
        }
    }
}