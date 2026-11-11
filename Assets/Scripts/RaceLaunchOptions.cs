using System;
using UnityEngine;
using UnityEngine.SceneManagement;

using Unity.MLAgents;

namespace GameAI
{
    /// <summary>
    /// Launch-time options, mainly for NN training builds started by mlagents-learn:
    ///  - LaunchedByTrainer / TrainerConnected / RecordingFromCommandLine: run in simulated time (no frame-rate cap)
    ///    and without audio
    ///  - -raceScene &lt;SceneName&gt;: start in that scene (must be in the build), e.g. via
    ///    Tools/ml/nntools train --scene WindingRaceTrackNN
    ///  - -raceScene auto: each training instance picks a build scene by its ML-Agents port, so parallel
    ///    instances (--num-envs) cover different tracks
    /// </summary>
    public static class RaceLaunchOptions
    {
        static string[] args;
        static string[] Args => args ?? (args = Environment.GetCommandLineArgs());

        // mlagents-learn passes --mlagents-port to the executables it launches
        public static bool LaunchedByTrainer => Array.IndexOf(Args, "--mlagents-port") >= 0;

        // Started to record demonstrations from the command line (DemonstrationSession -recordDemos, e.g. nntools record)
        public static bool RecordingFromCommandLine => Array.IndexOf(Args, "-recordDemos") >= 0;

        // Runs without anyone watching (trainer or command-line recording): simulated time, no audio
        public static bool Unattended => LaunchedByTrainer || RecordingFromCommandLine;

        // Also true when training in the Editor (mlagents-learn connected). Doesn't initialize ML-Agents.
        public static bool TrainerConnected =>
            LaunchedByTrainer || (Academy.IsInitialized && Academy.Instance.IsCommunicatorOn);

        public static int TrainerPort
        {
            get
            {
                var i = Array.IndexOf(Args, "--mlagents-port");
                return i >= 0 && i + 1 < Args.Length && int.TryParse(Args[i + 1], out var port) ? port : -1;
            }
        }

        public static string RequestedScene
        {
            get
            {
                var i = Array.IndexOf(Args, "-raceScene");
                return i >= 0 && i + 1 < Args.Length ? Args[i + 1] : null;
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void LoadRequestedScene()
        {
            if (Application.isEditor)
                return;

            var scene = RequestedScene;
            if (string.IsNullOrEmpty(scene))
                return;

            if (scene == "auto")
            {
                var count = SceneManager.sceneCountInBuildSettings;
                var port = TrainerPort;
                if (count <= 1 || port < 0)
                    return;
                var index = port % count;
                Debug.Log($"[RaceLaunchOptions] -raceScene auto: port {port} -> build scene {index}");
                if (SceneManager.GetActiveScene().buildIndex != index)
                    SceneManager.LoadScene(index);
                return;
            }

            if (SceneManager.GetActiveScene().name == scene)
                return;

            if (!Application.CanStreamedLevelBeLoaded(scene))
            {
                Debug.LogError($"[RaceLaunchOptions] -raceScene {scene} is not in this build.");
                return;
            }

            Debug.Log($"[RaceLaunchOptions] Loading scene {scene}");
            SceneManager.LoadScene(scene);
        }
    }
}
