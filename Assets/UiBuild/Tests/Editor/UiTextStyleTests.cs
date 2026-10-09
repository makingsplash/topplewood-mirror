using NUnit.Framework;
using TMPro;
using UnityEditor;
using UnityEngine;

namespace Iliai.UiBuild.Tests
{
    /// <summary>
    /// Поля стиля текста `material` и `gradient` (tokens.json → styles.&lt;имя&gt;). Тесты гоняют ровно тот
    /// разбор (<see cref="UiTokens.Parse"/>) и то применение (<see cref="UiTextStyleApplier.Apply"/>), что
    /// и сборка префаба.
    ///
    /// Фикстуры переносимы: шрифт — дефолтный шрифт TMP Essentials из TMP_Settings (есть в любом
    /// проекте с TMP), материалы создаются во временной папке и удаляются после прогона. Имён этой игры
    /// здесь нет.
    ///
    /// Носители защиты (сняты мутациями, TASK-403): отказ при чужом атласе — ОДИН,
    /// Material_ForeignAtlas_IsBuildErrorWithWhere; порядок «font раньше material» — ОДИН,
    /// Material_IsCheckedAgainstStyleFont_NotThePreviousOne. Почему отказ нужен (TMP откатывает материал
    /// молча) — канарейка TmpRevertsForeignMaterialOnLoad.
    /// </summary>
    [TestFixture]
    public sealed class UiTextStyleTests
    {
        const string TempDir = "Assets/__UiBuildTextStyleTests";
        const string Where = "screens/probe.json:title";

        TMP_FontAsset _font;
        string _fontPath;
        Material _matching;
        string _matchingPath;
        Material _foreign;
        string _foreignPath;
        GameObject _go;
        TextMeshProUGUI _text;

        [OneTimeSetUp]
        public void CreateFixtures()
        {
            _font = TMP_Settings.defaultFontAsset;
            Assert.That(_font, Is.Not.Null, "нет дефолтного шрифта TMP (TMP Essentials не импортированы) — фикстуре не на чем стоять");
            Assert.That(_font.atlasTexture, Is.Not.Null, "у дефолтного шрифта TMP нет атласа");
            _fontPath = AssetDatabase.GetAssetPath(_font);

            if (!AssetDatabase.IsValidFolder(TempDir)) AssetDatabase.CreateFolder("Assets", "__UiBuildTextStyleTests");

            _matchingPath = TempDir + "/matching.mat";
            var matching = new Material(_font.material) { name = "matching" };
            matching.EnableKeyword(ShaderUtilities.Keyword_Outline);
            matching.SetFloat(ShaderUtilities.ID_OutlineWidth, 0.2f);
            AssetDatabase.CreateAsset(matching, _matchingPath);

            _foreignPath = TempDir + "/foreign.mat";
            var foreign = new Material(_font.material) { name = "foreign" };
            AssetDatabase.CreateAsset(foreign, _foreignPath);
            var foreignAtlas = new Texture2D(4, 4) { name = "foreign-atlas" };
            AssetDatabase.AddObjectToAsset(foreignAtlas, _foreignPath);
            foreign.SetTexture(ShaderUtilities.ID_MainTex, foreignAtlas);
            EditorUtility.SetDirty(foreign);
            AssetDatabase.SaveAssets();

            _matching = AssetDatabase.LoadAssetAtPath<Material>(_matchingPath);
            _foreign = AssetDatabase.LoadAssetAtPath<Material>(_foreignPath);
        }

        [OneTimeTearDown]
        public void DeleteFixtures()
        {
            AssetDatabase.DeleteAsset(TempDir);
        }

        [SetUp]
        public void CreateText()
        {
            _go = new GameObject("title", typeof(RectTransform));
            _text = _go.AddComponent<TextMeshProUGUI>();
            _text.text = "1250";
        }

        [TearDown]
        public void DestroyText()
        {
            Object.DestroyImmediate(_go);
        }

        static string Json(string path) => path.Replace("\\", "\\\\").Replace("\"", "\\\"");

