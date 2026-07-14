namespace Tools.Constants
{
    public enum NavMeshArea
    {
        Walkable = 0,
        NonWalkable = 1,
    }
    
    public enum NavMeshAreaMask
    {
        Walkable = 1 << NavMeshArea.Walkable,
        NonWalkable = 1 << NavMeshArea.NonWalkable,
    }
    
}
