using System.Collections.Generic;
using SGS.Data;
using SGS.Infrastructure.Tools.Observable;
using Unity.Cinemachine;
using UnityEngine;

namespace DataStorage
{
    public class Scene
    {
        private Camera _deskCamera;

        private CinemachineCamera _virtualCamera;
        private CinemachineTargetGroup _targetGroup;

        public CinemachineCamera VirtualCamera => _virtualCamera;
        public CinemachineTargetGroup CinemachineTargetGroup => _targetGroup;

        public Camera MainCamera
        {
            get =>  SayData.Scene.MainCamera;
            set => SayData.Scene.MainCamera = value;
        }

        public Camera UiCamera
        {
            get =>  SayData.Scene.UiCamera;
            set => SayData.Scene.UiCamera = value;
        }
        public Canvas Ui3DCanvas
        {
            get =>  SayData.Scene.Ui3DCanvas;
            set => SayData.Scene.Ui3DCanvas = value;
        }

        public Observable<bool> SceneInit => SayData.Scene.SceneInit;
        public Dictionary<FlyTargetType, Transform> ResourceFlyTargets => SayData.Scene.ResourceFlyTargets;

        public void RegisterCinemachine(CinemachineCamera camera, CinemachineTargetGroup targetGroup)
        {
            _virtualCamera = camera;
            _targetGroup = targetGroup;
        }

        public void UnRegisterCinemachine()
        {
            _virtualCamera = null;
            _targetGroup = null;
        }
    }
}
