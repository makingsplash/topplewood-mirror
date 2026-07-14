using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

using Sirenix.OdinInspector.Editor;

using UnityEditor;

using UnityEngine;

namespace SGS.Editor.AttributeDrawers
{
    public class ConstantValuesAttributeDrawer<TAttribute, TConstants> : OdinAttributeDrawer<TAttribute> where TAttribute: Attribute
    {
        protected string[] Values;

        public ConstantValuesAttributeDrawer()
        {
            List<string> values = GetConstantValues();

            Values = values.ToArray();
        }

        protected List<string> GetConstantValues()
        {
            var type = typeof(TConstants);
            List<string> values = new();
            foreach (var field in type.GetFields(BindingFlags.Public | BindingFlags.Static))
            {
                if (field.FieldType != typeof(string))
                {
                    continue;
                }
                    
                values.Add((string)field.GetValue(null));
            }

            return values;
        }

        protected override void DrawPropertyLayout(GUIContent label)
        {
            var v = Property.ValueEntry.WeakSmartValue as string;
            var idx = Array.IndexOf(Values, v);
            var newIdx = EditorGUILayout.Popup(label, idx, Values);
            if (idx != newIdx)
            {
                Property.ValueEntry.WeakSmartValue = Values[newIdx];
            }
        }
    }

    public class ConstantOptionalValuesAttributeDrawer<TAttribute, TConstants> : ConstantValuesAttributeDrawer<TAttribute, TConstants> where TAttribute : Attribute
    {
        public ConstantOptionalValuesAttributeDrawer()
        {
            List<string> values = GetConstantValues();
            Values = values.Prepend("(none)").ToArray();
        }
        
        protected override void DrawPropertyLayout(GUIContent label)
        {
            var v = Property.ValueEntry.WeakSmartValue as string;
            var idx = string.IsNullOrWhiteSpace(v) ? 0 : Array.IndexOf(Values, v);
            var newIdx = EditorGUILayout.Popup(label, idx, Values);
            if (idx != newIdx)
            {
                Property.ValueEntry.WeakSmartValue = newIdx == 0 ? string.Empty : Values[newIdx];
            }
        }
    }
}