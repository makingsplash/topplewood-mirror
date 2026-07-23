#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;

namespace Gameplay.Sim.Editor
{
    /// <summary>
    /// Canonical headless entry point for the level-sim harness (RFC-Δ §4.1, ADR-0001 Block 4/5). Invoked as:
    /// <code>
    /// &lt;UnityEditor&gt; -batchmode -projectPath &lt;repo&gt; \
    ///   -executeMethod Gameplay.Sim.Editor.LevelSimBatch.Run \
    ///   -logFile &lt;repo&gt;/Artifacts/level-sim/unity-batch.log     (NO -quit)
    /// </code>
    /// Batchmode spins the player loop at full speed regardless of window focus, so the multi-frame
    /// <see cref="LevelSimRunner"/> coroutine completes and the CSV/JSON report reaches disk — the failure
    /// mode an interactive / unfocused (MCP-driven) editor hits (RFC-Δ §2).
    /// </summary>
    public static class LevelSimBatch
    {
        private const string ScenePath = "Assets/Scenes/LevelSim.unity";

        public static void Run()
        {
            // Do NOT pass -quit on the command line: it would end the process the instant Run() returns, before
            // play mode ticks. The scene's LevelSimRunner drives the sweep and self-exits via
            // EditorApplication.Exit(code) once the report is written (LevelSimRunner.Exit, batch path).
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            EditorApplication.EnterPlaymode(); // schedules the play-mode transition (with domain reload); returns now
        }
    }
}
#endif
