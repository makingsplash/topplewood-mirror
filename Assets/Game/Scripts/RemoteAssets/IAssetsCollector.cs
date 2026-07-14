using System.Collections.Generic;

namespace Services.AssetPreload
{
    public interface IAssetsCollector
    {
        HashSet<string> CollectAssets(int levelNum);
    }
}
