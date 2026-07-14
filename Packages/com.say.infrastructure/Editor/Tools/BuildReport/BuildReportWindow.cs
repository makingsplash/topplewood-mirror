using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

using Sirenix.OdinInspector;
using Sirenix.OdinInspector.Editor;

using UnityEditor;
using UnityEditor.Build.Reporting;

using Object = UnityEngine.Object;

namespace SGS.Editor.Tools.BuildReport
{
    public class BuildReportWindow : OdinEditorWindow
    {
        private class AssetInfo
        {
            public string Path;
            public ulong Size;
            public Object Asset;
            public string SizeString;
        }
        
        // Fields are used implicitly in editor
        [SuppressMessage("ReSharper", "NotAccessedField.Local")]
        private class AssetObjectInfo
        {
            public Object Asset;
            public string Size;
        }
        
        private readonly List<AssetInfo> _assets = new(1000);
        private readonly List<AssetObjectInfo> _assetObjectInfoPool = new(1000);

        [ShowIf("@this._assets.Count == 0")]
        [Button("Load Report")]
        private void LoadReport()
        {
            _assets.Clear();
            
            UnityEditor.Build.Reporting.BuildReport report = BuildReportMenu.LoadBuildReport();
            Dictionary<string, ulong> assetsDictionary = new(1000);
            
            foreach (PackedAssets packedAssets in report.packedAssets)
            {
                foreach (PackedAssetInfo assetInfo in packedAssets.contents)
                {
                    if (assetsDictionary.ContainsKey(assetInfo.sourceAssetPath))
                    {
                        assetsDictionary[assetInfo.sourceAssetPath] += assetInfo.packedSize;
                    }
                    else
                    {
                        assetsDictionary.Add(assetInfo.sourceAssetPath, assetInfo.packedSize);
                    }
                }
            }
            
            foreach (KeyValuePair<string,ulong> pair in assetsDictionary)
            {
                _assets.Add(new AssetInfo
                {
                    Path = pair.Key,
                    Size = pair.Value,
                    Asset = AssetDatabase.LoadAssetAtPath<Object>(pair.Key),
                    SizeString = $"{pair.Value:N0}"
                });
            }
            
            _assets.Sort((a, b) => (int)((double)b.Size - a.Size));
            
            assetsDictionary.Clear();
        }

        [ShowIf("@this._assets.Count > 0")]
        [ShowInInspector]
        private string _filter = "Assets";
        
        [ShowIf("@this._assets.Count > 0")]
        [Button("Filter Assets")]
        private void FilterAssetsTable()
        {
            ReturnAssetObjectsToPool();
            
            ulong totalSize = 0;

            foreach (AssetInfo assetInfo in _assets)
            {
                if (assetInfo.Path.Contains(_filter))
                {
                    AssetObjectInfo objectInfo = GetAssetObjectInfo();

                    objectInfo.Asset = assetInfo.Asset;
                    objectInfo.Size = assetInfo.SizeString;
                    
                    totalSize += assetInfo.Size;
                    
                    _assetsList.Add(objectInfo);
                }
            }

            if (_assetsList.Count == 0)
            {
                _meanSize = "0";
                _totalSize = "0";
                return;
            }

            float meanSize = totalSize * 1.0f / _assetsList.Count;
            
            _meanSize = $"{meanSize:N0}";
            _totalSize = $"{totalSize:N0}";
        }

        private void ReturnAssetObjectsToPool()
        {
            _assetObjectInfoPool.AddRange(_assetsList);
            _assetsList.Clear();
        }

        private AssetObjectInfo GetAssetObjectInfo()
        {
            if (_assetObjectInfoPool.Count == 0)
            {
                return new AssetObjectInfo();
            }

            AssetObjectInfo objectInfo = _assetObjectInfoPool[_assetObjectInfoPool.Count - 1];
            _assetObjectInfoPool.RemoveAt(_assetObjectInfoPool.Count - 1);

            return objectInfo;
        }

        [TableList(AlwaysExpanded = true, IsReadOnly = true, ShowPaging = true, NumberOfItemsPerPage = 30)]
        [ShowInInspector]
        [PropertyOrder(1)]
        private List<AssetObjectInfo> _assetsList = new();
        
        [ShowInInspector]
        [ReadOnly]
        [PropertyOrder(2)]
        private string _meanSize;
        
        [ShowInInspector]
        [ReadOnly]
        [PropertyOrder(3)]
        private string _totalSize;
    }
}