using System.Collections.Generic;
using System.IO;
using System.Linq;
using Gameplay.Core;
using Gameplay.View;
using Newtonsoft.Json;
using UnityEditor;
using UnityEngine;

namespace Game.Editor
{
    /// <summary>
    /// Раскрашивает уровни: <c>Tools → Wood Style → Раскрасить уровни</c>.
    ///
    /// ПРАВИЛО ОДНО: соседние детали обязаны ОТБИВАТЬСЯ друг от друга. Владелец:
    /// «по конструктору не делай похожие цвета рядом, гораздо круче типа как красный и синий,
    /// зелёный и фиолетовый — чтобы чётко всё отбивалось, но сочеталось».
    ///
    /// Поэтому цвет раздаётся НЕ по кругу Иттена и НЕ по массе, а по СОСЕДСТВУ в собранной фигуре:
    ///  1. строим граф касаний — какие виды деталей стоят вплотную в целевой фигуре;
    ///  2. идём от самого «связного» вида к самому одинокому;
    ///  3. каждому даём цвет из готовой сочной палитры, максимально далёкий по тону от УЖЕ покрашенных
    ///     соседей. Красный рядом с синим, зелёный рядом с фиолетовым.
    ///
    /// Две прошлые попытки были мимо, обе забракованы глазами:
    ///  * равношаговые схемы (гексада 60°, пентада 72°) давали радугу — шесть равноправных тонов это
    ///    отсутствие гармонии;
    ///  * аналоговые массы «одной семьёй» — это буквально похожие цвета рядом, то есть ровно то,
    ///    чего делать нельзя.
    ///
    /// ОКРУЖЕНИЕ здесь не участвует: пол, стены и колонны красит один общий тёмный цвет
    /// (<see cref="WoodTuning.EnvironmentColor"/>). Сцена не должна спорить с фигурой.
    ///
    /// ПОЧЕМУ НЕ В ДАННЫЕ УРОВНЕЙ. Схемы пишутся в <c>wood_tuning.json</c>, во вью: цвет — часть
    /// рескина, авторские данные кампании им переписывать нельзя. Выключил <c>IttenPalettes</c> —
    /// вернулись цвета уровня, и ни один уровень при этом не тронут.
    /// </summary>
    public static class WoodIttenPalette
    {
        private const string LevelsPath = "Assets/StreamingAssets/shapefill_levels.json";

        /// <summary>Зазор, при котором две детали считаются соседями (мировые единицы).</summary>
        private const float TouchEpsilon = 0.06f;

        /// <summary>
        /// ПАЛИТРА ВЛАДЕЛЬЦА — та самая, что лежит в <c>wood_tuning.json</c> и настроена им вручную.
        ///
        /// Три моих попытки подобрать цвет самому провалились одна за другой: HSV-генерация дала неон
        /// (S 0.88 при V 0.92), аналоговые массы — «похожие цвета рядом», подобранная мной звонкая
        /// девятка — «слишком ядрёные, не художественно». Вывод очевиден: цвета брать не мои,
        /// а его — они уже проверены глазом и держатся в узком характере (S 0.43..0.89 при V 0.54..0.80).
        ///
        /// Коричневый #8A5A3B из списка исключён намеренно: он почти совпадает с окружением
        /// (<see cref="WoodTuning.EnvironmentColor"/>), и деталь такого цвета терялась бы на полу.
        /// Кремовый #DFC694 тоже — он светлее стены и читается как подсветка, а не как деталь.
        /// Красный #BA3C32 тоже (владелец 2026-10-09, TASK-391): на ореховой стене он сливается —
        /// ΔE ≈ 21 к стене против 43..84 у остальных тонов; яркостный контраст (WCAG) тут не мера,
        /// он у ВСЕХ тонов палитры против стены ниже 2.4. Мера — <c>docs/design/level-authoring.md</c> §D6а.
        /// Оранжевый #C7681D тоже (владелец 2026-10-09, TASK-392): к стене он ещё ближе красного —
        /// ΔE 16.3 к #A45322 против 21.6 у красного.
        ///
        /// Тон рядом с hex — для расчёта расстояний, посчитан из самого hex.
        /// </summary>
        private static readonly (string Hex, float Hue)[] Palette =
        {
            ("#CBA016", 44f),   // жёлтый
            ("#5C8E3E", 93f),   // зелёный
            ("#2F8C86", 176f),  // бирюзовый
            ("#48699A", 214f),  // синий
            ("#5A55A3", 244f),  // индиго
            ("#8B5A9E", 289f),  // слива
            ("#B8577A", 340f),  // розово-терракотовый
        };

