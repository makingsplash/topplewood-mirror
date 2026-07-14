using SGS.Services.Interfaces;
using UnityEngine;

namespace SGS.Services.Gameplay
{
    public class PlaneRaycastService : IService
    {
        private Plane _plane;
        
        public void Init()
        {
            _plane = new Plane(Vector3.up, Vector3.zero);
        }

        public void DeInit()
        {
            
        }

        public Vector3 GetRayIntersectionPoint(Ray ray)
        {
            if (_plane.Raycast(ray, out var distance)) 
            {
                Vector3 hitPoint = ray.GetPoint(distance);
                hitPoint.y = 0;
                return hitPoint;
            }
            return Vector3.zero;
        }
    }
}