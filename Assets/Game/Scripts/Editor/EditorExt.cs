using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Game
{
    public static class EditorExt
    {
        public static void SelectAssetsWithType(string typeName, string[] searchFolders)
        {
            string[] guids = AssetDatabase.FindAssets($"t:{typeName}", searchFolders);

            List<Object> objects = new List<Object>();

            foreach (var guid in guids)
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var asset = AssetDatabase.LoadMainAssetAtPath(path);
                
                objects.Add(asset);
            }


            if (objects.Count > 0)
            {
                EditorUtility.FocusProjectWindow();
                Selection.objects = objects.ToArray();
            }
        }


        public static List<T> GetAssetsWithComponent<T>(string[] searchFolders) where T : MonoBehaviour
        {
            string[] guids = AssetDatabase.FindAssets($"t:GameObject", searchFolders);

            List<Object> objects = new List<Object>();

            foreach (var guid in guids)
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var asset = AssetDatabase.LoadMainAssetAtPath(path);
                
                objects.Add(asset);
            }

            var result = new List<T>();

            foreach (var obj in objects)
            {
                var go = obj as GameObject;
                var levelObj = go.GetComponent<T>();
                if (levelObj != null)
                {
                    result.Add(levelObj);
                }
            }

            return result;
        }
        
        public static List<T> GetAssetsOfType<T>(string[] searchFolders) where T : Object
        {
            string[] guids = AssetDatabase.FindAssets($"t:Object", searchFolders);

            List<T> objects = new List<T>();

            foreach (var guid in guids)
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var asset = AssetDatabase.LoadMainAssetAtPath(path);
                var obj = asset as T;

                if (obj != null)
                {
                    objects.Add(obj);
                }
            }

            return objects;
        }
        
        
        public static T GetAssetWithId<T>(string[] searchFolders, string id) where T : MonoBehaviour
        {
            string[] guids = AssetDatabase.FindAssets($"t:GameObject", searchFolders);
            

            foreach (var guid in guids)
            {
                if (guid == id)
                {
                    var path = AssetDatabase.GUIDToAssetPath(guid);
                    var asset = AssetDatabase.LoadMainAssetAtPath(path);

                    var go = asset as GameObject;
                    if (go != null)
                    {
                        var instObj = go.GetComponent<T>();
                        return instObj;
                    }
                }
            }

            return default;
        }
        
        
        public static T GetAssetWithType<T>() where T : Object
        {
            var typeName = typeof(T).Name;
            string[] guids = AssetDatabase.FindAssets($"t:{typeName}", new[] {"Assets/Game"});


            if (guids.Length > 0)
            {
                var guid = guids[0];
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var asset = AssetDatabase.LoadMainAssetAtPath(path);
                
                if (guids.Length > 1)
                {
                    Debug.LogWarning($"Found more then 1 object of type {typeName}. Using {path}");
                }
                
                return asset as T;
            }

            return null;
        }
        


        public static void FixLinksToLocal<T>(SerializedObject serObj, string path, string propertyName, Func<Object, Object> processor)
        {
            var viewSerProp = serObj.FindProperty(propertyName);
            if (viewSerProp == null)
            {
                Debug.LogError($"Fix {propertyName} Error");
                return;
            }
            
            var configFolder = Path.GetDirectoryName(path);
            
            var typeName = typeof(T).Name;
            string[] guids = AssetDatabase.FindAssets($"t:{typeName}", new []{configFolder});


            foreach (var guid in guids)
            {
                var assetPath = AssetDatabase.GUIDToAssetPath(guid);
                var assets = AssetDatabase.LoadAllAssetsAtPath(assetPath);
                foreach (var asset in assets)
                {
                    var result = processor.Invoke(asset);
                    if (result != null)
                    {
                        viewSerProp.objectReferenceValue = result;
                        serObj.ApplyModifiedProperties();
                
                        Debug.Log($"Fix {propertyName} Success");
                        return;
                    }
                }
            }
        }
    }
}
