using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Game.Editor
{
    /// <summary>
    /// Чинит материалы перенесённого эффекта ветра под URP: <c>Tools → Wood Style → Починить ветер под URP</c>.
    ///
    /// ЗАЧЕМ. Эффект <c>Wind_FX</c> взят из соседнего проекта, а тот на встроенном рендер-пайплайне.
    /// Его материалы ссылаются на ВСТРОЕННЫЕ шейдеры по числовому fileID (10720 — Particles/Additive,
    /// 10721 — Particles/Alpha Blended). В URP этих шейдеров нет: материал приезжает розовым, и весь
    /// эффект пропадает. Замену нельзя было сделать копированием файлов — нужен пересбор материала.
    ///
    /// ЧТО ДЕЛАЕТ. Каждому такому материалу ставит URP-шейдер частиц, переносит текстуру из
    /// <c>_MainTex</c> в <c>_BaseMap</c> (в URP имя другое, иначе материал станет белым) и выставляет
    /// прозрачность с нужным режимом смешивания: аддитивный для свечения, альфа для дымки.
    ///
    /// Идемпотентно: повторный запуск ничего не портит, поэтому его можно звать из съёмки перед
    /// каждым кадром.
    /// </summary>
    public static class WindFxImport
    {
        /// <summary>Материалы эффекта. Полные пути, а не поиск по имени: одноимённые материалы есть и
        /// в Polygon Arsenal, и трогать их нельзя — они уже настроены под наш проект.</summary>
        private static readonly string[] Materials =
        {
            "Assets/Resources/WindFX/SG_Wind.mat",
        };

        /// <summary>fileID встроенных шейдеров, которые в URP не существуют.</summary>
        private static readonly Dictionary<int, bool> BuiltinParticleShaders = new Dictionary<int, bool>
        {
            { 10720, true },   // Particles/Additive → аддитивный
            { 10721, false },  // Particles/Alpha Blended → альфа
        };

        [MenuItem("Tools/Wood Style/Починить ветер под URP")]
        public static void Apply()
        {
            var fixedCount = 0;
            foreach (var path in Materials)
            {
                var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (mat == null)
                {
                    Debug.LogWarning($"[wind] нет материала {path} — пропускаю");
                    continue;
                }

                if (!NeedsFix(mat))
                    continue;

                // Текстуру снимаем ДО подмены шейдера: после неё старое свойство уже недоступно.
                var tex = mat.HasProperty("_MainTex") ? mat.GetTexture("_MainTex") : null;
                var tint = mat.HasProperty("_TintColor") ? mat.GetColor("_TintColor") : Color.white;
                var additive = IsAdditive(mat);

                var shader = Shader.Find("Universal Render Pipeline/Particles/Unlit")
                             ?? Shader.Find("Universal Render Pipeline/Particles/Simple Lit")
                             ?? Shader.Find("Sprites/Default");
                if (shader == null)
                {
                    Debug.LogError("[wind] URP-шейдер частиц не найден — материал остался как есть");
                    continue;
                }

                mat.shader = shader;

                if (tex != null)
                {
                    if (mat.HasProperty("_BaseMap")) mat.SetTexture("_BaseMap", tex);
                    if (mat.HasProperty("_MainTex")) mat.SetTexture("_MainTex", tex);
                }
                if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", tint);

                // Прозрачность: 1 = Transparent. Режим смешивания — 1 аддитивный, 0 альфа.
                if (mat.HasProperty("_Surface")) mat.SetFloat("_Surface", 1f);
                if (mat.HasProperty("_Blend")) mat.SetFloat("_Blend", additive ? 1f : 0f);
                if (mat.HasProperty("_ZWrite")) mat.SetFloat("_ZWrite", 0f);
                mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
                mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");

                EditorUtility.SetDirty(mat);
                fixedCount++;
                Debug.Log($"[wind] {System.IO.Path.GetFileName(path)} → {shader.name}, " +
                          $"смешивание {(additive ? "аддитивное" : "альфа")}, текстура {(tex != null ? tex.name : "нет")}");
            }

            if (fixedCount > 0)
                AssetDatabase.SaveAssets();
            Debug.Log($"[wind] материалов починено: {fixedCount} из {Materials.Length}");
        }

        /// <summary>Материал сломан, если его шейдер не найден (розовый) или это встроенный
        /// партикловый шейдер из другого пайплайна.</summary>
        private static bool NeedsFix(Material mat)
        {
            if (mat.shader == null || mat.shader.name == "Hidden/InternalErrorShader")
                return true;
            return mat.shader.name.StartsWith("Particles/") || mat.shader.name.StartsWith("Legacy Shaders/");
        }

        private static bool IsAdditive(Material mat)
        {
            var name = mat.shader != null ? mat.shader.name : string.Empty;
            if (name.Contains("Additive"))
                return true;
            // Шейдер не резолвится (чужой пайплайн) — идём по fileID из самого ассета.
            var path = AssetDatabase.GetAssetPath(mat);
            if (string.IsNullOrEmpty(path) || !System.IO.File.Exists(path))
                return false;
            var text = System.IO.File.ReadAllText(path);
            var match = System.Text.RegularExpressions.Regex.Match(text, @"m_Shader: \{fileID: (\d+)");
            if (match.Success && int.TryParse(match.Groups[1].Value, out var id) &&
                BuiltinParticleShaders.TryGetValue(id, out var add))
                return add;
            return false;
        }
    }
}
