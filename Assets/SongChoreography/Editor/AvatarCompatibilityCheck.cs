using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace SongChoreography.Editor
{
    public static class AvatarCompatibilityCheck
    {
        [Serializable] class BoneRecord {public string human,mapped;public Vector3 position;}
        [Serializable] class PoseRecord {public string motion;public float time,leftUpperArmAngle,rightUpperArmAngle,leftForearmAngle,rightForearmAngle,leftFootAngle,rightFootAngle,maxArmMuscleDifference;}
        [Serializable] class Report
        {public bool valid,human;public float humanScale,leftUpperArmLength,rightUpperArmLength,leftForearmLength,rightForearmLength,cpuSkinMaxVertexError,rawMeanMs,fixedMeanMs;public int vertices;public List<BoneRecord> rest=new List<BoneRecord>();public List<PoseRecord> comparisons=new List<PoseRecord>();}
        static Quaternion Frame(Animator a)
        {
            var up=(a.GetBoneTransform(HumanBodyBones.Neck).position-a.GetBoneTransform(HumanBodyBones.Hips).position).normalized;
            var right=(a.GetBoneTransform(HumanBodyBones.RightUpperArm).position-a.GetBoneTransform(HumanBodyBones.LeftUpperArm).position).normalized;return Quaternion.LookRotation(Vector3.Cross(right,up),up);
        }
        static Vector3 Direction(Animator a,HumanBodyBones first,HumanBodyBones second)=>Quaternion.Inverse(Frame(a))*(a.GetBoneTransform(second).position-a.GetBoneTransform(first).position).normalized;
        static float Compare(Animator a,Animator b,HumanBodyBones first,HumanBodyBones second)=>Vector3.Angle(Direction(a,first,second),Direction(b,first,second));
        [MenuItem("Tools/CHOREO AI/Review/Check avatar compatibility")]
        public static void Run()
        {
            var lib=AssetDatabase.LoadAssetAtPath<GeneratedLibrary>(GeneratedAssetBuilder.LibraryPath);var target=UnityEngine.Object.Instantiate(lib.characters[0].prefab);var a=target.GetComponentInChildren<Animator>();var report=new Report{valid=a.avatar.isValid,human=a.avatar.isHuman,humanScale=a.humanScale};var mesh=new Mesh();
            try
            {
                foreach(HumanBodyBones bone in Enum.GetValues(typeof(HumanBodyBones)))
                {if(bone==HumanBodyBones.LastBone)continue;var t=a.GetBoneTransform(bone);if(t)report.rest.Add(new BoneRecord{human=bone.ToString(),mapped=t.name,position=t.position});}
                Func<HumanBodyBones,HumanBodyBones,float> length=(x,y)=>Vector3.Distance(a.GetBoneTransform(x).position,a.GetBoneTransform(y).position);
                report.leftUpperArmLength=length(HumanBodyBones.LeftUpperArm,HumanBodyBones.LeftLowerArm);report.rightUpperArmLength=length(HumanBodyBones.RightUpperArm,HumanBodyBones.RightLowerArm);report.leftForearmLength=length(HumanBodyBones.LeftLowerArm,HumanBodyBones.LeftHand);report.rightForearmLength=length(HumanBodyBones.RightLowerArm,HumanBodyBones.RightHand);
                using(var raw=new MotionPlayer(target,false))
                foreach(string id in new[]{"mixamo-snake-hip-hop-dance-109070901","mixamo-hip-hop-dancing-101960901","mixamo-slide-hip-hop-dance-108870901"})
                {
                    var definition=lib.catalog.motions.First(m=>m.id==id);var wrapper=new GameObject("Source rig reference");var source=UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(definition.sourceFile));source.transform.SetParent(wrapper.transform,false);var b=source.GetComponentInChildren<Animator>();
                    try
                    {
                        using(var sourcePlayer=new MotionPlayer(wrapper,false))
                        foreach(float time in new[]{.3f,1.3f,Mathf.Min(lib.Motion(id).clip.length-.1f,7.666667f)})
                        {
                            raw.Sample(lib.Motion(id).clip,time);sourcePlayer.Sample(lib.Motion(id).clip,time);
                            var r=new PoseRecord{motion=id,time=time,leftUpperArmAngle=Compare(a,b,HumanBodyBones.LeftUpperArm,HumanBodyBones.LeftLowerArm),rightUpperArmAngle=Compare(a,b,HumanBodyBones.RightUpperArm,HumanBodyBones.RightLowerArm),leftForearmAngle=Compare(a,b,HumanBodyBones.LeftLowerArm,HumanBodyBones.LeftHand),rightForearmAngle=Compare(a,b,HumanBodyBones.RightLowerArm,HumanBodyBones.RightHand),leftFootAngle=Compare(a,b,HumanBodyBones.LeftFoot,HumanBodyBones.LeftToes),rightFootAngle=Compare(a,b,HumanBodyBones.RightFoot,HumanBodyBones.RightToes)};
                            var p1=new HumanPose();var p2=new HumanPose();using(var h1=new HumanPoseHandler(a.avatar,a.transform))h1.GetHumanPose(ref p1);using(var h2=new HumanPoseHandler(b.avatar,b.transform))h2.GetHumanPose(ref p2);
                            for(int i=0;i<HumanTrait.MuscleCount;i++)if(HumanTrait.MuscleName[i].Contains("Arm")||HumanTrait.MuscleName[i].Contains("Forearm")||HumanTrait.MuscleName[i].Contains("Hand"))r.maxArmMuscleDifference=Mathf.Max(r.maxArmMuscleDifference,Mathf.Abs(p1.muscles[i]-p2.muscles[i]));report.comparisons.Add(r);
                        }
                    }
                    finally{UnityEngine.Object.DestroyImmediate(wrapper);}
                }
                var skin=target.GetComponentInChildren<SkinnedMeshRenderer>();skin.BakeMesh(mesh,true);var actual=mesh.vertices;report.vertices=actual.Length;var sourceVertices=skin.sharedMesh.vertices;var weights=skin.sharedMesh.boneWeights;var bind=skin.sharedMesh.bindposes;var bones=skin.bones;
                for(int i=0;i<actual.Length;i+=17)
                {
                    var w=weights[i];var v=sourceVertices[i];var expected=bones[w.boneIndex0].localToWorldMatrix.MultiplyPoint3x4(bind[w.boneIndex0].MultiplyPoint3x4(v))*w.weight0+bones[w.boneIndex1].localToWorldMatrix.MultiplyPoint3x4(bind[w.boneIndex1].MultiplyPoint3x4(v))*w.weight1+bones[w.boneIndex2].localToWorldMatrix.MultiplyPoint3x4(bind[w.boneIndex2].MultiplyPoint3x4(v))*w.weight2+bones[w.boneIndex3].localToWorldMatrix.MultiplyPoint3x4(bind[w.boneIndex3].MultiplyPoint3x4(v))*w.weight3;
                    report.cpuSkinMaxVertexError=Mathf.Max(report.cpuSkinMaxVertexError,Vector3.Distance(expected,skin.transform.TransformPoint(actual[i])));
                }
                foreach(bool corrected in new[]{false,true})using(var player=new MotionPlayer(target,corrected))
                {
                    var watch=new System.Diagnostics.Stopwatch();player.Sample(lib.Motion("mixamo-snake-hip-hop-dance-109070901").clip,7.6);
                    watch.Start();for(int i=0;i<20;i++)player.Sample(lib.Motion("mixamo-snake-hip-hop-dance-109070901").clip,7.6+i/60d);watch.Stop();if(corrected)report.fixedMeanMs=(float)watch.Elapsed.TotalMilliseconds/20;else report.rawMeanMs=(float)watch.Elapsed.TotalMilliseconds/20;
                }
                Directory.CreateDirectory(".generation/arm-avoidance");File.WriteAllText(".generation/arm-avoidance/avatar-compatibility.json",JsonUtility.ToJson(report,true));
            }
            finally{UnityEngine.Object.DestroyImmediate(mesh);UnityEngine.Object.DestroyImmediate(target);}
        }
    }
}
