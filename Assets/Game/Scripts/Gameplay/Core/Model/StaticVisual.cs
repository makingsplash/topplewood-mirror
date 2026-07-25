namespace Gameplay.Core
{
    /// <summary>
    /// Optional render descriptor for one entry of <see cref="LevelData.StaticGeometry"/>, parallel by index
    /// (TASK-031c). Static geometry is invisible by default — it only builds a collider — so pre-TASK-031 levels
    /// never reveal their floors/cradles. A level opts a static block into being drawn by setting
    /// <c>visible:true</c> (+ an optional <c>color</c>) in its JSON. Unity-free: the actual mesh/material lives in
    /// <c>Gameplay.View.StaticGeometryView</c>; the sim signature (<c>LoadStatics(Polygon[]…)</c>) is untouched.
    /// </summary>
    public readonly struct StaticVisual
    {
        /// <summary>When true, <see cref="StaticGeometryPresenter"/> renders this static's outline (default false).</summary>
        public readonly bool Visible;

        /// <summary>Optional "#RRGGBB" fill; null/empty → the view's default block colour.</summary>
        public readonly string Color;

        public StaticVisual(bool visible, string color)
        {
            Visible = visible;
            Color = color;
        }
    }
}
