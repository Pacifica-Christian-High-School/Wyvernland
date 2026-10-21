using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace Wyvernland.Editor
{
    public static class WebBrowserBuild
    {
        [MenuItem("Tools/Wyvernland/Build Browser Game")]
        public static void Run()
        {
            if (!BuildPipeline.IsBuildTargetSupported(BuildTargetGroup.WebGL, BuildTarget.WebGL))
                throw new InvalidOperationException("Install WebGL Build Support for this Unity editor in Unity Hub.");

            string output = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Builds", "Web"));
            string[] arguments = Environment.GetCommandLineArgs();
            for (int i = 0; i < arguments.Length - 1; i++)
                if (arguments[i] == "-wyvernlandWebOutput") output = Path.GetFullPath(arguments[i + 1]);

            // The command-line helper runs this in a separate project snapshot.
            PlayerSettings.WebGL.template = "PROJECT:Wyvernland";
            PlayerSettings.runInBackground = true;
            PlayerSettings.WebGL.compressionFormat = WebGLCompressionFormat.Gzip;
            PlayerSettings.WebGL.decompressionFallback = true;
            PlayerSettings.WebGL.dataCaching = true;
            PlayerSettings.WebGL.initialMemorySize = 256;
            PlayerSettings.WebGL.maximumMemorySize = 4096;
            PlayerSettings.WebGL.memoryGrowthMode = WebGLMemoryGrowthMode.Geometric;
            PlayerSettings.defaultWebScreenWidth = 1280;
            PlayerSettings.defaultWebScreenHeight = 720;
            PlayerSettings.SetGraphicsAPIs(BuildTarget.WebGL, new[] { UnityEngine.Rendering.GraphicsDeviceType.OpenGLES3 });

            Directory.CreateDirectory(output);
            PauseInputValidation.Run();
            LeftDropAimValidation.Run();
            PlayerNeckMeshSeal.Validate();HeldStoneMovementValidation.Run();LegDamageStagesValidation.Run();PresentationAndFragmentValidation.Run();ThrowSafetyValidation.Run();InteractionRefinementValidation.Run();RoomThrowAndJointValidation.Run();ExtendedBodyDestructionValidation.Run();BodyPersistenceValidation.Run();
            PlayerDamageValidation.Run();
            HeadShatterValidation.Run();
            TrainingDummyValidation.Run();
            RoomObjectPhysicsValidation.Run();
            HumanRigValidation.RunInteractionFixValidation();
            PickupResponsivenessValidation.Run();
            PlayerRigColliderValidation.Run();
            PhysicalDestructionValidation.RunHeldStrikeReaction();
            SimilarHeldStrikeValidation.Run();
            BrowserAndTileValidation.Run();
            WebSharedWorldValidation.Run();
            WebPerformanceValidation.Run();
            PlayerWorldLoadingValidation.Run();
            HeldWeaponSwingValidation.Run();
            CorpseDestructionValidation.Run();
            SmallFragmentRestValidation.Run();
            FragmentMaterialPerformanceValidation.Run();
            CombatRefinementValidation.Run();
            BrowserTerrainBake.EnsureFresh();
            BuildReport report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = new[] { "Assets/Scenes/SampleScene.unity" },
                locationPathName = output,
                target = BuildTarget.WebGL,
                options = BuildOptions.None
            });
            if (report.summary.result != BuildResult.Succeeded)
                throw new InvalidOperationException($"Browser build failed: {report.summary.result}. See the build log.");
            Debug.Log($"WYVERNLAND_WEB_BUILD_PASS {output} ({report.summary.totalSize:N0} bytes)");
        }
    }
}
