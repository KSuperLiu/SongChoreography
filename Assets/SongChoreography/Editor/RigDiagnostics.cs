using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

namespace SongChoreography.Editor
{
    public static class RigDiagnostics
    {
        [Serializable]class Entry {public string character,muscle;public float value,retrieved,poseRoundTripAngle,clipAngle,clipFootGap,minimum,maximum,upperLegTwist,lowerLegTwist;}
        [Serializable]class Report {public string unity;public List<Entry> cases=new List<Entry>();}
        public static void Run()
        {
            var report=new Report{unity=Application.unityVersion};
            foreach(string sex in new[]{"female","male"})
            {
                var dancer=DancerCharacters.Create(sex);var animator=dancer.GetComponentInChildren<Animator>();
                animator.applyRootMotion=false;animator.cullingMode=AnimatorCullingMode.AlwaysAnimate;
                var thigh=animator.GetBoneTransform(HumanBodyBones.LeftUpperLeg);var foot=animator.GetBoneTransform(HumanBodyBones.LeftFoot);
                using(var handler=new HumanPoseHandler(animator.avatar,animator.transform))
                {
                    var baseline=new HumanPose{muscles=new float[HumanTrait.MuscleCount]};handler.GetHumanPose(ref baseline);
                    foreach(string name in new[]{"Left Upper Leg Twist In-Out","Left Foot Twist In-Out","Spine Twist Left-Right"})
                    {
                        int muscle=Array.IndexOf(HumanTrait.MuscleName,name);
                        foreach(float value in new[]{0f,.5f,1f,2f,3f,-3f,6f})
                        {
                            var pose=new HumanPose{bodyPosition=baseline.bodyPosition,bodyRotation=baseline.bodyRotation,muscles=new float[HumanTrait.MuscleCount]};pose.muscles[muscle]=value;
                            handler.SetHumanPose(ref pose);var target=name.Contains("Foot")?foot:thigh;Quaternion expected=target.rotation;Vector3 expectedFoot=foot.position;
                            var read=new HumanPose{muscles=new float[HumanTrait.MuscleCount]};handler.GetHumanPose(ref read);handler.SetHumanPose(ref read);
                            var entry=new Entry{character=sex,muscle=name,value=value,retrieved=read.muscles[muscle],poseRoundTripAngle=Quaternion.Angle(expected,target.rotation),minimum=HumanTrait.GetMuscleDefaultMin(muscle),maximum=HumanTrait.GetMuscleDefaultMax(muscle),upperLegTwist=animator.avatar.humanDescription.upperLegTwist,lowerLegTwist=animator.avatar.humanDescription.lowerLegTwist};
                            var clip=new AnimationClip();
                            for(int i=0;i<HumanTrait.MuscleCount;i++)clip.SetCurve("",typeof(Animator),HumanTrait.MuscleName[i],AnimationCurve.Constant(0,1,read.muscles[i]));
                            float[] root={read.bodyPosition.x,read.bodyPosition.y,read.bodyPosition.z,read.bodyRotation.x,read.bodyRotation.y,read.bodyRotation.z,read.bodyRotation.w};
                            string[] fields={"RootT.x","RootT.y","RootT.z","RootQ.x","RootQ.y","RootQ.z","RootQ.w"};
                            for(int i=0;i<7;i++)clip.SetCurve("",typeof(Animator),fields[i],AnimationCurve.Constant(0,1,root[i]));
                            var settings=AnimationUtility.GetAnimationClipSettings(clip);settings.keepOriginalOrientation=settings.keepOriginalPositionY=settings.keepOriginalPositionXZ=true;settings.loopBlendOrientation=settings.loopBlendPositionY=settings.loopBlendPositionXZ=true;AnimationUtility.SetAnimationClipSettings(clip,settings);
                            var graph=PlayableGraph.Create("Rig round trip");graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
                            var playable=AnimationClipPlayable.Create(graph,clip);playable.SetApplyFootIK(false);playable.SetSpeed(0);
                            var output=AnimationPlayableOutput.Create(graph,"Rig",animator);output.SetSourcePlayable(playable);graph.Play();graph.Evaluate(0);
                            entry.clipAngle=Quaternion.Angle(expected,target.rotation);entry.clipFootGap=Vector3.Distance(expectedFoot,foot.position);report.cases.Add(entry);
                            graph.Destroy();UnityEngine.Object.DestroyImmediate(clip);
                        }
                    }
                }
                UnityEngine.Object.DestroyImmediate(dancer);
            }
            Directory.CreateDirectory("Builds/Windows/RigDiagnostics");File.WriteAllText("Builds/Windows/RigDiagnostics/results.json",JsonUtility.ToJson(report,true));
            Debug.Log("RIG_DIAGNOSTICS_COMPLETE "+report.cases.Count);
        }
    }
}
