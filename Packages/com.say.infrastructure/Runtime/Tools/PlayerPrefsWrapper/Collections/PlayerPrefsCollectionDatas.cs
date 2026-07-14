using System;
using System.Collections.Generic;

using SGS.Infrastructure.Tools.PlayerPrefsWrapper.Collections.CollectionData;

namespace SGS.Infrastructure.Tools.PlayerPrefsWrapper.Collections
{
    public static class PlayerPrefsCollectionDatas
    {
        public static readonly Dictionary<Type, IPlayerPrefsCollectionData> TypeToCollectionData = new()
        {
            { typeof(int), new PlayerPrefsIntCollectionData() },
            { typeof(string), new PlayerPrefsStringCollectionData() },
            { typeof(DateTime), new PlayerPrefsDateTimeCollectionData() },
            // { typeof(ResourceData), }
        };
    }
}