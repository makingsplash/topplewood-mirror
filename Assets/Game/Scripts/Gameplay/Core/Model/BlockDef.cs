namespace Gameplay.Core
{
    /// <summary>
    /// Authoring definition of a droppable block within a level pool (GAME_RULES §1).
    /// <see cref="Color"/> is v0 cosmetic (win rule is colour-agnostic; "colour as match" is an A/B).
    /// </summary>
    public sealed class BlockDef
    {
        public string Kind { get; }
        public Shape Shape { get; }
        public MaterialProfile Material { get; }
        public string Color { get; }
        public int Count { get; }

        public BlockDef(string kind, Shape shape, MaterialProfile material, string color = null, int count = 1)
        {
            Kind = kind;
            Shape = shape;
            Material = material;
            Color = color;
            Count = count < 1 ? 1 : count;
        }
    }
}
