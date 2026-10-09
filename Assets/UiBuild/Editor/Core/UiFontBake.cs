using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.TextCore;
using UnityEngine.TextCore.LowLevel;

namespace Iliai.UiBuild
{
    /// <summary>
    /// Рецепт шрифта (`design/fonts/*.json`) → статичный SDF-атлас TMP + материалы-пресеты стилей.
    ///
    /// Зачем рецептом, а не окном Font Asset Creator: выпечка воспроизводима одной командой, параметры
    /// (кегль сэмплинга, padding, состав символов) лежат в git рядом со спекой, а пресеты обводки/тени
    /// ПЕРЕВЫПЕКАЮТСЯ вместе с атласом. Перевыпечка шрифта руками оставляет старые пресеты на старом
    /// атласе, и TMP молча подменяет их материалом шрифта (docs/platform/unity-gotchas.md, «Материал под
    /// ЧУЖОЙ атлас»). Класс задач: любая гарнитура проекта и любой набор стилей с обводкой/тенью.
    ///
    /// Толщины задаются в PX ПРИ НАЗВАННОМ КЕГЛЕ (`atFontSize`), а не в долях SDF: доли зависят от
    /// выпечки (`padding`, `pointSize`), и при перевыпечке кант поменял бы толщину. Перевод —
    /// `OutlineWidth = FaceDilate = px · pointSize / (2 · padding · fontSize)` — кант целиком снаружи, его
    /// наружу выносят и дилейт, и сам кант (подписано кадром TASK-401: 8 px заказано → 8 px на кадре);
    /// смещение тени — `px · pointSize / (padding · fontSize)`.
    ///
    /// Рецепт:
    /// <code>
    /// { "source": "Assets/…/Font.ttf", "faceIndex": 0, "expectStyle": "Black",
    ///   "out": "Assets/…/Font SDF.asset", "pointSize": 72, "padding": 12, "atlas": { "w": 2048, "h": 1024 },
    ///   "ranges": ["20-7E", "D7", "400-45F"], "chars": "",
    ///   "materials": [ { "out": "Assets/…/Font SDF - Reward.mat", "atFontSize": 108,
    ///                    "outline":  { "color": "token|#hex", "px": 8 },
    ///                    "underlay": { "color": "token|#hex", "x": 0, "y": -4, "softness": 0.3 },
    ///                    "face":     { "texture": "Assets/…/ramp.png", "fit": "cap|x|line" } } ] }
    /// </code>
    /// Цвета — токены `tokens.json` (hex допустим с предупреждением, как в спеке). `face` — см. ApplyFace.
    /// </summary>
    public static class UiFontBake
    {
        public static UiBuildReport Bake(string recipeRel)
        {
            var report = new UiBuildReport();
            UiBuildConfig cfg = UiBuildConfig.Load(report);
            if (!report.Ok) return report;

            string recipePath = cfg.Abs(recipeRel);
            if (!File.Exists(recipePath)) { report.Error(recipeRel, "рецепт не найден"); return report; }

            Dictionary<string, object> r;
            try { r = UiJson.Parse(File.ReadAllText(recipePath)) as Dictionary<string, object>; }
            catch (UiJsonException e) { report.Error(recipeRel, "битый JSON: " + e.Message); return report; }
            if (r == null) { report.Error(recipeRel, "корень должен быть объектом"); return report; }

            string source = Str(r, "source");
            string outPath = Str(r, "out");
            int pointSize = Int(r, "pointSize", 0);
            int padding = Int(r, "padding", 0);
            int faceIndex = Int(r, "faceIndex", 0);
            var atlas = r.TryGetValue("atlas", out object a) ? a as Dictionary<string, object> : null;
            int atlasW = atlas == null ? 0 : Int(atlas, "w", 0);
            int atlasH = atlas == null ? 0 : Int(atlas, "h", 0);

            // Дефолтов у параметров выпечки нет намеренно: подставленный «типовой» кегль/padding молча
            // задал бы предел толщины обводки, о котором рецепт ничего не говорит.
            if (string.IsNullOrEmpty(source)) report.Error(recipeRel, "нет 'source' (путь к .ttf/.otf в Assets)");
            if (string.IsNullOrEmpty(outPath) || !outPath.EndsWith(".asset")) report.Error(recipeRel, "нет 'out' (путь к .asset)");
            if (pointSize <= 0) report.Error(recipeRel, "нет 'pointSize' (кегль сэмплинга атласа)");
            if (padding <= 0) report.Error(recipeRel, "нет 'padding' (поле SDF, px атласа)");
            if (atlasW <= 0 || atlasH <= 0) report.Error(recipeRel, "нет 'atlas': { w, h }");
            if (!report.Ok) return report;
            if (padding < 0.1f * pointSize)
                report.Warn(recipeRel, $"padding {padding} < 10% pointSize {pointSize}: толстая обводка/тень упрётся в поле атласа");

            uint[] unicodes = Charset(r, recipeRel, report);
            if (!report.Ok) return report;

            var font = AssetDatabase.LoadAssetAtPath<Font>(source);
            if (font == null) { report.Error(recipeRel, $"исходник не найден или не шрифт: {source}"); return report; }

            TMP_FontAsset fa = Build(font, faceIndex, pointSize, padding, atlasW, atlasH, unicodes, recipeRel, report);
            if (fa == null) return report;

            string expect = Str(r, "expectStyle");
            if (!string.IsNullOrEmpty(expect) && !string.Equals(fa.faceInfo.styleName, expect, StringComparison.OrdinalIgnoreCase))
            {
                // Вариативный файл на faceIndex 0 даёт ДЕФОЛТНЫЙ инстанс, а не тот, что просили.
                report.Error(recipeRel, $"выпеклось начертание '{fa.faceInfo.styleName}', ожидалось '{expect}' (faceIndex {faceIndex}). Вариативный шрифт: см. unity-gotchas «Вариативный шрифт»");
                return report;
            }

            Save(fa, outPath);
            report.Note($"атлас {outPath}: {fa.faceInfo.familyName} {fa.faceInfo.styleName}, символов {fa.characterTable.Count}, " +
                        $"{atlasW}x{atlasH}, pointSize {pointSize}, padding {padding}, gradientScale {padding + 1}, Static");

            if (r.TryGetValue("materials", out object mats) && mats is List<object> list)
            {
                UiTokens tokens = UiTokens.Load(cfg, report);
                var saved = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(outPath);
                for (int i = 0; i < list.Count; i++)
                    BakeMaterial(saved, list[i] as Dictionary<string, object>, pointSize, padding, tokens, $"{recipeRel}:materials[{i}]", report);
            }

            AssetDatabase.SaveAssets();
            return report;
        }

