#if !SAY_INFRASTURCTURE
#if UNITY_EDITOR

using System;
using UnityEditor;
using UnityEngine;

namespace Tools.Purchases.Data.Common
{
    [CustomPropertyDrawer(typeof(TimeSpanValue))]
    public class TimeSpanDrawer : PropertyDrawer
    {
        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            var value = property.boxedValue as TimeSpanValue;
            if (value == null)
            {
                return;
            }
            var stringValue = ((TimeSpan)value).ToConfigString();
            var newStringValue =  EditorGUI.TextField(position, label, stringValue);

            if (newStringValue != stringValue)
            {
                property.boxedValue = new TimeSpanValue(TimeSpanExtensions.FromConfigString(newStringValue));   
            }
        }
    }
}
#endif
#endif
