using System;
using System.Reflection;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;

namespace Wyvernland.Editor
{
    public static class PauseInputValidation
    {
        const BindingFlags Private=BindingFlags.Instance|BindingFlags.NonPublic;
        static void Check(bool value,string message)
        { if(!value) throw new InvalidOperationException("Pause input: "+message); }
        static void Call(FirstPersonController player,string method) =>
            typeof(FirstPersonController).GetMethod(method,Private).Invoke(player,null);
        static T Read<T>(FirstPersonController player,string field) =>
            (T)typeof(FirstPersonController).GetField(field,Private).GetValue(player);

        public static void Run()
        {
            var previous=SceneManager.GetActiveScene();
            if(string.IsNullOrEmpty(previous.path))
            {
                EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity");
                previous=SceneManager.GetActiveScene();
            }
            var scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Additive);
            SceneManager.SetActiveScene(scene);
            var previousKeyboard=Keyboard.current;
            var previousMouse=Mouse.current;
            var keyboard=InputSystem.AddDevice<Keyboard>();
            var mouse=InputSystem.AddDevice<Mouse>();
            var simulation=Physics.simulationMode;
            float timeScale=Time.timeScale;
            try
            {
                Physics.simulationMode=SimulationMode.Script;Time.timeScale=1f;
                var actor=new GameObject("Paused room player");actor.transform.position=Vector3.up*1000;
                var body=actor.AddComponent<Rigidbody>();body.useGravity=false;
                var view=new GameObject("View",typeof(Camera));view.transform.SetParent(actor.transform,false);
                var player=actor.AddComponent<FirstPersonController>();player.Initialize(view.transform);player.enabled=false;
                void Press(params Key[] keys)
                {
                    InputSystem.Update();
                    InputState.Change(keyboard,new KeyboardState(keys));
                    keyboard.MakeCurrent();
                }
                Press(player.Bindings[0],player.Bindings[4],player.Bindings[5]);
                Call(player,"CaptureMovementInput");
                Check(Read<Vector2>(player,"movementInput").y>0&&Read<bool>(player,"jumpHeld"),
                    $"fixture has walking and held jump input (move={Read<Vector2>(player,"movementInput")}, jump={Read<bool>(player,"jumpHeld")})");
                typeof(FirstPersonController).GetField("jumpRequested",Private).SetValue(player,true);
                body.linearVelocity=new Vector3(6,8,0);
                player.SetPaused(true);
                Check(body.linearVelocity==Vector3.zero,"pausing immediately cancels walking and upward jump velocity");
                Check(Read<Vector2>(player,"movementInput")==Vector2.zero&&!Read<bool>(player,"jumpRequested")&&
                    !Read<bool>(player,"jumpHeld")&&!player.IsSprinting,"cached movement and jump input cleared");
                Check(!keyboard[player.Bindings[0]].isPressed&&!keyboard[player.Bindings[5]].isPressed,"held keyboard state cleared without key-up");
                float start=body.position.y;
                for(int step=0;step<50;step++) { Call(player,"FixedUpdate");scene.GetPhysicsScene().Simulate(.02f); }
                Check(body.position.y<start-1f&&body.linearVelocity.y<0,"paused player falls instead of flying in running room physics");
                Check(Mathf.Abs(body.position.x)<.001f,"paused player cannot keep walking");
                // Keys pressed in a menu must not leak back into gameplay.
                Press(player.Bindings[0],player.Bindings[5]);
                player.SetPaused(false);InputSystem.Update();Call(player,"CaptureMovementInput");
                Check(Read<Vector2>(player,"movementInput")==Vector2.zero&&!Read<bool>(player,"jumpRequested"),"resume discards menu-held keys and queued jump");
                Press(player.Bindings[0],player.Bindings[5]);Call(player,"CaptureMovementInput");
                Check(Read<Vector2>(player,"movementInput").y>0&&Read<bool>(player,"jumpHeld"),"fresh presses work after resume");
                player.SetPaused(true);player.SetPaused(false);InputSystem.Update();Call(player,"CaptureMovementInput");
                Check(!Read<bool>(player,"jumpRequested"),"repeated pause/resume cannot replay a jump");
                Debug.Log("PAUSE_INPUT_VALIDATION_PASS: held-key pause, lost key-up, menu-held keys, resume, fresh presses, repeated pauses and running-room falling instead of flight.");
            }
            finally
            {
                InputSystem.RemoveDevice(keyboard);InputSystem.RemoveDevice(mouse);
                previousKeyboard?.MakeCurrent();previousMouse?.MakeCurrent();
                Physics.simulationMode=simulation;Time.timeScale=timeScale;
                SceneManager.SetActiveScene(previous);EditorSceneManager.CloseScene(scene,true);
            }
        }
    }
}
