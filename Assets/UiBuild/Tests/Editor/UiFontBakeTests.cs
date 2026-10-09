using System.IO;
using NUnit.Framework;
using TMPro;
using UnityEditor;
using UnityEngine;

namespace Iliai.UiBuild.Tests
{
    /// <summary>
    /// Рецепт шрифта → статичный SDF-атлас + пресеты (<see cref="UiFontBake"/>). Гоняется ровно тот вызов, что
    /// и команда эндпоинта `font`: рецепт на диске (Temp/), ассеты — во временной папке, снос после прогона.
    ///
    /// Фикстура переносима: исходник — ЛЮБОЙ .ttf проекта (имён этой игры здесь нет). Носители защиты:
    /// отказ без параметра выпечки — Recipe_WithoutPointSize_RefusesAndWritesNothing; перевод px → доли SDF
    /// (делитель 2, подписан кадром TASK-401) — Material_OutlinePx_IsConvertedWithMeasuredFormula; материал
    /// на атласе ЭТОГО шрифта с keyword — Material_SamplesTheBakedAtlas_WithKeyword; калибровка растяжки
    /// лица под базовую линию…прописные — Face_AssignsRampOnFullSdfShader_CalibratedToBaselineAndCap; Line на
    /// компоненте — Face_StyleWithFaceMaterial_SetsLineVerticalMapping; кадр — Face_Render_….
    /// </summary>
    [TestFixture]
    public sealed class UiFontBakeTests
    {
        const string TempDir = "Assets/__UiFontBakeTests";
        const string RecipeRel = "Temp/uifontbake-test.json";
        const string OutAsset = TempDir + "/Probe SDF.asset";
        const string OutMat = TempDir + "/Probe SDF - Outline.mat";

        string _ttf;

        [OneTimeSetUp]
        public void FindSourceFont()
        {
            foreach (string guid in AssetDatabase.FindAssets("t:Font"))
            {
                string p = AssetDatabase.GUIDToAssetPath(guid);
                if (p.EndsWith(".ttf") || p.EndsWith(".otf")) { _ttf = p; break; }
            }
            if (_ttf == null) Assert.Ignore("в проекте нет ни одного .ttf/.otf — выпекать не из чего");
            if (!AssetDatabase.IsValidFolder(TempDir)) AssetDatabase.CreateFolder("Assets", "__UiFontBakeTests");
        }

        [OneTimeTearDown]
        public void Cleanup()
        {
            AssetDatabase.DeleteAsset(TempDir);
            string recipe = Path.Combine(UiBuildConfig.FindProjectRoot(), RecipeRel);
            if (File.Exists(recipe)) File.Delete(recipe);
        }

        static UiBuildReport BakeWith(string json)
        {
            string abs = Path.Combine(UiBuildConfig.FindProjectRoot(), RecipeRel);
            Directory.CreateDirectory(Path.GetDirectoryName(abs));
            File.WriteAllText(abs, json);
            return UiFontBake.Bake(RecipeRel);
        }

        string Recipe(string pointSize) =>
            "{ \"source\": \"" + _ttf + "\", \"out\": \"" + OutAsset + "\", " + pointSize +
            " \"padding\": 16, \"atlas\": { \"w\": 512, \"h\": 512 }, \"chars\": \"AB\", " +
            "\"materials\": [ { \"out\": \"" + OutMat + "\", \"atFontSize\": 108, " +
            "\"outline\": { \"color\": \"#4A2814\", \"px\": 8 } } ] }";

        [Test]
        public void Recipe_WithoutPointSize_RefusesAndWritesNothing()
        {
            AssetDatabase.DeleteAsset(OutAsset);
            UiBuildReport report = BakeWith(Recipe(""));

            Assert.That(report.Ok, Is.False, "без pointSize выпечка обязана отказать, а не подставить кегль");
            Assert.That(report.ToText(), Does.Contain("pointSize"), "отказ обязан назвать недостающий параметр");
            Assert.That(AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(OutAsset), Is.Null, "при отказе атлас не пишется");
        }

