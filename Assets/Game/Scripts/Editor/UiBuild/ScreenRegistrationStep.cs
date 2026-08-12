using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using Iliai.UiBuild;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEngine;

namespace Game.Editor.UiBuild
{
    /// <summary>
    /// ПРОЕКТНЫЙ post-build шаг: без него собранный попап невозможно показать.
    ///
    /// Контракт экрана в этом проекте: префаб лежит в Addressables-группе «UI» по адресу
    /// `Screens/&lt;Имя&gt;.prefab`, а `Service.UI.Show&lt;T&gt;(ScreenNames.X)` грузит его по константе из
    /// сгенерированного `ScreenNames`. Оба шага делаются здесь, потому что оба — про этот проект.
    ///
    /// ⚠️ Мину видно только в коде: штатный `PanelNamesGenerator` (com.say.infrastructure) пишет
    /// результат в `Assets/Scripts/Ui/ScreenNames.cs`, а файл этого проекта лежит в
    /// `Assets/Game/Scripts/UI/ScreenNames.cs` — то есть пункт меню «Generate Ui Panels Constants»
    /// создаст ВТОРОЙ файл с тем же классом и уронит компиляцию. Поэтому здесь константы пишутся
    /// в НАЙДЕННЫЙ на диске файл, а меню Say-генератора для этого проекта трогать нельзя.
    /// </summary>
    public sealed class ScreenRegistrationStep : IUiPostBuildStep
    {
        const string GroupName = "UI";
        const string ScreensSubdir = "Screens";

        public int Order => 100;

        public void Run(UiBuildContext ctx, UiSpec spec, GameObject prefabRoot, string prefabPath)
        {
            if (spec.Kind != "screen") return;                 // компоненты в загрузчик не регистрируются

            AddressableAssetSettings settings = AddressableAssetSettingsDefaultObject.Settings;
            if (settings == null)
            {
                ctx.Report.Error(spec.SourcePath, "Addressables не инициализированы в проекте — экран некуда регистрировать");
                return;
            }

            AddressableAssetGroup group = settings.FindGroup(GroupName);
            if (group == null)
            {
                ctx.Report.Error(spec.SourcePath, $"нет Addressables-группы '{GroupName}' — экран грузиться не будет");
                return;
            }

            string guid = AssetDatabase.AssetPathToGUID(prefabPath);
            if (string.IsNullOrEmpty(guid))
            {
                ctx.Report.Error(spec.SourcePath, $"не получил GUID для {prefabPath}");
                return;
            }

            string address = $"{ScreensSubdir}/{spec.Name}.prefab";
            AddressableAssetEntry entry = settings.CreateOrMoveEntry(guid, group, false, false);
            if (entry == null)
            {
                ctx.Report.Error(spec.SourcePath, "Addressables не создали запись для префаба");
                return;
            }
            if (entry.address != address)
            {
                entry.SetAddress(address);
                ctx.Report.Note($"адрес Addressables: {address}");
            }
            EditorUtility.SetDirty(settings);

            RegenerateScreenNames(ctx, group, spec.Name, address);
        }

