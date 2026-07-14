using UnityEngine;
using UnityEditor;

public static class CopyBasePropsToLegacyProps
{
    private const string BaseColorProp = "_BaseColor";
    private const string ColorProp = "_Color";

    private const string BaseMapProp = "_BaseMap";
    private const string MainTexProp = "_MainTex";

    private const string TextureCopyPrefKey = "CopyBasePropsToLegacyProps_CopyTexture";

    // Текущее состояние флага (читаем из EditorPrefs)
    private static bool CopyTexture
    {
        get => EditorPrefs.GetBool(TextureCopyPrefKey, true); // по умолчанию: копируем текстуру
        set => EditorPrefs.SetBool(TextureCopyPrefKey, value);
    }

    [MenuItem("Tools/Materials/Copy _BaseColor -> _Color (& optionally _BaseMap -> _MainTex) (Selected)")]
    private static void CopyPropsOnSelectedMaterials()
    {
        var objects = Selection.objects;
        if (objects == null || objects.Length == 0)
        {
            Debug.LogWarning("Нет выделенных объектов. Выдели материалы в Project и повтори.");
            return;
        }

        int processed = 0;
        int changedColor = 0;
        int changedTexture = 0;

        foreach (var obj in objects)
        {
            if (obj is not Material mat)
                continue;

            processed++;
            bool matChanged = false;

            Undo.RecordObject(mat, "Copy Base props to legacy props");

            // --- Цвет: _BaseColor -> _Color ---
            if (mat.HasProperty(BaseColorProp) && mat.HasProperty(ColorProp))
            {
                Color baseColor = mat.GetColor(BaseColorProp);
                Color curColor = mat.GetColor(ColorProp);

                if (baseColor != curColor)
                {
                    mat.SetColor(ColorProp, baseColor);
                    matChanged = true;
                    changedColor++;
                }
            }

            // --- Текстура: _BaseMap -> _MainTex (опционально) ---
            if (CopyTexture && mat.HasProperty(BaseMapProp) && mat.HasProperty(MainTexProp))
            {
                Texture baseMap = mat.GetTexture(BaseMapProp);
                Texture curMainTex = mat.GetTexture(MainTexProp);

                Vector2 baseTiling = mat.GetTextureScale(BaseMapProp);
                Vector2 baseOffset = mat.GetTextureOffset(BaseMapProp);

                bool textureDiffers = baseMap != curMainTex;
                bool tilingDiffers = baseTiling != mat.GetTextureScale(MainTexProp);
                bool offsetDiffers = baseOffset != mat.GetTextureOffset(MainTexProp);

                if (textureDiffers || tilingDiffers || offsetDiffers)
                {
                    mat.SetTexture(MainTexProp, baseMap);
                    mat.SetTextureScale(MainTexProp, baseTiling);
                    mat.SetTextureOffset(MainTexProp, baseOffset);
                    matChanged = true;
                    changedTexture++;
                }
            }

            if (matChanged)
                EditorUtility.SetDirty(mat);
        }

        Debug.Log(
            $"Обработано материалов: {processed}. " +
            $"Изменено цветов: {changedColor}, текстур: {changedTexture}. " +
            $"Копирование текстур: {(CopyTexture ? "ВКЛ" : "ВЫКЛ")}."
        );
    }

    // Пункт меню виден, только если в выделении есть материалы
    [MenuItem("Tools/Materials/Copy _BaseColor -> _Color (& optionally _BaseMap -> _MainTex) (Selected)", true)]
    private static bool ValidateCopyPropsOnSelectedMaterials()
    {
        foreach (var obj in Selection.objects)
        {
            if (obj is Material)
                return true;
        }
        return false;
    }

    // Тоггл для включения/выключения копирования текстуры
    [MenuItem("Tools/Materials/Toggle copying _BaseMap -> _MainTex")]
    private static void ToggleCopyTexture()
    {
        CopyTexture = !CopyTexture;
    }

    // Отображаем галочку в меню у тоггла
    [MenuItem("Tools/Materials/Toggle copying _BaseMap -> _MainTex", true)]
    private static bool ToggleCopyTextureValidate()
    {
        Menu.SetChecked("Tools/Materials/Toggle copying _BaseMap -> _MainTex", CopyTexture);
        return true;
    }
}
