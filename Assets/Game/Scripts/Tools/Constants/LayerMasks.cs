namespace Tools.Constants
{
    public static class LayerMasks
    {
        public static readonly int Default = 1;
        public static readonly int Input = 1 << (int)Layer.Input;
        public static readonly int Ui = 1 << (int)Layer.Ui;
        public static readonly int Figure = 1 << (int)Layer.Figure;
        public static readonly int Wall = 1 << (int)Layer.Wall;
        public static readonly int Door = 1 << (int)Layer.Door;
        public static readonly int DoubleFigure = 1 << (int)Layer.DoubleFigure;
        public static readonly int Mask = 1 << (int)Layer.Mask;
        public static readonly int Fx = 1 << (int)Layer.Fx;
        
        public static readonly int GamplayObjects = Figure | Wall | Door;
    }
}
