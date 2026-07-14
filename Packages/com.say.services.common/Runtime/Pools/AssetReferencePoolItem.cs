using System;
using SGS.Infrastructure.Tools.Addressables;

namespace SGS.Services.Pools
{
    /// <summary>
    /// PoolItem only asset reference.
    /// </summary>
    [Serializable]
    public class AssetReferencePoolItem : AssetReferenceCustom<PoolItemBase>
    {
        /// <summary>
        /// Constructs a new reference to a Camera.
        /// </summary>
        /// <param name="guid">The object guid.</param>
        public AssetReferencePoolItem(string guid) : base(guid) {}
    }
}