        [MenuItem("Tools/Wood Style/Раскрасить уровни")]
        public static void Generate()
        {
            if (!File.Exists(LevelsPath))
            {
                Debug.LogError($"[wood] нет файла уровней: {LevelsPath}");
                return;
            }

            var file = JsonConvert.DeserializeObject<CampaignFileDto>(File.ReadAllText(LevelsPath));
            if (file?.Levels == null || file.Levels.Count == 0)
            {
                Debug.LogError("[wood] кампания не разобралась");
                return;
            }

            var tuning = WoodTuning.Current;
            tuning.LevelPalettes = new List<WoodLevelPalette>();
            tuning.IttenPalettes = true;

            var worst = 360f;
            var worstAt = "—";
            for (var index = 0; index < file.Levels.Count; index++)
            {
                var level = file.Levels[index].ToDomain();
                var palette = BuildFor(level, index, out var minNeighbourGap);
                if (palette == null)
                    continue;
                tuning.LevelPalettes.Add(palette);
                if (minNeighbourGap < worst)
                {
                    worst = minNeighbourGap;
                    worstAt = level.LevelId;
                }
            }

            var path = tuning.Save();
            tuning.Apply(rebuildMeshes: false);

            // Печатаем ХУДШУЮ пару по всей кампании: это единственная метрика, по которой видно,
            // выполнено требование «отбивается» или нет. Меньше 90° — где-то соседи слиплись.
            Debug.Log($"[wood] раскрашено уровней: {tuning.LevelPalettes.Count} → {path}. " +
                      $"Худшая пара соседей: {worst:0}° ({worstAt})");
        }

        // ------------------------------------------------------------- один уровень

