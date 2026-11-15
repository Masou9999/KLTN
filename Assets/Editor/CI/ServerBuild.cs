using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;

namespace KLTN.Editor.CI
{
    public static class ServerBuild
    {
        // Independent of the client Scene List and the currently selected Build Profile.
        public static void BuildLinux()
        {
            string[] scenes =
            {
                "Assets/Scenes/ServerBootstrap.unity",
                "Assets/Scenes/GameScene.unity",
            };
            foreach (string scene in scenes)
                if (!File.Exists(scene))
                    throw new Exception($"Missing server scene: {scene}");

            const string output = "Builds/LinuxServer";
            Directory.CreateDirectory(output);
            BuildReport report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = scenes,
                locationPathName = output + "/KLTNServer.x86_64",
                target = BuildTarget.StandaloneLinux64,
                subtarget = (int)StandaloneBuildSubtarget.Server,
                options = BuildOptions.StrictMode,
            });
            if (report.summary.result != BuildResult.Succeeded)
                throw new Exception($"Server build failed: {report.summary.result}");
        }
    }
}
