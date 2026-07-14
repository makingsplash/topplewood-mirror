#if UNITY_EDITOR

using System;
using SGS.Extensions;
using UnityEditor;

using UnityEngine;

namespace SGS.Infrastructure.Tools.Common
{
    [CustomPropertyDrawer(typeof(TimeOfDayValue))]
    public class TimeOfDayDrawer : PropertyDrawer
    {
        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            var value = property.boxedValue as TimeOfDayValue;
            if (value == null)
            {
                return;
            }
            var stringValue = ((TimeSpan)value).ToTimeOfDayString();
            var newStringValue =  EditorGUI.TextField(position, label, stringValue);

            if (newStringValue != stringValue)
            {
                property.boxedValue = new TimeOfDayValue(TimeSpanExtensions.FromTimeOfDayString(newStringValue));   
            }
        }
    }
}
#endif