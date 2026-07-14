
using Unity.Cinemachine;

namespace Tools.CinemachineExtensions
{
    public class SyncWithMainCamera : CinemachineExtension
    {
        protected override void PostPipelineStageCallback(CinemachineVirtualCameraBase vcam, CinemachineCore.Stage stage, ref CameraState state, float deltaTime)
        {
            if (stage == CinemachineCore.Stage.Finalize
                && vcam.transform.position != state.PositionCorrection)
            {
                vcam.transform.position = state.PositionCorrection;
            }
        }
    }
}
