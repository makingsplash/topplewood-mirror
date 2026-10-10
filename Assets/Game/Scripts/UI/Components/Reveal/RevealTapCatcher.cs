using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Ui.Components.Reveal
{
    /// <summary>
    /// Full-screen raycast target the <see cref="ScreenReveal"/> puts on top of its screen while the intro plays: it
    /// swallows every tap (so a tap aimed at the level cannot press a button that is still flying in) and reports
    /// it, so the reveal can fast-forward. A Graphic that draws nothing — raycastable without a full-screen quad of
    /// overdraw. Pointer-DOWN, not click: the press that skips must not also start a press on a button underneath.
    /// RequireComponent is NOT inherited from <see cref="Graphic"/> in uGUI 2.0 — without it the editor throws
    /// MissingComponentException every frame (docs/platform/unity-gotchas.md; hit again in TASK-425's first play).
    /// </summary>
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class RevealTapCatcher : Graphic, IPointerDownHandler
    {
        public event Action Tapped;

        public void OnPointerDown(PointerEventData eventData)
        {
            Tapped?.Invoke();
        }

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
        }
    }
}