        [Test]
        public void Bake_ProducesStaticAtlasWithExactlyTheRequestedCharacters()
        {
            UiBuildReport report = BakeWith(Recipe("\"pointSize\": 72,"));
            Assert.That(report.Ok, Is.True, report.ToText());

            var fa = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(OutAsset);
            Assert.That(fa, Is.Not.Null);
            Assert.That(fa.atlasPopulationMode, Is.EqualTo(AtlasPopulationMode.Static), "атлас обязан быть заморожен: Dynamic дорисовывал бы глифы в рантайме");
            Assert.That(fa.characterTable.Count, Is.EqualTo(2));
            Assert.That(fa.atlasPadding, Is.EqualTo(16));
        }

        [Test]
        public void Material_SamplesTheBakedAtlas_WithKeyword()
        {
            Assert.That(BakeWith(Recipe("\"pointSize\": 72,")).Ok, Is.True);
            var fa = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(OutAsset);
            var mat = AssetDatabase.LoadAssetAtPath<Material>(OutMat);

            Assert.That(mat, Is.Not.Null);
            Assert.That(mat.GetTexture(ShaderUtilities.ID_MainTex), Is.EqualTo(fa.atlasTexture), "пресет на чужом атласе TMP молча подменит материалом шрифта");
            Assert.That(mat.IsKeywordEnabled(ShaderUtilities.Keyword_Outline), Is.True, "без OUTLINE_ON кант не рисуется вовсе");
        }

        [Test]
        public void Material_OutlinePx_IsConvertedWithMeasuredFormula()
        {
            Assert.That(BakeWith(Recipe("\"pointSize\": 72,")).Ok, Is.True);
            var mat = AssetDatabase.LoadAssetAtPath<Material>(OutMat);

            // 8 px при кегле 108, выпечка 72/16: кант целиком снаружи = (dilate + width)·padding·fs/ps.
            float expected = 8f * 72f / (2f * 16f * 108f);
            Assert.That(mat.GetFloat(ShaderUtilities.ID_OutlineWidth), Is.EqualTo(expected).Within(1e-4f));
            Assert.That(mat.GetFloat(ShaderUtilities.ID_FaceDilate), Is.EqualTo(expected).Within(1e-4f), "FaceDilate = OutlineWidth: кромка лица стоит на месте");
        }

        // ───────────────────────────── face (текстура лица) ─────────────────────────────

        const string RampPng = TempDir + "/ramp.png";

