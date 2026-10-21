using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Wyvernland.Editor
{
    public sealed class PlayerRigEditor : EditorWindow
    {
        private const string RootName = "PLAYER RIG EDITOR";
        private const string SourcePath = "Assets/Resources/Player/HumanRigged.fbx";
        private const string EditedPrefabPath = "Assets/Resources/Player/HumanRiggedEdited.prefab";
        private const string MeshFolder = "Assets/Resources/Player/EditedMeshes";
        private static readonly string[] JointNames =
            { "Pelvis", "Waist", "LeftShoulder", "RightShoulder", "LeftElbow", "RightElbow",
                "LeftWrist", "RightWrist",
                "LeftHip", "RightHip", "LeftKnee", "RightKnee", "LeftAnkle", "RightAnkle", "RightFingers1", "RightFingers2", "RightFingers3", "RightThumb1", "RightThumb2" };

        private readonly struct PoseJoint
        {
            public readonly string Name;
            public readonly Vector3 Euler;
            public readonly float LocalXAfter;

            public PoseJoint(string name, Vector3 euler, float localXAfter = 0f)
            {
                Name = name;
                Euler = euler;
                LocalXAfter = localXAfter;
            }
        }

        private sealed class PoseDefinition
        {
            public readonly string Name;
            public readonly string Timing;
            public readonly PoseJoint[] Joints;

            public PoseDefinition(string name, string timing, params PoseJoint[] joints)
            {
                Name = name;
                Timing = timing;
                Joints = joints;
            }
        }

        private static readonly PoseJoint[] PunchFist =
        {
            new("RightFingers1", new Vector3(0f, 0f, -24f)),
            new("RightFingers2", new Vector3(0f, 0f, 75f)),
            new("RightFingers3", new Vector3(0f, 0f, 80f)),
            new("RightThumb1", new Vector3(-40f, 0f, 15f)),
            new("RightThumb2", new Vector3(55f, 0f, 0f))
        };

        private static readonly PoseDefinition[] Poses =
        {
            new("Standing Idle", "continuous",
                new("RightHip", new Vector3(175f, 11f, -3f)),
                new("RightKnee", new Vector3(11f, 0f, 0f)),
                new("LeftHip", new Vector3(180f, -20f, 3f)),
                new("LeftKnee", new Vector3(153f, 0f, 0f)),
                new("RightShoulder", new Vector3(-183f, -3f, -3f)),
                new("RightElbow", new Vector3(-10f, 3f, -3f)),
                new("LeftShoulder", new Vector3(173f, 3f, 3f)),
                new("LeftElbow", new Vector3(-10f, -3f, 3f))),
            new("Crouch Idle", "continuous",
                new("LeftHip", new Vector3(130f, 10f, 20f)),
                new("RightHip", new Vector3(60f, 0f, 0f)),
                new("LeftKnee", new Vector3(120f, 0f, 0f)),
                new("RightKnee", new Vector3(110f, 0f, 0f)),
                new("LeftAnkle", new Vector3(26f, 0f, 0f)),
                new("RightAnkle", new Vector3(-70f, 0f, 0f))),
            new("Crouch Walk A", "cycle endpoint",
                new("LeftHip", new Vector3(-190f, 0f, 10f)),
                new("RightHip", new Vector3(120f, 0f, 0f)),
                new("LeftKnee", new Vector3(120f, 0f, 0f)),
                new("RightKnee", new Vector3(110f, 0f, 0f)),
                new("LeftAnkle", new Vector3(26f, 0f, 0f)),
                new("RightAnkle", new Vector3(-70f, 0f, 0f))),
            new("Crouch Walk B", "cycle endpoint",
                new("LeftHip", new Vector3(120f, 0f, 0f)),
                new("RightHip", new Vector3(-190f, 0f, 0f)),
                new("LeftKnee", new Vector3(110f, 0f, 0f)),
                new("RightKnee", new Vector3(120f, 0f, 0f)),
                new("LeftAnkle", new Vector3(-70f, 0f, 0f)),
                new("RightAnkle", new Vector3(26f, 0f, 0f))),
            new("Reach for Object", "while reaching",
                new("RightFingers1", new Vector3(0f, 0f, 10f)),
                new("RightFingers2", new Vector3(0f, 0f, 20f))),
            new("Hold Irregular Object", "while held",
                new("RightFingers1", new Vector3(0f, 0f, 26f)),
                new("RightFingers2", new Vector3(0f, 0f, 33f)),
                new("RightFingers3", new Vector3(0f, 0f, 42f)),
                new("RightThumb1", new Vector3(-27f, -30f, 28f)),
                new("RightThumb2", new Vector3(45f, 0f, 0f))),
            PunchPose("Punch 1 - Ready", "0.357 s", new Vector3(-180f, -10f, -110f), new Vector3(-100f, 200f, -200f)),
            PunchPose("Punch 2 - Strike", "0.071 s", new Vector3(150f, 3f, -150f), new Vector3(-105f, -15f, 11f)),
            PunchPose("Punch 3 - Intermediate", "0.071 s", new Vector3(180f, -12f, -150f), new Vector3(-100f, -170f, 165f)),
            PunchPose("Punch 4 - Extended", "0.071 s", new Vector3(185f, -85f, -110f), new Vector3(-40f, 15f, -7f)),
            PunchPose("Punch 5 - Follow-through", "0.286 s", new Vector3(185f, -85f, -110f), new Vector3(-40f, 15f, -7f), 10f, -10f),
            new("Throw Charge", "up to full charge",
                new("RightShoulder", new Vector3(60f, 80f, 30f)),
                new("RightElbow", new Vector3(-80f, 20f, -20f))),
            new("Throw Follow-through", "throw release",
                new("RightShoulder", new Vector3(60f, 70f, 30f)),
                new("RightElbow", new Vector3(0f, 0f, 50f))),
            new("Slide - Fixed Left Arm", "continuous while sliding",
                new PoseJoint("LeftShoulder", new Vector3(150f, 10f, 80f)))
        };

        private GameObject editingRoot;
        private GameObject posePreviewRoot;
        private SkinnedMeshRenderer[] renderers = Array.Empty<SkinnedMeshRenderer>();
        private int rendererIndex;
        private int boneIndex;
        private bool paintWeights;
        private bool erase;
        private float brushRadius = 32f;
        private float brushStrength = 0.25f;
        private Vector2 scroll;
        private bool showPoses = true;

        [MenuItem("Wyvernland/Player Rig Editor")]
        public static void Open() => GetWindow<PlayerRigEditor>("Player Rig");

        private void OnEnable()
        {
            SceneView.duringSceneGui += DuringSceneGUI;
            FindExistingRoot();
        }

        private void OnDisable()
        {
            SceneView.duringSceneGui -= DuringSceneGUI;
            RestorePreviewPose();
        }

        private void OnGUI()
        {
            EditorGUILayout.HelpBox(
                "Move joints with Unity's Move tool. Weight Paint changes which vertices follow the selected bone. Apply saves a Resources prefab used automatically in Play mode.",
                MessageType.Info);

            if (editingRoot == null)
            {
                if (GUILayout.Button("Open Rig for Editing", GUILayout.Height(36))) OpenRig();
                return;
            }

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Frame Rig"))
            {
                Selection.activeGameObject = editingRoot;
                SceneView.lastActiveSceneView?.FrameSelected();
            }
            if (GUILayout.Button("Apply Rig to Game")) ApplyRig();
            EditorGUILayout.EndHorizontal();
            if (FindChild(editingRoot.transform,"LeftWrist") == null ||
                FindChild(editingRoot.transform,"RightWrist") == null)
                if (GUILayout.Button("Add Wrist Joints")) AddWristsToEditingRig();

            scroll = EditorGUILayout.BeginScrollView(scroll);
            DrawPoseChecker();

            paintWeights = EditorGUILayout.ToggleLeft("Weight Paint", paintWeights);
            if (paintWeights && renderers.Length > 0)
            {
                string[] rendererNames = renderers.Select(r => r.name).ToArray();
                rendererIndex = EditorGUILayout.Popup("Mesh", Mathf.Clamp(rendererIndex, 0, renderers.Length - 1), rendererNames);
                Transform[] bones = ActiveRenderer.bones;
                if (bones.Length > 0)
                {
                    boneIndex = EditorGUILayout.Popup("Bone", Mathf.Clamp(boneIndex, 0, bones.Length - 1), bones.Select(b => b.name).ToArray());
                    erase = EditorGUILayout.ToggleLeft("Erase selected bone weight", erase);
                    brushRadius = EditorGUILayout.Slider("Brush radius", brushRadius, 5f, 100f);
                    brushStrength = EditorGUILayout.Slider("Brush strength", brushStrength, 0.01f, 1f);
                    EditorGUILayout.HelpBox("Drag the left mouse button over visible vertices in the Scene view. Hold Alt to orbit normally.", MessageType.None);
                }
            }

            EditorGUILayout.Space(8);
            EditorGUILayout.LabelField("Select a joint to move", EditorStyles.boldLabel);
            foreach (string jointName in JointNames)
            {
                Transform joint = FindChild(editingRoot.transform, jointName);
                using (new EditorGUI.DisabledScope(joint == null))
                    if (GUILayout.Button(jointName))
                    {
                        paintWeights = false;
                        Selection.activeTransform = joint;
                        Tools.current = Tool.Move;
                        SceneView.lastActiveSceneView?.FrameSelected();
                    }
            }
            EditorGUILayout.EndScrollView();
        }

        private static PoseDefinition PunchPose(string name, string timing, Vector3 shoulder,
            Vector3 elbow, float shoulderLocalX = 0f, float elbowLocalX = 0f)
        {
            var joints = new List<PoseJoint>
            {
                new("RightShoulder", shoulder, shoulderLocalX),
                new("RightElbow", elbow, elbowLocalX)
            };
            joints.AddRange(PunchFist);
            return new PoseDefinition(name, timing, joints.ToArray());
        }

        private void DrawPoseChecker()
        {
            EditorGUILayout.Space(8);
            showPoses = EditorGUILayout.Foldout(showPoses, "Pose Checker", true,
                EditorStyles.foldoutHeader);
            if (!showPoses) return;
            EditorGUILayout.HelpBox(
                "Preview fixed animation keyframes on a temporary copy. The editable rig and saved bind pose stay independent of the preview.",
                MessageType.None);
            if (GUILayout.Button("Restore Rig Pose")) RestorePreviewPose();
            foreach (PoseDefinition pose in Poses)
            {
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField(pose.Name, EditorStyles.boldLabel);
                GUILayout.FlexibleSpace();
                EditorGUILayout.LabelField(pose.Timing, GUILayout.Width(135f));
                if (GUILayout.Button("Preview", GUILayout.Width(70f))) PreviewPose(pose);
                EditorGUILayout.EndHorizontal();
                foreach (PoseJoint joint in pose.Joints)
                {
                    string value = $"({joint.Euler.x:0.#}, {joint.Euler.y:0.#}, {joint.Euler.z:0.#})";
                    if (!Mathf.Approximately(joint.LocalXAfter, 0f))
                        value += $"  then local X {joint.LocalXAfter:+0.#;-0.#}°";
                    EditorGUILayout.LabelField(joint.Name, value);
                }
                EditorGUILayout.EndVertical();
            }
            EditorGUILayout.HelpBox(
                "Runtime-generated poses: standing walk, sprint, strafe, turn, airborne, pickup reach, view aiming, and slope-adjusted slide. These depend on movement, terrain, camera direction, or target position and do not have one fixed rotation to preview.",
                MessageType.Info);
        }

        public void RefreshRestPoseAfterRepair()
        {
            RestorePreviewPose();
            RefreshRenderers();
            Repaint();
        }

        private void RestorePreviewPose()
        {
            if(posePreviewRoot!=null) DestroyImmediate(posePreviewRoot);
            posePreviewRoot=null;
            SceneView.RepaintAll();
        }

        private void PreviewPose(PoseDefinition pose)
        {
            RestorePreviewPose();
            posePreviewRoot=Instantiate(editingRoot);
            posePreviewRoot.name="Pose Preview - "+pose.Name;
            posePreviewRoot.hideFlags=HideFlags.HideAndDontSave;
            posePreviewRoot.transform.position+=Vector3.right*2.5f;
            foreach (PoseJoint poseJoint in pose.Joints)
            {
                Transform joint = FindChild(posePreviewRoot.transform, poseJoint.Name);
                if (joint == null) continue;
                joint.localRotation = Quaternion.Euler(poseJoint.Euler) *
                    Quaternion.AngleAxis(poseJoint.LocalXAfter, Vector3.right);
            }
            Selection.activeGameObject=posePreviewRoot;
            SceneView.lastActiveSceneView?.FrameSelected();
            SceneView.RepaintAll();
        }

        private SkinnedMeshRenderer ActiveRenderer => renderers[Mathf.Clamp(rendererIndex, 0, renderers.Length - 1)];

        private void FindExistingRoot()
        {
            editingRoot = GameObject.Find(RootName);
            RefreshRenderers();
        }

        private void OpenRig()
        {
            GameObject source = AssetDatabase.LoadAssetAtPath<GameObject>(EditedPrefabPath);
            if (source == null) source = AssetDatabase.LoadAssetAtPath<GameObject>(SourcePath);
            if (source == null)
            {
                EditorUtility.DisplayDialog("Player Rig", "HumanRigged.fbx could not be found.", "OK");
                return;
            }

            editingRoot = (GameObject)PrefabUtility.InstantiatePrefab(source);
            editingRoot.name = RootName;
            Undo.RegisterCreatedObjectUndo(editingRoot, "Open player rig");
            if (PrefabUtility.IsPartOfPrefabInstance(editingRoot))
                PrefabUtility.UnpackPrefabInstance(editingRoot, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            RefreshRenderers();
            foreach (SkinnedMeshRenderer renderer in renderers)
            {
                Mesh clone = Instantiate(renderer.sharedMesh);
                clone.name = renderer.sharedMesh.name + " Editable";
                renderer.sharedMesh = clone;
            }
            AddWristsToEditingRig();
            Selection.activeGameObject = editingRoot;
            SceneView.lastActiveSceneView?.FrameSelected();
        }

        private void RefreshRenderers()
        {
            renderers = editingRoot == null
                ? Array.Empty<SkinnedMeshRenderer>()
                : editingRoot.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            rendererIndex = Mathf.Clamp(rendererIndex, 0, Mathf.Max(0, renderers.Length - 1));
        }

        private void AddWristsToEditingRig()
        {
            if (editingRoot == null) return;
            Transform oldLeft=FindChild(editingRoot.transform,"LeftWrist");
            Transform oldRight=FindChild(editingRoot.transform,"RightWrist");
            Undo.RegisterFullObjectHierarchyUndo(editingRoot,"Add wrist joints");
            Mesh wristMesh=RiggedPlayerAvatar.EnsureWristJoints(editingRoot);
            if(oldLeft==null)
                Undo.RegisterCreatedObjectUndo(FindChild(editingRoot.transform,"LeftWrist").gameObject,
                    "Add left wrist");
            if(oldRight==null)
                Undo.RegisterCreatedObjectUndo(FindChild(editingRoot.transform,"RightWrist").gameObject,
                    "Add right wrist");
            if(wristMesh!=null) wristMesh.name="HumanBody Wrist Rig Editable";
            RefreshRenderers();
            SceneView.RepaintAll();
        }

        private void DuringSceneGUI(SceneView sceneView)
        {
            if (!paintWeights || editingRoot == null || renderers.Length == 0 || ActiveRenderer.bones.Length == 0)
                return;

            Event current = Event.current;
            Vector2 mouse = current.mousePosition;
            Handles.BeginGUI();
            Color oldColor = GUI.color;
            GUI.color = erase ? new Color(1f, 0.25f, 0.2f, 0.8f) : new Color(0.2f, 0.9f, 1f, 0.8f);
            GUI.DrawTexture(new Rect(mouse.x - brushRadius, mouse.y - brushRadius, brushRadius * 2f, brushRadius * 2f),
                MakeCircleTexture(), ScaleMode.ScaleToFit, true);
            GUI.color = oldColor;
            Handles.EndGUI();

            HandleUtility.AddDefaultControl(GUIUtility.GetControlID(FocusType.Passive));
            if ((current.type == EventType.MouseDown || current.type == EventType.MouseDrag) && current.button == 0 && !current.alt)
            {
                Paint(mouse);
                current.Use();
            }
        }

        private static Texture2D circleTexture;
        private static Texture2D MakeCircleTexture()
        {
            if (circleTexture != null) return circleTexture;
            const int size = 64;
            circleTexture = new Texture2D(size, size, TextureFormat.RGBA32, false) { hideFlags = HideFlags.HideAndDontSave };
            Color[] pixels = new Color[size * size];
            Vector2 centre = Vector2.one * (size - 1) * 0.5f;
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float distance = Vector2.Distance(new Vector2(x, y), centre) / centre.x;
                    float alpha = distance > 1f ? 0f : distance > 0.88f ? 1f : 0.08f;
                    pixels[y * size + x] = new Color(1f, 1f, 1f, alpha);
                }
            circleTexture.SetPixels(pixels);
            circleTexture.Apply();
            return circleTexture;
        }

        private void Paint(Vector2 mouse)
        {
            SkinnedMeshRenderer renderer = ActiveRenderer;
            Mesh mesh = renderer.sharedMesh;
            Mesh posedMesh = new();
            renderer.BakeMesh(posedMesh);
            Vector3[] vertices = posedMesh.vertices;
            BoneWeight[] weights = mesh.boneWeights;
            int selectedBone = Mathf.Clamp(boneIndex, 0, renderer.bones.Length - 1);
            bool changed = false;
            Undo.RecordObject(mesh, erase ? "Erase bone weights" : "Paint bone weights");

            for (int i = 0; i < vertices.Length; i++)
            {
                Vector2 screen = HandleUtility.WorldToGUIPoint(renderer.transform.TransformPoint(vertices[i]));
                float distance = Vector2.Distance(screen, mouse);
                if (distance > brushRadius) continue;
                float amount = brushStrength * (1f - distance / brushRadius);
                BoneWeight updated = ChangeWeight(weights[i], selectedBone, amount, erase);
                if (!updated.Equals(weights[i]))
                {
                    weights[i] = updated;
                    changed = true;
                }
            }
            DestroyImmediate(posedMesh);
            if (!changed) return;
            mesh.boneWeights = weights;
            mesh.RecalculateBounds();
            EditorUtility.SetDirty(mesh);
            SceneView.RepaintAll();
        }

        private static BoneWeight ChangeWeight(BoneWeight source, int selectedBone, float amount, bool remove)
        {
            var values = new Dictionary<int, float>();
            Add(values, source.boneIndex0, source.weight0);
            Add(values, source.boneIndex1, source.weight1);
            Add(values, source.boneIndex2, source.weight2);
            Add(values, source.boneIndex3, source.weight3);
            values.TryGetValue(selectedBone, out float current);
            values[selectedBone] = remove ? Mathf.Max(0f, current - amount) : Mathf.Lerp(current, 1f, amount);
            var best = values.Where(pair => pair.Value > 0.0001f).OrderByDescending(pair => pair.Value).Take(4).ToArray();
            float total = best.Sum(pair => pair.Value);
            if (total < 0.0001f) return source;
            BoneWeight result = new();
            for (int i = 0; i < best.Length; i++)
            {
                int index = best[i].Key;
                float weight = best[i].Value / total;
                if (i == 0) { result.boneIndex0 = index; result.weight0 = weight; }
                else if (i == 1) { result.boneIndex1 = index; result.weight1 = weight; }
                else if (i == 2) { result.boneIndex2 = index; result.weight2 = weight; }
                else { result.boneIndex3 = index; result.weight3 = weight; }
            }
            return result;
        }

        private static void Add(Dictionary<int, float> values, int bone, float weight)
        {
            if (weight > 0f) values[bone] = values.TryGetValue(bone, out float old) ? old + weight : weight;
        }

        private void ApplyRig()
        {
            RestorePreviewPose();
            AddWristsToEditingRig();
            EnsureFolder("Assets/Resources/Player", "EditedMeshes");
            foreach (SkinnedMeshRenderer renderer in renderers)
            {
                Mesh working = renderer.sharedMesh;
                working.bindposes = renderer.bones.Select(bone => bone.worldToLocalMatrix * renderer.transform.localToWorldMatrix).ToArray();
                working.RecalculateBounds();
                string path = $"{MeshFolder}/{Sanitize(renderer.name)}.asset";
                Mesh saved = AssetDatabase.LoadAssetAtPath<Mesh>(path);
                if (saved == null)
                {
                    saved = Instantiate(working);
                    saved.name = renderer.name + " Edited";
                    AssetDatabase.CreateAsset(saved, path);
                }
                else
                {
                    EditorUtility.CopySerialized(working, saved);
                }
                renderer.sharedMesh = saved;
                EditorUtility.SetDirty(saved);
            }
            PrefabUtility.SaveAsPrefabAsset(editingRoot, EditedPrefabPath);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            EditorUtility.DisplayDialog("Player Rig", "Saved. The game will now load HumanRiggedEdited.prefab.", "OK");
        }

        private static void EnsureFolder(string parent, string child)
        {
            string path = parent + "/" + child;
            if (!AssetDatabase.IsValidFolder(path)) AssetDatabase.CreateFolder(parent, child);
        }

        private static string Sanitize(string value)
        {
            foreach (char invalid in System.IO.Path.GetInvalidFileNameChars()) value = value.Replace(invalid, '_');
            return value;
        }

        private static Transform FindChild(Transform parent, string name)
        {
            foreach (Transform child in parent.GetComponentsInChildren<Transform>(true))
                if (child.name == name) return child;
            return null;
        }
    }
}

