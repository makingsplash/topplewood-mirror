using System;
using JetBrains.Annotations;
using Sirenix.OdinInspector;
using TMPro;
using UnityEditor;
using UnityEngine;

namespace SGS.Store.Editor
{
    public class FontReplacerWindowConfig : ScriptableObject
    {
        [Serializable]
        public class FontMaterialData
        {
            [ValueDropdown("_sourceMaterials")]
            [InlineButton("SelectSource", "Sel")]
            public Material SourceMaterial;
            [ValueDropdown("_replacementMaterials")]
            [InlineButton("SelectDest", "Sel")]
            public Material ReplacementMaterial;

            [UsedImplicitly]
            private Material[] _sourceMaterials;
            [UsedImplicitly]
            private Material[] _replacementMaterials;

            public void SetMaterials(Material[] sourceMaterials, Material[] replacementMaterials)
            {
                _sourceMaterials = sourceMaterials;
                _replacementMaterials = replacementMaterials;
            }

            private void SelectSource()
            {
                Selection.activeObject = SourceMaterial;
            }

            private void SelectDest()
            {
                Selection.activeObject = ReplacementMaterial;
            }
        }

        [Serializable]
        public class FontData
        {
            [OnValueChanged("FontUpdated")]
            public TMP_FontAsset SourceFont;
            [OnValueChanged("FontUpdated")]
            public TMP_FontAsset ReplacementFont;

            [OnValueChanged("MaterialUpdated")]
            public FontMaterialData[] MaterialReplacements;

            private Material[] _sourceMaterials;
            private Material[] _replacementMaterials;

            public Material[] SourceMaterials => _sourceMaterials;
            public Material[] ReplacementMaterials => _replacementMaterials;

            private void FontUpdated()
            {
                if (FontReplacerWindow.Instance != null)
                {
                    FontReplacerWindow.Instance.UpdateFontMaterials();
                }
            }

            private void MaterialUpdated()
            {
                if (FontReplacerWindow.Instance != null)
                {
                    FontReplacerWindow.Instance.AssignFontMaterials();
                }
            }

            public void SetMaterials(Material[] sourceMaterials, Material[] replacementMaterials)
            {
                _sourceMaterials = sourceMaterials;
                _replacementMaterials = replacementMaterials;
            }
        }

        public FontData[] Replacements;
    }
}