        /// <summary>Растяжка сверху вниз: стоп 0 — верхняя строка PNG (= верх глифа), 1 — нижняя.</summary>
        static void WriteRamp(params string[] stops)
        {
            const int h = 64;
            var at = new float[stops.Length];
            var col = new Color[stops.Length];
            for (int i = 0; i < stops.Length; i++)
            {
                string[] p = stops[i].Split(':');
                at[i] = float.Parse(p[0], System.Globalization.CultureInfo.InvariantCulture);
                ColorUtility.TryParseHtmlString(p[1], out col[i]);
            }
            var tex = new Texture2D(4, h, TextureFormat.RGBA32, false);
            for (int y = 0; y < h; y++)
            {
                float depth = 1f - y / (h - 1f);                 // строка 0 текстуры — НИЗ картинки
                int k = 0;
                while (k < at.Length - 2 && depth > at[k + 1]) k++;
                Color c = Color.Lerp(col[k], col[k + 1], Mathf.InverseLerp(at[k], at[k + 1], depth));
                for (int x = 0; x < 4; x++) tex.SetPixel(x, y, c);
            }
            File.WriteAllBytes(Path.Combine(UiBuildConfig.FindProjectRoot(), RampPng), tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            AssetDatabase.ImportAsset(RampPng, ImportAssetOptions.ForceUpdate);
        }

        string FaceRecipe(string chars, string face, bool outline) =>
            "{ \"source\": \"" + _ttf + "\", \"out\": \"" + OutAsset + "\", \"pointSize\": 72," +
            " \"padding\": 16, \"atlas\": { \"w\": 512, \"h\": 512 }, \"chars\": \"" + chars + "\", " +
            "\"materials\": [ { \"out\": \"" + OutMat + "\", \"atFontSize\": 108, " +
            (outline ? "\"outline\": { \"color\": \"#451F16\", \"px\": 8 }, " : "") +
            "\"face\": " + face + " } ] }";

        const string CapFace = "{ \"texture\": \"" + RampPng + "\", \"fit\": \"cap\" }";

        [Test]
        public void Face_AssignsRampOnFullSdfShader_CalibratedToBaselineAndCap()
        {
            WriteRamp("0:#FFF0A9", "1:#F6B633");
            UiBuildReport report = BakeWith(FaceRecipe("AB", CapFace, true));
            Assert.That(report.Ok, Is.True, report.ToText());

            var fa = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(OutAsset);
            var mat = AssetDatabase.LoadAssetAtPath<Material>(OutMat);
            Assert.That(mat.GetTexture(ShaderUtilities.ID_FaceTex), Is.EqualTo(AssetDatabase.LoadAssetAtPath<Texture2D>(RampPng)));
            Assert.That(mat.shader.name, Is.EqualTo("TextMeshPro/Distance Field"), "в мобильном SDF текстуры лица нет — она молча не нарисуется");
            Assert.That(mat.IsKeywordEnabled(ShaderUtilities.Keyword_Outline), Is.True, "смена шейдера не должна снимать кант");

            // UV2 при verticalMapping Line: v = (y − descender)/(ascender − descender). Растяжка обязана лечь
            // ровно на базовую линию (u = 0) и высоту прописных (u = 1) ЭТОГО шрифта.
            var fi = fa.faceInfo;
            Vector2 s = mat.GetTextureScale(ShaderUtilities.ID_FaceTex), o = mat.GetTextureOffset(ShaderUtilities.ID_FaceTex);
            float U(float y) => (y - fi.descentLine) / (fi.ascentLine - fi.descentLine) * s.y + o.y;
            Assert.That(U(0f), Is.EqualTo(0f).Within(1e-4f), "низ растяжки — базовая линия");
            Assert.That(U(fi.capLine), Is.EqualTo(1f).Within(1e-4f), "верх растяжки — высота прописных");

            var importer = (TextureImporter)AssetImporter.GetAtPath(RampPng);
            Assert.That(importer.wrapModeV, Is.EqualTo(TextureWrapMode.Clamp), "Repeat положил бы верх растяжки под базовую линию");
        }

        [Test]
        public void Face_MissingTexture_RefusesNamingThePath()
        {
            string missing = TempDir + "/no-such-ramp.png";
            UiBuildReport report = BakeWith(FaceRecipe("AB", "{ \"texture\": \"" + missing + "\", \"fit\": \"cap\" }", false));

            Assert.That(report.Ok, Is.False, "нет файла растяжки — выпечка обязана отказать, а не выпечь плоское лицо");
            Assert.That(report.ToText(), Does.Contain(missing));
        }

        [Test]
        public void Face_WithoutFit_RefusesNamingTheOptions()
        {
            WriteRamp("0:#FFF0A9", "1:#F6B633");
            UiBuildReport report = BakeWith(FaceRecipe("AB", "{ \"texture\": \"" + RampPng + "\" }", false));

            Assert.That(report.Ok, Is.False, "без fit неизвестно, на какую высоту глифа ложится растяжка — дефолта нет");
            Assert.That(report.ToText(), Does.Contain("cap"));
        }

        [Test]
        public void Face_Rebake_KeepsMaterialGuidAndTexture()
        {
            WriteRamp("0:#FFF0A9", "1:#F6B633");
            Assert.That(BakeWith(FaceRecipe("AB", CapFace, true)).Ok, Is.True);
            string before = AssetDatabase.AssetPathToGUID(OutMat);
            Assert.That(BakeWith(FaceRecipe("AB", CapFace, true)).Ok, Is.True);

            var mat = AssetDatabase.LoadAssetAtPath<Material>(OutMat);
            Assert.That(AssetDatabase.AssetPathToGUID(OutMat), Is.EqualTo(before), "перевыпечка не должна рвать ссылки префабов на пресет");
            Assert.That(mat.GetTexture(ShaderUtilities.ID_FaceTex), Is.Not.Null);
            Assert.That(mat.GetTexture(ShaderUtilities.ID_MainTex), Is.EqualTo(AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(OutAsset).atlasTexture));
        }

        [Test]
        public void Face_StyleWithFaceMaterial_SetsLineVerticalMapping()
        {
            WriteRamp("0:#FFF0A9", "1:#F6B633");
            Assert.That(BakeWith(FaceRecipe("AB", CapFace, false)).Ok, Is.True);

            var go = new GameObject("probe", typeof(RectTransform));
            try
            {
                var text = go.AddComponent<TextMeshProUGUI>();
                Assert.That(text.verticalMapping, Is.EqualTo(TextureMappingOptions.Character), "предпосылка: дефолт TMP — Character");
                ApplyStyle(text, 108f);
                Assert.That(text.verticalMapping, Is.EqualTo(TextureMappingOptions.Line), "калибровка растяжки сделана под Line — при Character блик встанет на разную высоту у разных глифов");
            }
            finally { Object.DestroyImmediate(go); }
        }

        static void ApplyStyle(TMP_Text text, float size)
        {
            var report = new UiBuildReport();
            string json = "{ \"colors\": {}, \"styles\": { \"s\": { \"fontSize\": " + size.ToString(System.Globalization.CultureInfo.InvariantCulture) +
                          ", \"font\": \"" + OutAsset + "\", \"material\": \"" + OutMat + "\" } } }";
            UiTokens tokens = UiTokens.Parse(json, "tokens.json", report);
            UiTextStyleApplier.Apply(text, tokens.Style("s", report, "probe"), tokens, report, "probe");
            Assert.That(report.Ok, Is.True, report.ToText());
        }

        /// <summary>
        /// Пробный рендер: «+70» с растяжкой из 4 стопов мокапа (TASK-411: блик #FFF0A9, середина #FFE78F,
        /// низ #F09329, рефлекс #F6B633) и кантом. Проверяет не свойства материала, а КАДР: верх лица светлее
        /// середины, нижняя кромка светлее полосы над ней. Кроп — Logs/uifontbake-face-probe.png.
        /// </summary>
        [Test]
        public void Face_Render_ShowsHighlightOnTopAndReflexAtBottom()
        {
            if (SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null)
                Assert.Ignore("нет графического устройства (-nographics) — кадр снять нечем");

            WriteRamp("0:#FFF0A9", "0.2:#FFE78F", "0.92:#F09329", "1:#F6B633");
            Assert.That(BakeWith(FaceRecipe("+70", CapFace, true)).Ok, Is.True);

            const int w = 640, h = 320;
            Texture2D frame = RenderText("+70", 220f, w, h);
            try
            {
                string png = Path.Combine(UiBuildConfig.FindProjectRoot(), "Logs/uifontbake-face-probe.png");
                Directory.CreateDirectory(Path.GetDirectoryName(png));
                File.WriteAllBytes(png, frame.EncodeToPNG());

                // Пиксели лица: светлые и насыщенные (кант #451F16 и фон тёмные). Строки — сверху вниз.
                int top = -1, bottom = -1;
                for (int y = h - 1; y >= 0; y--)
                    if (FaceCount(frame, y) > 0) { if (top < 0) top = y; bottom = y; }
                Assert.That(top - bottom, Is.GreaterThan(60), "лица на кадре нет или оно крошечное — проба ничего не доказывает");

                float G(float depth) => MedianFaceG(frame, Mathf.RoundToInt(Mathf.Lerp(top, bottom, depth)));
                float gTop = G(0.08f), gMid = G(0.6f), gAboveReflex = G(0.85f), gReflex = G(0.97f);
                string at = $"G: верх {gTop:0.000}, середина {gMid:0.000}, над рефлексом {gAboveReflex:0.000}, рефлекс {gReflex:0.000} (строки лица {bottom}…{top})";
                TestContext.WriteLine(at);
                Assert.That(gTop, Is.GreaterThan(gMid + 0.08f), "блик в верхней трети: " + at);
                Assert.That(gReflex, Is.GreaterThan(gAboveReflex + 0.04f), "рефлекс у нижней кромки: " + at);
            }
            finally { Object.DestroyImmediate(frame); }
        }

        static bool IsFace(Color c) => c.r > 0.75f && c.g > 0.4f;

        static int FaceCount(Texture2D t, int y)
        {
            int n = 0;
            for (int x = 0; x < t.width; x++) if (IsFace(t.GetPixel(x, y))) n++;
            return n;
        }

        static float MedianFaceG(Texture2D t, int y)
        {
            var g = new System.Collections.Generic.List<float>();
            for (int x = 0; x < t.width; x++) { Color c = t.GetPixel(x, y); if (IsFace(c)) g.Add(c.g); }
            if (g.Count == 0) return 0f;
            g.Sort();
            return g[g.Count / 2];
        }

        Texture2D RenderText(string s, float size, int w, int h)
        {
            var far = new Vector3(200000f, 200000f, 0f);
            var canvasGo = new GameObject("__face_probe", typeof(Canvas)) { hideFlags = HideFlags.HideAndDontSave, layer = 5 };
            var camGo = new GameObject("__face_probe_cam", typeof(Camera)) { hideFlags = HideFlags.HideAndDontSave };
            var rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32) { antiAliasing = 2 };
            RenderTexture prev = RenderTexture.active;
            try
            {
                canvasGo.GetComponent<Canvas>().renderMode = RenderMode.WorldSpace;
                var cr = canvasGo.GetComponent<RectTransform>();
                cr.position = far;
                cr.sizeDelta = new Vector2(w, h);

                var textGo = new GameObject("text", typeof(RectTransform)) { hideFlags = HideFlags.HideAndDontSave, layer = 5 };
                var tr = textGo.GetComponent<RectTransform>();
                tr.SetParent(cr, false);
                tr.anchorMin = Vector2.zero; tr.anchorMax = Vector2.one; tr.offsetMin = tr.offsetMax = Vector2.zero;
                var text = textGo.AddComponent<TextMeshProUGUI>();
                ApplyStyle(text, size);
                text.color = Color.white;                       // лицо = растяжка × цвет вершин
                text.alignment = TextAlignmentOptions.Center;
                text.text = s;
                text.ForceMeshUpdate();
                Canvas.ForceUpdateCanvases();

                var cam = camGo.GetComponent<Camera>();
                cam.orthographic = true;
                cam.orthographicSize = h * 0.5f;
                cam.cullingMask = 1 << 5;
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = new Color(0.11f, 0.11f, 0.13f, 1f);
                cam.nearClipPlane = 0.01f; cam.farClipPlane = 1000f;
                cam.enabled = false;
                camGo.transform.position = far + new Vector3(0f, 0f, -100f);
                cam.targetTexture = rt;
                cam.Render();

                RenderTexture.active = rt;
                var frame = new Texture2D(w, h, TextureFormat.RGBA32, false);
                frame.ReadPixels(new Rect(0, 0, w, h), 0, 0);
                frame.Apply();
                return frame;
            }
            finally
            {
                RenderTexture.active = prev;
                camGo.GetComponent<Camera>().targetTexture = null;
                Object.DestroyImmediate(camGo);
                Object.DestroyImmediate(canvasGo);
                rt.Release();
                Object.DestroyImmediate(rt);
            }
        }

        [Test]
        public void Rebake_KeepsMaterialGuid()
        {
            Assert.That(BakeWith(Recipe("\"pointSize\": 72,")).Ok, Is.True);
            string before = AssetDatabase.AssetPathToGUID(OutMat);
            Assert.That(BakeWith(Recipe("\"pointSize\": 72,")).Ok, Is.True);

            Assert.That(AssetDatabase.AssetPathToGUID(OutMat), Is.EqualTo(before), "перевыпечка не должна рвать ссылки префабов на пресет");
            var fa = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(OutAsset);
            Assert.That(AssetDatabase.LoadAssetAtPath<Material>(OutMat).GetTexture(ShaderUtilities.ID_MainTex), Is.EqualTo(fa.atlasTexture), "после перевыпечки пресет обязан смотреть на НОВЫЙ атлас");
        }
    }
}
