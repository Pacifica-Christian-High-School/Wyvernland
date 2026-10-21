using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Wyvernland.Editor
{
    public static class WorldAndReachValidation
    {
        [MenuItem("Tools/Wyvernland/Validate Terrain, Vines, Neck, and Reach")]
        public static void Run()
        {
            EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity");
            PlayerNeckMeshSeal.Validate();
            PlayerWorldLoadingValidation.Run();
            RopePhysicsValidation.Run();
            HumanRigValidation.RunInteractionFixValidation();
            Debug.Log("WORLD_AND_REACH_VALIDATION_PASS");
        }
    }
}