        private static WoodLevelPalette BuildFor(LevelData level, int levelIndex, out float minNeighbourGap)
        {
            minNeighbourGap = 360f;

            var shapes = new Dictionary<string, Shape>();
            if (level.Pieces != null)
            {
                foreach (var placement in level.Pieces)
                {
                    var type = placement.Type;
                    if (type?.Shape != null && !shapes.ContainsKey(type.Kind))
                        shapes[type.Kind] = type.Shape;
                }
            }
            if (shapes.Count == 0)
                return null;

            var masses = Masses(level, shapes);
            var neighbours = Neighbours(level, shapes);

            // Порядок обхода: сперва самые «связные» виды — у них больше всего ограничений, и если
            // красить их последними, свободных далёких тонов уже не остаётся. При равной связности
            // вперёд идёт масса: крупное заметнее, ему важнее попасть в хороший цвет.
            var order = shapes.Keys
                .OrderByDescending(k => neighbours.TryGetValue(k, out var n) ? n.Count : 0)
                .ThenByDescending(k => masses.TryGetValue(k, out var m) ? m : 0f)
                .ToList();

            // Стартовый цвет крутим по уровню, иначе вся кампания начиналась бы с одного цвета.
            var startShift = levelIndex * 4;

            var chosen = new Dictionary<string, int>();
            var used = new HashSet<int>();
            foreach (var kind in order)
            {
                // ПОКА ЕСТЬ НЕИСПОЛЬЗОВАННЫЕ ЦВЕТА — выбираем только из них. Без этого правила
                // уровень схлопывался в ДВА цвета через один: комплемент (180°) всегда даёт максимум
                // расстояния до соседа, поэтому чередовать одну пару выгоднее, чем брать свежий тон.
                // Формально «не похожие рядом» при этом выполнено идеально, а художественно бедно —
                // grand_seesaw вышел весь зелёно-сливовый. Разнообразие важнее лишних градусов.
                var freeLeft = used.Count < Palette.Length;

                var best = -1;
                var bestScore = float.MinValue;
                for (var i = 0; i < Palette.Length; i++)
                {
                    var index = (i + startShift) % Palette.Length;
                    if (freeLeft && used.Contains(index))
                        continue;
                    var score = Score(index, kind, neighbours, chosen);
                    if (score <= bestScore)
                        continue;
                    bestScore = score;
                    best = index;
                }

                if (best < 0)
                    best = 0;
                chosen[kind] = best;
                used.Add(best);
            }

            var palette = new WoodLevelPalette { LevelId = level.LevelId };
            foreach (var kind in order)
            {
                palette.Kinds.Add(kind);
                palette.Colors.Add(Palette[chosen[kind]].Hex);
            }

            // Замер: насколько худшая пара СОСЕДЕЙ разошлась по тону. Это и есть проверка требования.
            foreach (var pair in neighbours)
            {
                foreach (var other in pair.Value)
                {
                    if (!chosen.ContainsKey(pair.Key) || !chosen.ContainsKey(other))
                        continue;
                    var d = HueDistance(Palette[chosen[pair.Key]].Hue, Palette[chosen[other]].Hue);
                    if (d < minNeighbourGap)
                        minNeighbourGap = d;
                }
            }
            if (minNeighbourGap > 359f)
                minNeighbourGap = 360f;

            Debug.Log($"[wood] {level.LevelId}: {order.Count} видов, худшая пара соседей " +
                      $"{(minNeighbourGap > 359f ? "нет соседей" : $"{minNeighbourGap:0}°")} — " +
                      string.Join(", ", order.Select(k => $"{k}={Palette[chosen[k]].Hex}")));
            return palette;
        }

        /// <summary>
        /// Насколько хорош этот цвет для вида. Главное слагаемое — расстояние по тону до уже
        /// покрашенных СОСЕДЕЙ (их нельзя спутать), с большим весом. Второе — до всех остальных
        /// покрашенных: при прочих равных лучше не повторять цвет, уже занятый в уровне.
        /// </summary>
        private static float Score(int candidate, string kind,
            Dictionary<string, List<string>> neighbours, Dictionary<string, int> chosen)
        {
            var hue = Palette[candidate].Hue;

            var neighbourScore = 360f;
            if (neighbours.TryGetValue(kind, out var list))
            {
                foreach (var other in list)
                {
                    if (!chosen.TryGetValue(other, out var idx))
                        continue;
                    var d = HueDistance(hue, Palette[idx].Hue);
                    if (d < neighbourScore)
                        neighbourScore = d;
                }
            }

            var globalScore = 360f;
            foreach (var pair in chosen)
            {
                var d = HueDistance(hue, Palette[pair.Value].Hue);
                if (d < globalScore)
                    globalScore = d;
            }

            return neighbourScore * 4f + globalScore;
        }

        private static float HueDistance(float a, float b)
        {
            var d = Mathf.Abs(a - b) % 360f;
            return d > 180f ? 360f - d : d;
        }

        // ------------------------------------------------------------- геометрия

