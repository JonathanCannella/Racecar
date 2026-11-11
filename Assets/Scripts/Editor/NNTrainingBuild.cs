using System;
using System.IO;
using System.Linq;

using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace GameAI.EditorTools
{
    /// <summary>
    /// Builds a standalone player for NN training with mlagents-learn (see Tools/ml/nntools train).
    /// The same build runs windowed (small, low quality, so you can watch) or headless (--no-graphics).
    ///
    /// Menu: Tools > NN Training > Build Training Environment
    /// Command line: Tools/ml/nntools build (Windows: Tools\ml\nntools.cmd build)
    ///   (Unity -batchmode -executeMethod GameAI.EditorTools.NNTrainingBuild.BuildFromCommandLine [-buildOutput PATH])
    /// </summary>
    public static class NNTrainingBuild
    {
        // First scene is the default track; nntools train --scene picks another (-raceScene)
        public static readonly string[] StudentScenes =
        {
            "Assets/Scenes/RaceTrackNN.unity",
            "Assets/Scenes/WindingRaceTrackNN.unity",
            "Assets/Scenes/FastSweepersRaceTrackNN.unity",
            "Assets/Scenes/DragRaceTrackNN.unity",
        };

        public const string StudentBuildName = "RacetrackNN";

        [MenuItem("Tools/NN Training/Build Training Environment")]
        static void BuildMenu()
        {
            var path = DefaultOutputPath(StudentBuildName);
            if (Build(StudentScenes, path))
                EditorUtility.RevealInFinder(path);
        }

        public static void BuildFromCommandLine()
        {
            var output = GetArg("-buildOutput") ?? DefaultOutputPath(StudentBuildName);
            EditorApplication.Exit(Build(StudentScenes, output) ? 0 : 1);
        }

        public static string PlatformFolder
        {
            get
            {
                switch (Application.platform)
                {
                    case RuntimePlatform.OSXEditor: return "macOS";
                    case RuntimePlatform.LinuxEditor: return "Linux";
                    default: return "Windows";
                }
            }
        }

        static BuildTarget StandaloneTarget
        {
            get
            {
                switch (Application.platform)
                {
                    case RuntimePlatform.OSXEditor: return BuildTarget.StandaloneOSX;
                    case RuntimePlatform.LinuxEditor: return BuildTarget.StandaloneLinux64;
                    default: return BuildTarget.StandaloneWindows64;
                }
            }
        }

        static string Extension
        {
            get
            {
                switch (Application.platform)
                {
                    case RuntimePlatform.OSXEditor: return ".app";
                    case RuntimePlatform.LinuxEditor: return ".x86_64";
                    default: return ".exe";
                }
            }
        }

        public static string DefaultOutputPath(string buildName) =>
            Path.Combine("Builds", "NNTraining", PlatformFolder, buildName + Extension);

        /// <summary>Build the given scenes as a training player. Player settings are restored afterwards.</summary>
        public static bool Build(string[] scenes, string outputPath)
        {
            var missing = scenes.Where(s => !File.Exists(s)).ToArray();
            if (missing.Length > 0)
            {
                Debug.LogError("[NNTrainingBuild] Missing scenes: " + string.Join(", ", missing));
                return false;
            }

            // Training-friendly player settings: keep running when not focused, small resizable window
            var runInBackground = PlayerSettings.runInBackground;
            var fullScreenMode = PlayerSettings.fullScreenMode;
            var width = PlayerSettings.defaultScreenWidth;
            var height = PlayerSettings.defaultScreenHeight;
            var resizable = PlayerSettings.resizableWindow;
            var visibleInBackground = PlayerSettings.visibleInBackground;

            try
            {
                PlayerSettings.runInBackground = true;
                PlayerSettings.fullScreenMode = FullScreenMode.Windowed;
                PlayerSettings.defaultScreenWidth = 320;
                PlayerSettings.defaultScreenHeight = 180;
                PlayerSettings.resizableWindow = true;
                PlayerSettings.visibleInBackground = true;

                Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outputPath)));

                var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
                {
                    scenes = scenes,
                    locationPathName = outputPath,
                    target = StandaloneTarget,
                    options = BuildOptions.None,
                });

                var ok = report.summary.result == BuildResult.Succeeded;
                Debug.Log($"[NNTrainingBuild] {report.summary.result}: {outputPath} " +
                    $"({report.summary.totalSize / (1024 * 1024)} MB, {report.summary.totalTime.TotalSeconds:0} s)");
                return ok;
            }
            finally
            {
                PlayerSettings.runInBackground = runInBackground;
                PlayerSettings.fullScreenMode = fullScreenMode;
                PlayerSettings.defaultScreenWidth = width;
                PlayerSettings.defaultScreenHeight = height;
                PlayerSettings.resizableWindow = resizable;
                PlayerSettings.visibleInBackground = visibleInBackground;
            }
        }

        public static string GetArg(string name)
        {
            var args = Environment.GetCommandLineArgs();
            var i = Array.IndexOf(args, name);
            return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
        }
    }
}
