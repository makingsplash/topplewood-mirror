using System;

using UnityEngine;

namespace SGS.Infrastructure.Tools.Addressables
{
    /// <summary>
    /// MeshRenderer only asset reference.
    /// </summary>
    [Serializable]
    public class AssetReferenceMeshRender : AssetReferenceCustom<MeshRenderer>
    {
        /// <summary>
        /// Constructs a new reference to a Camera.
        /// </summary>
        /// <param name="guid">The object guid.</param>
        public AssetReferenceMeshRender(string guid) : base(guid) {}
    }
}