        static TMP_FontAsset Build(Font font, int faceIndex, int pointSize, int padding, int w, int h, uint[] unicodes, string where, UiBuildReport report)
        {
            // Публичный путь TMP: Dynamic-ассет → добавить символы → заморозить в Static. Окно Font Asset
            // Creator делает то же через internal-члены, которых из чужой сборки не видно.
            if (faceIndex != 0)
            {
                report.Error(where, "faceIndex ≠ 0 через Font-объект недоступен (перегрузка приватная). Положи статичный инстанс начертания отдельным файлом");
                return null;
            }
            TMP_FontAsset fa = TMP_FontAsset.CreateFontAsset(font, pointSize, padding, GlyphRenderMode.SDFAA, w, h, AtlasPopulationMode.Dynamic, false);
            if (fa == null) { report.Error(where, "TMP не загрузил face шрифта (Include Font Data в импортёре?)"); return null; }

            fa.TryAddCharacters(unicodes, out uint[] missing, true);
            if (missing != null && missing.Length > 0)
            {
                // Без мульти-атласа «не влезло» и «нет в шрифте» выглядят одинаково — называем оба варианта.
                var sb = new StringBuilder();
                foreach (uint u in missing) sb.Append(u.ToString("X4")).Append(' ');
                report.Error(where, $"не добавлено символов: {missing.Length} ({sb.ToString().Trim()}). Либо их нет в шрифте, либо атлас {w}x{h} переполнен — подними atlas или опусти pointSize");
                return null;
            }

            fa.atlasPopulationMode = AtlasPopulationMode.Static;   // сеттер в редакторе снимает ссылку на TTF из билда
            return fa;
        }

