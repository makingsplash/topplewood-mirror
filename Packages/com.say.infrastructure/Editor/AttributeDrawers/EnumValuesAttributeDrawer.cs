using System;

using Sirenix.OdinInspector.Editor;

using UnityEditor;

using UnityEngine;

namespace SGS.Editor.AttributeDrawers
{
    public class EnumValuesAttributeDrawer<TAttribute, TEnum> : OdinAttributeDrawer<TAttribute> where TAttribute: Attribute
    {
        private readonly string[] _values = Enum.GetNames(typeof(TEnum));

        protected override void DrawPropertyLayout(GUIContent label)
        {
            var v = Property.ValueEntry.WeakSmartValue as string;
            var idx = Array.IndexOf(_values, v);
            var newIdx = EditorGUILayout.Popup(label, idx, _values);
            if (idx != newIdx)
            {
                Property.ValueEntry.WeakSmartValue = _values[newIdx];
            }
        }
    }
}