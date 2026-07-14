using System;

namespace SGS.Infrastructure.Tools.PlayerPrefsWrapper.Collections.CollectionData
{
    public class PlayerPrefsDateTimeCollectionData : BasePlayerPrefsCollectionData<DateTime>
    {
        public override void TryParseValue(string valueString)
        {
            if (long.TryParse(valueString, out long binValue1))
            {
                Value = DateTime.FromBinary(binValue1);
            }
        }

         public override string GetValueString()
         {
             return Value.ToBinary().ToString();
         }
    }
}