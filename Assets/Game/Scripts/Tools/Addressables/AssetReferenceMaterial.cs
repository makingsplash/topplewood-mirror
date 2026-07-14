using System;
using UnityEngine;
using UnityEngine.AddressableAssets;

namespace Tools.Addressables
{
	[Serializable]
	public class AssetReferenceMaterial : AssetReferenceT<Material>
	{
		public AssetReferenceMaterial(string guid) : base(guid)
		{
		}
	}
}
