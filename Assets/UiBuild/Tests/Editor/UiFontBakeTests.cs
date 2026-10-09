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
    /// на атласе ЭТОГО шрифта с keyword — Material_SamplesTheBakedAtlas_WithKeyword.
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
