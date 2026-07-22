#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;

namespace Gameplay.Sim.Editor
{
    /// <summary>
    /// Editor convenience (RFC §7 п.14): open the level-sim scene and enter play mode to run the solve-sweep.
    /// Handy for balancer/QA; the harness itself does not depend on it.
    /// </summary>
    public static class LevelSimMenu
    {
        private const string ScenePath = "Assets/Scenes/LevelSim.unity";

        [MenuItem("iliai-studio/Level-Sim/Run Sweep")]
        public static void RunSweep()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                return;

            EditorSceneManager.OpenScene(ScenePath);
            EditorApplication.isPlaying = true;
        }
    }
}
#endif
