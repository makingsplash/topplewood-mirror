using System;
using System.Collections.Generic;
using System.Linq;
using JetBrains.Annotations;
using Sirenix.OdinInspector;
using Sirenix.OdinInspector.Editor;
using TMPro;
using TMPro.EditorUtilities;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditor.Search;
using UnityEngine;

namespace SGS.Store.Editor
{
  public class FontReplacerWindow : OdinEditorWindow
  {

    [Serializable]
    public class RefData
    {
      [HorizontalGroup("")]
      public MonoBehaviour Owner;
      [HorizontalGroup("")]
      public MonoBehaviour Target;
    }

    private static FontReplacerWindow _instance;
    public static FontReplacerWindow Instance => _instance;

    [SerializeField]
    private FontReplacerWindowConfig.FontData[] _replacements;

    private const string _configFileName = "Assets/Prefabs/Editor/FontReplacerWindowConfig.asset";
    private FontReplacerWindowConfig _config;

    [MenuItem("DevTools/Font Replacer window")]
    static void Init()
    {
      var window = (FontReplacerWindow)GetWindow(typeof(FontReplacerWindow));
      _instance = window;
      window.Show();
    }


    [Button("Save config")]
    private void SaveConfig()
    {
      SaveChanges();
    }

    [UsedImplicitly]
    private bool HavePrefabOpen()
    {
      return PrefabStageUtility.GetCurrentPrefabStage() != null;
    }

    [EnableIf("HavePrefabOpen")]
    [Button("Apply to opened prefab", ButtonSizes.Large)]
    private void ApplyToPrefab()
    {
      var stage = PrefabStageUtility.GetCurrentPrefabStage();
      AssetDatabase.StartAssetEditing();
      Undo.SetCurrentGroupName("Font replacement");
      int group = Undo.GetCurrentGroup();

      try
      {
        Undo.RecordObject(stage.prefabContentsRoot, "");
        ApplyToObject(stage.prefabContentsRoot);
      }
      finally
      {
        AssetDatabase.StopAssetEditing();
        Undo.CollapseUndoOperations(group);
      }
    }

    [EnableIf("HavePrefabOpen")]
    [Button("Show external references", ButtonSizes.Large)]
    private void ShowReferences()
    {
      var stage = PrefabStageUtility.GetCurrentPrefabStage();
      var refs = new List<RefData>();
      FindExternalRefs(refs, stage.prefabContentsRoot);
      _externalRefsPrefab = stage.prefabContentsRoot;
      _externalRefs = refs.ToArray();
    }

    [VerticalGroup(Order = 1, PaddingTop = 20)]
    [ShowIf("@_externalRefsPrefab != null")]
    [SerializeField]
    [UsedImplicitly]
    private RefData[] _externalRefs;

    private GameObject _externalRefsPrefab;

    private void ApplyToObject(GameObject stagePrefabContentsRoot)
    {
      int materialsFound = 0;
      int materialsReplaced = 0;
      foreach (var text in stagePrefabContentsRoot.GetComponentsInChildren<TMP_Text>(true))
      {
        materialsFound += 1;
        var replacement = _replacements.FirstOrDefault(r => r.SourceFont == text.font);
        if (replacement == null)
        {
          continue;
        }

        Undo.RecordObject(text, "");

        var replMaterial = replacement.MaterialReplacements.FirstOrDefault(r => SameMaterials(r.SourceMaterial, text.fontMaterial));

        text.font = replacement.ReplacementFont;
        materialsReplaced += 1;

        if (replMaterial == null)
        {
          Debug.LogWarning(
            $"Replacing font in {SearchUtils.GetHierarchyPath(text.gameObject, false)}, but no material preset {text.fontMaterial.name} in replacements");
        }
        else
        {
          text.fontMaterial = replMaterial.ReplacementMaterial;
        }
      }

      Debug.Log(
        $"Replaced {materialsReplaced} out of {materialsFound} in prefab {PrefabStageUtility.GetCurrentPrefabStage().assetPath} ");

      EditorUtility.SetDirty(stagePrefabContentsRoot);
    }

    private void FindExternalRefs(List<RefData> refs, GameObject stagePrefabContentsRoot)
    {
      SerializedObject so = new(stagePrefabContentsRoot);
      var it = so.GetIterator();

      it.Next(true);

      do
      {
        if (it.propertyType != SerializedPropertyType.ObjectReference)
        {
          continue;
        }

        var obj = it.objectReferenceValue;
        if (obj is MonoBehaviour beh)
        {
          TraverseComponent(refs, beh);
        }
      } while (it.Next(true));

      for (int i = 0; i < stagePrefabContentsRoot.transform.childCount; i++)
      {
        FindExternalRefs(refs, stagePrefabContentsRoot.transform.GetChild(i).gameObject);
      }
    }