        /// <summary>
        /// СЛИЯНИЕ констант, а не перегенерация: существующие строки сохраняются, из группы
        /// добавляются/обновляются.
        ///
        /// Почему не «сгенерировать заново из группы», как делает штатный генератор: в этом проекте
        /// `ScreenNames` перечисляет 20 экранов, префабов которых на диске НЕТ (остатки выпотрошенного
        /// шаблона), но на константы уже ссылается живой код — `HeartsCounterComponent`,
        /// `MessagesService`, дебаг-меню. Авторитетная перегенерация из группы снесла бы их все и
        /// уронила бы компиляцию проекта. Удаление мёртвых констант — отдельная осознанная уборка,
        /// а не побочный эффект сборки попапа.
        ///
        /// Константа ТЕКУЩЕГО экрана (`currentName`/`currentAddress`) вносится напрямую из `Run()`,
        /// а НЕ через повторное сканирование группы: свежесозданная `CreateOrMoveEntry`-запись не
        /// видна `group.GatherAllAssets` в том же синхронном билде (наблюдалось «0 из группы»),
        /// поэтому новый экран иначе не попадал бы в файл с первого билда. Сканирование группы
        /// остаётся как источник для остальных, уже осевших экранов.
        ///
        /// Пишем только при реальном изменении: каждая запись .cs = рекомпиляция + перезагрузка
        /// домена, а она обрывает прогон роли.
        /// </summary>
        static void RegenerateScreenNames(UiBuildContext ctx, AddressableAssetGroup group, string currentName, string currentAddress)
        {
            string filePath = FindScreenNamesFile();
            if (filePath == null)
            {
                ctx.Report.Warn("ScreenNames", "не нашёл ScreenNames.cs — константу для нового экрана добавь вручную");
                return;
            }

            var names = new SortedDictionary<string, string>(StringComparer.Ordinal);
            foreach (KeyValuePair<string, string> existing in ParseExistingConstants(filePath))
                names[existing.Key] = existing.Value;

            var entries = new List<AddressableAssetEntry>();
            group.GatherAllAssets(entries, false, true, true);

            int fromGroup = 0;
            foreach (AddressableAssetEntry entry in entries)
            {
                if (entry.IsFolder || string.IsNullOrEmpty(entry.AssetPath)) continue;
                string dir = Path.GetDirectoryName(entry.AssetPath)?.Replace('\\', '/');
                if (dir == null || !dir.EndsWith("/" + ScreensSubdir)) continue;
                string name = Path.GetFileNameWithoutExtension(entry.AssetPath);
                if (string.IsNullOrEmpty(name)) continue;
                names[name] = entry.address;
                fromGroup++;
            }

            // Текущий экран — авторитетно и в обход сканирования группы (см. XML-док выше).
            bool currentFromGroup = false;
            if (!string.IsNullOrEmpty(currentName) && !string.IsNullOrEmpty(currentAddress))
            {
                currentFromGroup = names.ContainsKey(currentName) && names[currentName] == currentAddress;
                names[currentName] = currentAddress;
            }

            if (names.Count == 0)
            {
                ctx.Report.Warn("ScreenNames", "в группе UI нет ни одного экрана и файл пуст — константы не тронуты");
                return;
            }

            var sb = new StringBuilder();
            sb.Append("// Generated by PanelNamesGenerator / UiBuild ScreenRegistrationStep, don't change\n");
            sb.Append("namespace Ui\n{\n");
            sb.Append("    public static class ScreenNames\n    {\n");
            foreach (KeyValuePair<string, string> kv in names)
                sb.Append($"        public const string {kv.Key} = \"{kv.Value}\";\n");
            sb.Append("    }\n}\n");

            string next = sb.ToString();
            string current = File.Exists(filePath) ? File.ReadAllText(filePath) : null;
            if (NormalizeNewlines(current) == NormalizeNewlines(next)) return;

            File.WriteAllText(filePath, next);
            string currentSource = currentFromGroup ? "" : " + текущий экран напрямую";
            ctx.Report.Note($"обновил {filePath}: {names.Count} констант ({fromGroup} из группы Addressables{currentSource}, остальные сохранены) — дальше пойдёт рекомпиляция");
            // Импорт откладываем: перезагрузка домена внутри прогона оборвала бы запись отчёта.
            string toImport = filePath;
            EditorApplication.delayCall += () => AssetDatabase.ImportAsset(toImport);
        }

        /// <summary>Вытаскивает уже существующие константы, чтобы слияние их не потеряло.</summary>
        static IEnumerable<KeyValuePair<string, string>> ParseExistingConstants(string filePath)
        {
            var found = new List<KeyValuePair<string, string>>();
            if (!File.Exists(filePath)) return found;
            foreach (Match m in Regex.Matches(File.ReadAllText(filePath), @"public\s+const\s+string\s+(\w+)\s*=\s*""([^""]*)""\s*;"))
                found.Add(new KeyValuePair<string, string>(m.Groups[1].Value, m.Groups[2].Value));
            return found;
        }

        static string FindScreenNamesFile()
        {
            foreach (string guid in AssetDatabase.FindAssets("ScreenNames t:MonoScript"))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (path.EndsWith("/ScreenNames.cs")) return path;
            }
            return null;
        }

        static string NormalizeNewlines(string s) => s?.Replace("\r\n", "\n");
    }
}
