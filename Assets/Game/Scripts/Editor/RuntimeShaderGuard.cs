using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using Gameplay.View;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace Game.Editor
{
    // TASK-554: guard of the class "a shader looked up by name at runtime is not in the player". Shader.Find is a
    // string lookup, the build does not see it as a reference, so a shader nothing else references is left out and
    // the lookup returns null on device — the wood grain and piece shadows never rendered on Android, and nothing
    // failed. Two checks, shared by the build hook below and the EditMode test (RuntimeShaderGuardTests):
    //   1. runtime code (Assets/Game/Scripts minus Editor/Tests folders) has NO Shader.Find — shaders for code-built
    //      materials go through RuntimeShaders, whose asset lives under Resources and so is in the player;
    //   2. that asset exists, and every entry is a real reference to the shader with the expected name, compiling.
    // A new runtime shader therefore either gets an entry in RuntimeShaders or fails the build/test — it cannot pass
    // silently the way Game/Wood Block did.
    public static class RuntimeShaderGuard
    {
        public const string AssetPath = "Assets/Resources/" + RuntimeShaders.ResourcePath + ".asset";

        private static readonly string[] ScanRoots = { "Assets/Game/Scripts" };

        // The registry itself keeps one string lookup as the loud last resort.
        private const string RegistryFile = "RuntimeShaders.cs";

        private static readonly Regex FindCall = new Regex(@"\bShader\s*\.\s*Find\s*\(", RegexOptions.Compiled);

        /// <summary>All problems found; empty = the guarantee holds.</summary>
        public static List<string> Validate()
        {
            var problems = new List<string>();
            ValidateRegistry(problems);
            ValidateSources(problems);
            return problems;
        }

        private static void ValidateRegistry(List<string> problems)
        {
            var registry = AssetDatabase.LoadAssetAtPath<RuntimeShaders>(AssetPath);
            if (registry == null)
            {
                problems.Add($"{AssetPath}: asset missing — runtime materials would fall back to flat shaders in the player.");
                return;
            }

            foreach (var entry in registry.Entries())
            {
                var shader = entry.Value;
                if (shader == null)
                    problems.Add($"{AssetPath}: no reference for '{entry.Key}'.");
                else if (shader.name != entry.Key)
                    problems.Add($"{AssetPath}: entry for '{entry.Key}' points at '{shader.name}'.");
                else if (ShaderUtil.ShaderHasError(shader))
                    problems.Add($"{AssetPath}: shader '{entry.Key}' has compile errors.");
            }
        }

        private static void ValidateSources(List<string> problems)
        {
            foreach (var root in ScanRoots)
            {
                if (!Directory.Exists(root))
                    continue;
                foreach (var file in Directory.GetFiles(root, "*.cs", SearchOption.AllDirectories))
                {
                    var path = file.Replace('\\', '/');
                    if (path.Contains("/Editor/") || path.Contains("/Tests/") || Path.GetFileName(path) == RegistryFile)
                        continue;
                    foreach (var line in FindCallLines(File.ReadAllText(path)))
                        problems.Add($"{path}:{line}: Shader.Find in runtime code — take the shader from RuntimeShaders (a string lookup does not put the shader into the player).");
                }
            }
        }

        /// <summary>1-based lines with a <c>Shader.Find(</c> call; whole-line <c>//</c> comments are skipped.</summary>
        public static IEnumerable<int> FindCallLines(string source)
        {
            var lines = source.Split('\n');
            for (var i = 0; i < lines.Length; i++)
            {
                var text = lines[i].TrimStart();
                if (text.StartsWith("//"))
                    continue;
                if (FindCall.IsMatch(text))
                    yield return i + 1;
            }
        }
    }

    /// <summary>Fails any player build (Android/Jenkins/batch) while <see cref="RuntimeShaderGuard"/> reports a problem.</summary>
    public sealed class RuntimeShaderBuildGate : IPreprocessBuildWithReport
    {
        public int callbackOrder => 0;

        public void OnPreprocessBuild(BuildReport report)
        {
            var problems = RuntimeShaderGuard.Validate();
            if (problems.Count == 0)
                return;
            foreach (var p in problems)
                Debug.LogError("[RuntimeShaderGuard] " + p);
            throw new BuildFailedException("RuntimeShaderGuard: " + problems.Count + " runtime shader(s) not guaranteed in the player:\n" + string.Join("\n", problems));
        }
    }
}
