using System;
using System.Collections.Generic;
using CopyPasteScripts;
using Iliai.UiBuild;
using UnityEditor;
using UnityEngine;

namespace Game.Editor.UiBuild
{
    /// <summary>
    /// ПРОЕКТНЫЕ хэндлеры блока `behaviour`. Живут здесь, а не в Assets/UiBuild, потому что тянут типы
    /// этого проекта (SayKit-стек). Ядро компилятора о них ничего не знает — находит через TypeCache
    /// по атрибуту. В новом проекте этот файл пишется заново под его UI-фреймворк; ядро не меняется.
    /// </summary>
    [UiHandler("SafeArea")]
    public sealed class SafeAreaHandler : IUiBehaviourHandler
    {
        public IEnumerable<Type> ManagedTypes => new[] { typeof(CustomAdjustToSafeArea) };

        public void Apply(UiBuildContext ctx, GameObject target, UiBehaviourSpec spec)
        {
            UiReader r = spec.Reader(ctx.Report);
            string align = r.Str("align", "stretch");
            bool banner = r.Bool("banner", false);
            r.Finish();

            int alignIndex;
            switch (align)
            {
                case "top": alignIndex = 0; break;
                case "bottom": alignIndex = 1; break;
                case "stretch": alignIndex = 2; break;
                default:
                    ctx.Report.Error(spec.Where, $"SafeArea.align '{align}' — допустимо top | bottom | stretch");
                    return;
            }

            var component = target.GetComponent<CustomAdjustToSafeArea>() ?? target.AddComponent<CustomAdjustToSafeArea>();

            // Поля приватные с [SerializeField] — пишем через SerializedObject, а не рефлексией:
            // так значение точно попадает в сериализацию префаба.
            var so = new SerializedObject(component);
            SerializedProperty alignProp = so.FindProperty("align");
            SerializedProperty bannerProp = so.FindProperty("canHaveSaykitBanner");
            if (alignProp == null || bannerProp == null)
            {
                ctx.Report.Error(spec.Where, "CustomAdjustToSafeArea изменился: не нашёл поля 'align'/'canHaveSaykitBanner' — хэндлер надо обновить");
                return;
            }
            alignProp.enumValueIndex = alignIndex;
            bannerProp.boolValue = banner;
            so.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