        UiTokens Tokens(string styleBody, UiBuildReport report)
        {
            string json = "{ \"colors\": { \"gold-hi\": \"#FFE27A\", \"gold-lo\": \"#D9881F\" }, \"styles\": { \"s\": { " + styleBody + " } } }";
            return UiTokens.Parse(json, "tokens.json", report);
        }

        void Apply(string styleBody, UiBuildReport report)
        {
            UiTokens tokens = Tokens(styleBody, report);
            UiTextStyle style = tokens.Style("s", report, Where);
            UiTextStyleApplier.Apply(_text, style, tokens, report, Where);
        }

        // ───────────────────────────── material ─────────────────────────────

        [Test]
        public void Material_SameAtlas_IsAssignedAsSharedMaterial()
        {
            var report = new UiBuildReport();
            Apply($"\"font\": \"{Json(_fontPath)}\", \"material\": \"{Json(_matchingPath)}\"", report);

            Assert.That(report.Ok, Is.True, report.ToText());
            Assert.That(_text.fontSharedMaterial, Is.SameAs(_matching));
        }

        [Test]
        public void Material_WithoutFontInStyle_IsCheckedAgainstTheTextsActualFont()
        {
            var report = new UiBuildReport();
            Apply($"\"material\": \"{Json(_matchingPath)}\"", report);   // шрифт текста — дефолтный TMP, атлас тот же

            Assert.That(report.Ok, Is.True, report.ToText());
            Assert.That(_text.fontSharedMaterial, Is.SameAs(_matching));
        }

        /// <summary>Текст стартует с ДРУГИМ шрифтом: атлас материала обязан сверяться со шрифтом стиля, а не
        /// с прежним шрифтом текста. Носитель порядка «font раньше material» в применении стиля.</summary>
        [Test]
        public void Material_IsCheckedAgainstStyleFont_NotThePreviousOne()
        {
            // Второй шрифт — клон дефолтного со СВОИМ атласом и материалом (CreateFontAsset из встроенного
            // LegacyRuntime.ttf в батче возвращает null: у встроенного шрифта нет данных начертания).
            TMP_FontAsset other = Object.Instantiate(_font);
            var otherAtlas = new Texture2D(4, 4) { name = "other-atlas" };
            var otherMaterial = new Material(_font.material) { name = "other" };
            otherMaterial.SetTexture(ShaderUtilities.ID_MainTex, otherAtlas);
            other.atlasTextures = new[] { otherAtlas };
            other.material = otherMaterial;
            try
            {
                Assert.That(other.atlasTexture, Is.SameAs(otherAtlas), "у клона должен быть свой атлас — иначе тест ничего не различает");
                _text.font = other;

                var report = new UiBuildReport();
                Apply($"\"font\": \"{Json(_fontPath)}\", \"material\": \"{Json(_matchingPath)}\"", report);

                Assert.That(report.Ok, Is.True, report.ToText());
                Assert.That(_text.fontSharedMaterial, Is.SameAs(_matching));
            }
            finally
            {
                Object.DestroyImmediate(other);
                Object.DestroyImmediate(otherMaterial);
                Object.DestroyImmediate(otherAtlas);
            }
        }

        [Test]
        public void Material_ForeignAtlas_IsBuildErrorWithWhere()
        {
            var report = new UiBuildReport();
            Apply($"\"font\": \"{Json(_fontPath)}\", \"material\": \"{Json(_foreignPath)}\"", report);

            Assert.That(report.Ok, Is.False, "материал под чужой атлас обязан ронять сборку");
            Assert.That(report.Errors.Count, Is.EqualTo(1), report.ToText());
            Assert.That(report.Errors[0].Where, Is.EqualTo(Where), "ошибка адресуется файлом и id узла");
            StringAssert.Contains("атлас", report.Errors[0].Message);
            Assert.That(_text.fontSharedMaterial, Is.Not.SameAs(_foreign), "чужой материал не назначается");
        }

        [Test]
        public void Material_NotFound_IsBuildError()
        {
            var report = new UiBuildReport();
            Apply($"\"material\": \"{TempDir}/missing.mat\"", report);

            Assert.That(report.Ok, Is.False);
            StringAssert.Contains("материал не найден", report.Errors[0].Message);
        }

