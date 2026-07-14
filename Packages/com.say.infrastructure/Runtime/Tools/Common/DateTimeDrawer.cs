#if UNITY_EDITOR
using System;
using SGS.Extensions;

using UnityEditor;

using UnityEngine;

namespace SGS.Infrastructure.Tools.Common
{
    [CustomPropertyDrawer(typeof(DateTimeValue))]
    public class DateTimeDrawer : PropertyDrawer
    {
        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            var value = property.boxedValue as DateTimeValue;
            if (value == null)
            {
                return;
            }
            var stringValue = ((DateTime)value).ToConfigString();
            var newStringValue =  EditorGUI.TextField(position, label, stringValue);

            if (newStringValue != stringValue)
            {
                property.boxedValue = new DateTimeValue(TimeSpanExtensions.FromDateTimeString(newStringValue));   
            }
        }
    }
}
#endif