using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Scripting.APIUpdating;

namespace Ui.Components.Message
{
    [CreateAssetMenu(fileName = "MessagesViewConfig", menuName = "UI/Messages/View Config")]
    [MovedFrom(sourceNamespace: "UI.Components.Message")]
    public sealed class MessagesViewConfig : ScriptableObject
    {
        [SerializeReference] private MessageViewParameters[] _parameters;
        
        private Dictionary<string, MessageViewParameters> _parametersDictionary;

        public MessageViewParameters GetParameters(string parameterName)
        {
            EnsureDictionary();
            return _parametersDictionary[parameterName];
        }

        public bool TryGetParameters(string parameterName, out MessageViewParameters parameters)
        {
            EnsureDictionary();
            return _parametersDictionary.TryGetValue(parameterName, out parameters);
        }

        private void EnsureDictionary()
        {
            if (_parametersDictionary == null)
            {
                _parametersDictionary = new Dictionary<string, MessageViewParameters>(_parameters.Length);
                foreach (MessageViewParameters parameters in _parameters)
                {
                    _parametersDictionary.Add(parameters.Name, parameters);
                }
            }
        }
    }
}
