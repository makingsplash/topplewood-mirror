using System;
using R3;
using Sirenix.OdinInspector;
using UnityEngine;

namespace MiroTools
{
    [Serializable]
    public abstract class Feature
    {
        protected MiroToolsConfig ToolsConfig;
        protected ReactiveProperty<string> Status;
        
        [ShowInInspector, InlineEditor(InlineEditorModes.FullEditor), LabelText("Settings"), PropertyOrder(-1)]
        protected ScriptableObject _featureConfig;
        

        public virtual void Initialize(MiroToolsConfig toolsConfig, ReactiveProperty<string> statusProperty, ScriptableObject featureConfig)
        {
            ToolsConfig = toolsConfig;
            Status = statusProperty;
            _featureConfig = featureConfig;
        }

        public abstract Type GetConfigType();
    }
}
