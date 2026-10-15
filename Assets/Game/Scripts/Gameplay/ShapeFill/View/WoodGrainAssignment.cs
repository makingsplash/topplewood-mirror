using System.Collections.Generic;
using System.Linq;
using Gameplay.Core;
using UnityEngine;

namespace Gameplay.View
{
    /// <summary>
    /// Какая доска достаётся какой детали уровня (TASK-559, правило — <c>TASK-559/01-art-director.md</c> §3).
    /// Детерминировано от <c>levelId</c>, без рандома в рантайме: тот же уровень всегда выглядит одинаково,
    /// в игре, в каталоге и на кадре приёмки.
    ///
    /// Четыре решения, в таком порядке:
    ///  1. НАБОР ПО УРОВНЮ. Уровню достаются 2–3 варианта из таблицы (<see cref="WoodTuning.GrainVariantsPerLevel"/>),
    ///     выбор — сидом уровня. Все пять в одном кадре — каша; два-три — «одна мастерская, несколько досок».
    ///  2. ВАРИАНТ ПО ВИДУ через граф соседства (<see cref="WoodKindNeighbours"/>, тот же, что у цвета): соседи в
    ///     собранной фигуре получают разные варианты, при нехватке — как можно меньше совпадений. Детали одного
    ///     вида несут один вариант, поэтому пара ног жирафа остаётся парой.
    ///  3. ФАЗА ПО ЭКЗЕМПЛЯРУ. Волокно считается в координатах детали, и без сдвига две детали одной формы несли
    ///     побайтно одинаковый рисунок. Каждой детали — свой сдвиг координаты волокна от (уровень, вид, номер).
    ///  4. СТАТИКА — всегда один спокойный вариант (<see cref="WoodTuning.StaticGrainVariant"/>, прямослой) со своим
    ///     сдвигом: окружение не спорит с фигурой.
    ///
    /// Упаковка ответа — ровно то, что ждёт инстанс-свойство шейдера <c>_GrainPick</c>:
    /// <c>x</c> — номер строки таблицы + 1 (0 = «вариантов нет, общие числа материала»), <c>yz</c> — сдвиг фазы в
    /// опорных единицах волокна, <c>w</c> — знак угла (±1) для косого варианта.
    ///
    /// Чистая логика: от Unity здесь только <see cref="Vector4"/> и лог не пишется — его пишет
    /// <see cref="WoodTuning"/>, у которого есть имена вариантов.
    /// </summary>
    public sealed class WoodGrainAssignment
    {
        private readonly uint _seed;
        private readonly int[] _levelSet;
        private readonly Dictionary<string, int> _variantByKind;
        private readonly int _staticVariant;
        private readonly float _phaseRange;

        public string LevelId { get; }

        /// <summary>Строки таблицы, доставшиеся уровню, в порядке выбора сидом.</summary>
        public IReadOnlyList<int> LevelSet => _levelSet;

        /// <summary>Вид → строка таблицы. Для лога и тестов.</summary>
        public IReadOnlyDictionary<string, int> VariantByKind => _variantByKind;

        /// <summary>Сколько пар СОСЕДЕЙ получили одинаковый вариант. Ноль — требование «соседи различаются»
        /// выполнено; больше нуля бывает, когда видов-соседей больше, чем досок в наборе (клика из 4 при 3).</summary>
        public int SameVariantNeighbourPairs { get; }

        private WoodGrainAssignment(string levelId, uint seed, int[] levelSet, Dictionary<string, int> variantByKind,
            int staticVariant, float phaseRange, int sameVariantNeighbourPairs)
        {
            LevelId = levelId;
            _seed = seed;
            _levelSet = levelSet;
            _variantByKind = variantByKind;
            _staticVariant = staticVariant;
            _phaseRange = phaseRange;
            SameVariantNeighbourPairs = sameVariantNeighbourPairs;
        }

