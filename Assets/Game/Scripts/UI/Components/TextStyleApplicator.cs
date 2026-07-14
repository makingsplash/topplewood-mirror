using System.Collections.Generic;
using Game.Configs;
using Sirenix.OdinInspector;
using TMPro;
using UnityEngine;

namespace Ui.Components
{
    public class TextStyleApplicator : MonoBehaviour
    {
        [SerializeField] private TextStyleConfig _config;
        [SerializeField, ValueDropdown(nameof(GetStyleNames))] private string _styleName;
        [SerializeField] private TMP_Text _text;

        private void Awake()
        {
            Apply();
        }

        [Button]
        public void Apply()
        {
            if (_config == null || _text == null)
                return;

            if (!_config.TryGetStyle(_styleName, out var style))
                return;

            if (style.Font != null)
                _text.font = style.Font;

            if (style.FontMaterial != null && _text.fontSharedMaterial != null)
            {
                // Copy visual properties into TMP's own instance material instead of replacing
                // the shared material — prevents atlas mismatch that causes broken glyph UVs.
                var mat = _text.fontMaterial;
                mat.CopyPropertiesFromMaterial(style.FontMaterial);
                mat.SetTexture(ShaderUtilities.ID_MainTex, _text.font.atlasTexture);
                _text.UpdateMeshPadding();
            }

            if (style.FontSize > 0f)
                _text.fontSize = style.FontSize;

            _text.color = style.Color;
            _text.characterSpacing = style.CharacterSpacing;
            _text.ForceMeshUpdate(false, true);
        }

        private void OnValidate()
        {
            // Skip during prefab instantiation (before Awake) — TMP's m_renderer is null then
            if (gameObject.scene.isLoaded)
                Apply();
        }

#if UNITY_EDITOR
        private void Reset()
        {
            if (_config == null)
            {
                var guids = UnityEditor.AssetDatabase.FindAssets("t:TextStyleConfig");
                if (guids.Length > 0)
                {
                    var path = UnityEditor.AssetDatabase.GUIDToAssetPath(guids[0]);
                    _config = UnityEditor.AssetDatabase.LoadAssetAtPath<TextStyleConfig>(path);
                }
            }

            if (_text == null)
                _text = GetComponent<TMP_Text>();
        }
#endif

        private IEnumerable<string> GetStyleNames() =>
            _config != null ? _config.GetStyleNames() : System.Array.Empty<string>();
    }
}
