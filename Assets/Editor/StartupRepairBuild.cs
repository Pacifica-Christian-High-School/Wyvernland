using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace Wyvernland.Editor
{
    public static class StartupRepairBuild
    {
        public static void Run()
        {
            string directory=Path.GetFullPath(Path.Combine(Application.dataPath,"..",
                "Artifacts","Wyvernland-Startup-Fix"));
            Directory.CreateDirectory(directory);
            BuildReport report=BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes=new[]{"Assets/Scenes/SampleScene.unity"},
                locationPathName=Path.Combine(directory,"Wyvernland.exe"),
                target=BuildTarget.StandaloneWindows64,
                options=BuildOptions.None
            });
            if(report.summary.result!=BuildResult.Succeeded)
                throw new System.InvalidOperationException($"Windows build failed: {report.summary.result}");
            Debug.Log($"WYVERNLAND_STARTUP_FIX_BUILD_PASS {directory}");
        }
    }
}
