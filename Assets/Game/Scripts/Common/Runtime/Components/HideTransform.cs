using UnityEngine;

namespace Common.Components
{
    [ExecuteInEditMode]
    public sealed class HideTransform : MonoBehaviour
    {
        private void OnEnable() => 
            transform.hideFlags = HideFlags.NotEditable;

        private void OnDisable() => 
            transform.hideFlags = HideFlags.None;
    }
}
