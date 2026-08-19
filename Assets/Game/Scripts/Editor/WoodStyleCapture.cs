using System.IO;
using Gameplay.Core;
// Именно using, а не Gameplay.Dev.WoodSandbox по месту: этот файл лежит в namespace Game.Editor,
// и короткое имя компилятор ищет сначала внутри Game — то есть как несуществующий Game.Gameplay.Dev.
using Gameplay.Dev;
using Gameplay.View;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Game.Editor
{
    /// <summary>
    /// Рендерит деревянный стиль в PNG — проверка арта и шейдеров без входа в Play.
    /// Два кадра, оба через ТОТ ЖЕ код, что идёт в игру (PieceMeshBuilder, BlockMaterial,
    /// PieceShadow, StaticGeometryView, WoodArt), иначе стенд проверял бы сам себя:
    ///  * <c>Artifacts/wood-style.png</c> — верстка экрана: фон-стена, рамка превью, кнопки,
    ///    фигура на полке с тенями;
    ///  * <c>Artifacts/wood-pieces.png</c> — сетка деталей в раскладке и цветах референса
    ///    владельца (<c>Artifacts/ref/pieces-reference.png</c>), чтобы сравнивать один в один.
    /// <para>
    /// Батчем: Unity.exe -batchmode -projectPath . -executeMethod Game.Editor.WoodStyleCapture.CaptureBatch -quit
    /// Без -nographics: нужен графический контекст, иначе рендерить нечем.
    /// </para>
    /// </summary>
    public static class WoodStyleCapture
    {
        private const int Width = 853;
        private const int Height = 1517;   // пропорция телефона, как у фона (1059×1920)
        private const string ScreenPath = "Artifacts/wood-style.png";
        private const string PiecesPath = "Artifacts/wood-pieces.png";
        private const string SandboxPath = "Artifacts/wood-sandbox.png";

        // Цвета сняты пипеткой с референса владельца — сравнение честное только на них.
        private static readonly Color Blue = new Color32(72, 105, 154, 255);
        private static readonly Color Green = new Color32(92, 142, 62, 255);
        private static readonly Color Yellow = new Color32(203, 160, 22, 255);
        private static readonly Color Orange = new Color32(199, 104, 29, 255);
        private static readonly Color Red = new Color32(186, 60, 50, 255);

        [MenuItem("Tools/Wood Style/Снять стенд")]
        public static void CaptureBatch()
        {
            // Спрайты приходят от владельца пачкой; настройки импорта (9-slice, PPU) выставляем
            // сами перед съёмкой, иначе стенд рисует полку и рамку с чужими границами.
            WoodArtImport.Apply();
            WoodArt.ResetCache();

            if (!ShadersPresent())
                return;

            CaptureScreen();
            CapturePieces();
            CaptureSandbox();

            if (Application.isBatchMode) EditorApplication.Exit(0);
        }

        /// <summary>
        /// Кадр ПЕСОЧНИЦЫ (<see cref="WoodSandbox"/>) — той самой сцены, которую владелец
        /// откроет по «Tools → Wood Style → Песочница ▶». Снимается тем же кодом, что собирает её в
        /// Play (<c>WoodSandbox.Build</c>), поэтому кадр и живая сцена не могут разойтись.
        /// </summary>
        [MenuItem("Tools/Wood Style/Снять кадр песочницы")]
        public static void CaptureSandbox()
        {
            WoodArtImport.Apply();
            WoodArt.ResetCache();

            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var sandbox = new GameObject("WoodSandbox").AddComponent<WoodSandbox>();
            var cam = sandbox.Build();

            // Кадр стенда фиксированный (853×1517), а камера в batchmode берёт aspect экрана —
            // без явной подстановки песочница вписалась бы не в тот кадр, что снимается.
            cam.aspect = Width / (float)Height;
            sandbox.FitCamera();

            Render(cam, SandboxPath);
        }

        /// <summary>
        /// Только кадр экрана. Нужна, когда правится что-то одно (тень под рамкой, кнопки) — гонять
        /// ради этого всю тройку кадров дорого: каждый прогон батча поднимает редактор с нуля.
        /// </summary>
        [MenuItem("Tools/Wood Style/Снять только экран")]
        public static void CaptureScreenOnly()
        {
            WoodArtImport.Apply();
            WoodArt.ResetCache();
            if (!ShadersPresent())
                return;

            CaptureScreen();

            if (Application.isBatchMode) EditorApplication.Exit(0);
        }

        /// <summary>
        /// Печатает, во что палитра превращает КАЖДЫЙ цвет уровней. Нужна из-за цены тумблера
        /// <c>WoodTuning.PaletteToGame</c>: цветов в уровнях 16, в палитре 6, и два разных типа
        /// деталей могут схлопнуться в один цвет. Увидеть это заранее дешевле, чем ловить в игре.
        /// Цвета вытаскиваются регуляркой, а не разбором модели уровня: нужен только список строк
        /// «color», и тащить сюда весь LevelData ради него незачем.
        /// </summary>
        [MenuItem("Tools/Wood Style/Показать раскладку палитры")]
        public static void LogPaletteRemap()
        {
            const string levelsPath = "Assets/StreamingAssets/shapefill_levels.json";
            if (!File.Exists(levelsPath))
            {
                Debug.LogWarning($"[wood] уровни не найдены: {levelsPath}");
                return;
            }

            var text = File.ReadAllText(levelsPath);
            var matches = System.Text.RegularExpressions.Regex.Matches(text, "\"color\"\\s*:\\s*\"(#[0-9A-Fa-f]{6})\"");

            var tuning = WoodTuning.Current;
            var seen = new System.Collections.Generic.HashSet<string>();
            var buckets = new System.Collections.Generic.Dictionary<string, System.Collections.Generic.List<string>>();

            foreach (System.Text.RegularExpressions.Match match in matches)
            {
                var hex = match.Groups[1].Value.ToUpperInvariant();
                if (!seen.Add(hex) || !ColorUtility.TryParseHtmlString(hex, out var color))
                    continue;
                var mapped = "#" + ColorUtility.ToHtmlStringRGB(tuning.ResolveLevelColor(color));
                if (!buckets.TryGetValue(mapped, out var list))
                    buckets[mapped] = list = new System.Collections.Generic.List<string>();
                list.Add(hex);
            }

            Debug.Log($"[wood] раскладка палитры (тумблер {(tuning.PaletteToGame ? "ВКЛ" : "выкл")}): " +
                      $"{seen.Count} цветов уровней → {buckets.Count} цветов палитры");
            foreach (var bucket in buckets)
                Debug.Log($"[wood]   {bucket.Key} ← {string.Join(", ", bucket.Value)}" +
                          (bucket.Value.Count > 1 ? "   ⚠ схлопнулись в один" : string.Empty));
        }

        private static bool ShadersPresent()
        {
            var block = Shader.Find("Game/Wood Block");
            var shadow = Shader.Find("Game/Wood Piece Shadow");
            Debug.Log($"[wood] шейдеры: block={(block != null)} shadow={(shadow != null)}; " +
                      $"спрайты: bg={(WoodArt.Background != null)} shelf={(WoodArt.Shelf != null)} " +
                      $"frame={(WoodArt.Frame != null)} dev={(WoodArt.ButtonDev != null)} " +
                      $"restart={(WoodArt.ButtonRestart != null)}");
            if (block != null && shadow != null)
                return true;
            Debug.LogError("[wood] шейдер не найден — стенд не снят");
            EditorApplication.Exit(2);
            return false;
        }

        // ---------------------------------------------------------------- экран

        private static void CaptureScreen()
        {
            var cam = NewScene(new Color(0.35f, 0.16f, 0.06f));

            Backdrop(cam);

            // Фигура «жираф» из кубиков — как на прежнем стенде, чтобы сравнивать с вчерашним кадром.
            Block(-1.5f, -5.0f, Red); Block(0.5f, -5.0f, Red);
            Block(-1.5f, -4.0f, Red); Block(0.5f, -4.0f, Red);
            Block(-1.5f, -3.0f, Red); Block(0.5f, -3.0f, Red);
            Block(-1.5f, -2.0f, Orange); Block(-0.5f, -2.0f, Orange); Block(0.5f, -2.0f, Orange);
            Block(-1.5f, -1.0f, Orange); Block(-0.5f, -1.0f, Orange); Block(0.5f, -1.0f, Orange);
            Block(0.5f, 0.0f, Yellow); Block(0.5f, 1.0f, Yellow);
            Block(0.5f, 2.0f, Yellow); Block(0.5f, 3.0f, Yellow);
            Block(0.5f, 4.0f, Green); Block(1.5f, 4.0f, Green);
            Triangle(0.5f, 5.0f, Green); Triangle(1.5f, 5.0f, Blue);

            // Деталь в воздухе и повёрнутая: проверяем, что тень не уезжает при вращении,
            // а волокно идёт вдоль длинной стороны, а не по экрану.
            Bar(-3.4f, 1.6f, 2.0f, 1.0f, 0f, Yellow);
            Bar(3.0f, -0.4f, 2.0f, 1.0f, 34f, Orange);

            // Полка — через настоящий StaticGeometryView (тот же путь, что в игре).
            Shelf(-0.5f, -6.0f, 6.4f, 0.9f);

            // Рамка превью с мини-фигурой внутри — как её строит TargetPreviewView.
            PreviewCard(new Vector2(0f, 7.4f), 3.0f);

            // Кнопки: высота 116 UI-px при референсе 1920 ⇒ ≈1.2 мировых единицы на этой камере.
            UiSprite(WoodArt.ButtonDev, new Vector2(-3.9f, 9.0f), 1.2f, "DEV");
            UiSprite(WoodArt.ButtonRestart, new Vector2(3.6f, 9.0f), 1.2f, "RESTART");

            Render(cam, ScreenPath);
        }

        // ------------------------------------------------------- сетка деталей

        /// <summary>Раскладка референса: два треугольника и квадрат, лежачий брусок и квадрат, три стоячих.</summary>
        private static void CapturePieces()
        {
            var cam = NewScene(new Color(0.10f, 0.10f, 0.11f));
            cam.orthographicSize = 6.4f;

            // Кадр 853×1517 при orthographicSize 6.4 — это ±3.6 по горизонтали. На первом прогоне
            // правый столбец уехал за край: держим все детали внутри ±3.4.
            Triangle(-3.3f, 3.0f, Blue, 1.5f);
            Bar(-0.75f, 3.0f, 1.5f, 1.5f, 0f, Green);
            Triangle(1.8f, 3.0f, Blue, 1.5f);

            Bar(-3.3f, 0.8f, 3.6f, 1.5f, 0f, Yellow);
            Bar(0.9f, 0.8f, 1.7f, 1.5f, 0f, Orange);

            Bar(-3.3f, -2.6f, 1.5f, 3.2f, 0f, Orange);
            Bar(-0.75f, -2.6f, 1.5f, 3.2f, 0f, Red);
            Bar(1.8f, -2.6f, 1.5f, 3.2f, 0f, Red);

            Render(cam, PiecesPath);
        }

        // ------------------------------------------------------------ примитивы

        private static Camera NewScene(Color clear)
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var camGo = new GameObject("CaptureCamera");
            var cam = camGo.AddComponent<Camera>();
            cam.orthographic = true;
            cam.orthographicSize = 10f;
            cam.transform.position = new Vector3(0f, 0f, -10f);
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = clear;
            cam.nearClipPlane = 0.01f;
            cam.farClipPlane = 100f;
            return cam;
        }

        private static void Backdrop(Camera cam)
        {
            var sprite = WoodArt.Background;
            if (sprite == null)
                return;

            var go = new GameObject("[Wood] Backdrop");
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = sprite;
            sr.sortingOrder = -10000;
            // Bind сразу подгоняет: в редакторе LateUpdate не идёт, а рендер случится сейчас.
            go.AddComponent<WoodBackdropFit>().Bind(cam, sr);
        }

        private static void Shelf(float centerX, float centerY, float width, float height)
        {
            var half = new Vector2(width * 0.5f, height * 0.5f);
            var outline = new[]
            {
                new Vec2(centerX - half.x, centerY - half.y),
                new Vec2(centerX + half.x, centerY - half.y),
                new Vec2(centerX + half.x, centerY + half.y),
                new Vec2(centerX - half.x, centerY + half.y),
            };
            var go = new GameObject("Shelf");
            go.AddComponent<StaticGeometryView>().Initialize(outline, null);
        }

        /// <summary>Карточка превью: рамка (9-slice) + мини-фигура внутри, как в TargetPreviewView.</summary>
        private static void PreviewCard(Vector2 center, float side)
        {
            var frame = WoodArt.Frame;
            if (frame != null)
            {
                var go = new GameObject("PreviewFrame");
                go.transform.position = new Vector3(center.x, center.y, 0.1f);
                var sr = go.AddComponent<SpriteRenderer>();
                sr.sprite = frame;
                sr.drawMode = SpriteDrawMode.Sliced;
                sr.size = Vector2.one * side * (1f + 2f * WoodArt.FramePadFraction);
                // Тем же вызовом, что в игре (TargetPreviewView) — иначе стенд показывал бы рамку
                // без тени и «проверял» не то, что видит игрок.
                FrameShadow.Attach(sr);
            }

            // Мини-фигура: домик из квадрата и треугольника, в масштабе карточки.
            var scale = side * 0.28f;
            Bar(center.x - scale * 0.5f, center.y - scale * 0.9f, scale, scale, 0f, Orange, -0.2f);
            Triangle(center.x - scale * 0.5f, center.y + scale * 0.1f, Red, scale, -0.2f);
        }

        private static void UiSprite(Sprite sprite, Vector2 center, float height, string slot)
        {
            if (sprite == null)
                return;
            // Печатаем, ЧТО именно легло в слот: на первом кадре в слоте DEV оказался спрайт RESTART,
            // и без этой строки причину пришлось бы угадывать по картинке.
            Debug.Log($"[wood] слот {slot}: спрайт '{sprite.name}' текстура '{sprite.texture.name}' " +
                      $"{sprite.rect.width}×{sprite.rect.height}");
            var go = new GameObject("Ui_" + slot);
            go.transform.position = new Vector3(center.x, center.y, -1f);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = sprite;
            var spriteHeight = sprite.bounds.size.y;
            var scale = spriteHeight > 1e-4f ? height / spriteHeight : 1f;
            go.transform.localScale = new Vector3(scale, scale, 1f);
        }

        private static void Block(float x, float y, Color color) => Bar(x, y, 0.94f, 0.94f, 0f, color);

        private static void Triangle(float x, float y, Color color, float size = 0.94f, float z = 0f)
        {
            var outline = new[]
            {
                new Vec2(x, y), new Vec2(x + size, y), new Vec2(x + size * 0.5f, y + size * 1.15f)
            };
            Piece(outline, color, $"Tri_{x}_{y}", z);
        }

        private static void Bar(float x, float y, float w, float h, float rotationDeg, Color color, float z = 0f)
        {
            var outline = new[]
            {
                new Vec2(-w * 0.5f, -h * 0.5f), new Vec2(w * 0.5f, -h * 0.5f),
                new Vec2(w * 0.5f, h * 0.5f), new Vec2(-w * 0.5f, h * 0.5f)
            };
            var go = Piece(outline, color, $"Bar_{x}_{y}", z);
            // Контур собран вокруг нуля, поэтому позиция — центр детали (как у настоящих деталей,
            // где меш строится в локальных координатах формы, а трансформ несёт позу из физики).
            go.transform.position = new Vector3(x + w * 0.5f, y + h * 0.5f, z);
            go.transform.rotation = Quaternion.Euler(0f, 0f, rotationDeg);
        }

        private static GameObject Piece(Vec2[] outline, Color color, string name, float z = 0f)
        {
            var go = new GameObject(name);
            go.transform.position = new Vector3(0f, 0f, z);
            var mesh = PieceMeshBuilder.Build(outline);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = BlockMaterial.Shared;

            var mpb = new MaterialPropertyBlock();
            color.a = 1f;
            mpb.SetColor(BlockMaterial.ColorProperty, color);
            mpb.SetColor(BlockMaterial.LegacyColorProperty, color);
            mr.SetPropertyBlock(mpb);

            PieceShadow.Attach(go.transform, mesh);
            return go;
        }

        private static void Render(Camera cam, string path)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path) ?? "Artifacts");
            var rt = new RenderTexture(Width, Height, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
            cam.targetTexture = rt;
            cam.Render();

            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            var tex = new Texture2D(Width, Height, TextureFormat.RGB24, false);
            // Rect уточняем полностью: в домене есть свой Gameplay.Core.Rect, иначе CS0104.
            tex.ReadPixels(new UnityEngine.Rect(0, 0, Width, Height), 0, 0);
            tex.Apply();
            RenderTexture.active = prev;
            cam.targetTexture = null;

            File.WriteAllBytes(path, tex.EncodeToPNG());
            Debug.Log($"[wood] снято: {Path.GetFullPath(path)}");

            Object.DestroyImmediate(tex);
            rt.Release();
            Object.DestroyImmediate(rt);
        }
    }
}
