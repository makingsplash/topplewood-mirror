using System;
using SGS.Infrastructure.Tools.Addressables;
using Ui.Components.Message;

namespace Tools.Addressables
{
    /// <summary>
    /// MessageContent only asset reference.
    /// </summary>
    [Serializable]
    public class AssetReferenceMessageContent : AssetReferenceCustom<MessageContentElement>
    {
        /// <summary>
        /// Constructs a new reference to a MessageContent.
        /// </summary>
        /// <param name="guid">The object guid.</param>
        public AssetReferenceMessageContent(string guid) : base(guid)
        {
        }
    }
}
