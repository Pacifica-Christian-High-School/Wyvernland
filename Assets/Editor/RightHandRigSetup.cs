using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Wyvernland.Editor
{
    // Adds the hand to the existing rig without rebinding or resetting posed body joints.
    [InitializeOnLoad]
    public static class RightHandRigSetup
    {
        private const string Request = "Artifacts/Player/apply-right-hand.request";
        private const string RebindRightArmRequest = "Artifacts/Player/rebuild-right-arm-v5.request";
        private const string PrefabPath = "Assets/Resources/Player/HumanRiggedEdited.prefab";
        private static readonly string[] RightArmNames =
            { "RightShoulder", "RightElbow", "RightFingers1", "RightFingers2",
                "RightFingers3", "RightThumb1", "RightThumb2" };
        public static readonly string[] Names = { "RightFingers1", "RightFingers2", "RightFingers3", "RightThumb1", "RightThumb2" };
        private static readonly Vector3[] Positions = {
            new(0.0001f,0.00338f,0), new(-0.00002f,0.000281f,0), new(0.00001f,0.00047f,0),
            new(-0.00007f,0.00305f,-0.00045f), new(0.000007f,0.00027f,0.00004f) };
        private static readonly Vector3[] Rotations = { Vector3.zero,Vector3.zero,Vector3.zero,new(-40,0,10),new(26,0,0) };
        static RightHandRigSetup() { EditorApplication.update += ProcessRequest; }
        private static void ProcessRequest()
        {
            if (EditorApplication.isCompiling || EditorApplication.isUpdating ||
                EditorApplication.isPlayingOrWillChangePlaymode) return;
            if (File.Exists(RebindRightArmRequest))
            {
                File.Delete(RebindRightArmRequest);
                try { RebindRightArmBones(); }
                catch(Exception error)
                {
                    File.WriteAllText("Artifacts/Player/right-arm-rebind-result.txt",error.ToString());
                    Debug.LogException(error);
                }
                return;
            }
            if (!File.Exists(Request)) return;
            File.Delete(Request);
            try { Apply(); }
            catch(Exception error) { File.WriteAllText("Artifacts/Player/right-hand-result.txt", error.ToString()); Debug.LogException(error); }
        }
        [MenuItem("Wyvernland/Bind Right Hand Bones")]
        public static void Apply()
        {
            GameObject root = GameObject.Find("PLAYER RIG EDITOR");
            Require(root != null, "Open the player rig for editing first.");
            var body = root.GetComponentsInChildren<SkinnedMeshRenderer>().Single(r=>r.name=="HumanBody");
            var elbow = root.GetComponentsInChildren<Transform>(true).Single(t=>t.name=="RightElbow");
            if (Names.All(n=>body.bones.Any(b=>b.name==n))) { ValidateSaved(); VerifySavedDeformation(body); return; }
            Undo.RegisterFullObjectHierarchyUndo(root,"Bind right hand bones");
            Mesh original = body.sharedMesh;
            Vector3[] before = Bake(body);
            Transform[] joints = new Transform[5];
            for(int i=0;i<5;i++)
            {
                Transform parent = i==0 || i==3 ? elbow : joints[i-1];
                var joint = parent.Cast<Transform>().FirstOrDefault(t=>t.name==Names[i]) ??
                    parent.Cast<Transform>().FirstOrDefault(t=>t.name.StartsWith("GameObject") && Vector3.Distance(t.localPosition,Positions[i])<0.00003f);
                if(joint==null) { joint=new GameObject(Names[i]).transform; joint.SetParent(parent,false); Undo.RegisterCreatedObjectUndo(joint.gameObject,"Add hand bone"); }
                joint.name=Names[i]; joint.localPosition=Positions[i]; joint.localRotation=Quaternion.Euler(Rotations[i]); joint.localScale=Vector3.one;
                joints[i]=joint;
            }
            Mesh mesh=UnityEngine.Object.Instantiate(original); mesh.name="HumanBody Hand Rig";
            int elbowIndex=Array.IndexOf(body.bones,elbow), offset=body.bones.Length;
            Require(elbowIndex>=0,"Right elbow must be a deform bone.");
            Matrix4x4 elbowBind=original.bindposes[elbowIndex];
            var bindposes=original.bindposes.ToList();
            // Derive each new bind from the existing elbow bind, even when the elbow is posed.
            foreach(var joint in joints) bindposes.Add(joint.worldToLocalMatrix * elbow.localToWorldMatrix * elbowBind);
            body.bones=body.bones.Concat(joints).ToArray(); mesh.bindposes=bindposes.ToArray();
            Vector3[] vertices=mesh.vertices; BoneWeight[] weights=mesh.boneWeights;
            int[] regions=new int[vertices.Length];
            for(int i=0;i<vertices.Length;i++)
            {
                var values=Unpack(weights[i]);
                if(!values.TryGetValue(elbowIndex,out float available) || available<0.001f)continue;
                Vector3 p=elbowBind.MultiplyPoint3x4(vertices[i]);
                // Thumb lies on the negative-Z side, separated from the four-finger slab.
                bool thumb=p.y>0.0031f && ((p.z < -0.00050f && p.x < -0.00004f) || (p.z < -0.00030f && p.x < -0.00015f));
                float amount=thumb ? Smooth(0.00308f,0.00330f,p.y) : Smooth(0.00325f,0.00348f,p.y);
                if(amount<=0)continue;
                regions[i]=thumb?2:1;
                values[elbowIndex]=available*(1-amount);
                if(thumb)
                {
                    var q=Quaternion.Inverse(Quaternion.Euler(Rotations[3]))*(p-Positions[3]);
                    float distal=Smooth(0.00020f,0.00038f,q.y);
                    values[offset+3]=available*amount*(1-distal); values[offset+4]=available*amount*distal;
                }
                else
                {
                    float middle=Smooth(0.00356f,0.00380f,p.y), distal=Smooth(0.00403f,0.00423f,p.y);
                    values[offset]=available*amount*(1-middle);
                    values[offset+1]=available*amount*middle*(1-distal);
                    values[offset+2]=available*amount*middle*distal;
                }
                weights[i]=Pack(values);
            }
            mesh.boneWeights=weights; body.sharedMesh=mesh;
            var report=new List<string>();
            Validate(body,joints,report);
            Vector3[] neutral=Bake(body);
            float error=neutral.Select((p,i)=>body.transform.TransformVector(p-before[i]).magnitude).Max();
            Require(error<0.00001f,$"Binding changed the existing pose by {error} metres.");
            report.Add($"Neutral pose maximum change: {error:R} m");
            for(int j=0;j<5;j++)
            {
                Quaternion rest=joints[j].localRotation;
                Vector3[] posed;
                try { joints[j].localRotation=rest*Quaternion.Euler(0,0,35); posed=Bake(body); }
                finally { joints[j].localRotation=rest; }
                int moved=0; float maxMove=0;
                for(int i=0;i<posed.Length;i++)
                {
                    float delta=body.transform.TransformVector(posed[i]-neutral[i]).magnitude;
                    if(delta<0.000001f)continue;
                    Require(regions[i]==(j<3?1:2),$"{Names[j]} moved a vertex outside its hand region: {i}");
                    moved++; maxMove=Mathf.Max(maxMove,delta);
                }
                Require(moved>0,$"{Names[j]} does not deform any vertices.");
                report.Add($"{Names[j]}: isolated curl moves {moved} vertices; maximum {maxMove:R} m; opposite chain and body unchanged.");
            }
            if(!AssetDatabase.IsValidFolder("Assets/Resources/Player/EditedMeshes")) AssetDatabase.CreateFolder("Assets/Resources/Player","EditedMeshes");
            foreach(var renderer in root.GetComponentsInChildren<SkinnedMeshRenderer>())
            {
                string path=$"Assets/Resources/Player/EditedMeshes/{renderer.name}.asset";
                Mesh saved=AssetDatabase.LoadAssetAtPath<Mesh>(path);
                if(saved==null) { saved=UnityEngine.Object.Instantiate(renderer.sharedMesh); AssetDatabase.CreateAsset(saved,path); }
                else if(saved!=renderer.sharedMesh) EditorUtility.CopySerialized(renderer.sharedMesh,saved);
                renderer.sharedMesh=saved; EditorUtility.SetDirty(saved);
            }
            PrefabUtility.SaveAsPrefabAsset(root,PrefabPath);
            AssetDatabase.SaveAssets();
            ValidateSaved();
            EditorSceneManager.MarkSceneDirty(root.scene);
            EditorSceneManager.SaveScene(root.scene);
            report.Add("PASS: saved prefab reloaded; bone hierarchy, transforms, bind poses, and normalized weights verified.");
            File.WriteAllLines("Artifacts/Player/right-hand-result.txt",report);
            Selection.activeTransform=joints[0];
            if(SceneView.lastActiveSceneView!=null) { SceneView.lastActiveSceneView.pivot=elbow.TransformPoint(new Vector3(0,0.0036f,-0.0001f)); SceneView.lastActiveSceneView.size=0.16f; }
            SceneView.RepaintAll(); Debug.Log("RIGHT_HAND_RIG_PASS\n"+string.Join("\n",report));
        }

        [MenuItem("Wyvernland/Rebind Right Arm Bones")]
        public static void RebindRightArmBones()
        {
            const string version="RIGHT_ARM_SOURCE_REBUILD_V5";
            string backup="Artifacts/Player/BeforeRightArmSourceRebuild";
            Directory.CreateDirectory(backup);
            foreach(string path in new[]{PrefabPath,
                "Assets/Resources/Player/EditedMeshes/HumanBody.asset",
                "Assets/Resources/Player/EditedMeshes/HumanHead.asset"})
            {
                string destination=Path.Combine(backup,Path.GetFileName(path));
                if(!File.Exists(destination)) File.Copy(path,destination);
            }
            var source=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Resources/Player/HumanRigged.fbx");
            Require(source!=null,"Original rig is missing.");
            var original=source.GetComponentsInChildren<Transform>(true).ToDictionary(t=>t.name);
            GameObject root=PrefabUtility.LoadPrefabContents(PrefabPath);
            var report=new List<string>{version};
            try
            {
                var bones=root.GetComponentsInChildren<Transform>(true).ToDictionary(t=>t.name);
                foreach(string name in new[]{"RightShoulder","RightElbow"})
                {
                    Transform joint=bones[name], reference=original[name];
                    joint.SetParent(bones[reference.parent.name],false);
                    joint.localPosition=reference.localPosition;
                    joint.localRotation=reference.localRotation;
                    joint.localScale=reference.localScale;
                    report.Add(name+" restored from source: "+joint.localPosition.ToString("F8"));
                }
                for(int i=0;i<Names.Length;i++)
                {
                    Transform joint=bones[Names[i]];
                    joint.SetParent(i==0||i==3?bones["RightElbow"]:bones[Names[i-1]],false);
                    joint.localPosition=Positions[i];
                    joint.localRotation=Quaternion.Euler(Rotations[i]);
                    joint.localScale=Vector3.one;
                }
                foreach(var renderer in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                {
                    string path=$"Assets/Resources/Player/EditedMeshes/{renderer.name}.asset";
                    Mesh mesh=AssetDatabase.LoadAssetAtPath<Mesh>(path);
                    Require(mesh!=null,"Missing mesh: "+path);
                    renderer.sharedMesh=mesh;
                    var reference=source.GetComponentsInChildren<SkinnedMeshRenderer>(true)
                        .Single(r=>r.name==renderer.name);
                    Matrix4x4[] binds=mesh.bindposes;
                    Require(binds.Length==renderer.bones.Length,"Bone count mismatch.");
                    foreach(string name in new[]{"RightShoulder","RightElbow"})
                    {
                        int index=Array.FindIndex(renderer.bones,t=>t.name==name);
                        int sourceIndex=Array.FindIndex(reference.bones,t=>t.name==name);
                        Require(index>=0&&sourceIndex>=0,"Missing source arm influence.");
                        binds[index]=reference.sharedMesh.bindposes[sourceIndex];
                    }
                    int elbow=Array.FindIndex(renderer.bones,t=>t.name=="RightElbow");
                    foreach(string name in Names)
                    {
                        int index=Array.FindIndex(renderer.bones,t=>t.name==name);
                        if(index<0) continue;
                        binds[index]=bones[name].worldToLocalMatrix*
                            bones["RightElbow"].localToWorldMatrix*binds[elbow];
                    }
                    mesh.bindposes=binds;
                    EditorUtility.SetDirty(mesh);
                    report.Add(renderer.name+": source shoulder/elbow binds and elbow-relative hand binds restored.");
                }
                var body=root.GetComponentsInChildren<SkinnedMeshRenderer>(true).Single(r=>r.name=="HumanBody");
                Validate(body,Names.Select(n=>bones[n]).ToArray(),report);
                VerifySavedDeformation(body);
                VerifyPunchAnimation(root,body,bones,report);
                // Rotate each arm joint independently: vertices without any right-arm
                // influence must remain stationary, and every joint must deform skin.
                var neutral=Bake(body);
                foreach(string name in RightArmNames)
                {
                    var joint=bones[name];
                    var rest=joint.localRotation;
                    Vector3[] posed;
                    try { joint.localRotation=rest*Quaternion.Euler(0f,0f,20f); posed=Bake(body); }
                    finally { joint.localRotation=rest; }
                    int moved=0;
                    var weights=body.sharedMesh.boneWeights;
                    for(int i=0;i<posed.Length;i++)
                    {
                        float delta=(posed[i]-neutral[i]).magnitude;
                        Require(!float.IsNaN(delta)&&!float.IsInfinity(delta),"Invalid deformation.");
                        if(delta<0.000001f) continue;
                        Require(Unpack(weights[i]).Any(w=>RightArmNames.Contains(body.bones[w.Key].name)),
                            name+" moved vertices outside the right arm.");
                        moved++;
                    }
                    Require(moved>0,name+" does not deform the skin.");
                    report.Add(name+": independent rotation moves "+moved+" arm vertices.");
                }
                AssetDatabase.SaveAssets();
                AssetDatabase.ForceReserializeAssets(new[]{
                    "Assets/Resources/Player/EditedMeshes/HumanBody.asset",
                    "Assets/Resources/Player/EditedMeshes/HumanHead.asset"});
                PrefabUtility.SaveAsPrefabAsset(root,PrefabPath);
                // Refresh the open editable rig from the repaired asset without saving
                // unrelated scene changes or retaining a preview pose as a bind pose.
                var editing=GameObject.Find("PLAYER RIG EDITOR");
                if(editing!=null)
                {
                    Undo.RegisterFullObjectHierarchyUndo(editing,"Repair right arm");
                    var editBones=editing.GetComponentsInChildren<Transform>(true).ToDictionary(t=>t.name);
                    foreach(string name in RightArmNames)
                    {
                        var joint=editBones[name];
                        joint.SetParent(editBones[bones[name].parent.name],false);
                        joint.localPosition=bones[name].localPosition;
                        joint.localRotation=bones[name].localRotation;
                        joint.localScale=bones[name].localScale;
                    }
                    foreach(var renderer in editing.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                        renderer.sharedMesh=UnityEngine.Object.Instantiate(
                            AssetDatabase.LoadAssetAtPath<Mesh>($"Assets/Resources/Player/EditedMeshes/{renderer.name}.asset"));
                    foreach(var window in Resources.FindObjectsOfTypeAll<PlayerRigEditor>())
                        window.RefreshRestPoseAfterRepair();
                    EditorSceneManager.MarkSceneDirty(editing.scene);
                }
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
            var saved=AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            Require(saved.GetComponentsInChildren<SkinnedMeshRenderer>(true).All(r=>r.sharedMesh!=null),
                "Saved prefab lost a mesh reference.");
            ValidateSaved();
            report.Add("PASS: "+version+" saved and reloaded; hierarchy, source binding, weights and isolated deformations verified.");
            File.WriteAllLines("Artifacts/Player/right-arm-rebind-result.txt",report);
            Debug.Log(string.Join("\n",report));
            SceneView.RepaintAll();
        }

        public static void ValidateRegularHeldPunch()
        {
            var root=new GameObject("Regular held punch validation");
            var flags=System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance;
            try
            {
                var avatar=root.AddComponent<RiggedPlayerAvatar>();
                Action<string,object> set=(name,value)=>typeof(RiggedPlayerAvatar).GetField(name,flags).SetValue(avatar,value);
                var shoulder=new GameObject("Shoulder").transform;
                var elbow=new GameObject("Elbow").transform;
                shoulder.SetParent(root.transform); elbow.SetParent(shoulder);
                set("rightShoulder",shoulder); set("rightElbow",elbow);
                set("rightShoulderRestRotation",Quaternion.identity);
                set("rightElbowRestRotation",Quaternion.identity);
                set("rightElbowRestScale",Vector3.one);
                var digits=(Transform[])typeof(RiggedPlayerAvatar).GetField("punchDigits",flags).GetValue(avatar);
                var rests=(Quaternion[])typeof(RiggedPlayerAvatar).GetField("punchDigitRest",flags).GetValue(avatar);
                for(int i=0;i<digits.Length;i++)
                {
                    digits[i]=new GameObject(Names[i]).transform;
                    digits[i].SetParent(elbow); rests[i]=Quaternion.identity;
                }
                var item=new GameObject("Held object"); item.transform.SetParent(root.transform);
                var pickable=item.AddComponent<PickableObject>();
                var apply=typeof(RiggedPlayerAvatar).GetMethod("ApplyRightHandPunchPose",flags);
                foreach(bool impactReturn in new[]{false,true})
                foreach(float elapsed in new[]{0f,0.1f,0.3f,0.4f,0.5f,0.7f,1.2f})
                {
                    Quaternion[] baseline=null;
                    foreach(bool held in new[]{false,true})
                    {
                        set("heldRightHandItem",held?pickable:null); set("punching",false);
                        shoulder.localRotation=elbow.localRotation=Quaternion.identity;
                        foreach(var digit in digits) digit.localRotation=Quaternion.identity;
                        avatar.BeginRightHandPunch();
                        Require((bool)typeof(RiggedPlayerAvatar).GetField("punching",flags).GetValue(avatar),
                            "Regular held or empty-hand punch did not start.");
                        set("punchStartedAt",Time.time-elapsed);
                        set("punchImpactReturning",impactReturn);
                        set("punchImpactReturnStartedAt",Time.time-elapsed);
                        set("punchImpactShoulderReturnStart",Quaternion.Euler(20,30,40));
                        set("punchImpactElbowReturnStart",Quaternion.Euler(40,30,20));
                        var impactDigits=(Quaternion[])typeof(RiggedPlayerAvatar).GetField("punchImpactDigitReturnStart",flags).GetValue(avatar);
                        for(int i=0;i<impactDigits.Length;i++) impactDigits[i]=Quaternion.identity;
                        apply.Invoke(avatar,null);
                        var pose=new[]{shoulder.localRotation,elbow.localRotation}
                            .Concat(digits.Select(d=>d.localRotation)).ToArray();
                        if(!held) { baseline=pose; continue; }
                        for(int i=0;i<pose.Length;i++)
                            Require(Quaternion.Angle(pose[i],i==2?Quaternion.Euler(0,0,-30):baseline[i])<0.01f,
                                "Held punch changed an unexpected joint or lost its finger override.");
                    }
                }
                typeof(PickableObject).GetField("<GrabMode>k__BackingField",flags)
                    .SetValue(pickable,PickableGrabMode.Stick);
                set("punching",false); avatar.BeginRightHandPunch();
                Require(!(bool)typeof(RiggedPlayerAvatar).GetField("punching",flags).GetValue(avatar),
                    "Stick mode unexpectedly started a Regular punch.");
                set("punching",false);
                set("heldRightHandItem",pickable);
                var leftDigits=(Transform[])typeof(RiggedPlayerAvatar).GetField(
                    "leftGripDigits",flags).GetValue(avatar);
                for(int i=0;i<leftDigits.Length;i++)
                {
                    leftDigits[i]=new GameObject("Left "+Names[i]).transform;
                    leftDigits[i].SetParent(elbow);
                    digits[i].localRotation=Quaternion.identity;
                    leftDigits[i].localRotation=Quaternion.identity;
                }
                set("heldLeftHandItem",pickable);
                typeof(RiggedPlayerAvatar).GetMethod("ApplyRightHandObjectGripPose",flags)
                    .Invoke(avatar,null);
                typeof(RiggedPlayerAvatar).GetMethod("ApplyLeftHandObjectGripPose",flags)
                    .Invoke(avatar,null);
                Vector3[] fist={new(0,0,-24),new(0,0,75),new(0,0,80),
                    new(-40,0,15),new(55,0,0)};
                var mirror=typeof(RiggedPlayerAvatar).GetMethod("MirrorPunchPose",
                    flags|System.Reflection.BindingFlags.Static);
                for(int i=0;i<digits.Length;i++)
                {
                    Quaternion punchPose=Quaternion.Euler(fist[i]);
                    Require(Quaternion.Angle(digits[i].localRotation,punchPose)<0.01f&&
                        Quaternion.Angle(leftDigits[i].localRotation,
                            (Quaternion)mirror.Invoke(null,new object[]{punchPose}))<0.01f,
                        "Both hands retain the punch finger pose while holding a Stick.");
                }
                var heldPunch=typeof(RiggedPlayerAvatar).GetMethod(
                    "ApplyHeldPunchFingerPose",flags);
                foreach(bool left in new[]{false,true})
                {
                    set("punchUsingLeftHand",left);
                    Transform[] activeDigits=left?leftDigits:digits;
                    foreach(var digit in activeDigits) digit.localRotation=Quaternion.identity;
                    heldPunch.Invoke(avatar,null);
                    for(int i=0;i<activeDigits.Length;i++)
                    {
                        Quaternion punchPose=Quaternion.Euler(fist[i]);
                        if(left) punchPose=(Quaternion)mirror.Invoke(null,new object[]{punchPose});
                        Require(Quaternion.Angle(activeDigits[i].localRotation,punchPose)<0.01f,
                            "A held Stick keeps the punch finger pose during punch animation.");
                    }
                }
                Debug.Log("REGULAR_HELD_PUNCH_VALIDATION_PASS: matching empty-hand animation, exact finger override, contact return, and Stick exclusion.");
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
        }

        private static void VerifyPunchAnimation(GameObject rig,SkinnedMeshRenderer body,
            Dictionary<string,Transform> bones,List<string> report)
        {
            var probe=new GameObject("Punch validation");
            var avatar=probe.AddComponent<RiggedPlayerAvatar>();
            var flags=System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance;
            Action<string,object> set=(name,value)=>typeof(RiggedPlayerAvatar).GetField(name,flags).SetValue(avatar,value);
            var rest=RightArmNames.ToDictionary(n=>n,n=>bones[n].localRotation);
            var positions=RightArmNames.ToDictionary(n=>n,n=>bones[n].localPosition);
            try
            {
                set("rightShoulder",bones["RightShoulder"]);
                set("rightElbow",bones["RightElbow"]);
                set("rightShoulderRestPosition",positions["RightShoulder"]);
                set("rightElbowRestPosition",positions["RightElbow"]);
                set("rightElbowRestScale",bones["RightElbow"].localScale);
                set("rightShoulderRestRotation",rest["RightShoulder"]);
                set("rightElbowRestRotation",rest["RightElbow"]);
                set("punchShoulderStart",rest["RightShoulder"]);
                set("punchElbowStart",rest["RightElbow"]);
                var digits=(Transform[])typeof(RiggedPlayerAvatar).GetField("punchDigits",flags).GetValue(avatar);
                var digitRest=(Quaternion[])typeof(RiggedPlayerAvatar).GetField("punchDigitRest",flags).GetValue(avatar);
                var digitStart=(Quaternion[])typeof(RiggedPlayerAvatar).GetField("punchDigitStart",flags).GetValue(avatar);
                for(int i=0;i<Names.Length;i++)
                { digits[i]=bones[Names[i]]; digitRest[i]=rest[Names[i]]; digitStart[i]=rest[Names[i]]; }
                float[] times={0.5f/1.4f,0.6f/1.4f,0.7f/1.4f,0.8f/1.4f,
                    1.2f/1.4f,1.45f/1.4f};
                Quaternion[] shoulders={Quaternion.Euler(-180,-10,-110),Quaternion.Euler(150,3,-150),
                    Quaternion.Euler(180,-12,-150),Quaternion.Euler(185,-85,-110),
                    Quaternion.Euler(185,-85,-110)*Quaternion.Euler(10,0,0),rest["RightShoulder"]};
                Quaternion[] elbows={Quaternion.Euler(-100,200,-200),Quaternion.Euler(-105,-15,11),
                    Quaternion.Euler(-100,-170,165),Quaternion.Euler(-40,15,-7),
                    Quaternion.Euler(-40,15,-7)*Quaternion.Euler(-10,0,0),rest["RightElbow"]};
                Vector3[] fist={new(0,0,-24),new(0,0,75),new(0,0,80),new(-40,0,15),new(55,0,0)};
                var apply=typeof(RiggedPlayerAvatar).GetMethod("ApplyRightHandPunchPose",flags);
                for(int stage=0;stage<times.Length;stage++)
                {
                    // Simulate another animation writing first; punch must win.
                    bones["RightShoulder"].localRotation=Quaternion.Euler(10,20,30);
                    bones["RightElbow"].localPosition=Vector3.one;
                    set("punching",true);
                    set("punchStartedAt",Time.time-times[stage]);
                    apply.Invoke(avatar,null);
                    Require(Quaternion.Angle(bones["RightShoulder"].localRotation,shoulders[stage])<1f,
                        "Incorrect punch shoulder at "+times[stage]);
                    Require(Quaternion.Angle(bones["RightElbow"].localRotation,elbows[stage])<1f,
                        "Incorrect punch elbow at "+times[stage]);
                    Require(Vector3.Distance(bones["RightElbow"].localPosition,positions["RightElbow"])<1e-7f,
                        "Punch allowed elbow displacement.");
                    for(int i=0;i<Names.Length;i++)
                        Require(Quaternion.Angle(digits[i].localRotation,stage==5?digitRest[i]:Quaternion.Euler(fist[i]))<1f,
                            "Incorrect punch digit "+Names[i]);
                    Require(Bake(body).All(v=>float.IsFinite(v.x)&&float.IsFinite(v.y)&&float.IsFinite(v.z)),
                        "Non-finite punch mesh.");
                }
                var contactMethod=typeof(RiggedPlayerAvatar).GetMethod(
                    "OnRightHandPunchContact",flags);
                var obstacle=new GameObject("Punch obstacle");
                var obstacleCollider=obstacle.AddComponent<BoxCollider>();
                try
                {
                    set("punching",true);
                    set("punchStartedAt",Time.time-0.45f);
                    contactMethod.Invoke(avatar,new object[]{obstacleCollider});
                    bool slowed=(bool)typeof(RiggedPlayerAvatar)
                        .GetField("punchImpactSlowActive",flags).GetValue(avatar);
                    float slowUntil=(float)typeof(RiggedPlayerAvatar)
                        .GetField("punchImpactSlowUntil",flags).GetValue(avatar);
                    Require(slowed&&Mathf.Abs((slowUntil-Time.time)-0.2f)<0.02f,
                        "Solid hand contact did not start the 5% speed interval.");
                    set("punchImpactSlowUntil",Time.time+0.1f);
                    float beforeSlowApply=Time.time-(float)typeof(RiggedPlayerAvatar)
                        .GetField("punchStartedAt",flags).GetValue(avatar);
                    apply.Invoke(avatar,null);
                    float afterSlowApply=Time.time-(float)typeof(RiggedPlayerAvatar)
                        .GetField("punchStartedAt",flags).GetValue(avatar);
                    Require(Mathf.Abs((afterSlowApply-beforeSlowApply)-0.005f)<0.002f,
                        "Impact interval did not advance at 5% of normal speed.");
                    set("punchImpactSlowUntil",Time.time-0.01f);
                    apply.Invoke(avatar,null);
                    bool returning=(bool)typeof(RiggedPlayerAvatar)
                        .GetField("punchImpactReturning",flags).GetValue(avatar);
                    Require(returning,"Impact slowdown did not enter the arm-return path.");
                    set("punchImpactReturnStartedAt",Time.time-1f);
                    apply.Invoke(avatar,null);
                    bool stillPunching=(bool)typeof(RiggedPlayerAvatar)
                        .GetField("punching",flags).GetValue(avatar);
                    Require(!stillPunching,"Impact return did not stop the punch.");
                    obstacleCollider.isTrigger=true;
                    set("punching",true);
                    set("punchImpactSlowActive",false);
                    set("punchImpactReturning",false);
                    set("punchStartedAt",Time.time-0.3f);
                    contactMethod.Invoke(avatar,new object[]{obstacleCollider});
                    slowed=(bool)typeof(RiggedPlayerAvatar)
                        .GetField("punchImpactSlowActive",flags).GetValue(avatar);
                    Require(!slowed,"Trigger collider incorrectly slowed the punch.");
                }
                finally { UnityEngine.Object.DestroyImmediate(obstacle); }

                var sensorObject=new GameObject("Punch sensor validation");
                var sensorSphere=sensorObject.AddComponent<SphereCollider>();
                var sensor=sensorObject.AddComponent<RightHandPunchContactTrigger>();
                var ownObject=new GameObject("Own collider");
                ownObject.transform.SetParent(rig.transform,false);
                var ownCollider=ownObject.AddComponent<BoxCollider>();
                var externalObject=new GameObject("External collider");
                var externalCollider=externalObject.AddComponent<BoxCollider>();
                int reported=0;
                try
                {
                    sensor.Initialize(sensorSphere,rig.transform,0.18f,_=>reported++);
                    var reportContact=typeof(RightHandPunchContactTrigger)
                        .GetMethod("Report",flags);
                    reportContact.Invoke(sensor,new object[]{ownCollider});
                    Require(reported==0,"The punch sphere reported the player's own collider.");
                    externalCollider.isTrigger=true;
                    reportContact.Invoke(sensor,new object[]{externalCollider});
                    Require(reported==0,"The punch sphere reported a trigger collider.");
                    externalCollider.isTrigger=false;
                    reportContact.Invoke(sensor,new object[]{externalCollider});
                    Require(reported==1,"The punch sphere did not report a solid external collider.");
                }
                finally
                {
                    UnityEngine.Object.DestroyImmediate(sensorObject);
                    UnityEngine.Object.DestroyImmediate(ownObject);
                    UnityEngine.Object.DestroyImmediate(externalObject);
                }
                report.Add("PASS: runtime Ready, Strike, Intermediate, Extended, follow-through and idle return match specified angles, timing and fixed joint positions.");
                report.Add("PASS: right-hand impact runs at 5% speed for 0.2 seconds, then smoothly returns the arm and stops the punch; triggers and player colliders are ignored.");
            }
            finally
            {
                foreach(string name in RightArmNames)
                { bones[name].localRotation=rest[name]; bones[name].localPosition=positions[name]; }
                UnityEngine.Object.DestroyImmediate(probe);
            }
        }
        private static void VerifySavedDeformation(SkinnedMeshRenderer body)
        {
            var joints=Names.Select(n=>body.bones.Single(b=>b.name==n)).ToArray();
            var report=new List<string>(); Validate(body,joints,report);
            var neutral=Bake(body); var mesh=body.sharedMesh;
            int elbow=Array.FindIndex(body.bones,b=>b.name=="RightElbow");
            // Reconstruct the original elbow-only skin to verify preservation independently.
            var baseline=UnityEngine.Object.Instantiate(mesh); var weights=baseline.boneWeights;
            for(int i=0;i<weights.Length;i++)
            {
                var d=Unpack(weights[i]); float hand=0;
                foreach(int index in d.Keys.ToArray()) if(Names.Contains(body.bones[index].name)) { hand+=d[index]; d.Remove(index); }
                if(hand>0) d[elbow]=(d.TryGetValue(elbow,out float old)?old:0)+hand;
                weights[i]=Pack(d);
            }
            baseline.boneWeights=weights; Vector3[] original;
            try { body.sharedMesh=baseline; original=Bake(body); }
            finally { body.sharedMesh=mesh; UnityEngine.Object.DestroyImmediate(baseline); }
            float error=neutral.Select((p,i)=>body.transform.TransformVector(p-original[i]).magnitude).Max();
            Require(error<0.00001f,"Neutral skin changed."); report.Add($"Neutral maximum difference from original elbow binding: {error:R} m");
            for(int j=0;j<joints.Length;j++)
            {
                var rest=joints[j].localRotation;Vector3[] posed;
                try { joints[j].localRotation=rest*Quaternion.Euler(0,0,35);posed=Bake(body); }
                finally { joints[j].localRotation=rest; }
                int moved=0; float maximum=0;
                for(int i=0;i<posed.Length;i++)
                {
                    float delta=body.transform.TransformVector(posed[i]-neutral[i]).magnitude;
                    if(delta<0.000001f)continue;
                    Vector3 p=mesh.bindposes[elbow].MultiplyPoint3x4(mesh.vertices[i]);
                    bool thumb=p.y>0.0031f && ((p.z < -0.00050f && p.x < -0.00004f) || (p.z < -0.00030f && p.x < -0.00015f));
                    Require((j>=3)==thumb && p.y>0.0031f,"Curl affected the opposite chain or body.");
                    moved++;maximum=Mathf.Max(maximum,delta);
                }
                Require(moved>0 && maximum<0.15f,"Invalid hand deformation.");
                report.Add($"{Names[j]}: 35-degree isolated curl moves {moved} vertices, maximum {maximum:R} m; other chain and body unchanged.");
            }
            report.Add("PASS: saved prefab, exact transforms, hierarchy, bind poses, normalized weights, neutral pose and independent curls.");
            File.WriteAllLines("Artifacts/Player/right-hand-result.txt",report);
            Debug.Log("RIGHT_HAND_RIG_PASS: persisted hand deformation checks passed.");
        }
        public static void ValidateSaved()
        {
            var root=UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath));
            try { var body=root.GetComponentsInChildren<SkinnedMeshRenderer>().Single(r=>r.name=="HumanBody"); Validate(body,Names.Select(n=>root.GetComponentsInChildren<Transform>().Single(t=>t.name==n)).ToArray(),new List<string>()); }
            finally { UnityEngine.Object.DestroyImmediate(root); }
        }
        private static void Validate(SkinnedMeshRenderer body,Transform[] joints,List<string> report)
        {
            var mesh=body.sharedMesh; int e=Array.FindIndex(body.bones,b=>b.name=="RightElbow");
            Require(mesh.bindposes.Length==body.bones.Length,"Bind-pose and bone counts differ.");
            for(int i=0;i<5;i++)
            {
                Require(joints[i].parent==(i==0 || i==3?body.bones[e]:joints[i-1]),"Wrong hand hierarchy.");
                Require(Vector3.Distance(joints[i].localPosition,Positions[i])<1e-8f && Quaternion.Angle(joints[i].localRotation,Quaternion.Euler(Rotations[i]))<0.001f,"Wrong hand transform.");
                int index=Array.IndexOf(body.bones,joints[i]); Require(index>=0,"Unregistered deform bone.");
                Matrix4x4 expected=joints[i].worldToLocalMatrix*body.bones[e].localToWorldMatrix*mesh.bindposes[e];
                for(int k=0;k<16;k++) Require(Mathf.Abs(expected[k]-mesh.bindposes[index][k])<0.0001f,"Incorrect hand bind pose.");
                Require(mesh.boneWeights.Any(w=>Unpack(w).TryGetValue(index,out float weight)&&weight>0.001f),"Bone has no skin weights.");
                report.Add($"{Names[i]} parent={joints[i].parent.name} position={joints[i].localPosition.ToString("F8")} rotation={Rotations[i]}");
            }
            foreach(var w in mesh.boneWeights)
            {
                var values=Unpack(w); Require(Mathf.Abs(values.Values.Sum()-1)<0.00001f,"Unnormalized skin weights.");
                Require(values.All(p=>p.Key>=0 && p.Key<body.bones.Length && p.Value>=0 && !float.IsNaN(p.Value)),"Invalid skin influence.");
            }
        }
        private static Vector3[] Bake(SkinnedMeshRenderer renderer) { var m=new Mesh(); renderer.BakeMesh(m,true); var v=m.vertices; UnityEngine.Object.DestroyImmediate(m); return v; }
        private static float Smooth(float a,float b,float v) { float t=Mathf.InverseLerp(a,b,v);return t*t*(3-2*t); }
        private static Dictionary<int,float> Unpack(BoneWeight w)
        {
            var d=new Dictionary<int,float>(); int[] ids={w.boneIndex0,w.boneIndex1,w.boneIndex2,w.boneIndex3}; float[] ws={w.weight0,w.weight1,w.weight2,w.weight3};
            for(int i=0;i<4;i++)if(ws[i]>0)d[ids[i]]=d.TryGetValue(ids[i],out float old)?old+ws[i]:ws[i];return d;
        }
        private static BoneWeight Pack(Dictionary<int,float> d)
        {
            var a=d.Where(p=>p.Value>0).OrderByDescending(p=>p.Value).Take(4).ToArray();float total=a.Sum(p=>p.Value);var w=new BoneWeight();
            for(int i=0;i<a.Length;i++) { int b=a[i].Key;float v=a[i].Value/total; if(i==0){w.boneIndex0=b;w.weight0=v;}if(i==1){w.boneIndex1=b;w.weight1=v;}if(i==2){w.boneIndex2=b;w.weight2=v;}if(i==3){w.boneIndex3=b;w.weight3=v;} }return w;
        }
        private static void Require(bool condition,string message) { if(!condition)throw new InvalidOperationException(message); }
    }
}


