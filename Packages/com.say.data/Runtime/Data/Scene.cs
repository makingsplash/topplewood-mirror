using System.Collections.Generic;
using SGS.Infrastructure.Tools.Observable;
using UnityEngine;

namespace SGS.Data
{
    public enum FlyTargetType
    {
        Energy,
        Experience
    }
    
    public class SayScene
    {
        public Camera MainCamera;
        public Camera UiCamera;
        public Canvas Ui3DCanvas;

        public readonly Observable<bool> SceneInit = new ();
        public readonly Dictionary<FlyTargetType, Transform> ResourceFlyTargets = new ();
    }
}