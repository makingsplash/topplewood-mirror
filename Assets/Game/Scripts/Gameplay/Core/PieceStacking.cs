namespace Gameplay.Core
{
    /// <summary>
    /// TASK-645: кто кого перекрывает внутри одного слоя деталей — ОДИН источник для картинки и для тапа. Рисует его
    /// <c>PieceDepth</c> (TASK-631: меньший ранг — ближе к камере), судит тап <see cref="PieceHitResolver"/> (среди деталей,
    /// содержащих точку, побеждает меньший ранг — та, что видна сверху). Две копии правила разошлись бы ровно так, как
    /// до TASK-645: сверху рисовалась одна деталь, а тап уходил другой (ближайшей по центру).
    /// </summary>
    public static class PieceStacking
    {
        /// <summary>Сколько индексов размещения различается по глубине. Деталям с индексом ≥ Slots достаётся последний
        /// ранг: между ними порядок снова не определён — и в картинке, и в тапе (там решает ближайший центр).</summary>
        public const int Slots = 50;

        /// <summary>Ранг детали по её индексу размещения в уровне: меньше — выше (ближе к камере).</summary>
        public static int Rank(int placementIndex)
        {
            if (placementIndex < 0)
                return 0;
            return placementIndex < Slots ? placementIndex : Slots - 1;
        }
    }
}
