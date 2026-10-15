using System.Collections.Generic;
using Gameplay.Core;
using UnityEngine;

namespace Gameplay.View
{
    /// <summary>
    /// TASK-567: ЕДИНСТВЕННАЯ точка, где рождается видимая статика уровня. До неё один и тот же цикл «видимый блок →
    /// <see cref="StaticGeometryView"/>» жил в трёх копиях: игра (<see cref="StaticGeometryPresenter"/>), тула кадров
    /// (<c>WoodLevelCapture</c>, через неё идут <c>render-level.sh</c> и вкладка «Кривая сложности») и песочница
    /// (<c>WoodCatalogSandbox</c>). Слияние, внесённое только в одну копию, дало бы кадры, которые врут о том, что
    /// видит игрок.
    /// <para>
    /// Порядок: группы слияния (<see cref="StaticMassUnion.Group"/>) рисуются одной массой
    /// (<see cref="StaticGeometryView.InitializeMass"/>), каждый одиночный блок и каждая лента — прежним
    /// <see cref="StaticGeometryView.Initialize(Polygon, string)"/>, то есть уровень без групп рисуется побайтно как
    /// раньше. Масса, которую нечем нарисовать (нет RGBAHalf), откатывается на поблочный рендер — со швами, но целиком.
    /// Объекты создаются по минимальному индексу группы; одиночка сохраняет имя <c>{prefix}_{i}</c>, масса получает
    /// <c>{prefix}_mass_{i0}_{i1}…</c>.
    /// </para>
    /// Сим и коллайдеры спаунер не видит: он читает <see cref="LevelData"/> и ничего в нём не меняет.
    /// </summary>
    public static class StaticGeometrySpawner
    {
        /// <summary>Что создано: объект, его вью, цвет и индексы статик уровня, которые он рисует.</summary>
        public readonly struct Spawned
        {
            public readonly GameObject Go;
            public readonly StaticGeometryView View;
            public readonly string ColorHex;
            public readonly int[] Members;

            public Spawned(GameObject go, StaticGeometryView view, string colorHex, int[] members)
            {
                Go = go;
                View = view;
                ColorHex = colorHex;
                Members = members;
            }
        }

        public static List<Spawned> Spawn(LevelData level, string namePrefix)
        {
            var result = new List<Spawned>();
            var statics = level?.StaticGeometry;
            var visuals = level?.StaticVisuals;
            if (statics == null || visuals == null)
                return result;

            var count = Mathf.Min(statics.Length, visuals.Count);
            var groups = StaticMassUnion.Group(statics, visuals);
            var groupOf = new int[count];
            for (var i = 0; i < count; i++)
                groupOf[i] = -1;
            for (var g = 0; g < groups.Length; g++)
                foreach (var member in groups[g])
                    groupOf[member] = g;

            for (var i = 0; i < count; i++)
            {
                var g = groupOf[i];
                if (g >= 0)
                {
                    // Группа рисуется на своём минимальном индексе; остальные её члены пропускаются.
                    if (groups[g][0] == i)
                        SpawnGroup(statics, visuals, groups[g], namePrefix, result);
                    continue;
                }
                SpawnSingle(statics, visuals, i, namePrefix, result);
            }
            return result;
        }

        private static void SpawnGroup(Polygon[] statics, IReadOnlyList<StaticVisual> visuals, int[] group,
            string namePrefix, List<Spawned> result)
        {
            var members = new Polygon[group.Length];
            for (var k = 0; k < group.Length; k++)
                members[k] = statics[group[k]];
            var color = visuals[group[0]].Color;

            var go = new GameObject($"{namePrefix}_mass_{string.Join("_", group)}");
            var view = go.AddComponent<StaticGeometryView>();
            if (view.InitializeMass(members, color))
            {
                result.Add(new Spawned(go, view, color, group));
                return;
            }

            DestroySafe(go);
            foreach (var i in group)
                SpawnSingle(statics, visuals, i, namePrefix, result);
        }

        private static void SpawnSingle(Polygon[] statics, IReadOnlyList<StaticVisual> visuals, int i,
            string namePrefix, List<Spawned> result)
        {
            if (!visuals[i].Visible)
                return;

            // TASK-325: a SURFACE (open edge-chain chute) is a valid 2-point polyline; a filled block needs ≥3.
            var poly = statics[i];
            var outline = poly?.Outline;
            var minVerts = poly != null && poly.IsSurface ? 2 : 3;
            if (outline == null || outline.Length < minVerts)
                return;

            var go = new GameObject($"{namePrefix}_{i}");
            var view = go.AddComponent<StaticGeometryView>();
            view.Initialize(poly, visuals[i].Color);
            result.Add(new Spawned(go, view, visuals[i].Color, new[] { i }));
        }

        // Вне Play (кадры редактора, песочница в Edit) Destroy запрещён — там только DestroyImmediate.
        private static void DestroySafe(GameObject go)
        {
            if (Application.isPlaying)
                Object.Destroy(go);
            else
                Object.DestroyImmediate(go);
        }
    }
}
