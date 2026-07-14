using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Text;
using Sirenix.OdinInspector;
using Sirenix.OdinInspector.Editor;
using UnityEditor;
using UnityEngine;
using SPath = System.IO.Path;

namespace Common.Editor.Tool
{
    public static class CreateModuleTool
    {
        [MenuItem("Assets/Create/Create Module")]
        private static void OpenCreateModuleWindow()
        {
            var window = EditorWindow.GetWindow<CreateModuleWindow>(true, "Create Module", true);
            window.Path = AssetDatabase.GetAssetPath(Selection.activeObject);
            window.RootNamespace = EditorSettings.projectGenerationRootNamespace;
            
            window.minSize = new Vector2(500, 300);
            window.ShowUtility();
        }
    }

    public sealed class CreateModuleWindow : OdinEditorWindow
    {
        private const string EditorGroup = "Editor";
        private const string RuntimeGroup = "Runtime";
        
        [ShowInInspector, FolderPath, Required]
        public string Path { get; set; } = string.Empty;
        
        [ShowInInspector]
        public string RootNamespace { get; set; } = string.Empty;
        
        [ShowInInspector]
        [ValidateInput(nameof(ValidateNamespaceName))]
        public string ModuleName { get; set; } = string.Empty;
        
        [ShowInInspector]
        public bool DotInModuleNameAsDirectory { get; set; } = false;

        [ShowInInspector, BoxGroup(EditorGroup)]
        public bool CreateEditor { get; set; } = false;
        
        [ShowInInspector, BoxGroup(EditorGroup), ShowIf(nameof(CreateEditor))]
        public string EditorDirectoryPath => GetModuleRootPath(true);

        [ShowInInspector, BoxGroup(EditorGroup), ShowIf(nameof(CreateEditor))]
        public string EditorAssemblyDefinitionName => GetAssemblyDefinitionName(true);
        
        [ShowInInspector, BoxGroup(RuntimeGroup)]
        public bool CreateRuntime { get; set; } = true;
        
        [ShowInInspector, BoxGroup(RuntimeGroup), ShowIf(nameof(CreateRuntime))]
        public string RuntimeDirectoryPath => GetModuleRootPath(false);

        [ShowInInspector, BoxGroup(RuntimeGroup), ShowIf(nameof(CreateRuntime))]
        public string RuntimeAssemblyDefinitionName => GetAssemblyDefinitionName(false);

        [Button]
        [ShowIf(nameof(CanGenerate))]
        public void Generate()
        {
            if(CreateEditor)
                GenerateEditor();
            
            if(CreateRuntime)
                GenerateRuntime();
            
            AssetDatabase.Refresh();
            Close();
        }

        // ReSharper disable once RedundantAssignment
        private bool ValidateNamespaceName(string namespaceName, ref string errorMessage)
        {
            if (string.IsNullOrEmpty(namespaceName) || string.IsNullOrWhiteSpace(namespaceName))
            {
                errorMessage = "Can not be empty";
                return false;
            }
            
            if (namespaceName.Contains(" "))
            {
                errorMessage = "Should not contain spaces";
                return false;
            }
            
            if (namespaceName.StartsWith(".") || namespaceName.EndsWith(".") || namespaceName.Contains(".."))
            {
                errorMessage = "Must not start/end with a period and must not contain two consecutive periods";
                return false;
            }

            var parts = namespaceName.Split('.');
            for (var i = 0; i < parts.Length; i++)
            {
                var part = parts[i];
                if (string.IsNullOrEmpty(part) || string.IsNullOrWhiteSpace(part))
                {
                    errorMessage = $"Part with index '{i}' is empty";
                    return false;
                }

                if ((char.IsLetter(part[0]) || part[0] == '_') == false)
                {
                    errorMessage = $"Part '{i}' must start with a character or '_'";
                    return false;
                }

                foreach (var c in part.Skip(1))
                {
                    if (char.IsLetterOrDigit(c) || c == '_')
                        continue;
                    
                    errorMessage = $"Part '{i}' contains invalid character '{c}' (code {(int)c})";
                    return false;
                }
            }

            return true;
        }

        private string GetModuleRootPath(bool isEditor)
        {
            var moduleRelativePath = DotInModuleNameAsDirectory 
                ? ModuleName.Replace('.', '/')
                : ModuleName;
            
            var platformPath = isEditor ? "Editor" : "Runtime";
            
            return SPath.Combine(Path, moduleRelativePath, platformPath).Replace('\\', '/');
        }

        private string GetAssemblyDefinitionName(bool isEditor)
        {
            var stringBuilder = new StringBuilder();
            
            if(string.IsNullOrEmpty(RootNamespace) == false && string.IsNullOrWhiteSpace(RootNamespace) == false)
                stringBuilder.Append(RootNamespace).Append('.');

            stringBuilder.Append(ModuleName);
            
            if(isEditor)
                stringBuilder.Append('.').Append("Editor");
            
            return stringBuilder.ToString();
        }

        private bool CanGenerate()
        {
            if (CreateEditor == false && CreateRuntime == false)
                return false;

            var errorMessage = string.Empty;
            return ValidateNamespaceName(ModuleName, ref errorMessage);
        }
        
        private void GenerateEditor()
        {
            Directory.CreateDirectory(EditorDirectoryPath);
            var assemblyDefinition = CreateAssemblyDefinition(true);
            var json = JsonUtility.ToJson(assemblyDefinition, true);
            
            var assemblyDefinitionFilePath = SPath.Combine(EditorDirectoryPath, $"{EditorAssemblyDefinitionName}.asmdef");
            File.WriteAllText(assemblyDefinitionFilePath, json);
        }

        private void GenerateRuntime()
        {
            Directory.CreateDirectory(RuntimeDirectoryPath);
            var assemblyDefinition = CreateAssemblyDefinition(false);
            var json = JsonUtility.ToJson(assemblyDefinition, true);
            
            var assemblyDefinitionFilePath = SPath.Combine(RuntimeDirectoryPath, $"{RuntimeAssemblyDefinitionName}.asmdef");
            File.WriteAllText(assemblyDefinitionFilePath, json);
        }

        private AssemblyDefinition CreateAssemblyDefinition(bool isEditor)
        {
            var assemblyDefinition = new AssemblyDefinition
            {
                name = GetAssemblyDefinitionName(isEditor),
                rootNamespace = RootNamespace
            };

            // ReSharper disable once InvertIf
            if (isEditor)
            {
                assemblyDefinition.includePlatforms.Add("Editor");

                // ReSharper disable once InvertIf
                if (CreateRuntime)
                {
                    var runtimeAssemblyDefinitionName = GetAssemblyDefinitionName(false);
                    assemblyDefinition.references.Add(runtimeAssemblyDefinitionName);
                }
            }
            
            return assemblyDefinition;
        }
    }

    [Serializable]
    [SuppressMessage("ReSharper", "InconsistentNaming")]
    public sealed class AssemblyDefinition
    {
        public string name = string.Empty;
        public string rootNamespace = string.Empty;
        public List<string> references = new();
        public List<string> includePlatforms = new();
        public List<string> excludePlatforms = new();
        public bool allowUnsafeCode = false;
        public bool overrideReferences = true;
        public List<string> precompiledReferences = new();
        public bool autoReferenced = false;
        public List<string> defineConstraints = new();
        public List<VersionDefine> versionDefines = new();
        public bool noEngineReferences = false;
    }

    [Serializable]
    [SuppressMessage("ReSharper", "InconsistentNaming")]
    public sealed class VersionDefine
    {
        public string name = string.Empty;
        public string expression = string.Empty;
        public string define = string.Empty;
    }
}
