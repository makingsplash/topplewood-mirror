using System;
using Tools.Addressables;
using UnityEngine;
using UnityEngine.Scripting.APIUpdating;

namespace Ui.Components.Message
{
    [Serializable]
    [MovedFrom(sourceNamespace: "UI.Components.Message")]
    public abstract class MessageViewParameters
    {
        [SerializeField] private string _name;
        
        [SerializeField] private AssetReferenceMessageContent _prefab;
        [SerializeField] private string _title;
        
        public string Title => _title;
        
        public AssetReferenceMessageContent Prefab => _prefab;
        public string Name => _name;
    }
}
