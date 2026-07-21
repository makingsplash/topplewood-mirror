namespace Gameplay.Core.Adapters
{
    /// <summary>Creates a view for a block definition. Unity impl instantiates the block GameObject.</summary>
    public interface IBlockViewFactory
    {
        IBlockView Create(BlockDef def);
    }
}