        static void Save(TMP_FontAsset fa, string outPath)
        {
            string dir = Path.GetDirectoryName(outPath);
            if (!string.IsNullOrEmpty(dir) && !AssetDatabase.IsValidFolder(dir)) Directory.CreateDirectory(dir);

            // Перевыпечка = новый ассет: старые атлас и материал — саб-ассеты прежнего файла, сшивать их
            // по одному дороже, чем пересобрать. GUID ассета от этого меняется — пресеты ниже
            // перевыпекаются этим же вызовом, а префабы пересобирает ui-build.
            if (AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(outPath) != null) AssetDatabase.DeleteAsset(outPath);

            string baseName = Path.GetFileNameWithoutExtension(outPath);
            fa.name = baseName;
            fa.atlasTexture.name = baseName + " Atlas";
            fa.material.name = baseName + " Material";

            AssetDatabase.CreateAsset(fa, outPath);
            AssetDatabase.AddObjectToAsset(fa.atlasTexture, fa);
            AssetDatabase.AddObjectToAsset(fa.material, fa);
            EditorUtility.SetDirty(fa);
            AssetDatabase.SaveAssets();
            AssetDatabase.ImportAsset(outPath);
        }

        static void BakeMaterial(TMP_FontAsset fa, Dictionary<string, object> m, int pointSize, int padding, UiTokens tokens, string where, UiBuildReport report)
        {
            if (m == null) { report.Error(where, "ожидался объект"); return; }
            string outPath = Str(m, "out");
            float atFontSize = Float(m, "atFontSize", 0f);
            if (string.IsNullOrEmpty(outPath) || !outPath.EndsWith(".mat")) { report.Error(where, "нет 'out' (путь к .mat)"); return; }
            if (atFontSize <= 0f) { report.Error(where, "нет 'atFontSize' — px обводки и тени без кегля не переводятся в доли SDF"); return; }

            // px на экране при кегле atFontSize ↔ единица SDF-поля: padding атласа, растянутый до кегля.
            float pxPerUnit = padding * atFontSize / pointSize;

            var mat = new Material(fa.material) { name = Path.GetFileNameWithoutExtension(outPath) };
            mat.shaderKeywords = new string[0];

            // Лицо — первым: оно меняет шейдер, а ключи обводки/тени ставятся уже на итоговом.
            if (m.TryGetValue("face", out object fo))
            {
                if (!(fo is Dictionary<string, object> face)) { report.Error(where + ".face", "ожидался объект { texture, fit }"); return; }
                if (!ApplyFace(mat, face, fa.faceInfo, where + ".face", report)) return;
            }

            if (m.TryGetValue("outline", out object o) && o is Dictionary<string, object> outline)
            {
                if (!tokens.TryColor(Str(outline, "color"), report, where + ".outline", out Color c)) return;
                // FaceDilate = OutlineWidth: кромка лица остаётся на месте, кант уходит целиком НАРУЖУ — и
                // наружу его выносят ОБА слагаемых: дилейт сдвигает кромку на dilate·padding, кант добавляет
                // ещё OW·padding (альфа SDF 0..1 = ±padding вокруг кромки). Отсюда делитель 2: замер TASK-401 —
                // без него «8 px» дали на кадре 16–17 px.
                float width = Float(outline, "px", 0f) / (2f * pxPerUnit);
                mat.SetColor(ShaderUtilities.ID_OutlineColor, c);
                mat.SetFloat(ShaderUtilities.ID_OutlineWidth, width);
                mat.SetFloat(ShaderUtilities.ID_FaceDilate, width);
                mat.EnableKeyword(ShaderUtilities.Keyword_Outline);
            }

            if (m.TryGetValue("underlay", out object u) && u is Dictionary<string, object> underlay)
            {
                if (!tokens.TryColor(Str(underlay, "color"), report, where + ".underlay", out Color c)) return;
                mat.SetColor(ShaderUtilities.ID_UnderlayColor, c);
                mat.SetFloat(ShaderUtilities.ID_UnderlayOffsetX, Float(underlay, "x", 0f) / pxPerUnit);
                mat.SetFloat(ShaderUtilities.ID_UnderlayOffsetY, Float(underlay, "y", 0f) / pxPerUnit);
                mat.SetFloat(ShaderUtilities.ID_UnderlaySoftness, Float(underlay, "softness", 0f));
                // Подложка повторяет силуэт С КАНТОМ: иначе тень выглядывает из-под обводки уже лица.
                mat.SetFloat(ShaderUtilities.ID_UnderlayDilate, mat.GetFloat(ShaderUtilities.ID_FaceDilate));
                mat.EnableKeyword(ShaderUtilities.Keyword_Underlay);
            }

            float sum = mat.GetFloat(ShaderUtilities.ID_FaceDilate) + mat.GetFloat(ShaderUtilities.ID_OutlineWidth);
            if (sum > 1f) report.Warn(where, $"FaceDilate+OutlineWidth = {sum:0.###} > 1: кант шире поля атласа и обрежется. Подними padding выпечки");

            // Пересчёт сам: иначе TMP запишет его в ассет на первом рендере, и плеймод оставит .mat в git status.
            ShaderUtilities.UpdateShaderRatios(mat);

            var existing = AssetDatabase.LoadAssetAtPath<Material>(outPath);
            if (existing != null)
            {
                // Правка на месте: GUID пресета, на который ссылаются префабы и токены, не меняется.
                existing.shader = mat.shader;
                existing.CopyPropertiesFromMaterial(mat);
                existing.shaderKeywords = mat.shaderKeywords;
                EditorUtility.SetDirty(existing);
            }
            else
            {
                AssetDatabase.CreateAsset(mat, outPath);
            }
            report.Note($"материал {outPath}: при {atFontSize:0.#}px единица SDF = {pxPerUnit:0.##}px; " +
                        $"OutlineWidth {mat.GetFloat(ShaderUtilities.ID_OutlineWidth):0.###}, FaceDilate {mat.GetFloat(ShaderUtilities.ID_FaceDilate):0.###}, " +
                        $"Underlay ({mat.GetFloat(ShaderUtilities.ID_UnderlayOffsetX):0.###}, {mat.GetFloat(ShaderUtilities.ID_UnderlayOffsetY):0.###}) " +
                        $"keywords [{string.Join(",", mat.shaderKeywords)}]");
        }

