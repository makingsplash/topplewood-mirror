using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using Unity.Pipeline.Commands;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace Game.Editor.Pipeline
{
    /// <summary>
    /// GENERIC (проекто-независимые) команды студии поверх пакета com.unity.pipeline (TASK-028) — ТОЛЬКО то,
    /// чего в пакете нет; всё остальное берём у пакета как есть (editor_status/recompile/console/
    /// editor_play/editor_stop/find_gameobjects/get_scene_hierarchy/eval/run_tests/menu …):
    ///  • ui_click    — укол pointer-click по uGUI-объекту (simulate_pointer пакета живёт только в Player-рантайме);
    ///  • image_stats — вердикт «пустой кадр» по записанному PNG (кадр приёмки, референс, рендер компилятора UI):
    ///                  сам кадр снимает `capture_game_view --source screen` пакета (композит с Overlay-UI из
    ///                  рендер-таргета Game view), обёртка — .studio/tools/capture.sh (пишет PNG по любому пути и зовёт
    ///                  image_stats). Своего захвата экрана здесь НЕТ намеренно: ScreenCapture в расфокусированном
    ///                  редакторе снимает «какое окно отрендерилось» (Scene view вместо Game view — проверено в TASK-028).
    /// Ноль имён игры — слой studio-template. Продуктовые команды (например `<игра>_tap_ui`) живут рядом с игрой
    /// и лишь зовут <see cref="UiEventInjector"/>. Ошибки — исключениями: пакет заворачивает их в success:false.
    /// </summary>
    public static class StudioPipelineCommands
    {
        // ─────────────────────────────────────────── ui_click ───────────────────────────────────────────

        [CliCommand("ui_click", "Pointer-click по uGUI-объекту (путь иерархии или уникальное имя) через ExecuteEvents — настоящий путь Button.onClick (respect'ит interactable). Play Mode.", Tags = new[] { "studio/ui" })]
        public static object UiClick(
            [CliArg("target", "Путь в иерархии (Canvas/Panel/Button) или уникальное имя активного GameObject", Required = true)] string target,
            [CliArg("count", "Сколько кликов в одном кадре (2 = путь двойного тапа)")] int count = 1)
        {
            if (string.IsNullOrWhiteSpace(target)) throw new ArgumentException("param 'target' обязателен");
            var go = GameObject.Find(target);
            if (go == null) throw new ArgumentException($"GameObject не найден (активный, по пути/имени): '{target}'");

            var button = go.GetComponent<Button>();
            var interactable = go.activeInHierarchy && (button == null || button.interactable);
            var clicks = Math.Max(1, count);
            var handled = false;
            for (var i = 0; i < clicks; i++) handled |= UiEventInjector.ClickObject(go);

            return new
            {
                ok = handled,           // какой-то IPointerClickHandler получил событие (цель или её родитель)
                target,
                path = HierarchyPath(go.transform),
                hasButton = button != null,
                interactable,           // false ⇒ Button проглотил клик no-op'ом — ровно как под пальцем
                count = clicks,
                isPlaying = EditorApplication.isPlaying,
            };
        }

        private static string HierarchyPath(Transform t)
        {
            var path = t.name;
            for (var p = t.parent; p != null; p = p.parent) path = p.name + "/" + path;
            return path;
        }

        // ───────────────────────────────────────── image_stats ─────────────────────────────────────────

        [CliCommand("image_stats", "Вердикт по УЖЕ записанному PNG: blank (однотонный кадр), distinctColors (сетка 32×32, 4 бита/канал), доминирующий цвет, md5, размер. Детерминированно, без гонки с рендером.", Tags = new[] { "studio/capture" })]
        public static object ImageStats(
            [CliArg("path", "PNG для анализа: абсолютный путь или относительно корня проекта", Required = true)] string path)
        {
            if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("param 'path' обязателен");
            var absPath = ResolvePath(path);
            if (!File.Exists(absPath)) throw new FileNotFoundException($"файл не найден: {absPath}");

            var bytes = File.ReadAllBytes(absPath);
            var tex = new Texture2D(2, 2);
            try
            {
                if (!ImageConversion.LoadImage(tex, bytes))
                    throw new InvalidDataException($"не декодируется как PNG/JPG: {absPath} ({bytes.Length} байт)");
                var stats = Stats(tex, bytes);
                return new
                {
                    path = absPath,
                    stats.width,
                    stats.height,
                    stats.blank,
                    stats.distinctColors,
                    stats.uniformColorHex,
                    stats.md5,
                    stats.bytes,
                };
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(tex);
            }
        }

        // ─────────────────────────────────────────── общее ───────────────────────────────────────────

        private static string ResolvePath(string rawPath)
        {
            var projectRoot = Directory.GetParent(Application.dataPath)!.FullName;
            return Path.IsPathRooted(rawPath) ? rawPath : Path.GetFullPath(Path.Combine(projectRoot, rawPath));
        }

        private readonly struct ImageStatsResult
        {
            public readonly int width;
            public readonly int height;
            public readonly bool blank;
            public readonly int distinctColors;
            public readonly string uniformColorHex;
            public readonly string md5;
            public readonly int bytes;

            public ImageStatsResult(int width, int height, bool blank, int distinctColors, string uniformColorHex, string md5, int bytes)
            {
                this.width = width; this.height = height; this.blank = blank; this.distinctColors = distinctColors;
                this.uniformColorHex = uniformColorHex; this.md5 = md5; this.bytes = bytes;
            }
        }

        // Сетка 32×32 сэмплов, квантование 4 бита/канал → корзины цветов. Однотонный кадр (пустая приёмка,
        // неотрендеренный Game view, зависший бут) схлопывается в одну корзину ⇒ blank=true.
        private static ImageStatsResult Stats(Texture2D tex, byte[] png)
        {
            string md5;
            using (var hasher = MD5.Create())
                md5 = BitConverter.ToString(hasher.ComputeHash(png)).Replace("-", "").ToLowerInvariant();

            int width = tex.width, height = tex.height;
            const int gridSteps = 32;
            var counts = new Dictionary<int, int>();
            for (var gy = 0; gy < gridSteps; gy++)
            for (var gx = 0; gx < gridSteps; gx++)
            {
                var px = Mathf.Clamp((int)((gx + 0.5f) / gridSteps * width), 0, width - 1);
                var py = Mathf.Clamp((int)((gy + 0.5f) / gridSteps * height), 0, height - 1);
                var key = QuantizeColor(tex.GetPixel(px, py));
                counts.TryGetValue(key, out var n);
                counts[key] = n + 1;
            }

            int dominantKey = 0, best = -1;
            foreach (var kv in counts)
                if (kv.Value > best) { best = kv.Value; dominantKey = kv.Key; }

            return new ImageStatsResult(width, height, counts.Count <= 1, counts.Count, ColorKeyToHex(dominantKey), md5, png.Length);
        }

        // Квантование цвета до 4 бит/канал → 12-битный ключ: почти одинаковые пиксели делят корзину.
        private static int QuantizeColor(Color c)
        {
            var r = Mathf.Clamp((int)(c.r * 15f + 0.5f), 0, 15);
            var g = Mathf.Clamp((int)(c.g * 15f + 0.5f), 0, 15);
            var b = Mathf.Clamp((int)(c.b * 15f + 0.5f), 0, 15);
            return (r << 8) | (g << 4) | b;
        }

        private static string ColorKeyToHex(int key)
        {
            var r = ((key >> 8) & 0xF) * 17; // 0..15 → 0..255
            var g = ((key >> 4) & 0xF) * 17;
            var b = (key & 0xF) * 17;
            return $"#{r:X2}{g:X2}{b:X2}";
        }
    }
}
