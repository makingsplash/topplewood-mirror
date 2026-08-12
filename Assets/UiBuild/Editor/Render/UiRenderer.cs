using System;
using System.Collections.Generic;
using System.IO;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace Iliai.UiBuild
{
    /// <summary>
    /// Один рендерер, два режима — и это не экономия кода, а требование пайплайна:
    ///
    /// • <c>flat = true</c>  → серый wireframe: обведённые боксы с подписями id. Это КОНДИШН для
    ///   генератора картинок: раскладка приходит из спеки, а не из фантазии модели.
    /// • <c>flat = false</c> → настоящий PNG собранного префаба. Это ОБРАТНАЯ СВЯЗЬ для агента:
    ///   он смотрит на свой результат (Read видит картинку) и правит спеку.
    ///
    /// Оба режима идут через одну и ту же сцену-подмостки, поэтому wireframe гарантированно совпадает
    /// с геометрией того, что будет в игре.
    ///
    /// Подмостки живут в World Space далеко от сцены и на UI-слое: открытая сцена не участвует в кадре
    /// и НЕ сохраняется (объекты — HideAndDontSave, живут внутри одного вызова).
    /// </summary>
    public static class UiRenderer
    {
        const int UiLayer = 5;                                  // встроенный слой "UI"
        static readonly Vector3 FarAway = new Vector3(100000f, 100000f, 0f);

        /// <summary>
        /// Инстанс префаба под настоящим Canvas с посчитанной раскладкой.
        ///
        /// Зачем: layout-группы UGUI считаются только под Canvas. На этапе записи префаба Canvas'а нет,
        /// поэтому размеры детей внутри групп там остаются дефолтными (у TMP это 200×50) — мерить по
        /// ним что-либо бессмысленно, а попытка дала ложное предупреждение «текст шире бокса» на узле,
        /// который в игре занимает всю ширину. Любая проверка геометрии обязана идти здесь.
        /// </summary>
        internal sealed class Stage : IDisposable
        {
            public GameObject CanvasGo;
            public GameObject Instance;
            public int Width;
            public int Height;

            public void Dispose()
            {
                if (CanvasGo != null) UnityEngine.Object.DestroyImmediate(CanvasGo);
                CanvasGo = null;
                Instance = null;
            }
        }

        internal static Stage CreateStage(string prefabPath, UiBuildConfig cfg, UiBuildReport report, bool flat)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            if (prefab == null)
            {
                report.Error(prefabPath, "префаб не найден — рендерить/проверять нечего");
                return null;
            }

            var stage = new Stage { Width = cfg.ReferenceResolution.x, Height = cfg.ReferenceResolution.y };

            stage.CanvasGo = new GameObject("__uibuild_canvas", typeof(Canvas))
            {
                hideFlags = HideFlags.HideAndDontSave,
                layer = UiLayer,
            };
            var canvas = stage.CanvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            var canvasRect = stage.CanvasGo.GetComponent<RectTransform>();
            canvasRect.position = FarAway;
            canvasRect.sizeDelta = new Vector2(stage.Width, stage.Height);
            canvasRect.localScale = Vector3.one;

            stage.Instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, stage.CanvasGo.transform);
            stage.Instance.hideFlags = HideFlags.HideAndDontSave;
            SetLayerRecursive(stage.Instance, UiLayer);

            var instanceRect = stage.Instance.GetComponent<RectTransform>();
            if (instanceRect == null)
            {
                report.Error(prefabPath, "у корня префаба нет RectTransform — это не UI");
                stage.Dispose();
                return null;
            }
            // Экран растягиваем на «экран», компонент показываем как есть по центру.
            if (instanceRect.sizeDelta.x >= stage.Width - 1 && instanceRect.sizeDelta.y >= stage.Height - 1)
            {
                instanceRect.anchorMin = Vector2.zero;
                instanceRect.anchorMax = Vector2.one;
                instanceRect.offsetMin = Vector2.zero;
                instanceRect.offsetMax = Vector2.zero;
            }
            instanceRect.anchoredPosition = Vector2.zero;

            if (flat) Flatten(stage.Instance);
            LayoutRebuilder.ForceRebuildLayoutImmediate(instanceRect);
            Canvas.ForceUpdateCanvases();
            return stage;
        }

        public static bool Render(string prefabPath, string outputAbsPath, bool flat, UiBuildConfig cfg, UiBuildReport report)
        {
            Stage stage = null;
            GameObject cameraGo = null;
            RenderTexture rt = null;
            Texture2D readback = null;
            RenderTexture previouslyActive = RenderTexture.active;

            try
            {
                stage = CreateStage(prefabPath, cfg, report, flat);
                if (stage == null) return false;

                cameraGo = new GameObject("__uibuild_camera", typeof(Camera)) { hideFlags = HideFlags.HideAndDontSave };
                var cam = cameraGo.GetComponent<Camera>();
                cam.orthographic = true;
                cam.orthographicSize = stage.Height * 0.5f;
                cam.cullingMask = 1 << UiLayer;                  // сцена в кадр не попадает
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = flat ? new Color(0.97f, 0.97f, 0.97f, 1f) : new Color(0.11f, 0.11f, 0.13f, 1f);
                cam.nearClipPlane = 0.01f;
                cam.farClipPlane = 1000f;
                cam.enabled = false;                             // рисуем только по явному Render()
                // Камера перед канвасом смотрит вдоль +Z — так UI не зеркалится.
                cameraGo.transform.position = FarAway + new Vector3(0f, 0f, -100f);
                cameraGo.transform.rotation = Quaternion.identity;

                rt = new RenderTexture(stage.Width, stage.Height, 24, RenderTextureFormat.ARGB32) { antiAliasing = 2 };
                cam.targetTexture = rt;
                cam.Render();

                RenderTexture.active = rt;
                readback = new Texture2D(stage.Width, stage.Height, TextureFormat.RGBA32, false);
                readback.ReadPixels(new Rect(0, 0, stage.Width, stage.Height), 0, 0);
                readback.Apply();

                Directory.CreateDirectory(Path.GetDirectoryName(outputAbsPath));
                File.WriteAllBytes(outputAbsPath, readback.EncodeToPNG());
                report.Note($"{(flat ? "wireframe" : "скриншот")}: {outputAbsPath} ({stage.Width}×{stage.Height})");
                return true;
            }
            catch (Exception e)
            {
                report.Error(prefabPath, $"рендер упал: {e.GetType().Name}: {e.Message}");
                return false;
            }
            finally
            {
                RenderTexture.active = previouslyActive;
                if (readback != null) UnityEngine.Object.DestroyImmediate(readback);
                if (cameraGo != null)
                {
                    var cam = cameraGo.GetComponent<Camera>();
                    if (cam != null) cam.targetTexture = null;
                    UnityEngine.Object.DestroyImmediate(cameraGo);
                }
                stage?.Dispose();
                if (rt != null)
                {
                    rt.Release();
                    UnityEngine.Object.DestroyImmediate(rt);
                }
            }
        }

        static void SetLayerRecursive(GameObject go, int layer)
        {
            go.layer = layer;
            foreach (Transform child in go.transform) SetLayerRecursive(child.gameObject, layer);
        }

        // ───────────────────────── flat / wireframe ─────────────────────────

        /// <summary>
        /// Убирает арт и оставляет структуру: боксы с обводкой + подписи id. Именно это подаётся
        /// генератору как раскладка — без спрайтов он не «додумывает» композицию, а следует ей.
        /// </summary>
        static void Flatten(GameObject instance)
        {
            var boxes = new List<RectTransform>();
            foreach (Graphic graphic in instance.GetComponentsInChildren<Graphic>(true))
            {
                if (graphic is TMP_Text text)
                {
                    text.color = new Color(0.25f, 0.25f, 0.28f, 1f);
                    continue;
                }
                if (graphic is Image image)
                {
                    image.sprite = null;
                    image.type = Image.Type.Simple;
                    image.color = Color.white;
                    boxes.Add(image.rectTransform);
                }
            }

            foreach (RectTransform box in boxes)
            {
                AddOutline(box);
                AddLabel(box);
            }
        }

        static void AddOutline(RectTransform box)
        {
            const float t = 4f;
            var color = new Color(0.35f, 0.35f, 0.4f, 1f);
            AddStrip(box, "__edge_top", new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, -t), Vector2.zero, color);
            AddStrip(box, "__edge_bottom", new Vector2(0f, 0f), new Vector2(1f, 0f), Vector2.zero, new Vector2(0f, t), color);
            AddStrip(box, "__edge_left", new Vector2(0f, 0f), new Vector2(0f, 1f), Vector2.zero, new Vector2(t, 0f), color);
            AddStrip(box, "__edge_right", new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(-t, 0f), Vector2.zero, color);
        }

        static void AddStrip(RectTransform parent, string name, Vector2 anchorMin, Vector2 anchorMax, Vector2 offsetMin, Vector2 offsetMax, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image)) { hideFlags = HideFlags.HideAndDontSave, layer = parent.gameObject.layer };
            var rect = go.GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.offsetMin = offsetMin;
            rect.offsetMax = offsetMax;
            var image = go.GetComponent<Image>();
            image.color = color;
            image.raycastTarget = false;
            go.AddComponent<LayoutElement>().ignoreLayout = true;   // обводка не участвует в раскладке родителя
        }

        static void AddLabel(RectTransform box)
        {
            if (box.rect.width < 64f || box.rect.height < 40f) return;      // на мелких иконках подпись только мешает
            var go = new GameObject("__label", typeof(RectTransform)) { hideFlags = HideFlags.HideAndDontSave, layer = box.gameObject.layer };
            var rect = go.GetComponent<RectTransform>();
            rect.SetParent(box, false);
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = new Vector2(6f, 6f);
            rect.offsetMax = new Vector2(-6f, -6f);

            var label = go.AddComponent<TextMeshProUGUI>();
            label.text = box.gameObject.name;
            label.fontSize = 28f;
            label.color = new Color(0.45f, 0.45f, 0.5f, 1f);
            label.alignment = TextAlignmentOptions.TopLeft;
            label.textWrappingMode = TextWrappingModes.NoWrap;
            label.raycastTarget = false;
            go.AddComponent<LayoutElement>().ignoreLayout = true;
        }
    }
}