        /// <summary>
        /// Текстура лица (`_FaceTex`) — многостопная заливка глифа: блик, тело, рефлекс. Вершинный
        /// `gradient` стиля даёт только два цвета на квад глифа, и растяжка с бликом в нём не выразима.
        ///
        /// Как ложится. Шейдер берёт UV лица из UV2 меша, а UV2 строит КОМПОНЕНТ по своему
        /// `verticalMapping`. Character — 0..1 по прямоугольнику глифа с паддингом: у «+» и «7» квады разной высоты,
        /// и блик на них оказывается на разной высоте. Line — по высоте строки, (y − descender) /
        /// (ascender − descender), одинаково для всех глифов. Поэтому выпечка калибрует тайлинг/офсет
        /// `_FaceTex` под Line и под метрики ЭТОГО шрифта так, что 0..1 растяжки ложится на полосу `fit`
        /// (`cap` — базовая линия…высота прописных/цифр, `x` — …высота строчных, `line` — вся строка),
        /// а Line на компоненте ставит компилятор, увидев лицо в материале (UiTextStyleApplier).
        /// Вне полосы — Clamp: кромка растяжки, а не её повтор.
        ///
        /// Шейдер — полный «TextMeshPro/Distance Field»: в мобильном SDF текстуры лица нет.
        /// </summary>
        static bool ApplyFace(Material mat, Dictionary<string, object> face, FaceInfo fi, string where, UiBuildReport report)
        {
            string texPath = Str(face, "texture");
            string fit = Str(face, "fit");
            if (string.IsNullOrEmpty(texPath)) { report.Error(where, "нет 'texture' (PNG растяжки в Assets; делает artcut.py ramp)"); return false; }

            float lo, hi;
            switch (fit)
            {
                case "cap": lo = 0f; hi = fi.capLine; break;
                case "x": lo = 0f; hi = fi.meanLine; break;
                case "line": lo = fi.descentLine; hi = fi.ascentLine; break;
                default:
                    report.Error(where, $"fit '{fit ?? "—"}': ожидается 'cap' (цифры, прописные), 'x' (строчные) или 'line' (вся строка) — на какую высоту глифа ляжет растяжка");
                    return false;
            }
            if (hi - lo <= 0f) { report.Error(where, $"у шрифта нет метрики для fit '{fit}' ({lo}…{hi})"); return false; }

            var importer = AssetImporter.GetAtPath(texPath) as TextureImporter;
            if (importer != null && (importer.wrapModeU != TextureWrapMode.Clamp || importer.wrapModeV != TextureWrapMode.Clamp))
            {
                // Repeat кладёт верх растяжки под базовую линию: дилейт и хвосты глифов выходят за 0..1.
                importer.wrapMode = TextureWrapMode.Clamp;
                importer.SaveAndReimport();
                report.Note($"{texPath}: wrapMode → Clamp (растяжка вне полосы не должна повторяться)");
            }
            var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(texPath);
            if (tex == null) { report.Error(where, $"текстура лица не найдена или не текстура: {texPath}"); return false; }

            var shader = Shader.Find(FaceShader);
            if (shader == null) { report.Error(where, $"шейдер '{FaceShader}' не найден (TMP Essentials не импортированы?)"); return false; }
            mat.shader = shader;

            // u = (y − lo) / (hi − lo) при v = (y − descender) / (ascender − descender) ⇒ u = v·tiling + offset.
            float band = hi - lo;
            mat.SetTexture(ShaderUtilities.ID_FaceTex, tex);
            mat.SetTextureScale(ShaderUtilities.ID_FaceTex, new Vector2(1f, (fi.ascentLine - fi.descentLine) / band));
            mat.SetTextureOffset(ShaderUtilities.ID_FaceTex, new Vector2(0f, (fi.descentLine - lo) / band));
            report.Note($"лицо {texPath} ({tex.width}x{tex.height}), fit {fit}: tiling.y {mat.GetTextureScale(ShaderUtilities.ID_FaceTex).y:0.###}, " +
                        $"offset.y {mat.GetTextureOffset(ShaderUtilities.ID_FaceTex).y:0.###} при verticalMapping Line");
            return true;
        }

