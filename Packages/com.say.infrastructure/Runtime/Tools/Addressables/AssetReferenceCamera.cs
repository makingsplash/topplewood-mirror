using System;

using UnityEngine;

namespace SGS.Infrastructure.Tools.Addressables
{
    /// <summary>
    /// Camera only asset reference.
    /// </summary>
    [Serializable]
    public class AssetReferenceCamera : AssetReferenceCustom<Camera>
    {
        /// <summary>
        /// Constructs a new reference to a Camera.
        /// </summary>
        /// <param name="guid">The object guid.</param>
        public AssetReferenceCamera(string guid) : base(guid) {}
    }
}