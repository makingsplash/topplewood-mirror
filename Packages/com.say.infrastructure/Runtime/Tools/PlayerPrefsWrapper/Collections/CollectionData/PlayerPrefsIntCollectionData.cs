namespace SGS.Infrastructure.Tools.PlayerPrefsWrapper.Collections.CollectionData
{
    public class PlayerPrefsIntCollectionData : BasePlayerPrefsCollectionData<int>
    {
        public PlayerPrefsIntCollectionData()
        {
            Value = 0;
        }
        
        public override void TryParseValue(string valueString)
        {
            Value = int.Parse(valueString);
        }
    }
}