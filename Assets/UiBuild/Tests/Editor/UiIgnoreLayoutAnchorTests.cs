using System.IO;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace Iliai.UiBuild.Tests
{
    /// <summary>
    /// Ребёнок layout-группы с `ignoreLayout: true` раскладывается САМ: свой `anchor` + `offset` + `size`, как вне
    /// группы (spec-schema §«Размер», последняя строка). До фикса компилятор не применял такому узлу анкер никогда,
    /// а `size` клал в `LayoutElement.preferred*`, который группа для него же игнорирует — узел оставался с
    /// дефолтным rect при чистом отчёте. Класс случаев: шапка, крестик, бейдж или ленточка поверх панели с
    /// авто-высотой (попап с переменным числом строк, список наград, ячейка сетки магазина).
    ///
    /// Сборка идёт настоящим <see cref="UiPrefabBuilder.Build"/> во временную папку компонентов (kind: component —
    /// регистраторы экранов на него не срабатывают). Имён этой игры здесь нет.
    ///
    /// Носитель защиты — <see cref="IgnoreLayoutChild_OfGroup_GetsAnchorOffsetSize"/>: откат фикса
    /// (`placedByGroup` → `inLayoutGroup` в BuildNode) делает его красным.
    /// </summary>
    [TestFixture]
    public sealed class UiIgnoreLayoutAnchorTests
    {
        const string TempDir = "Assets/__UiBuildIgnoreLayoutTests";

        const string SpecJson = @"{
  ""name"": ""IgnoreLayoutProbe"",
  ""kind"": ""component"",
  ""size"": { ""w"": 600, ""h"": 400 },
  ""children"": [
    {
      ""id"": ""Card"",
      ""type"": ""node"",
      ""anchor"": ""center"",
      ""offset"": { ""x"": 0, ""y"": 0 },
      ""size"": { ""w"": 500, ""h"": 300 },
      ""layout"": { ""dir"": ""vertical"", ""spacing"": 10, ""padding"": { ""left"": 20, ""right"": 20, ""top"": 20, ""bottom"": 20 } },
      ""children"": [
        { ""id"": ""Row"", ""type"": ""node"", ""size"": { ""w"": 460, ""h"": 80 } },
        {
          ""id"": ""Badge"",
          ""type"": ""node"",
          ""ignoreLayout"": true,
          ""anchor"": ""top-right"",
          ""pivot"": [0.5, 0.5],
          ""offset"": { ""x"": -46, ""y"": -30 },
          ""size"": { ""w"": 132, ""h"": 120 }
        }
      ]
    }
  ]
}";

        string _specAbs;

        [OneTimeSetUp]
        public void CreateFolder()
        {
            if (!AssetDatabase.IsValidFolder(TempDir)) AssetDatabase.CreateFolder("Assets", "__UiBuildIgnoreLayoutTests");
            _specAbs = Path.Combine(Path.GetTempPath(), "uibuild-ignorelayout-probe.json");
            File.WriteAllText(_specAbs, SpecJson);
        }

        [OneTimeTearDown]
        public void DeleteFolder()
        {
            AssetDatabase.DeleteAsset(TempDir);
            if (File.Exists(_specAbs)) File.Delete(_specAbs);
        }

        RectTransform BuildAndFind(string childPath, out UiBuildReport report)
        {
            report = new UiBuildReport();
            UiBuildConfig config = UiBuildConfig.Load(report);
            config.ComponentPrefabRoot = TempDir;
            UiTokens tokens = UiTokens.Parse("{ \"colors\": {}, \"styles\": {} }", "tokens.json", report);
            var ctx = new UiBuildContext(config, tokens, report);

            UiSpec spec = UiSpec.Load(_specAbs, "components/IgnoreLayoutProbe.json", report);
            Assert.That(spec, Is.Not.Null, report.ToText());
            UiBuildResult result = UiPrefabBuilder.Build(spec, ctx);
            Assert.That(result.Saved, Is.True, report.ToText());

            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(result.PrefabPath);
            Assert.That(prefab, Is.Not.Null, "префаб не записался: " + result.PrefabPath);
            Transform child = prefab.transform.Find(childPath);
            Assert.That(child, Is.Not.Null, $"нет узла {childPath}");
            return (RectTransform)child;
        }

        [Test]
        public void IgnoreLayoutChild_OfGroup_GetsAnchorOffsetSize()
        {
            RectTransform badge = BuildAndFind("Card/Badge", out UiBuildReport report);

            Assert.That(report.Ok, Is.True, report.ToText());
            Assert.That(badge.anchorMin, Is.EqualTo(new Vector2(1f, 1f)), "анкер top-right применён");
            Assert.That(badge.anchorMax, Is.EqualTo(new Vector2(1f, 1f)));
            Assert.That(badge.pivot, Is.EqualTo(new Vector2(0.5f, 0.5f)), "явный pivot узла");
            Assert.That(badge.anchoredPosition, Is.EqualTo(new Vector2(-46f, -30f)), "offset от угла");
            Assert.That(badge.sizeDelta, Is.EqualTo(new Vector2(132f, 120f)), "size лёг в rect, а не в LayoutElement");

            var element = badge.GetComponent<LayoutElement>();
            Assert.That(element, Is.Not.Null, "ignoreLayout держится LayoutElement'ом");
            Assert.That(element.ignoreLayout, Is.True);
            Assert.That(element.preferredWidth, Is.LessThan(0f), "size не дублируется в preferred — группа его всё равно не читает");
            Assert.That(element.preferredHeight, Is.LessThan(0f));
        }

        /// <summary>Обычный ребёнок группы фиксом не задет: размер по-прежнему уходит в LayoutElement.preferred.</summary>
        [Test]
        public void RegularChild_OfGroup_StillSizedThroughLayoutElement()
        {
            RectTransform row = BuildAndFind("Card/Row", out UiBuildReport report);

            Assert.That(report.Ok, Is.True, report.ToText());
            var element = row.GetComponent<LayoutElement>();
            Assert.That(element, Is.Not.Null);
            Assert.That(element.preferredWidth, Is.EqualTo(460f));
            Assert.That(element.preferredHeight, Is.EqualTo(80f));
        }
    }
}