        /// <summary>
        /// Раздать доски уровню. <paramref name="variantCount"/> — строк в таблице; ноль — вариантов нет, и
        /// <see cref="PickFor"/> отдаёт «общие числа» (x = 0). <paramref name="perLevel"/> — сколько досок на
        /// уровень, зажимается в [1, variantCount] и не больше числа видов.
        /// </summary>
        public static WoodGrainAssignment Build(LevelData level, int variantCount, int perLevel, int staticVariant,
            float phaseRange)
        {
            var levelId = level?.LevelId ?? string.Empty;
            var seed = Hash(levelId);
            var variantByKind = new Dictionary<string, int>();

            if (variantCount <= 0)
                return new WoodGrainAssignment(levelId, seed, new int[0], variantByKind, -1, phaseRange, 0);

            var shapes = WoodKindNeighbours.ShapesByKind(level);
            var neighbours = WoodKindNeighbours.Build(level, shapes);
            var masses = WoodKindNeighbours.Masses(level, shapes);

            // 1. Набор по уровню: перемешанный сидом список строк, берём первые perLevel.
            var setSize = Mathf.Clamp(perLevel, 1, variantCount);
            if (shapes.Count > 0)
                setSize = Mathf.Min(setSize, shapes.Count);
            var order = Enumerable.Range(0, variantCount).ToArray();
            var rng = seed;
            for (var i = order.Length - 1; i > 0; i--)
            {
                rng = Next(rng);
                var j = (int)(rng % (uint)(i + 1));
                (order[i], order[j]) = (order[j], order[i]);
            }
            var levelSet = order.Take(setSize).ToArray();

            // 2. Вариант по виду. Обход — как у цвета: сперва самые связные (у них больше ограничений), при
            // равенстве крупные (заметнее), затем по имени — чтобы порядок не зависел от порядка словаря.
            var kinds = shapes.Keys
                .OrderByDescending(k => neighbours.TryGetValue(k, out var n) ? n.Count : 0)
                .ThenByDescending(k => masses.TryGetValue(k, out var m) ? m : 0f)
                .ThenBy(k => k, System.StringComparer.Ordinal)
                .ToList();

            var used = new int[variantCount];
            foreach (var kind in kinds)
            {
                var best = levelSet[0];
                var bestConflicts = int.MaxValue;
                var bestUsed = int.MaxValue;
                foreach (var candidate in levelSet)
                {
                    var conflicts = 0;
                    if (neighbours.TryGetValue(kind, out var list))
                        foreach (var other in list)
                            if (variantByKind.TryGetValue(other, out var v) && v == candidate)
                                conflicts++;

                    // Лексикографически: меньше совпадений с соседями → реже занятый в уровне → раньше в наборе.
                    if (conflicts > bestConflicts || (conflicts == bestConflicts && used[candidate] >= bestUsed))
                        continue;
                    best = candidate;
                    bestConflicts = conflicts;
                    bestUsed = used[candidate];
                }
                variantByKind[kind] = best;
                used[best]++;
            }

            var samePairs = 0;
            foreach (var pair in neighbours)
                foreach (var other in pair.Value)
                    if (string.CompareOrdinal(pair.Key, other) < 0 &&
                        variantByKind.TryGetValue(pair.Key, out var a) &&
                        variantByKind.TryGetValue(other, out var b) && a == b)
                        samePairs++;

            var staticRow = Mathf.Clamp(staticVariant, 0, variantCount - 1);
            return new WoodGrainAssignment(levelId, seed, levelSet, variantByKind, staticRow, phaseRange, samePairs);
        }

        /// <summary>
        /// Инстанс-поле <c>_GrainPick</c> детали вида <paramref name="kind"/> с номером <paramref name="ordinal"/>
        /// (на поле — индекс размещения в уровне, в превью и в собранной позе — индекс слота). Вид, которого в
        /// уровне нет (песочница, чужой уровень), получает доску из набора по хешу вида — без исключения.
        /// </summary>
        public Vector4 PickFor(string kind, int ordinal)
        {
            if (_levelSet.Length == 0)
                return Vector4.zero;

            kind ??= string.Empty;
            if (!_variantByKind.TryGetValue(kind, out var row))
                row = _levelSet[(int)(Mix(_seed, Hash(kind)) % (uint)_levelSet.Length)];

            var h = Mix(Mix(_seed, Hash(kind)), (uint)ordinal);
            // Знак угла — от ВИДА, а не от экземпляра: детали одного вида остаются парой и в косом варианте.
            var sign = (Mix(_seed, Hash(kind)) & 1u) == 0 ? 1f : -1f;
            return Pack(row, h, sign);
        }

        /// <summary>Инстанс-поле статики (пол, стена, колонна, штырь). <paramref name="salt"/> — что угодно,
        /// различающее куски статики одного уровня (ключ формы, номер).</summary>
        public Vector4 PickForStatic(string salt)
        {
            if (_staticVariant < 0)
                return Vector4.zero;
            var h = Mix(Mix(_seed, Hash("static")), Hash(salt ?? string.Empty));
            return Pack(_staticVariant, h, 1f);
        }

        private Vector4 Pack(int row, uint h, float sign)
        {
            var a = Next(h);
            var b = Next(a);
            // 24 бита на ось — float их держит точно, фаза одинакова на всех платформах.
            var px = (a >> 8) / 16777216f * _phaseRange;
            var py = (b >> 8) / 16777216f * _phaseRange;
            return new Vector4(row + 1, px, py, sign);
        }

        // ---------------------------------------------------------------- хеш

        // Свой хеш, а не string.GetHashCode: тот не обязан совпадать между Mono, IL2CPP и запусками, а
        // уровень обязан выглядеть одинаково в редакторе, на кадре приёмки и на телефоне.
        private static uint Hash(string s)
        {
            var h = 2166136261u; // FNV-1a
            foreach (var c in s)
            {
                h ^= c;
                h *= 16777619u;
            }
            return h;
        }

        private static uint Mix(uint a, uint b) => Next(a ^ (b + 0x9E3779B9u + (a << 6) + (a >> 2)));

        private static uint Next(uint x)
        {
            // xorshift32; ноль — неподвижная точка, поэтому подмешиваем константу.
            x ^= 0x2545F491u;
            x ^= x << 13;
            x ^= x >> 17;
            x ^= x << 5;
            return x;
        }
    }
}