        /// <summary>
        /// Кто с кем соприкасается в СОБРАННОЙ фигуре. Именно в ней: игрок судит о цвете по
        /// финальной картинке, а на старте детали висят порознь и соседей там нет вовсе.
        /// </summary>
        private static Dictionary<string, List<string>> Neighbours(LevelData level, Dictionary<string, Shape> shapes)
        {
            var result = new Dictionary<string, List<string>>();
            var slots = level.Target?.Slots;
            if (slots == null)
                return result;

            var posed = new List<(string kind, Vector2[] poly)>();
            foreach (var slot in slots)
            {
                if (!shapes.TryGetValue(slot.Kind, out var shape))
                    continue;
                posed.Add((slot.Kind, Pose(shape, slot.Position, slot.Rotation)));
            }

            for (var i = 0; i < posed.Count; i++)
            {
                for (var j = i + 1; j < posed.Count; j++)
                {
                    if (posed[i].kind == posed[j].kind)
                        continue;
                    if (Separation(posed[i].poly, posed[j].poly) > TouchEpsilon)
                        continue;
                    Link(result, posed[i].kind, posed[j].kind);
                    Link(result, posed[j].kind, posed[i].kind);
                }
            }
            return result;
        }

        private static void Link(Dictionary<string, List<string>> map, string a, string b)
        {
            if (!map.TryGetValue(a, out var list))
                map[a] = list = new List<string>();
            if (!list.Contains(b))
                list.Add(b);
        }

        private static Vector2[] Pose(Shape shape, Vec2 position, float rotation)
        {
            var c = Mathf.Cos(rotation);
            var s = Mathf.Sin(rotation);
            var v = shape.Vertices;
            var result = new Vector2[v.Length];
            for (var i = 0; i < v.Length; i++)
                result[i] = new Vector2(
                    v[i].X * c - v[i].Y * s + position.X,
                    v[i].X * s + v[i].Y * c + position.Y);
            return result;
        }

        /// <summary>
        /// Зазор между двумя ВЫПУКЛЫМИ многоугольниками по теореме о разделяющей оси: положительное
        /// число — расстояние, отрицательное — глубина пересечения. Годится ровно потому, что все
        /// формы кампании выпуклые (это гарантирует домен: веер-меш и один PolygonCollider2D).
        /// </summary>
        private static float Separation(Vector2[] a, Vector2[] b)
        {
            var best = float.MinValue;
            foreach (var axis in Axes(a).Concat(Axes(b)))
            {
                Project(a, axis, out var a0, out var a1);
                Project(b, axis, out var b0, out var b1);
                var gap = Mathf.Max(b0 - a1, a0 - b1);
                if (gap > best)
                    best = gap;
            }
            return best;
        }

        private static IEnumerable<Vector2> Axes(Vector2[] poly)
        {
            for (var i = 0; i < poly.Length; i++)
            {
                var edge = poly[(i + 1) % poly.Length] - poly[i];
                var len = edge.magnitude;
                if (len > 1e-6f)
                    yield return new Vector2(-edge.y / len, edge.x / len);
            }
        }

        private static void Project(Vector2[] poly, Vector2 axis, out float min, out float max)
        {
            min = float.MaxValue;
            max = float.MinValue;
            foreach (var p in poly)
            {
                var d = Vector2.Dot(p, axis);
                if (d < min) min = d;
                if (d > max) max = d;
            }
        }

        /// <summary>Площадь вида в собранной фигуре — только чтобы разрулить ничью по связности.</summary>
        private static Dictionary<string, float> Masses(LevelData level, Dictionary<string, Shape> shapes)
        {
            var result = new Dictionary<string, float>();
            var slots = level.Target?.Slots;
            if (slots != null)
            {
                foreach (var slot in slots)
                {
                    if (!shapes.TryGetValue(slot.Kind, out var shape))
                        continue;
                    result.TryGetValue(slot.Kind, out var acc);
                    result[slot.Kind] = acc + Area(shape);
                }
            }
            foreach (var kind in shapes.Keys)
                if (!result.ContainsKey(kind))
                    result[kind] = Area(shapes[kind]);
            return result;
        }

        private static float Area(Shape shape)
        {
            var v = shape.Vertices;
            if (v == null || v.Length < 3)
                return 0f;
            var sum = 0f;
            for (var i = 0; i < v.Length; i++)
            {
                var a = v[i];
                var b = v[(i + 1) % v.Length];
                sum += a.X * b.Y - b.X * a.Y;
            }
            return Mathf.Abs(sum) * 0.5f;
        }
    }
}
