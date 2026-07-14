using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Pool;

namespace Common.Extensions
{
    public static class EventSystemExtensions
    {
        public static bool HandlePointerOverGameObject(this EventSystem eventSystem)
        {
            if (eventSystem == null)
                throw new ArgumentNullException(nameof(eventSystem));
            
            return eventSystem.IsPointerOverUI();
        }
        
        public static bool IsPointerOverUI(this EventSystem eventSystem)
        {
            var pointerData = new PointerEventData(EventSystem.current);
            pointerData.position = new Vector2(Input.mousePosition.x, Input.mousePosition.y);
            using var _ = ListPool<RaycastResult>.Get(out var results);
            eventSystem.RaycastAll(pointerData, results);
            return results.Count > 0;
        }
    }
}
