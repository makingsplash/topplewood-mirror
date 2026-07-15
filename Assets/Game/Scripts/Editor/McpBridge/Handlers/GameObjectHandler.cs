using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Game.Editor.McpBridge.Handlers
{
    // Generic GameObject CRUD in the open scene(s). Objects are referenced by
    // instanceId (returned by create/find). Local-space transform. No game types.
    [InitializeOnLoad]
    public static class GameObjectHandler
    {
        static GameObjectHandler()
        {
            McpBridgeServer.RegisterHandler("create_gameobject", Create);
            McpBridgeServer.RegisterHandler("find_gameobjects", Find);
            McpBridgeServer.RegisterHandler("set_gameobject_active", SetActive);
            McpBridgeServer.RegisterHandler("set_transform", SetTransform);
            McpBridgeServer.RegisterHandler("add_component", AddComponent);
            McpBridgeServer.RegisterHandler("delete_gameobject", Delete);
        }

        private static object Create(JObject p)
        {
            var name = p["name"]?.ToString() ?? "GameObject";

            GameObject go;
            var primitive = p["primitive"]?.ToString();
            if (!string.IsNullOrEmpty(primitive))
            {
                if (!Enum.TryParse<PrimitiveType>(primitive, true, out var prim))
                    throw new ArgumentException($"Unknown primitive '{primitive}'");
                go = GameObject.CreatePrimitive(prim);
                go.name = name;
            }
            else
            {
                go = new GameObject(name);
            }

            var parentId = p["parentId"]?.ToObject<int>();
            if (parentId.HasValue && parentId.Value != 0)
            {
                var parent = Resolve(parentId.Value);
                go.transform.SetParent(parent.transform, false);
            }

            Undo.RegisterCreatedObjectUndo(go, "Create GameObject (MCP)");
            EditorSceneManager.MarkSceneDirty(go.scene);
            return Describe(go);
        }

        private static object Find(JObject p)
        {
            var name = p["name"]?.ToString();
            var tag = p["tag"]?.ToString();

            IEnumerable<GameObject> all = AllSceneObjects();
            if (!string.IsNullOrEmpty(name))
                all = all.Where(g => g.name.IndexOf(name, StringComparison.OrdinalIgnoreCase) >= 0);
            if (!string.IsNullOrEmpty(tag))
                all = all.Where(g => g.CompareTag(tag));

            return all.Take(200).Select(Describe).ToList();
        }

        private static object SetActive(JObject p)
        {
            var go = Resolve(p["instanceId"].ToObject<int>());
            var active = p["active"]?.ToObject<bool>() ?? true;
            Undo.RecordObject(go, "Set Active (MCP)");
            go.SetActive(active);
            EditorSceneManager.MarkSceneDirty(go.scene);
            return new { ok = true, active };
        }

        private static object SetTransform(JObject p)
        {
            var go = Resolve(p["instanceId"].ToObject<int>());
            var t = go.transform;
            Undo.RecordObject(t, "Set Transform (MCP)");
            if (TryVec(p["position"], out var pos)) t.localPosition = pos;
            if (TryVec(p["rotation"], out var rot)) t.localEulerAngles = rot;
            if (TryVec(p["scale"], out var scale)) t.localScale = scale;
            EditorSceneManager.MarkSceneDirty(go.scene);
            return new { ok = true };
        }

        private static object AddComponent(JObject p)
        {
            var go = Resolve(p["instanceId"].ToObject<int>());
            var typeName = p["type"]?.ToString();
            if (string.IsNullOrEmpty(typeName))
                throw new ArgumentException("Missing 'type'");
            var type = ResolveComponentType(typeName);
            if (type == null)
                throw new ArgumentException($"Component type not found: {typeName}");
            var comp = Undo.AddComponent(go, type);
            EditorSceneManager.MarkSceneDirty(go.scene);
            return new { ok = true, component = comp.GetType().FullName };
        }

        private static object Delete(JObject p)
        {
            var go = Resolve(p["instanceId"].ToObject<int>());
            var scene = go.scene;
            Undo.DestroyObjectImmediate(go);
            EditorSceneManager.MarkSceneDirty(scene);
            return new { ok = true };
        }

        // ── helpers ──────────────────────────────────────────────────────────────
        private static GameObject Resolve(int instanceId)
        {
            if (EditorUtility.InstanceIDToObject(instanceId) is GameObject go)
                return go;
            throw new ArgumentException($"No GameObject with instanceId {instanceId}");
        }

        private static IEnumerable<GameObject> AllSceneObjects()
        {
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                var scene = SceneManager.GetSceneAt(i);
                if (!scene.isLoaded) continue;
                foreach (var root in scene.GetRootGameObjects())
                foreach (var t in root.GetComponentsInChildren<Transform>(true))
                    yield return t.gameObject;
            }
        }

        private static object Describe(GameObject go) => new
        {
            instanceId = go.GetInstanceID(),
            name = go.name,
            path = Path(go.transform),
            active = go.activeSelf,
            components = go.GetComponents<Component>()
                .Where(c => c != null)
                .Select(c => c.GetType().Name)
                .ToArray(),
        };

        private static string Path(Transform t)
        {
            var stack = new Stack<string>();
            while (t != null) { stack.Push(t.name); t = t.parent; }
            return string.Join("/", stack);
        }

        private static bool TryVec(JToken token, out Vector3 v)
        {
            v = Vector3.zero;
            if (token == null) return false;
            v = new Vector3(
                token["x"]?.ToObject<float>() ?? 0f,
                token["y"]?.ToObject<float>() ?? 0f,
                token["z"]?.ToObject<float>() ?? 0f);
            return true;
        }

        private static Type ResolveComponentType(string typeName)
        {
            var types = AppDomain.CurrentDomain.GetAssemblies()
                .SelectMany(SafeGetTypes)
                .Where(t => typeof(Component).IsAssignableFrom(t));
            return types.FirstOrDefault(t => t.FullName == typeName)
                ?? types.FirstOrDefault(t => t.Name == typeName);
        }

        private static IEnumerable<Type> SafeGetTypes(System.Reflection.Assembly a)
        {
            try { return a.GetTypes(); }
            catch { return Array.Empty<Type>(); }
        }
    }
}
