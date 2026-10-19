using UnityEngine;

namespace Gameplay.View
{
    /// <summary>One hole in a <see cref="SpotlightDim"/> (TASK-613, T1): a polygon in the dim's canvas-local units, the
    /// clear margin around it (<see cref="Pad"/>) and the soft edge beyond that margin (<see cref="Feather"/>, alpha
    /// rising from 0 to the dim's). Any simple polygon of either winding. Corners are mitred, and corners sharper than
    /// the dim's miter limit are cut square (<c>Geometry.OffsetOutward</c>) — a rectangle keeps square corners.</summary>
    public readonly struct SpotlightCutout
    {
        public readonly Vector2[] Polygon;
        public readonly float Pad;
        public readonly float Feather;

        public SpotlightCutout(Vector2[] polygon, float pad, float feather)
        {
            Polygon = polygon;
            Pad = pad;
            Feather = feather;
        }
    }
}
