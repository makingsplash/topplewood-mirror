using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace Iliai.UiBuild
{
    public sealed class UiBuildResult
    {
        public string PrefabPath;
        public bool Saved;
        public bool WasUpdate;
    }

    /// <summary>
    /// Компилятор: спека → UGUI-префаб. Детерминированно и без ручных шагов в инспекторе.
    ///
    /// Правило пересборки (`isUpdate`): корневой GameObject СОХРАНЯЕТСЯ вместе с GUID ассета и с
    /// компонентами, которых спека не касается, — дети пересоздаются, managed-компоненты, ушедшие из
    /// спеки, снимаются. Без этого итерация «правь спеку → пересобирай» ломала бы навешенное вручную
    /// и превращала все ссылки на префаб в missing.
    /// </summary>
    public static class UiPrefabBuilder
    {
        public static UiBuildResult Build(UiSpec spec, UiBuildContext ctx)
        {
            UiBuildReport report = ctx.Report;
            var result = new UiBuildResult();
            if (spec == null) return result;

            ctx.Spec = spec;
            ctx.Controller = null;

            string root = ctx.Config.PrefabRootFor(spec.Kind);
            result.PrefabPath = $"{root}/{spec.Name}.prefab";
            string absDir = ctx.Config.Abs(root);
            Directory.CreateDirectory(absDir);

            bool exists = File.Exists(ctx.Config.Abs(result.PrefabPath));
            result.WasUpdate = exists;

            GameObject contents = null;
            try
            {
                contents = exists
                    ? PrefabUtility.LoadPrefabContents(result.PrefabPath)
                    : new GameObject(spec.Name, typeof(RectTransform));

                if (contents == null)
                {
                    report.Error(spec.SourcePath, $"не открыл существующий префаб {result.PrefabPath}");
                    return result;
                }

                contents.name = spec.Name;
                BuildRoot(spec, ctx, contents);

                // Раскладку фиксируем ДО деактивации hidden-узлов и до рендера: без этого позиции
                // внутри layout-групп остаются нулевыми до первого кадра, и wireframe покажет свалку.
                var rootRect = contents.GetComponent<RectTransform>();
                if (rootRect != null) LayoutRebuilder.ForceRebuildLayoutImmediate(rootRect);
                ApplyHiddenFlags(spec, contents);

                BindReferences(spec, ctx, contents);

                if (!report.Ok)
                {
                    report.Note($"{result.PrefabPath} НЕ записан — сначала починить ошибки выше");
                    return result;
                }

                PrefabUtility.SaveAsPrefabAsset(contents, result.PrefabPath, out bool success);
                result.Saved = success;
                if (!success)
                {
                    report.Error(spec.SourcePath, $"Unity не записал префаб {result.PrefabPath}");
                    return result;
                }
                report.Note($"{(exists ? "перестроен" : "создан")}: {result.PrefabPath}");
            }
            catch (Exception e)
            {
                report.Error(spec.SourcePath, $"сборка упала: {e.GetType().Name}: {e.Message}");
                return result;
            }
            finally
            {
                if (contents != null)
                {
                    if (exists) PrefabUtility.UnloadPrefabContents(contents);
                    else UnityEngine.Object.DestroyImmediate(contents);
                }
            }

            if (result.Saved) RunPostBuildSteps(spec, ctx, result.PrefabPath);
            return result;
        }

        static void RunPostBuildSteps(UiSpec spec, UiBuildContext ctx, string prefabPath)
        {
            var saved = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            foreach (IUiPostBuildStep step in UiBehaviourRegistry.PostBuildSteps)
            {
                try { step.Run(ctx, spec, saved, prefabPath); }
                catch (Exception e)
                {
                    ctx.Report.Error(spec.SourcePath, $"post-build шаг {step.GetType().Name} упал: {e.Message}");
                }
            }
        }

        // ───────────────────────────── корень ─────────────────────────────

        static void BuildRoot(UiSpec spec, UiBuildContext ctx, GameObject go)
        {
            var rect = Ensure<RectTransform>(go);
            rect.pivot = spec.Pivot;
            rect.anchoredPosition = Vector2.zero;
            rect.localScale = Vector3.one;
            rect.localRotation = Quaternion.identity;

            if (spec.Kind == "screen")
            {
                // Экран РАСТЯГИВАЕТСЯ на родителя, а не печётся фиксированным боксом reference-размера.
                // Причина конкретная: показ идёт через `Addressables.InstantiateAsync(адрес, канвас)` БЕЗ
                // ре-анкеринга — что записано в префабе, то и живёт на экране. Фиксированные 1080×1920 по
                // центру означали бы, что на 1080×2400 затемнение не покрывает 480 px, а safe-area считает
                // от 1920 вместо реальной высоты. Нашла роль ui-composer на первой же спеке; хуже того,
                // рендерер это МАСКИРОВАЛ, переанкеривая инстанс только для картинки.
                rect.anchorMin = Vector2.zero;
                rect.anchorMax = Vector2.one;
                rect.offsetMin = Vector2.zero;
                rect.offsetMax = Vector2.zero;
                if (spec.Size.HasValue)
                    ctx.Report.Warn(spec.SourcePath, "'size' у kind: screen не применяется — экран растягивается на канвас. Оставь его как описание design-space или убери");
            }
            else
            {
                Vector2Int size = spec.Size ?? new Vector2Int(100, 100);
                rect.anchorMin = new Vector2(0.5f, 0.5f);
                rect.anchorMax = new Vector2(0.5f, 0.5f);
                rect.sizeDelta = new Vector2(size.x, size.y);
            }

            ApplyFill(ctx, go, spec.Background, spec.SourcePath + ".background");
            AttachController(spec, ctx, go);
            ClearChildren(go);

            if (spec.Children != null)
                foreach (UiNode child in spec.Children)
                    BuildNode(child, ctx, go.transform);

            ApplyBehaviours(ctx, go, spec.Behaviour, spec.SourcePath);
            RemoveStaleManaged(ctx, go, spec.Behaviour, spec.Background != null);
        }

        static void AttachController(UiSpec spec, UiBuildContext ctx, GameObject go)
        {
            if (string.IsNullOrEmpty(spec.Controller)) return;
            Type type = FindMonoBehaviourType(spec.Controller);
            if (type == null)
            {
                ctx.Report.Error(spec.SourcePath, $"controller '{spec.Controller}' — такого MonoBehaviour в проекте нет");
                return;
            }
            var existing = go.GetComponent(type) as MonoBehaviour;
            ctx.Controller = existing ?? (MonoBehaviour)go.AddComponent(type);
        }

        static Type FindMonoBehaviourType(string name)
        {
            Type match = null;
            foreach (Type t in TypeCache.GetTypesDerivedFrom<MonoBehaviour>())
            {
                if (t.Name != name && t.FullName != name) continue;
                if (match != null && t.FullName != name) continue;      // при неоднозначности выигрывает точное FullName
                match = t;
                if (t.FullName == name) break;
            }
            return match;
        }

        static void ClearChildren(GameObject go)
        {
            for (int i = go.transform.childCount - 1; i >= 0; i--)
                UnityEngine.Object.DestroyImmediate(go.transform.GetChild(i).gameObject);
        }

        // ───────────────────────────── узлы ─────────────────────────────

        static void BuildNode(UiNode node, UiBuildContext ctx, Transform parent)
        {
            if (node == null || string.IsNullOrEmpty(node.Id)) return;

            GameObject go;
            bool fromPrefab = false;
            if (!string.IsNullOrEmpty(node.Prefab))
            {
                GameObject prefab = ctx.ResolvePrefab(node.Prefab, node.Where);
                if (prefab == null) return;
                go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
                fromPrefab = true;
                go.name = node.Id;
            }
            else
            {
                go = new GameObject(node.Id, typeof(RectTransform));
                go.transform.SetParent(parent, false);
            }

            var rect = Ensure<RectTransform>(go);
            rect.localScale = Vector3.one;
            if (!fromPrefab) rect.anchoredPosition = Vector2.zero;

            bool inLayoutGroup = parent != null && parent.GetComponent<LayoutGroup>() != null;
            if (inLayoutGroup && !string.IsNullOrEmpty(node.Anchor) && !node.IgnoreLayout)
            {
                // Группа сама выставляет детям anchor/позицию/размер — заданный анкер был бы затёрт
                // молча, и автор бы гадал, почему «anchor не работает».
                ctx.Report.Error(node.Where, $"anchor '{node.Anchor}' внутри layout-группы родителя: позицию и размер здесь задаёт группа. Убери anchor и задай size/height (или поставь ignoreLayout: true)");
            }

            // Узел с ignoreLayout группа не раскладывает (LayoutElement.ignoreLayout) — значит, его место задаёт
            // он сам: свой anchor/offset/size, как вне группы. Раньше анкер ему не применялся НИКОГДА, а size
            // уходил в LayoutElement.preferred*, который группа для него же игнорирует: ошибка выше советовала
            // ignoreLayout, а узел молча оставался с дефолтным rect (шапка/крестик поверх авто-высотной карточки).
            bool placedByGroup = inLayoutGroup && !node.IgnoreLayout;
            bool anchored = !placedByGroup && UiAnchor.Apply(rect, node, ctx.Report);
            if (!anchored) ApplyDefaultRect(node, ctx, rect, placedByGroup);

            if (Mathf.Abs(node.Rotation) > 0.001f)
                rect.localRotation = Quaternion.Euler(0f, 0f, node.Rotation);

            switch (node.Type)
            {
                case "image": BuildImage(node, ctx, go); break;
                case "text": BuildText(node, ctx, go); break;
                default:
                    if (!string.IsNullOrEmpty(node.Sprite) || !string.IsNullOrEmpty(node.Color))
                        ApplyFill(ctx, go, new UiFill { Sprite = node.Sprite, Color = node.Color, ImageType = node.ImageType, PreserveAspect = node.PreserveAspect }, node.Where);
                    break;
            }

            ApplyLayoutGroup(node, ctx, go);
            ApplyFit(node, go);
            ApplyLayoutElement(node, go);
            if (node.DisableRaycast) DisableRaycast(go);

            if (node.Children != null)
            {
                if (fromPrefab)
                {
                    // Deep-override: у инстанса префаба дети уже есть, пересоздавать их нельзя —
                    // правим существующие по id, отсутствующие создаём.
                    foreach (UiNode child in node.Children) OverrideOrCreate(child, ctx, go.transform);
                }
                else
                {
                    foreach (UiNode child in node.Children) BuildNode(child, ctx, go.transform);
                }
            }

            ApplyBehaviours(ctx, go, node.Behaviour, node.Where);
        }

        /// <summary>Дефолтная геометрия, когда анкер не задан — ровно таблица размеров из схемы.</summary>
        static void ApplyDefaultRect(UiNode node, UiBuildContext ctx, RectTransform rect, bool inLayoutGroup)
        {
            int? w = node.Width ?? (node.Size.HasValue ? node.Size.Value.x : (int?)null);
            int? h = node.Height ?? (node.Size.HasValue ? node.Size.Value.y : (int?)null);

            if (node.Pivot.HasValue) rect.pivot = node.Pivot.Value;

            if (w.HasValue || h.HasValue)
            {
                // Внутри группы размер задаётся ТОЛЬКО через LayoutElement: sizeDelta группа перетирает
                // на своём проходе, и узел становится нулевой высоты — то есть невидимым. Наступал на
                // этом при первой же проверке рендера: заданный height: 160 просто не появился в кадре.
                if (inLayoutGroup)
                {
                    var element = Ensure<LayoutElement>(rect.gameObject);
                    if (w.HasValue) element.preferredWidth = w.Value;
                    if (h.HasValue) element.preferredHeight = h.Value;
                    return;
                }

                rect.anchorMin = new Vector2(0.5f, 0.5f);
                rect.anchorMax = new Vector2(0.5f, 0.5f);
                rect.sizeDelta = new Vector2(w ?? rect.sizeDelta.x, h ?? rect.sizeDelta.y);
                return;
            }

            if (inLayoutGroup) return;                       // размер посчитает группа

            if (node.Type == "text") return;                 // ContentSizeFitter поставит ApplyFit / BuildText

            rect.anchorMin = Vector2.zero;                   // ни размера, ни анкера, ни группы → на весь родитель
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }

        static void BuildImage(UiNode node, UiBuildContext ctx, GameObject go)
        {
            if (string.IsNullOrEmpty(node.Sprite) && string.IsNullOrEmpty(node.Color))
                ctx.Report.Warn(node.Where, "type 'image' без sprite и без color — на wireframe будет пусто");
            ApplyFill(ctx, go, new UiFill
            {
                Sprite = node.Sprite,
                Color = node.Color,
                ImageType = node.ImageType,
                PreserveAspect = node.PreserveAspect,
            }, node.Where);
        }

        static void ApplyFill(UiBuildContext ctx, GameObject go, UiFill fill, string where)
        {
            if (fill == null || (string.IsNullOrEmpty(fill.Sprite) && string.IsNullOrEmpty(fill.Color))) return;
            var image = Ensure<Image>(go);
            image.sprite = string.IsNullOrEmpty(fill.Sprite) ? null : ctx.ResolveSprite(fill.Sprite, where);
            image.color = ctx.Tokens.TryColor(fill.Color, ctx.Report, where, out Color c) ? c : Color.white;
            image.preserveAspect = fill.PreserveAspect;
            image.type = ParseImageType(fill.ImageType, image, ctx.Report, where);
        }

        static Image.Type ParseImageType(string raw, Image image, UiBuildReport report, string where)
        {
            switch (raw)
            {
                case null:
                case "":
                case "simple": return Image.Type.Simple;
                case "sliced":
                    if (image.sprite != null && image.sprite.border == Vector4.zero)
                        report.Error(where, $"imageType 'sliced', но у спрайта '{image.sprite.name}' нулевой border — 9-slice не сработает, задай border в импортёре спрайта");
                    return Image.Type.Sliced;
                case "tiled": return Image.Type.Tiled;
                case "filled": return Image.Type.Filled;
                default:
                    report.Error(where, $"imageType '{raw}' — допустимо simple | sliced | tiled | filled");
                    return Image.Type.Simple;
            }
        }

        static void BuildText(UiNode node, UiBuildContext ctx, GameObject go)
        {
            var text = Ensure<TextMeshProUGUI>(go);
            text.text = string.IsNullOrEmpty(node.Text) ? $"«{node.Id}»" : node.Text;
            text.richText = true;
            text.raycastTarget = false;                       // текст ловит тапы только если попросят явно

            UiTextStyle style = ctx.Tokens.Style(node.Style, ctx.Report, node.Where);
            UiTextStyleApplier.Apply(text, style, ctx.Tokens, ctx.Report, node.Where);

            string colorRef = node.Color ?? style?.Color;
            if (ctx.Tokens.TryColor(colorRef, ctx.Report, node.Where, out Color c)) text.color = c;

            text.alignment = ParseAlign(node.Align, ctx.Report, node.Where);
            text.textWrappingMode = node.Wrap ? TextWrappingModes.Normal : TextWrappingModes.NoWrap;

            if (node.MaxLines > 0)
            {
                if (!node.Wrap)
                    ctx.Report.Error(node.Where, "'maxLines' работает только вместе с 'wrap': true — без переноса строка всего одна");
                text.maxVisibleLines = node.MaxLines;
                text.overflowMode = TextOverflowModes.Ellipsis;
            }

            bool sized = node.Size.HasValue || node.Width.HasValue || node.Height.HasValue
                         || UiAnchor.KindOf(node.Anchor) != UiAnchorKind.Point && !string.IsNullOrEmpty(node.Anchor);
            if (!sized && node.Fit == null)
            {
                var fitter = Ensure<ContentSizeFitter>(go);
                fitter.horizontalFit = node.Wrap ? ContentSizeFitter.FitMode.Unconstrained : ContentSizeFitter.FitMode.PreferredSize;
                fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            }
        }

        static TextAlignmentOptions ParseAlign(string align, UiBuildReport report, string where)
        {
            switch (align)
            {
                case null:
                case "":
                case "center": return TextAlignmentOptions.Center;
                case "left": return TextAlignmentOptions.Left;
                case "right": return TextAlignmentOptions.Right;
                case "top": return TextAlignmentOptions.Top;
                case "bottom": return TextAlignmentOptions.Bottom;
                case "top-left": return TextAlignmentOptions.TopLeft;
                case "top-right": return TextAlignmentOptions.TopRight;
                case "bottom-left": return TextAlignmentOptions.BottomLeft;
                case "bottom-right": return TextAlignmentOptions.BottomRight;
                default:
                    report.Error(where, $"align '{align}' — допустимо left | center | right | top | bottom | top-left | top-right | bottom-left | bottom-right");
                    return TextAlignmentOptions.Center;
            }
        }

        static void ApplyLayoutGroup(UiNode node, UiBuildContext ctx, GameObject go)
        {
            if (node.Layout == null) return;
            UiLayoutSpec l = node.Layout;

            LayoutGroup group;
            switch (l.Dir)
            {
                case "vertical": group = Ensure<VerticalLayoutGroup>(go); break;
                case "horizontal": group = Ensure<HorizontalLayoutGroup>(go); break;
                case "grid": group = Ensure<GridLayoutGroup>(go); break;
                default:
                    ctx.Report.Error(node.Where, $"layout.dir '{l.Dir}' — допустимо vertical | horizontal | grid");
                    return;
            }

            group.padding = l.Padding;
            group.childAlignment = ParseAnchorAlign(l.Align, ctx.Report, node.Where);

            if (group is HorizontalOrVerticalLayoutGroup hv)
            {
                hv.spacing = l.Spacing;
                hv.childControlWidth = l.ControlSize;
                hv.childControlHeight = l.ControlSize;
                // `forceExpand` из спеки управляет ГЛАВНОЙ осью (раздавать ли свободное место).
                // Поперечная ось заполняется ВСЕГДА: строка в вертикальной группе обязана быть шириной
                // группы. Без этого ребёнок получает свой preferred (у TMP это дефолтные 200 px), и
                // вёрстка выглядит случайно сжатой — ловилось на первом же рендере пробы.
                bool vertical = l.Dir == "vertical";
                hv.childForceExpandWidth = vertical || l.ForceExpand;
                hv.childForceExpandHeight = !vertical || l.ForceExpand;
                hv.childScaleWidth = false;
                hv.childScaleHeight = false;
            }
            else if (group is GridLayoutGroup grid)
            {
                grid.cellSize = new Vector2(l.Cell.x, l.Cell.y);
                grid.spacing = new Vector2(l.Spacing, l.Spacing);
                if (l.ConstraintCount > 0)
                {
                    grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
                    grid.constraintCount = l.ConstraintCount;
                }
            }
        }

        static TextAnchor ParseAnchorAlign(string align, UiBuildReport report, string where)
        {
            switch (align)
            {
                case "upper-left": return TextAnchor.UpperLeft;
                case "upper-center": return TextAnchor.UpperCenter;
                case "upper-right": return TextAnchor.UpperRight;
                case "middle-left": return TextAnchor.MiddleLeft;
                case "middle-center": return TextAnchor.MiddleCenter;
                case "middle-right": return TextAnchor.MiddleRight;
                case "lower-left": return TextAnchor.LowerLeft;
                case "lower-center": return TextAnchor.LowerCenter;
                case "lower-right": return TextAnchor.LowerRight;
                default:
                    report.Error(where, $"layout.align '{align}' — допустимо upper/middle/lower-left/center/right");
                    return TextAnchor.UpperCenter;
            }
        }

        static void ApplyFit(UiNode node, GameObject go)
        {
            if (node.Fit == null) return;
            var fitter = Ensure<ContentSizeFitter>(go);
            fitter.horizontalFit = node.Fit == "both" || node.Fit == "w" ? ContentSizeFitter.FitMode.PreferredSize : ContentSizeFitter.FitMode.Unconstrained;
            fitter.verticalFit = node.Fit == "both" || node.Fit == "h" ? ContentSizeFitter.FitMode.PreferredSize : ContentSizeFitter.FitMode.Unconstrained;
        }

        static void ApplyLayoutElement(UiNode node, GameObject go)
        {
            bool needsElement = node.IgnoreLayout || node.FlexibleWidth >= 0f || node.FlexibleHeight >= 0f;
            if (!needsElement) return;
            var element = Ensure<LayoutElement>(go);
            element.ignoreLayout = node.IgnoreLayout;
            if (node.FlexibleWidth >= 0f) element.flexibleWidth = node.FlexibleWidth;
            if (node.FlexibleHeight >= 0f) element.flexibleHeight = node.FlexibleHeight;
        }

        static void DisableRaycast(GameObject go)
        {
            var graphic = go.GetComponent<Graphic>();
            if (graphic != null) graphic.raycastTarget = false;
        }

        static void OverrideOrCreate(UiNode node, UiBuildContext ctx, Transform parent)
        {
            Transform existing = parent.Find(node.Id);
            if (existing == null)
            {
                BuildNode(node, ctx, parent);
                return;
            }

            GameObject go = existing.gameObject;
            var rect = go.GetComponent<RectTransform>();
            if (rect != null && !string.IsNullOrEmpty(node.Anchor)) UiAnchor.Apply(rect, node, ctx.Report);
            else if (rect != null && node.Size.HasValue) rect.sizeDelta = new Vector2(node.Size.Value.x, node.Size.Value.y);

            if (node.Type == "text" || go.GetComponent<TMP_Text>() != null) OverrideText(node, ctx, go);
            else if (!string.IsNullOrEmpty(node.Sprite) || !string.IsNullOrEmpty(node.Color))
                ApplyFill(ctx, go, new UiFill { Sprite = node.Sprite, Color = node.Color, ImageType = node.ImageType, PreserveAspect = node.PreserveAspect }, node.Where);

            ApplyBehaviours(ctx, go, node.Behaviour, node.Where);
            if (node.Children != null)
                foreach (UiNode child in node.Children) OverrideOrCreate(child, ctx, go.transform);
        }

        static void OverrideText(UiNode node, UiBuildContext ctx, GameObject go)
        {
            var text = go.GetComponent<TMP_Text>();
            if (text == null) { BuildText(node, ctx, go); return; }
            if (!string.IsNullOrEmpty(node.Text)) text.text = node.Text;
            UiTextStyle style = ctx.Tokens.Style(node.Style, ctx.Report, node.Where);
            if (style != null) text.fontSize = style.FontSize;
            if (ctx.Tokens.TryColor(node.Color ?? style?.Color, ctx.Report, node.Where, out Color c)) text.color = c;
            if (!string.IsNullOrEmpty(node.Align)) text.alignment = ParseAlign(node.Align, ctx.Report, node.Where);
        }

        // ───────────────────────────── behaviour ─────────────────────────────

        static void ApplyBehaviours(UiBuildContext ctx, GameObject go, List<UiBehaviourSpec> list, string where)
        {
            if (list == null) return;
            foreach (UiBehaviourSpec b in list)
            {
                IUiBehaviourHandler handler = UiBehaviourRegistry.Find(b.Type);
                if (handler == null)
                {
                    ctx.Report.Error(b.Where ?? where, $"неизвестный behaviour '{b.Type}'. Зарегистрированные: {UiBehaviourRegistry.KnownList()}. Новый тип добавляется хэндлером в проекте — см. PORTING.md");
                    continue;
                }
                try { handler.Apply(ctx, go, b); }
                catch (Exception e)
                {
                    ctx.Report.Error(b.Where ?? where, $"хэндлер '{b.Type}' упал: {e.GetType().Name}: {e.Message}");
                }
            }
        }

        /// <summary>
        /// Снимает с корня managed-компоненты, которых больше нет в спеке. Иначе убранный из спеки
        /// блок остаётся на префабе навсегда — вёрстка «работает не как в файле», и это невидимо в диффе.
        /// </summary>
        static void RemoveStaleManaged(UiBuildContext ctx, GameObject go, List<UiBehaviourSpec> current, bool keepImage)
        {
            var alive = new HashSet<Type>();
            if (current != null)
            {
                foreach (UiBehaviourSpec b in current)
                {
                    IUiBehaviourHandler h = UiBehaviourRegistry.Find(b.Type);
                    if (h?.ManagedTypes == null) continue;
                    foreach (Type t in h.ManagedTypes) alive.Add(t);
                }
            }

            foreach (Type managed in UiBehaviourRegistry.ManagedTypes)
            {
                if (alive.Contains(managed)) continue;
                var component = go.GetComponent(managed);
                if (component == null || component is Transform) continue;
                UnityEngine.Object.DestroyImmediate(component);
                ctx.Report.Note($"снял {managed.Name} с корня — блока behaviour для него в спеке больше нет");
            }

            if (!keepImage)
            {
                var image = go.GetComponent<Image>();
                if (image != null)
                {
                    UnityEngine.Object.DestroyImmediate(image);
                    ctx.Report.Note("снял Image с корня — в спеке нет блока 'background'");
                }
            }
        }

        static void ApplyHiddenFlags(UiSpec spec, GameObject root)
        {
            if (spec.Children == null) return;
            foreach (UiNode node in spec.Children) ApplyHiddenRecursive(node, root.transform);
        }

        static void ApplyHiddenRecursive(UiNode node, Transform parent)
        {
            Transform t = parent.Find(node.Id);
            if (t == null) return;
            if (node.Children != null)
                foreach (UiNode child in node.Children) ApplyHiddenRecursive(child, t);
            if (node.Hidden) t.gameObject.SetActive(false);
        }

        // ───────────────────────────── bind ─────────────────────────────

        /// <summary>
        /// Прошивает объекты в поля контроллера. Агент не может «перетащить ссылку в инспекторе»,
        /// а без ссылок попап мёртв — поэтому связывание это часть сборки, а не ручной шаг.
        /// </summary>
        static void BindReferences(UiSpec spec, UiBuildContext ctx, GameObject root)
        {
            var targets = new List<KeyValuePair<string, GameObject>>();
            CollectBinds(spec.Children, root.transform, targets, ctx.Report);
            if (targets.Count == 0) return;

            if (ctx.Controller == null)
            {
                foreach (KeyValuePair<string, GameObject> pair in targets)
                    ctx.Report.Error(spec.SourcePath, $"'bind: {pair.Key}' есть, а 'controller' в спеке не объявлен — прошивать некуда");
                return;
            }

            Type type = ctx.Controller.GetType();
            foreach (KeyValuePair<string, GameObject> pair in targets)
            {
                FieldInfo field = FindField(type, pair.Key);
                if (field == null)
                {
                    ctx.Report.Error(spec.SourcePath, $"bind '{pair.Key}': у {type.Name} нет такого поля");
                    continue;
                }
                if (!field.IsPublic && field.GetCustomAttribute<SerializeField>() == null)
                {
                    ctx.Report.Error(spec.SourcePath, $"bind '{pair.Key}': поле приватное без [SerializeField] — Unity его не сериализует, ссылка потерялась бы молча");
                    continue;
                }

                object value = ResolveBindValue(field, pair.Value, ctx.Report, spec.SourcePath, pair.Key);
                if (value == null) continue;
                field.SetValue(ctx.Controller, value);
            }
            EditorUtility.SetDirty(ctx.Controller);
        }

        static object ResolveBindValue(FieldInfo field, GameObject go, UiBuildReport report, string where, string bindName)
        {
            Type ft = field.FieldType;
            if (ft == typeof(GameObject)) return go;
            if (typeof(Component).IsAssignableFrom(ft))
            {
                Component component = go.GetComponent(ft);
                if (component == null)
                    report.Error(where, $"bind '{bindName}': поле ждёт {ft.Name}, а на объекте '{go.name}' такого компонента нет");
                return component;
            }
            report.Error(where, $"bind '{bindName}': поле типа {ft.Name} — прошить можно только GameObject или компонент");
            return null;
        }

        static FieldInfo FindField(Type type, string name)
        {
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            for (Type t = type; t != null && t != typeof(MonoBehaviour); t = t.BaseType)
            {
                FieldInfo f = t.GetField(name, flags);
                if (f != null) return f;
            }
            return null;
        }

        static void CollectBinds(List<UiNode> nodes, Transform parent, List<KeyValuePair<string, GameObject>> into, UiBuildReport report)
        {
            if (nodes == null) return;
            foreach (UiNode node in nodes)
            {
                Transform t = parent.Find(node.Id);
                if (t == null) continue;
                if (!string.IsNullOrEmpty(node.Bind)) into.Add(new KeyValuePair<string, GameObject>(node.Bind, t.gameObject));
                CollectBinds(node.Children, t, into, report);
            }
        }

        static T Ensure<T>(GameObject go) where T : Component
        {
            T existing = go.GetComponent<T>();
            return existing != null ? existing : go.AddComponent<T>();
        }
    }
}
