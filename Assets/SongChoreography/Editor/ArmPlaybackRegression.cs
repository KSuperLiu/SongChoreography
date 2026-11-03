using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace SongChoreography.Editor
{
    public static class ArmPlaybackRegression
    {
        [Serializable] class Report
        {public bool passed;public float repeatError,seekError,boneLengthError,wristRotationError,legPoseError,maxRawStep,maxFixedStep,maxAddedStep,maxAddedTime;public List<string> errors=new List<string>();}
        static Vector3 Relative(Animator a,HumanBodyBones bone)=>a.GetBoneTransform(bone).position-a.GetBoneTransform(HumanBodyBones.Hips).position;
        static Vector3[] Arms(Animator a)=>new[]{Relative(a,HumanBodyBones.LeftHand),Relative(a,HumanBodyBones.RightHand),Relative(a,HumanBodyBones.LeftLowerArm),Relative(a,HumanBodyBones.RightLowerArm)};
        static float Difference(Vector3[] a,Vector3[] b)=>Enumerable.Range(0,a.Length).Max(i=>Vector3.Distance(a[i],b[i]));
        [MenuItem("Tools/CHOREO AI/Review/Verify arm playback regression")]
        public static void Run()
        {
            var lib=AssetDatabase.LoadAssetAtPath<GeneratedLibrary>(GeneratedAssetBuilder.LibraryPath);var actor=UnityEngine.Object.Instantiate(lib.characters[0].prefab);var rawActor=UnityEngine.Object.Instantiate(lib.characters[0].prefab);var a=actor.GetComponentInChildren<Animator>();var b=rawActor.GetComponentInChildren<Animator>();var report=new Report();
            try
            {
                using(var fixedPlayer=new MotionPlayer(actor))using(var rawPlayer=new MotionPlayer(rawActor,false))
                {
                    var snake=lib.Motion("mixamo-snake-hip-hop-dance-109070901").clip;fixedPlayer.Sample(snake,7.666667);var first=Arms(a);
                    for(int i=0;i<20;i++){fixedPlayer.Sample(snake,7.666667);report.repeatError=Mathf.Max(report.repeatError,Difference(first,Arms(a)));}
                    foreach(string id in new[]{"mixamo-hip-hop-dancing-101960905","mixamo-hip-hop-dancing-101960901","mixamo-slide-hip-hop-dance-108870901"})
                    {fixedPlayer.Sample(lib.Motion(id).clip,1.37);fixedPlayer.Sample(snake,7.666667);report.seekError=Mathf.Max(report.seekError,Difference(first,Arms(a)));}
                    Vector3[] previousRaw=null,previousFixed=null;
                    for(int i=0;i<=169;i++)
                    {
                        float time=5.6f+i/30f;rawPlayer.Sample(snake,time);fixedPlayer.Sample(snake,time);var raw=Arms(b);var corrected=Arms(a);
                        if(previousRaw!=null)for(int j=0;j<2;j++)
                        {var d1=raw[j]-previousRaw[j];var d2=corrected[j]-previousFixed[j];report.maxRawStep=Mathf.Max(report.maxRawStep,d1.magnitude);report.maxFixedStep=Mathf.Max(report.maxFixedStep,d2.magnitude);if((d2-d1).magnitude>report.maxAddedStep){report.maxAddedStep=(d2-d1).magnitude;report.maxAddedTime=time;}}
                        previousRaw=raw;previousFixed=corrected;
                        foreach(bool left in new[]{true,false})
                        {
                            var upper=left?HumanBodyBones.LeftUpperArm:HumanBodyBones.RightUpperArm;var elbow=left?HumanBodyBones.LeftLowerArm:HumanBodyBones.RightLowerArm;var hand=left?HumanBodyBones.LeftHand:HumanBodyBones.RightHand;var foot=left?HumanBodyBones.LeftFoot:HumanBodyBones.RightFoot;
                            report.boneLengthError=Mathf.Max(report.boneLengthError,Mathf.Abs(Vector3.Distance(a.GetBoneTransform(upper).position,a.GetBoneTransform(elbow).position)-Vector3.Distance(b.GetBoneTransform(upper).position,b.GetBoneTransform(elbow).position)),Mathf.Abs(Vector3.Distance(a.GetBoneTransform(elbow).position,a.GetBoneTransform(hand).position)-Vector3.Distance(b.GetBoneTransform(elbow).position,b.GetBoneTransform(hand).position)));
                            report.wristRotationError=Mathf.Max(report.wristRotationError,Quaternion.Angle(a.GetBoneTransform(hand).rotation,b.GetBoneTransform(hand).rotation));report.legPoseError=Mathf.Max(report.legPoseError,Vector3.Distance(Relative(a,foot),Relative(b,foot)));
                        }
                    }
                }
                if(report.repeatError>.001f)report.errors.Add("Repeated paused samples drift.");if(report.seekError>.001f)report.errors.Add("Seeking changes the corrected pose.");if(report.boneLengthError>.0001f)report.errors.Add("Arm bone lengths changed.");if(report.wristRotationError>.2f)report.errors.Add("Authored wrist orientation changed.");if(report.legPoseError>.001f)report.errors.Add("Arm correction changed the leg pose.");if(report.maxAddedStep>.08f)report.errors.Add("Correction introduces excessive frame-to-frame hand movement.");
                report.passed=report.errors.Count==0;Directory.CreateDirectory(".generation/arm-avoidance");File.WriteAllText(".generation/arm-avoidance/playback-regression.json",JsonUtility.ToJson(report,true));UnityEngine.Debug.Log("ARM PLAYBACK REGRESSION: "+report.passed+", repeat="+report.repeatError+", seek="+report.seekError+", added step="+report.maxAddedStep);
            }
            finally{UnityEngine.Object.DestroyImmediate(actor);UnityEngine.Object.DestroyImmediate(rawActor);}
        }
    }
}
