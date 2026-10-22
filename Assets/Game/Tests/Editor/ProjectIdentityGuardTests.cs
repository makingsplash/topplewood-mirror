using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace Gameplay.Tests
{
    /// <summary>
    /// СТОРОЖ идентичности приложения у издателя (TASK-064, образец — Piecework TASK-012). Игра родилась из шаблона
    /// tilesort и до TASK-064 уезжала на устройство без своего ключа SayKit, на smoke-обходах. Всё, что здесь
    /// сторожится, молчаливо по природе: сборка с чужим ключом или с забытым smoke-флагом собирается, ставится и
    /// играется, а ломается невидимое — аналитика, ремоут, реклама уходят не туда или не уходят вовсе.
    ///
    /// Механизм — ТЕКСТ файлов (<c>ProjectSettings.asset</c>, <c>SayKitApp.cs</c>), а не API редактора: утверждение
    /// звучит как «идентичность ЗАКРЕПЛЕНА В КОММИТЕ», а API отвечает из памяти процесса и на незакоммиченном
    /// значении был бы зелёным.
    /// </summary>
    [TestFixture]
    public sealed class ProjectIdentityGuardTests
    {
        /// <summary>
        /// Ключ приложения «Topplewood (Android)» в SayLauncher (TASK-064, 2026-10-21). Его называет ИЗДАТЕЛЬ.
        /// Сторожится РАВЕНСТВО выданному, а не неравенство чужим: ключей шаблона здесь нет ни литералом —
        /// статический анализ бота издателя принимает такую константу за настройку (Piecework TASK-800).
        /// </summary>
        private const string ExpectedSayKitAndroidKey = "tplwoda";

        /// <summary>Bundle id НЕ меняется при регистрации (решение владельца 2026-10-21): приложение заведено под ним.</summary>
        private const string ExpectedAndroidIdentifier = "com.game.dropcolorblocks";

        /// <summary>
        /// Порог пребилда SayKit пина <c>#2026102000</c>: <c>PreBuild.cs:1315</c>, <c>CheckMinAPILevel</c> (TASK-663).
        /// Без сторожа он вскрывается только минутами сборки (<c>build-android.sh</c> exit 4), здесь — за секунды.
        /// </summary>
        private const int SayKitMinTargetSdk = 36;

        /// <summary>Обходы smoke-сборки TASK-063/533, снятые в TASK-064. Вернуть любой — снова заглушить SDK в плеере.</summary>
        private static readonly string[] SmokeDefines =
        {
            "SAYKIT_SMOKE_DISABLE_RUNTIME",
            "SAYKIT_SHIFT_ANDROID_POSTPROCESS_ORDER",
        };

        [Test]
        public void SayKitAndroidAppKey_IsPublisherIssued()
        {
            string value = ReadSayKitAppConstant("APP_KEY_ANDROID", out string path);

            Assert.That(
                value,
                Is.EqualTo(ExpectedSayKitAndroidKey),
                $"Ключ SayKit для Android выдан издателем под приложение Topplewood (TASK-064) и равен "
                + $"'{ExpectedSayKitAndroidKey}'. Нашёл '{value}' в {path}. По ключу пребилд забирает у сервера "
                + "google-services.json, Facebook/AdMob id и ремоут: под пустым или чужим ключом сборка либо "
                + "краснеет на пребилде, либо молча везёт чужое приложение (docs/platform/saykit.md). Другой ключ — "
                + "это другое приложение издателя; меняется только вместе с секретом и этим тестом.");
        }

        [Test]
        public void SayKitAndroidAppSecret_IsNotEmpty()
        {
            string value = ReadSayKitAppConstant("APP_SECRET_ANDROID", out string path);

            Assert.That(value, Is.Not.Empty, $"APP_SECRET_ANDROID в {path} пуст — ключ без секрета издатель не примет.");
        }

        [Test]
        public void AndroidApplicationIdentifier_IsTheRegisteredBundle()
        {
            string value = ReadNestedScalar("applicationIdentifier", "Android", out string path);

            Assert.That(
                value,
                Is.EqualTo(ExpectedAndroidIdentifier),
                $"applicationIdentifier.Android в {path} = '{value}', а приложение в SayLauncher заведено под "
                + $"'{ExpectedAndroidIdentifier}'. Под другим bundle Firebase-конфиг пребилда не совпадёт с пакетом "
                + "(gradle: 'No matching client found for package name'). Смену принимает владелец, а не рефакторинг.");
        }

        [Test]
        public void SayKitAppIosBundle_MatchesProjectSettings_BecauseTheSdkMenuWritesItBack()
        {
            string bundle = ReadSayKitAppConstant("APP_BUNDLE_IOS", out string appPath);
            string name = ReadSayKitAppConstant("APP_NAME_IOS", out _);
            string identifier = ReadNestedScalar("applicationIdentifier", "iPhone", out string settingsPath);
            string productName = ReadScalar("productName", out _);

            Assert.That(
                bundle,
                Is.EqualTo(identifier),
                $"APP_BUNDLE_IOS в {appPath} ('{bundle}') разошёлся с applicationIdentifier.iPhone в {settingsPath} "
                + $"('{identifier}'). Пункт меню SDK «[iOS] Application config» пишет константу в настройки проекта — "
                + "расхождение это возврат чужого идентификатора одним кликом.");

            Assert.That(
                name,
                Is.EqualTo(productName),
                $"APP_NAME_IOS в {appPath} ('{name}') разошёлся с productName в {settingsPath} ('{productName}'): "
                + "тот же пункт меню SDK перепишет им имя продукта, а оно — половина persistentDataPath.");
        }

        [Test]
        public void AndroidDefines_CarryNoSmokeBypass()
        {
            string defines = ReadNestedScalar("scriptingDefineSymbols", "Android", out string path);
            string[] symbols = defines.Split(';').Select(s => s.Trim()).ToArray();

            foreach (string smoke in SmokeDefines)
            {
                Assert.That(
                    symbols,
                    Has.No.Member(smoke),
                    $"В {path} в Android-дефайнах вернулся '{smoke}' — обход smoke-сборки без ключа (TASK-063/533). "
                    + "С ним плеер глушит SayKit или переставляет его пост-процессор, а сборка выглядит обычной.");
            }

            Assert.That(symbols, Has.Member("SAYKIT"), $"В {path} в Android-дефайнах нет SAYKIT — сторож читает не ту строку.");
        }

        [Test]
        public void AndroidApplicationEntry_KeepsActivity_BecauseSayKitActivityExtendsUnityPlayerActivity()
        {
            string value = ReadScalar("androidApplicationEntry", out string path);

            Assert.That(int.TryParse(value, out int entry), Is.True, $"androidApplicationEntry в {path} = '{value}'");
            Assert.That(
                entry & 1,
                Is.EqualTo(1),
                $"androidApplicationEntry в {path} = {entry}: бит Activity снят. Лаунчер SayKit "
                + "(by.saygames.SayKitActivity) наследует UnityPlayerActivity; на одном GameActivity приложение "
                + "не стартует (docs/platform/saykit.md §Пребилд).");
        }

        [Test]
        public void AndroidTargetSdk_MeetsSayKitPrebuildThreshold()
        {
            string value = ReadScalar("AndroidTargetSdkVersion", out string path);

            Assert.That(int.TryParse(value, out int target), Is.True, $"AndroidTargetSdkVersion в {path} = '{value}'");
            Assert.That(
                target,
                Is.GreaterThanOrEqualTo(SayKitMinTargetSdk),
                $"AndroidTargetSdkVersion в {path} = {target}, а пребилд SayKit #2026102000 требует ≥ {SayKitMinTargetSdk} "
                + "(PreBuild.cs:1315, CheckMinAPILevel) — сборка краснеет на пребилде. Смена пина SayKit = сверить порог "
                + "заново (docs/platform/saykit.md, таблица порогов в шапке).");
        }

        private static string ReadScalar(string key, out string path)
        {
            string[] hits = ReadProjectSettings(out path)
                .Split('\n')
                .Select(line => line.Trim())
                .Where(line => line.StartsWith(key + ":", StringComparison.Ordinal))
                .ToArray();

            Assert.That(hits.Length, Is.EqualTo(1), $"В {path} ожидалась ровно одна строка '{key}:', нашлось {hits.Length}.");
            return hits[0].Substring(key.Length + 1).Trim();
        }

        /// <summary>
        /// Поле ВНУТРИ блока: ключ <c>Android</c> встречается у многих потаргетных словарей, и поиск по всему
        /// тексту нашёл бы чужой.
        /// </summary>
        private static string ReadNestedScalar(string blockKey, string key, out string path)
        {
            string[] lines = ReadProjectSettings(out path).Split('\n');

            int[] headers = Enumerable
                .Range(0, lines.Length)
                .Where(i => lines[i].Trim() == blockKey + ":")
                .ToArray();

            Assert.That(headers.Length, Is.EqualTo(1), $"В {path} ожидался ровно один блок '{blockKey}:', нашлось {headers.Length}.");

            int headerIndent = IndentOf(lines[headers[0]]);
            string[] hits = lines
                .Skip(headers[0] + 1)
                .TakeWhile(line => line.Trim().Length > 0 && IndentOf(line) > headerIndent)
                .Select(line => line.Trim())
                .Where(line => line.StartsWith(key + ":", StringComparison.Ordinal))
                .ToArray();

            Assert.That(hits.Length, Is.EqualTo(1),
                $"В {path} внутри блока '{blockKey}:' ожидалась ровно одна строка '{key}:', нашлось {hits.Length}.");
            return hits[0].Substring(key.Length + 1).Trim();
        }

        /// <summary>
        /// Строковая константа <c>SayKitApp</c> из ТЕКСТА исходника. Ровно одно объявление: под <c>#if</c> у класса
        /// бывают парные поля, и тихо взять первое из двух было бы враньём.
        /// </summary>
        private static string ReadSayKitAppConstant(string name, out string path)
        {
            path = Path.GetFullPath(Path.Combine(Application.dataPath, "SayKitApp", "SayKitApp.cs"));
            Assert.That(File.Exists(path), Is.True, $"не найден {path}");

            string declaration = "public const string " + name + " = \"";
            string[] hits = File.ReadAllText(path)
                .Split('\n')
                .Select(line => line.Trim())
                .Where(line => line.StartsWith(declaration, StringComparison.Ordinal))
                .ToArray();

            Assert.That(hits.Length, Is.EqualTo(1),
                $"В {path} ожидалось ровно одно объявление '{declaration}…', нашлось {hits.Length}. Имена полей "
                + "SayKitApp SDK читает по строкам сам (docs/platform/saykit.md).");

            string tail = hits[0].Substring(declaration.Length);
            int end = tail.IndexOf('"');
            Assert.That(end, Is.GreaterThanOrEqualTo(0), $"В {path} объявление '{name}' не закрыто кавычкой.");
            return tail.Substring(0, end);
        }

        private static int IndentOf(string line)
        {
            return line.Length - line.TrimStart(' ').Length;
        }

        private static string ReadProjectSettings(out string path)
        {
            // От Application.dataPath, а не от текущего каталога: у батча и у редактора он разный.
            path = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "ProjectSettings", "ProjectSettings.asset"));
            Assert.That(File.Exists(path), Is.True, $"не найден {path}");
            return File.ReadAllText(path);
        }
    }
}