    private void TraverseComponent(List<RefData> refs, MonoBehaviour component)
    {
      SerializedObject so = new(component);
      var it = so.GetIterator();

      it.Next(true);
      do
      {
        if (it.propertyType != SerializedPropertyType.ObjectReference)
        {
          continue;
        }

        if (it.name.StartsWith("m_"))
        {
          continue;
        }

        var obj = it.objectReferenceValue;
        if (obj is MonoBehaviour beh)
        {
          var go = beh.gameObject;
          var path = AssetDatabase.GetAssetPath(go);

          if (!string.IsNullOrEmpty(path))
          {
            refs.Add(new RefData
            {
              Owner = component,
              Target = beh
            });
            //$"{SearchUtils.GetHierarchyPath(component.gameObject, false)}[{component.GetType().Name}]/{it.name} -> {path}"
          }
        }
      } while (it.Next(true));
    }

    private bool SameMaterials(Material sourceMaterial, Material textFontMaterial)
    {
      var matName = textFontMaterial.name;
      if (textFontMaterial.name.EndsWith(" (Instance)"))
      {
        matName = matName.Substring(0, matName.Length - " (Instance)".Length);
      }

      return sourceMaterial.name == matName;
    }

    protected override void OnEnable()
    {
      base.OnEnable();
      _instance = this;

      if (string.IsNullOrEmpty(AssetDatabase.AssetPathToGUID(_configFileName)))
      {
        AssetDatabase.CreateAsset(CreateInstance(typeof(FontReplacerWindowConfig)), _configFileName);
        _config = AssetDatabase.LoadAssetAtPath<FontReplacerWindowConfig>(_configFileName);
      }

      _config = AssetDatabase.LoadAssetAtPath<FontReplacerWindowConfig>(_configFileName);
      // Sometimes LoadAssetAtPath returns null even if asset is exists (AssetPathToGUID returns correct Id). 
      // In that case we try to load  config again in Update() 
      if (_config == null)
      {
        return;
      }

      _replacements = _config.Replacements ?? Array.Empty<FontReplacerWindowConfig.FontData>();
      UpdateFontMaterials();
    }

    protected override void OnDisable()
    {
      SaveChanges();
      _instance = null;
      base.OnDisable();
    }

    private void Update()
    {
      if (_externalRefsPrefab != null)
      {
        var stage = PrefabStageUtility.GetCurrentPrefabStage();
        if (stage != null && _externalRefsPrefab != stage.prefabContentsRoot)
        {
          _externalRefs = null;
          _externalRefsPrefab = null;
        }
      }

      if (_config == null)
      {
        _config = AssetDatabase.LoadAssetAtPath<FontReplacerWindowConfig>(_configFileName);
        if (_config != null)
        {
          _replacements = _config.Replacements ?? Array.Empty<FontReplacerWindowConfig.FontData>();
        }
      }
    }

    public override void SaveChanges()
    {
      if (_config != null && _replacements.Length > 0)
      {
        _config.Replacements = _replacements;
        EditorUtility.SetDirty(_config);
        AssetDatabase.SaveAssetIfDirty(_config);
      }

      base.SaveChanges();
    }

    public void UpdateFontMaterials()
    {
      foreach (var data in _replacements)
      {
        data.SetMaterials(
          FindMaterialReferences(data.SourceFont),
          FindMaterialReferences(data.ReplacementFont));
      }

      AssignFontMaterials();
    }

    private static Material[] FindMaterialReferences(TMP_FontAsset fontAsset)
    {
      if (fontAsset == null)
      {
        return Array.Empty<Material>();
      }

      var materials = new HashSet<Material>();
      if (fontAsset.material != null)
      {
        materials.Add(fontAsset.material);
      }

      var fontPath = AssetDatabase.GetAssetPath(fontAsset);
      if (string.IsNullOrEmpty(fontPath))
      {
        return materials.ToArray();
      }

      foreach (var materialGuid in AssetDatabase.FindAssets("t:Material"))
      {
        var materialPath = AssetDatabase.GUIDToAssetPath(materialGuid);
        if (string.IsNullOrEmpty(materialPath))
        {
          continue;
        }

        var material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
        if (material == null || materials.Contains(material))
        {
          continue;
        }

        var dependencies = AssetDatabase.GetDependencies(materialPath, false);
        if (Array.IndexOf(dependencies, fontPath) >= 0)
        {
          materials.Add(material);
        }
      }

      return materials.ToArray();
    }

    public void AssignFontMaterials()
    {
      foreach (var data in _replacements)
      {
        foreach (var matData in data.MaterialReplacements)
        {
          matData.SetMaterials(data.SourceMaterials, data.ReplacementMaterials);
        }
      }
    }
  }
}
