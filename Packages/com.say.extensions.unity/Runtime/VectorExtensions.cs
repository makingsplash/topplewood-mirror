using UnityEngine;

namespace SGS.UnityExtensions
{
    public static class VectorExtensions
    {
        public static Vector2 SetX(this Vector2 value, float x)
        {
            value.x = x;
            return value;
        }

        public static Vector2 SetY(this Vector2 value, float y)
        {
            value.y = y;
            return value;
        }
        public static Vector3 SetX(this Vector3 value, float x)
        {
            value.x = x;
            return value;
        }

        public static Vector3 SetY(this Vector3 value, float y)
        {
            value.y = y;
            return value;
        }

        public static Vector3 SetZ(this Vector3 value, float z)
        {
            value.z = z;
            return value;
        }
        
        public static Vector2 AddX(this Vector2 v, float f) => new Vector2(v.x + f, v.y);
        public static Vector2 AddY(this Vector2 v, float f) => new Vector2(v.x, v.y + f);

        public static Vector3 AddX(this Vector3 v, float f) => new Vector3(v.x + f, v.y, v.z);
        public static Vector3 AddY(this Vector3 v, float f) => new Vector3(v.x, v.y + f, v.z);
        public static Vector3 AddZ(this Vector3 v, float f) => new Vector3(v.x, v.y, v.z + f);

        public static Vector3 Mul(this Vector3 v, Vector3 u) => new Vector3(v.x * u.x, v.y * u.y, v.z * u.z);
        public static Vector3 Div(this Vector3 v, Vector3 u) => new Vector3(v.x / u.x, v.y / u.y, v.z / u.z);

        public static Vector2 xx(this Vector3 v) { return new Vector2(v.x, v.x); }
        public static Vector2 xy(this Vector3 v) { return new Vector2(v.x, v.y); }
        public static Vector2 xz(this Vector3 v) { return new Vector2(v.x, v.z); }
        public static Vector2 yx(this Vector3 v) { return new Vector2(v.y, v.x); }
        public static Vector2 yy(this Vector3 v) { return new Vector2(v.y, v.y); }
        public static Vector2 yz(this Vector3 v) { return new Vector2(v.y, v.z); }
        public static Vector2 zx(this Vector3 v) { return new Vector2(v.z, v.x); }
        public static Vector2 zy(this Vector3 v) { return new Vector2(v.z, v.y); }
        public static Vector2 zz(this Vector3 v) { return new Vector2(v.z, v.z); }
        
        public static Vector3 x0y(this Vector2 v) { return new Vector3(v.x, 0, v.y); }
        public static Vector3 xy0(this Vector2 v) { return new Vector3(v.x, v.y, 0); }
        
        public static Vector3 x0y(this Vector2Int v) { return new Vector3(v.x, 0, v.y); }
        public static Vector3 xy0(this Vector2Int v) { return new Vector3(v.x, v.y, 0); }

    }
}
