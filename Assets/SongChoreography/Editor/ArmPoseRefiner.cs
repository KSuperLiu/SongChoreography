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
    // Alternate a centred trajectory filter and a length-preserving contact
    // projection. The working library stays private until every pass finishes.
    public static class ArmPoseRefiner
    {
        [Serializable] public class Plan {public string[] motions=new string[0];public int passes=8,radius=2;public float workBudgetMs=500;public bool exact,cropped,worldSpace;public string output=".generation/arm-pose-refinement";}
        [Serializable] class Progress {public string state,motion;public int completed,total,pass,passes,frame,frames;}
        static Plan plan;static GeneratedLibrary library;static ArmPoseLibrary result,persisted;static List<MotionDefinition> motions;static int index,pass,frame,first,last;static ArmPoseTrack input,output;
        static Scene scene;static GameObject actor;static Animator animator;static MotionPlayer player;static ArmBodyAvoidance guard;static Transform anchor;static string folder;
        static readonly HumanBodyBones[] Bones={HumanBodyBones.LeftUpperArm,HumanBodyBones.LeftLowerArm,HumanBodyBones.RightUpperArm,HumanBodyBones.RightLowerArm};
        [MenuItem("Tools/CHOREO AI/Review/Refine continuous arm trajectories")]
        public static void Start()
        {
            if(plan!=null||EditorApplication.isPlaying)throw new InvalidOperationException("请先停止播放或现有修正任务。");
            var p=JsonUtility.FromJson<Plan>(File.ReadAllText(".generation/arm-pose-refine-plan.json"));if(p.passes<1||p.passes>16||p.radius<1||p.radius>6)throw new InvalidDataException("无效的轨迹优化参数。");
            library=Resources.Load<GeneratedLibrary>("GeneratedLibrary");persisted=AssetDatabase.LoadAssetAtPath<ArmPoseLibrary>("Assets/Resources/ArmPoseLibrary.asset");
            result=ScriptableObject.CreateInstance<ArmPoseLibrary>();EditorUtility.CopySerialized(persisted,result);result.signature="refining";
            motions=library.catalog.motions.Where(m=>p.motions.Contains(m.id)).ToList();if(motions.Any(m=>!result.tracks.Any(t=>t.id==m.id&&t.HasPositionTargets)))throw new InvalidDataException("需要先生成完整的位置与旋转轨迹。");
            folder=Choreographer.ResolveLocal(GeneratedAssetBuilder.Root,p.output);Directory.CreateDirectory(folder);plan=p;index=pass=0;Save("running");EditorApplication.update+=Pump;AssemblyReloadEvents.beforeAssemblyReload+=Cancel;
        }
        static ArmPoseTrack Clone(ArmPoseTrack t)=>JsonUtility.FromJson<ArmPoseTrack>(JsonUtility.ToJson(t));
        static void Begin()
        {
            var m=motions[index];scene=EditorSceneManager.NewPreviewScene();actor=(GameObject)PrefabUtility.InstantiatePrefab(library.characters[0].prefab,scene);animator=actor.GetComponentInChildren<Animator>();anchor=ArmPoseLibrary.Anchor(animator);player=new MotionPlayer(actor,false);guard=new ArmBodyAvoidance(actor,animator,plan.exact);pass=0;BeginPass();
        }
        static void BeginPass()
        {
            input=result.tracks.First(t=>t.id==motions[index].id);int count=input.leftUpper.Length;first=plan.cropped?Mathf.Max(0,Mathf.FloorToInt(((float)motions[index].clipStart-.25f)*input.fps)):0;last=plan.cropped?Mathf.Min(count-1,Mathf.CeilToInt(((float)motions[index].clipEnd+.25f)*input.fps)):count-1;
            var filtered=Clone(input);Smooth(input,filtered);result.tracks[Array.FindIndex(result.tracks,t=>t.id==input.id)]=filtered;input=filtered;output=Clone(filtered);frame=first;Save("running");
        }
        static void Smooth(ArmPoseTrack source,ArmPoseTrack target)
        {
            if(plan.worldSpace){SmoothWorld(source,target);return;}
            var a=new[]{source.leftHandTarget,source.leftElbowTarget,source.rightHandTarget,source.rightElbowTarget};var b=new[]{target.leftHandTarget,target.leftElbowTarget,target.rightHandTarget,target.rightElbowTarget};
            var qa=new[]{source.leftUpper,source.leftLower,source.rightUpper,source.rightLower};var qb=new[]{target.leftUpper,target.leftLower,target.rightUpper,target.rightLower};float variance=plan.radius*plan.radius*.5f;
            for(int i=first;i<=last;i++)for(int k=0;k<4;k++)
            {
                Vector3 v=Vector3.zero;Quaternion q=Quaternion.identity;float total=0;
                for(int j=-plan.radius;j<=plan.radius;j++){int at=Mathf.Clamp(i+j,0,a[k].Length-1);float w=Mathf.Exp(-j*j/variance);v+=a[k][at]*w;q=total==0?qa[k][at]:Quaternion.Slerp(q,qa[k][at],w/(total+w));total+=w;}
                b[k][i]=v/total;qb[k][i]=q;
            }
        }
        static void SmoothWorld(ArmPoseTrack source,ArmPoseTrack target)
        {
            // Floor work and spins counter-rotate the arms against the torso.
            // Smooth their hip-relative world trajectories, retaining that
            // counter-rotation instead of averaging in the spinning chest frame.
            var a=new[]{source.leftHandTarget,source.leftElbowTarget,source.rightHandTarget,source.rightElbowTarget};var b=new[]{target.leftHandTarget,target.leftElbowTarget,target.rightHandTarget,target.rightElbowTarget};
            var qa=new[]{source.leftUpper,source.leftLower,source.rightUpper,source.rightLower};var qb=new[]{target.leftUpper,target.leftLower,target.rightUpper,target.rightLower};int count=a[0].Length;
            var points=Enumerable.Range(0,4).Select(_=>new Vector3[count]).ToArray();var rotations=Enumerable.Range(0,4).Select(_=>new Quaternion[count]).ToArray();
            for(int i=0;i<count;i++)
            {
                player.Sample(source.clip,Mathf.Min(source.clip.length-.00001f,i/source.fps));var hips=animator.GetBoneTransform(HumanBodyBones.Hips).position;
                for(int k=0;k<4;k++)points[k][i]=anchor.TransformPoint(a[k][i])-hips;
                for(int k=0;k<4;k++)animator.GetBoneTransform(Bones[k]).localRotation=qa[k][i];
                for(int k=0;k<4;k++)rotations[k][i]=animator.GetBoneTransform(Bones[k]).rotation;
            }
            float variance=plan.radius*plan.radius*.5f;
            for(int i=first;i<=last;i++)
            {
                player.Sample(source.clip,Mathf.Min(source.clip.length-.00001f,i/source.fps));var hips=animator.GetBoneTransform(HumanBodyBones.Hips).position;var matrix=anchor.worldToLocalMatrix;
                for(int k=0;k<4;k++)
                {
                    Vector3 p=Vector3.zero;Quaternion q=Quaternion.identity;float total=0;
                    for(int j=-plan.radius;j<=plan.radius;j++){int at=Mathf.Clamp(i+j,0,count-1);float w=Mathf.Exp(-j*j/variance);p+=points[k][at]*w;q=total==0?rotations[k][at]:Quaternion.Slerp(q,rotations[k][at],w/(total+w));total+=w;}
                    b[k][i]=matrix.MultiplyPoint3x4(hips+p/total);animator.GetBoneTransform(Bones[k]).rotation=q;
                }
                for(int k=0;k<4;k++)qb[k][i]=animator.GetBoneTransform(Bones[k]).localRotation;
            }
        }
        static void Pump()
        {
            long start=Stopwatch.GetTimestamp();try
            {
                while((Stopwatch.GetTimestamp()-start)/(double)Stopwatch.Frequency<Mathf.Clamp(plan.workBudgetMs,10,500)/1000d)
                {
                    if(index>=motions.Count){Finish();return;}if(!actor)Begin();float t=Mathf.Min(input.clip.length-.00001f,frame/input.fps);player.Sample(input.clip,t);result.Apply(animator,input.clip,t,null,0,1,out _);guard.Apply();
                    var q=new[]{output.leftUpper,output.leftLower,output.rightUpper,output.rightLower};for(int i=0;i<4;i++)q[i][frame]=animator.GetBoneTransform(Bones[i]).localRotation;
                    var matrix=anchor.worldToLocalMatrix;output.leftHandTarget[frame]=matrix.MultiplyPoint3x4(animator.GetBoneTransform(HumanBodyBones.LeftHand).position);output.leftElbowTarget[frame]=matrix.MultiplyPoint3x4(animator.GetBoneTransform(HumanBodyBones.LeftLowerArm).position);output.rightHandTarget[frame]=matrix.MultiplyPoint3x4(animator.GetBoneTransform(HumanBodyBones.RightHand).position);output.rightElbowTarget[frame]=matrix.MultiplyPoint3x4(animator.GetBoneTransform(HumanBodyBones.RightLowerArm).position);frame++;
                    if(frame>last){result.tracks[Array.FindIndex(result.tracks,t=>t.id==input.id)]=output;pass++;if(pass<plan.passes)BeginPass();else{End();index++;Save("running");}}
                }
            }
            catch(Exception e){File.WriteAllText(Path.Combine(folder,"error.txt"),e.ToString());Cancel();return;}EditorApplication.QueuePlayerLoopUpdate();
        }
        static void End(){player?.Dispose();player=null;guard?.Dispose();guard=null;if(actor)UnityEngine.Object.DestroyImmediate(actor);actor=null;if(scene.IsValid())EditorSceneManager.ClosePreviewScene(scene);input=output=null;}
        static void Save(string state)=>File.WriteAllText(Path.Combine(folder,"progress.json"),JsonUtility.ToJson(new Progress{state=state,motion=index<motions.Count?motions[index].id:"",completed=index,total=motions.Count,pass=pass,passes=plan.passes,frame=frame,frames=last+1},true));
        static void Finish(){EditorApplication.update-=Pump;AssemblyReloadEvents.beforeAssemblyReload-=Cancel;result.signature=GeneratedAssetBuilder.Hash(JsonUtility.ToJson(result));EditorUtility.CopySerialized(result,persisted);EditorUtility.SetDirty(persisted);AssetDatabase.SaveAssets();UnityEngine.Object.DestroyImmediate(result);result=null;Save("completed");plan=null;UnityEngine.Debug.Log("ARM TRAJECTORY REFINEMENT COMPLETE: "+motions.Count);}
        [MenuItem("Tools/CHOREO AI/Review/Cancel arm trajectory refinement")]
        public static void Cancel(){if(plan==null)return;EditorApplication.update-=Pump;AssemblyReloadEvents.beforeAssemblyReload-=Cancel;End();if(result)UnityEngine.Object.DestroyImmediate(result);result=null;Save("interrupted");plan=null;}
    }
}
