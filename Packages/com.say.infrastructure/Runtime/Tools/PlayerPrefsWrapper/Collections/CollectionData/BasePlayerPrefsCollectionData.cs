namespace SGS.Infrastructure.Tools.PlayerPrefsWrapper.Collections.CollectionData
{
    public interface IPlayerPrefsCollectionData
    {
        string GetValueString();
        void TryParseValue(string valueString);
    }
    
    
    public abstract class BasePlayerPrefsCollectionData<T> : IPlayerPrefsCollectionData
    {
        public T Value = default;
    
        public virtual string GetValueString()
        {
            return Value.ToString();
        }
        
        public virtual void TryParseValue(string valueString)
        {
            // nothing
        }

        public override bool Equals(object obj)
        {
            if (obj is BasePlayerPrefsCollectionData<T> objData)
            {
                return Value.Equals(objData.Value);
            }

            return false;
        }

        public override int GetHashCode()
        {
            return Value.GetHashCode();
        }
    }
}