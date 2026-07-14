using System;

using SGS.Infrastructure.Core;

namespace SGS.Infrastructure.Tools.Addressables
{
    /// <summary>
    /// GameLevel only asset reference.
    /// </summary>
    [Serializable]
    public class AssetReferenceGameLevel : AssetReferenceCustom<GameLevel>
    {
        /// <summary>
        /// Constructs a new reference to a Level.
        /// </summary>
        /// <param name="guid">The object guid.</param>
        public AssetReferenceGameLevel(string guid) : base(guid) {}
    }
}