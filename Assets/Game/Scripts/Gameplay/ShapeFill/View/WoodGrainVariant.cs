using System;

namespace Gameplay.View
{
    /// <summary>
    /// Один вариант волокна — «другая доска» (TASK-559). Владелец: «у нас у фигур текстура дерева везде +-
    /// одинаковая, давай сделаем несколько разных»; по листу образцов взял все пять (V1 прямослой, V2 волнистый,
    /// V3 плотный, V4 редкий, V5 косой). Вариант — это ЧИСЛА шейдера <c>Game/Wood Block</c>, а не текстура и не
    /// материал: строка таблицы <c>_GrainVariants</c>, которую деталь выбирает своим инстанс-полем <c>_GrainPick</c>.
    ///
    /// Здесь только характер линий: шаг, волна, резкость, разброс, сила, рельеф, шероховатость, угол. Масштаб,
    /// фаска, контур, свет и цвет в варианте НЕ меняются — это язык материала, а не рисунок доски
    /// (STYLE_LOCK v2 §material). Силу держать в коридоре 0.075–0.10, иначе стиль уровня рассыпается.
    /// </summary>
    [Serializable]
    public sealed class WoodGrainVariant
    {
        public string Name;

        public float GrainScale;
        public float GrainWarp;
        public float GrainSharp;
        public float GrainVary;
        public float GrainStrength;
        public float GrainEmboss;
        public float GrainNoise;

        /// <summary>Угол волокна (град.) поверх разворота вдоль длинной стороны. Не ноль — «косой» вариант: знак
        /// берётся от сида вида (<see cref="WoodGrainAssignment"/>), поэтому в таблице хранится модуль. Потолок
        /// ±15° — амендмент STYLE_LOCK от 2026-10-15.</summary>
        public float GrainAngle;
    }
}
