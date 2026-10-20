namespace Gameplay.Core
{
    /// <summary>
    /// TASK-645: кто кого перекрывает — ОДИН источник для картинки и для тапа. Рисует его <c>PieceDepth</c> (TASK-631:
    /// меньший ранг — ближе к камере), судит тап <see cref="PieceHitResolver"/> (среди деталей, содержащих точку,
    /// побеждает меньший ранг — та, что видна сверху). Две копии правила разошлись бы ровно так, как до TASK-645:
    /// сверху рисовалась одна деталь, а тап уходил другой (ближайшей по центру).
    ///
    /// Два слоя (TASK-631): любая активная деталь (не-Idle) выше любой висящей. Внутри слоя — по индексу размещения.
    /// TASK-649: слой тоже живёт здесь, а не в <c>PieceDepth</c>, — тапу он нужен так же, как картинке: падающая или
    /// лежащая деталь сверху ловит тап, висящая под ней его не получает.
    /// </summary>
    public static class PieceStacking
    {
        /// <summary>Сколько индексов размещения различается по глубине. Деталям с индексом ≥ Slots достаётся последний
        /// ранг: между ними порядок снова не определён — и в картинке, и в тапе (там решает ближайший центр).</summary>
        public const int Slots = 50;

        /// <summary>Ранг детали по её индексу размещения ВНУТРИ слоя: меньше — выше (ближе к камере).</summary>
        public static int Rank(int placementIndex)
        {
            if (placementIndex < 0)
                return 0;
            return placementIndex < Slots ? placementIndex : Slots - 1;
        }

        /// <summary>Слой активных (Falling, Placed, Lost, preplaced) — выше любого висящего (TASK-631).</summary>
        public static bool IsActiveLayer(PieceLifecycle state) => state != PieceLifecycle.Idle;

        /// <summary>Сквозной ранг по обоим слоям: любой активный меньше любого висящего, внутри слоя —
        /// <see cref="Rank(int)"/>. По нему судит тап (TASK-649).</summary>
        public static int Rank(PieceLifecycle state, int placementIndex)
        {
            return (IsActiveLayer(state) ? 0 : Slots) + Rank(placementIndex);
        }
    }
}
