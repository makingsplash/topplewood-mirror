using System;

using UnityEngine;

namespace SGS.Infrastructure.Tools.Addressables
{
    /// <summary>
    /// Canvas only asset reference.
    /// </summary>
    [Serializable]
    public class AssetReferenceCanvas : AssetReferenceCustom<Canvas>
    {
        /// <summary>
        /// Constructs a new reference to a Canvas.
        /// </summary>
        /// <param name="guid">The object guid.</param>
        public AssetReferenceCanvas(string guid) : base(guid) {}
    }
}