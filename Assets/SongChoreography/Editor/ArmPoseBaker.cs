using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace SongChoreography.Editor
{
    public static class ArmPoseBaker
    {
        [Serializable] public class Plan {public string[] motions=new string[0];public float fps=30,workBudgetMs=55;public int smoothingRadius=3;public bool exactSource;public string output=".generation/arm-pose-bake";}
        [Serializable] class Progress {public string state,motion;public int completed,total,frame,frames;}
        static Plan plan;static GeneratedLibrary library;static List<MotionDefinition> motions;static ArmPoseLibrary result,persisted;static int index,frame,frames;
        static Scene scene;static GameObject actor;static Animator animator;static MotionPlayer player;static ArmPoseTrack track;static Quaternion[][] raw;static Vector3[][] rawPositions;static Transform anchor;static string output;
        [MenuItem("Tools/CHOREO AI/Review/Bake continuous arm trajectories")]
        public static void Start()
        {
            if(plan!=null)throw new InvalidOperationException("手臂轨迹正在生成。");if(EditorApplication.isPlaying)throw new InvalidOperationException("请先停止 Play Mode。");
            var requested=File.Exists(".generation/arm-pose-bake-plan.json")?JsonUtility.FromJson<Plan>(File.ReadAllText(".generation/arm-pose-bake-plan.json")):new Plan();
            if(requested==null||requested.motions==null||!Choreographer.Finite(requested.fps)||requested.fps<15||requested.fps>120||requested.smoothingRadius<0||requested.smoothingRadius>12)throw new InvalidDataException("需要有效的动作列表、15–120 Hz 采样和 0–12 帧平滑半径。");
            output=Choreographer.ResolveLocal(GeneratedAssetBuilder.Root,requested.output);Directory.CreateDirectory(output);plan=requested;
            library=AssetDatabase.LoadAssetAtPath<GeneratedLibrary>(GeneratedAssetBuilder.LibraryPath);motions=library.catalog.motions.Where(m=>plan.motions.Length==0||plan.motions.Contains(m.id)).ToList();
            persisted=AssetDatabase.LoadAssetAtPath<ArmPoseLibrary>("Assets/Resources/ArmPoseLibrary.asset");result=ScriptableObject.CreateInstance<ArmPoseLibrary>();if(persisted)EditorUtility.CopySerialized(persisted,result);
            if(result.targetSourceHash!=library.catalog.characters[0].sha256)result.tracks=new ArmPoseTrack[0];
            result.targetSourceHash=library.catalog.characters[0].sha256;
            result.tracks=result.tracks.Where(t=>!motions.Any(m=>m.id==t.id)).ToArray();result.signature="baking";index=0;Save("running");EditorApplication.update+=Pump;AssemblyReloadEvents.beforeAssemblyReload+=Cancel;
        }
        static void Begin()
        {
            var m=motions[index];scene=EditorSceneManager.NewPreviewScene();actor=(GameObject)PrefabUtility.InstantiatePrefab(library.characters[0].prefab,scene);animator=actor.GetComponentInChildren<Animator>();player=new MotionPlayer(actor,true,false,true,plan.exactSource){BakeContinuity=true};
            var clip=library.Motion(m.id).clip;frames=Mathf.CeilToInt(clip.length*plan.fps)+1;track=new ArmPoseTrack{id=m.id,sourceHash=m.sha256,clip=clip,fps=plan.fps,leftUpper=new Quaternion[frames],leftLower=new Quaternion[frames],rightUpper=new Quaternion[frames],rightLower=new Quaternion[frames],leftHandTarget=new Vector3[frames],leftElbowTarget=new Vector3[frames],rightHandTarget=new Vector3[frames],rightElbowTarget=new Vector3[frames]};raw=Enumerable.Range(0,4).Select(_=>new Quaternion[frames]).ToArray();rawPositions=Enumerable.Range(0,4).Select(_=>new Vector3[frames]).ToArray();anchor=ArmPoseLibrary.Anchor(animator);frame=0;result.avatar=animator.avatar;
        }
        static void Pump()
        {
            long start=Stopwatch.GetTimestamp();
            try
            {
                while((Stopwatch.GetTimestamp()-start)/(double)Stopwatch.Frequency<Mathf.Clamp(plan.workBudgetMs,10,500)/1000d)
                {
                    if(index>=motions.Count){Finish();return;}if(track==null)Begin();player.Sample(track.clip,Mathf.Min(track.clip.length-.00001f,frame/plan.fps));
                    for(int bone=0;bone<4;bone++)raw[bone][frame]=player.SourceArmRotations[bone];
                    for(int bone=0;bone<4;bone++)rawPositions[bone][frame]=player.SourceArmPositions[bone];
                    var matrix=anchor.worldToLocalMatrix;track.leftHandTarget[frame]=matrix.MultiplyPoint3x4(animator.GetBoneTransform(HumanBodyBones.LeftHand).position);track.leftElbowTarget[frame]=matrix.MultiplyPoint3x4(animator.GetBoneTransform(HumanBodyBones.LeftLowerArm).position);track.rightHandTarget[frame]=matrix.MultiplyPoint3x4(animator.GetBoneTransform(HumanBodyBones.RightHand).position);track.rightElbowTarget[frame]=matrix.MultiplyPoint3x4(animator.GetBoneTransform(HumanBodyBones.RightLowerArm).position);
                    track.leftUpper[frame]=animator.GetBoneTransform(HumanBodyBones.LeftUpperArm).localRotation;track.leftLower[frame]=animator.GetBoneTransform(HumanBodyBones.LeftLowerArm).localRotation;track.rightUpper[frame]=animator.GetBoneTransform(HumanBodyBones.RightUpperArm).localRotation;track.rightLower[frame]=animator.GetBoneTransform(HumanBodyBones.RightLowerArm).localRotation;frame++;
                    if(frame>=frames){SmoothOffsets(track,raw);result.tracks=result.tracks.Concat(new[]{track}).ToArray();index++;End();Save("running");}
                }
            }
            catch(Exception e){File.WriteAllText(Path.Combine(output,"error.txt"),e.ToString());Cancel();return;}
            EditorApplication.QueuePlayerLoopUpdate();
        }
        static void SmoothOffsets(ArmPoseTrack track,Quaternion[][] source)
        {
            var positions=new[]{track.leftHandTarget,track.leftElbowTarget,track.rightHandTarget,track.rightElbowTarget};
            for(int bone=0;bone<4;bone++)
            {
                var delta=new Vector3[frames];for(int i=0;i<frames;i++)delta[i]=positions[bone][i]-rawPositions[bone][i];int radius=plan.smoothingRadius;float variance=Mathf.Max(.5f,radius*radius*.5f);
                for(int i=0;i<frames;i++){Vector3 sum=Vector3.zero;float total=0;for(int j=-radius;j<=radius;j++){float w=Mathf.Exp(-j*j/variance);sum+=delta[Mathf.Clamp(i+j,0,frames-1)]*w;total+=w;}positions[bone][i]=rawPositions[bone][i]+sum/total;}
            }
            // Filter the correction, preserving the authored dance accents.
            // Centred look-ahead starts clearance changes before body contact;
            // the runtime guard still projects the final pose out of the body.
            var channels=new[]{track.leftUpper,track.leftLower,track.rightUpper,track.rightLower};
            for(int bone=0;bone<4;bone++)
            {
                var offsets=new Quaternion[frames];for(int i=0;i<frames;i++)offsets[i]=channels[bone][i]*Quaternion.Inverse(source[bone][i]);
                for(int i=0;i<frames;i++)
                {
                    // Humanoid retargeting can switch its local twist frame
                    // near a straight elbow. Preserve the solver's compensation
                    // across that branch instead of smoothing it away.
                    int radius=plan.smoothingRadius;bool branch=false;for(int j=Mathf.Max(1,i-radius-1);j<=Mathf.Min(frames-1,i+radius+1);j++)for(int k=0;k<4;k++)if(Quaternion.Angle(source[k][j-1],source[k][j])>60)branch=true;
                    if(branch)continue;
                    Quaternion mean=Quaternion.identity;float total=0;
                    float variance=Mathf.Max(.5f,radius*radius*.5f);for(int j=-radius;j<=radius;j++){int at=Mathf.Clamp(i+j,0,frames-1);float weight=Mathf.Exp(-j*j/variance);mean=total==0?offsets[at]:Quaternion.Slerp(mean,offsets[at],weight/(total+weight));total+=weight;}
                    channels[bone][i]=mean*source[bone][i];
                }
            }
        }
        static void End(){player?.Dispose();player=null;if(actor)UnityEngine.Object.DestroyImmediate(actor);actor=null;if(scene.IsValid())EditorSceneManager.ClosePreviewScene(scene);track=null;}
        static void Save(string state)=>File.WriteAllText(Path.Combine(output,"progress.json"),JsonUtility.ToJson(new Progress{state=state,motion=index<motions.Count?motions[index].id:"",completed=index,total=motions.Count,frame=frame,frames=frames},true));
        static void Finish()
        {
            EditorApplication.update-=Pump;AssemblyReloadEvents.beforeAssemblyReload-=Cancel;
            result.signature=GeneratedAssetBuilder.Hash(JsonUtility.ToJson(result));string signature=result.signature;
            if(persisted){EditorUtility.CopySerialized(result,persisted);EditorUtility.SetDirty(persisted);UnityEngine.Object.DestroyImmediate(result);}else{persisted=result;AssetDatabase.CreateAsset(result,"Assets/Resources/ArmPoseLibrary.asset");}
            result=null;AssetDatabase.SaveAssets();Save("completed");UnityEngine.Debug.Log("ARM TRAJECTORY BAKE: "+motions.Count+" clips; "+signature);plan=null;
        }
        [MenuItem("Tools/CHOREO AI/Review/Cancel arm trajectory bake")]
        public static void Cancel(){if(plan==null)return;EditorApplication.update-=Pump;AssemblyReloadEvents.beforeAssemblyReload-=Cancel;End();if(result)UnityEngine.Object.DestroyImmediate(result);result=null;Save("interrupted");plan=null;}
    }
}