        const string FaceShader = "TextMeshPro/Distance Field";

        /// <summary>Состав атласа: `ranges` — hex-диапазоны «20-7E» или одиночные «D7», `chars` — строка как есть.</summary>
        static uint[] Charset(Dictionary<string, object> r, string where, UiBuildReport report)
        {
            var set = new SortedSet<uint>();
            if (r.TryGetValue("ranges", out object rv) && rv is List<object> ranges)
            {
                foreach (object o in ranges)
                {
                    string s = o as string;
                    string[] parts = s == null ? new string[0] : s.Split('-');
                    if (parts.Length < 1 || parts.Length > 2
                        || !uint.TryParse(parts[0], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out uint lo))
                    {
                        report.Error(where, $"диапазон '{s}' не разобран (ожидается hex «20-7E» или «D7»)");
                        continue;
                    }
                    uint hi = lo;
                    if (parts.Length == 2 && !uint.TryParse(parts[1], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out hi))
                    {
                        report.Error(where, $"диапазон '{s}' не разобран");
                        continue;
                    }
                    for (uint c = lo; c <= hi; c++) set.Add(c);
                }
            }
            string chars = Str(r, "chars");
            if (!string.IsNullOrEmpty(chars))
                for (int i = 0; i < chars.Length; i++)
                {
                    set.Add((uint)char.ConvertToUtf32(chars, i));
                    if (char.IsHighSurrogate(chars[i])) i++;
                }

            if (set.Count == 0) report.Error(where, "состав атласа пуст: задай 'ranges' и/или 'chars'");
            var arr = new uint[set.Count];
            set.CopyTo(arr);
            return arr;
        }

        static string Str(Dictionary<string, object> m, string key) => m.TryGetValue(key, out object v) ? v as string : null;
        static int Int(Dictionary<string, object> m, string key, int def) => m.TryGetValue(key, out object v) && v is double d ? (int)d : def;
        static float Float(Dictionary<string, object> m, string key, float def) => m.TryGetValue(key, out object v) && v is double d ? (float)d : def;
    }
}