        [Test]
        public void Material_SameAtlas_SurvivesPrefabReload()
        {
            var report = new UiBuildReport();
            Apply($"\"font\": \"{Json(_fontPath)}\", \"material\": \"{Json(_matchingPath)}\"", report);
            Assert.That(report.Ok, Is.True, report.ToText());

            Assert.That(ReloadedSharedMaterial(), Is.SameAs(_matching), "материал под тот же атлас TMP не откатывает");
        }

        /// <summary>Канарейка поведения TMP, ради которого существует проверка атласа: материал под ЧУЖОЙ
        /// атлас, назначенный в обход компилятора, после загрузки префаба тихо заменён материалом шрифта.
        /// Покраснеет — TMP перестал так делать, и текст ошибки компилятора надо пересмотреть.</summary>
        [Test]
        public void TmpRevertsForeignMaterialOnLoad()
        {
            _text.font = _font;
            _text.fontSharedMaterial = _foreign;                         // в обход компилятора

            Assert.That(ReloadedSharedMaterial(), Is.SameAs(_font.material));
        }

        Material ReloadedSharedMaterial()
        {
            string prefabPath = TempDir + "/text.prefab";
            PrefabUtility.SaveAsPrefabAsset(_go, prefabPath, out bool saved);
            Assert.That(saved, Is.True, "префаб не записался");
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            try { return instance.GetComponent<TMP_Text>().fontSharedMaterial; }
            finally { Object.DestroyImmediate(instance); }
        }

        // ───────────────────────────── gradient ─────────────────────────────

        [Test]
        public void Gradient_SetsVerticalVertexGradient()
        {
            var report = new UiBuildReport();
            Apply("\"gradient\": { \"top\": \"gold-hi\", \"bottom\": \"gold-lo\" }", report);

            Assert.That(report.Ok, Is.True, report.ToText());
            Assert.That(_text.enableVertexGradient, Is.True);
            ColorUtility.TryParseHtmlString("#FFE27A", out Color hi);
            ColorUtility.TryParseHtmlString("#D9881F", out Color lo);
            VertexGradient g = _text.colorGradient;
            Assert.That(g.topLeft, Is.EqualTo(hi));
            Assert.That(g.topRight, Is.EqualTo(hi));
            Assert.That(g.bottomLeft, Is.EqualTo(lo));
            Assert.That(g.bottomRight, Is.EqualTo(lo));
        }

        [Test]
        public void NoGradient_LeavesVertexGradientOff()
        {
            var report = new UiBuildReport();
            Apply("\"fontSize\": 40", report);

            Assert.That(report.Ok, Is.True, report.ToText());
            Assert.That(_text.enableVertexGradient, Is.False);
        }

        [Test]
        public void Gradient_HalfPair_IsTokensError()
        {
            var report = new UiBuildReport();
            Apply("\"gradient\": { \"top\": \"gold-hi\" }", report);

            Assert.That(report.Ok, Is.False);
            StringAssert.Contains("парой", report.Errors[0].Message);
            Assert.That(_text.enableVertexGradient, Is.False, "половина градиента не применяется");
        }

        [Test]
        public void Gradient_UnknownKey_IsTokensError()
        {
            var report = new UiBuildReport();
            Apply("\"gradient\": { \"top\": \"gold-hi\", \"middle\": \"gold-hi\", \"bottom\": \"gold-lo\" }", report);

            Assert.That(report.Ok, Is.False);
            StringAssert.Contains("'middle'", report.Errors[0].Message);
        }

        [Test]
        public void Gradient_UnknownColorToken_IsBuildErrorWithWhere()
        {
            var report = new UiBuildReport();
            Apply("\"gradient\": { \"top\": \"gold-hi\", \"bottom\": \"no-such\" }", report);

            Assert.That(report.Ok, Is.False);
            Assert.That(report.Errors[0].Where, Is.EqualTo(Where));
            Assert.That(_text.enableVertexGradient, Is.False);
        }
    }
}
