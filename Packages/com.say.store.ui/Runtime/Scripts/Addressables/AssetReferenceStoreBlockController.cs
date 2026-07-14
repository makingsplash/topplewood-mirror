using System;
using SGS.Infrastructure.Tools.Addressables;
using SGS.Store.Ui;

namespace SGS.Store.Addressables
{
    /// <summary>
    /// StoreBlockController only asset reference.
    /// </summary>
    [Serializable]
    public class AssetReferenceStoreBlockController : AssetReferenceCustom<StoreBlockController>
    {
        /// <summary>
        /// Constructs a new reference to a StoreBlockController.
        /// </summary>
        /// <param name="guid">The object guid.</param>
        public AssetReferenceStoreBlockController(string guid) : base(guid) {}
    }
}