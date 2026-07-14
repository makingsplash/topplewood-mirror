using UnityEngine;

namespace SGS.UnityExtensions
{
    public static class ColorExtensions
    {
        public static Color SetAlpha(this Color value, float alpha)
        {
            Color color = new Color(value.r, value.g, value.b, alpha);
            return color;
        }
    }
}