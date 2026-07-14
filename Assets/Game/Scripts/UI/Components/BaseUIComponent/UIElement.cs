using System;
using Cysharp.Threading.Tasks;
using SGS.Infrastructure.Tools.Addressables;
using UnityEngine;
using UnityEngine.Scripting.APIUpdating;

namespace Ui.Components
{
    [MovedFrom(sourceNamespace: "UI.Components")]
    public class UIElement : UIShowHide
    {
        [SerializeField] private bool _showOnStart = true;
        [SerializeField] private bool _showImmediately = true;
        
        private void Start()
        {
            if (_state == EState.Hidden)
            {
                if (_showOnStart)
                {
                    ShowAsync(_showImmediately).Forget();
                }
                else
                {
                    HideAsync(true).Forget();
                }
            }
        }
    }
    
    /// <summary>
    /// UIComponent only asset reference.
    /// </summary>
    [Serializable]
    [MovedFrom(sourceNamespace: "UI.Components")]
    public class AssetReferenceUIElement : AssetReferenceCustom<UIElement>
    {
        /// <summary>
        /// Constructs a new reference to a UIComponent.
        /// </summary>
        /// <param name="guid">The object guid.</param>
        public AssetReferenceUIElement(string guid) : base(guid)
        {
        }
    }
}
