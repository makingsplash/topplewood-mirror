using Gameplay.Core;

namespace Gameplay.View
{
    /// <summary>
    /// TASK-631: глубина детали по стейту. Плейтест владельца: падающая деталь проходила ПОД висящими. Детали непрозрачны
    /// и все стояли в одной плоскости z = 0, поэтому кто кого перекрывает решал порядок отрисовки — монетка.
    ///
    /// Правило: любая не-Idle деталь (Falling, Placed, Lost, preplaced) БЛИЖЕ к камере, чем любая Idle (камера смотрит
    /// вдоль +Z, меньше z — ближе). Внутри слоя порядок устойчив — по индексу размещения в уровне: меньший индекс ближе.
    /// Он не меняется за всю жизнь детали, поэтому смена стейта — один прыжок между слоями, без мерцания. Ранг внутри
    /// слоя — <see cref="PieceStacking.Rank"/>: тот же, по которому тап выбирает видимую сверху деталь (TASK-645).
    ///
    /// Числа держат цепочку глубин, на которую опираются соседи (всё — расстояние от камеры):
    /// активные [0, 0.01) &lt; ждущие [0.03, 0.04) &lt; пунктир ждущих (+0.05, <see cref="IdleDashOutline.ZBehind"/>)
    /// &lt; тени (+0.15, <see cref="PieceShadow"/>) &lt; статика (0.5). Тень любой детали остаётся за любой деталью.
    /// </summary>
    public static class PieceDepth
    {
        /// <summary>Слой активных: та же плоскость z = 0, на которой детали жили до TASK-631.</summary>
        public const float ActiveBase = 0f;

        /// <summary>Слой ждущих. Зазор 0.02 до конца слоя активных больше шага 16-битного буфера глубины на камере
        /// сцены (far 1000 → ≈0.015), чтобы правило между слоями держалось и на слабом буфере.</summary>
        public const float IdleBase = 0.03f;

        /// <summary>Шаг между соседними индексами внутри слоя.</summary>
        public const float Step = 0.0002f;

        /// <summary>Сколько индексов различается внутри слоя: <c>Slots·Step</c> = 0.01 — ширина слоя. Деталям с
        /// индексом ≥ Slots достаётся последняя ступень (порядок между ними снова не определён, слой — по-прежнему).</summary>
        public const int Slots = PieceStacking.Slots;

        public static float For(PieceLifecycle state, int ordinal)
        {
            var rank = PieceStacking.Rank(ordinal);
            return (state == PieceLifecycle.Idle ? IdleBase : ActiveBase) + rank * Step;
        }
    }
}
