using UnityEngine;

public static class MotionUtils
{

    public static Vector3 ComputeSpiralWorldPosition(Vector3 originPosition, Vector3 size,  float t, float turns)
    {
        t = Mathf.Clamp01(t);
        
        Vector3 center = Vector3.zero;

        // Радиус окружности: вписанная в прямоугольник основания (X-Z)
        float radius = 0.5f * Mathf.Min(size.x, size.z);

        // Угол поворота для t в [0..1]
        float angle = turns * Mathf.PI * 2f * t;

        // Линейное перемещение по локальной оси от "дна" к "верху" Bounds
        float z = center.z - size.z * 0.5f + size.z * t;

        // Локальная позиция: центр основания + окружность в XZ + смещение по Y
        Vector3 localPos =
            new Vector3(center.x, center.y, z) +
            new Vector3(Mathf.Cos(angle) * radius, Mathf.Sin(angle) * radius, 0f);
        
        return originPosition + localPos;
    }
}
