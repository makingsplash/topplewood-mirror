using System;
using SGS.Infrastructure.Tools.Addressables;
using SGS.Store.Ui;

namespace SGS.Store.Addressables
{
    /// <summary>
    /// StoreBlockController only asset reference.
    /// </summary>
    [Serializable]
    public class AssetReferenceStoreCardController : AssetReferenceCustom<StoreCardController>
    {
        /// <summary>
        /// Constructs a new reference to a StoreBlockController.
        /// </summary>
        /// <param name="guid">The object guid.</param>
        public AssetReferenceStoreCardController(string guid) : base(guid) {}
    }
}