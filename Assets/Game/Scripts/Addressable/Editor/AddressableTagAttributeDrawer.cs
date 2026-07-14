using System;
using UnityEngine;
using UnityEditor;
using UnityEditor.AddressableAssets;
using Sirenix.OdinInspector.Editor;

public class AddressableTagAttributeDrawer : OdinAttributeDrawer<AddressableTagAttribute, string>
{
    protected override void DrawPropertyLayout(GUIContent label)
    {
        if (AddressableAssetSettingsDefaultObject.Settings == null)
        {
            ValueEntry.SmartValue = EditorGUILayout.TextField(label, ValueEntry.SmartValue);
        }
        else
        {
            var labelList = AddressableAssetSettingsDefaultObject.Settings.GetLabels().ToArray();
            var currIndex = Array.IndexOf(labelList, ValueEntry.SmartValue);
            var newIndex = EditorGUILayout.Popup(label, currIndex, labelList);
            if (newIndex != currIndex) ValueEntry.SmartValue = labelList[newIndex];
        }
    }
}
