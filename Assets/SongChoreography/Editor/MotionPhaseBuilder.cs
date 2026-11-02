using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

namespace SongChoreography.Editor
{
    // Chooses comparable contact phases before the visual transition review.
    // This produces measurements, not approvals or safeNext declarations.
    public static class MotionPhaseBuilder
    {
        static readonly HumanBodyBones[] Joints={HumanBodyBones.Hips,HumanBodyBones.Chest,HumanBodyBones.Head,
            HumanBodyBones.LeftUpperArm,HumanBodyBones.LeftLowerArm,HumanBodyBones.LeftHand,
            HumanBodyBones.RightUpperArm,HumanBodyBones.RightLowerArm,HumanBodyBones.RightHand,
            HumanBodyBones.LeftUpperLeg,HumanBodyBones.LeftLowerLeg,HumanBodyBones.LeftFoot,HumanBodyBones.LeftToes,
            HumanBodyBones.RightUpperLeg,HumanBodyBones.RightLowerLeg,HumanBodyBones.RightFoot,HumanBodyBones.RightToes};
        sealed class Pose {public int frame;public Vector3 origin;public Vector3[] points;public Quaternion[] rotations;public bool contact;}
        sealed class Candidate {public MotionDefinition motion;public AnimationClip clip;public List<Pose> poses=new List<Pose>();public Pose chosen;}
        sealed class Seed {public Candidate owner;public Pose pose;public double cost;}
        [Serializable]class Choice {public string id;public int frame,frames;public float distanceToReference;}
        [Serializable]class Connection {public string from,to,maxAngleJoint;public float rmsMetres,maxFootMetres,maxHandMetres,maxAngleDegrees;public bool candidate,approved=false;}
        [Serializable]class Report {public string reference;public List<Choice> choices=new List<Choice>();public List<Connection> connections=new List<Connection>();}

        public static void Align(MotionCatalog catalog)
        {
            var motions=catalog.motions.Where(m=>m.style=="charleston-review").OrderBy(m=>m.id,StringComparer.Ordinal).ToArray();
            if(motions.Length==0)return;
            var dancer=DancerCharacters.Create("female");var animator=dancer.GetComponentInChildren<Animator>();
            var skin=dancer.GetComponentsInChildren<SkinnedMeshRenderer>().OrderByDescending(r=>r.sharedMesh.vertexCount).First();
            var joints=Joints.Select(b=>animator.GetBoneTransform(b)).ToArray();
            var feet=new[]{animator.GetBoneTransform(HumanBodyBones.LeftFoot),animator.GetBoneTransform(HumanBodyBones.RightFoot)};
            var weights=skin.sharedMesh.boneWeights;
            var sole=Enumerable.Range(0,weights.Length).Where(i=>
            {
                var w=weights[i];int[] ids={w.boneIndex0,w.boneIndex1,w.boneIndex2,w.boneIndex3};float[] values={w.weight0,w.weight1,w.weight2,w.weight3};float weight=0;
                for(int b=0;b<4;b++)if(feet.Any(root=>skin.bones[ids[b]]==root||skin.bones[ids[b]].IsChildOf(root)))weight+=values[b];return weight>.8f;
            }).ToArray();
            var mesh=new Mesh();var candidates=new List<Candidate>();
            try
            {
                var armReference=Resources.Load<AnimationClip>(motions[0].resource);
                var armCurves=AnimationUtility.GetCurveBindings(armReference).Where(binding=>IsArm(binding.propertyName)).Select(binding=>new{binding,curve=AnimationUtility.GetEditorCurve(armReference,binding)}).ToArray();
                float armDuration=armReference.length;
                foreach(var motion in motions)
                {
                    var clip=Resources.Load<AnimationClip>(motion.resource);var candidate=new Candidate{motion=motion,clip=clip};
                    var contactClip=UnityEngine.Object.Instantiate(clip);
                    foreach(var channel in armCurves)
                    {
                        float neutral=IsGaze(channel.binding.propertyName)?0:channel.curve.Evaluate(0);
                        AnimationUtility.SetEditorCurve(contactClip,channel.binding,AnimationCurve.Constant(0,clip.length,neutral));
                    }
                    try{Sample(candidate,dancer,animator,skin,mesh,sole,joints,feet,contactClip);}
                    finally{UnityEngine.Object.DestroyImmediate(contactClip);}
                    candidates.Add(candidate);
                }
                var seeds=new List<Seed>();
                foreach(var seed in candidates)
                    foreach(var pose in seed.poses.Where(p=>p.contact))
                    {
                        double sum=0,max=0;
                        foreach(var target in candidates)
                        {
                            double score=target.poses.Where(p=>p.contact).Min(p=>Distance(pose,p));sum+=score;max=Math.Max(max,score);
                        }
                        seeds.Add(new Seed{owner=seed,pose=pose,cost=sum+max*2});
                    }
                Dictionary<Candidate,Pose> plan=null;Seed selectedSeed=null;
                foreach(var seed in seeds.OrderBy(s=>s.cost).Take(16))
                {
                    var next=Grow(candidates,seed);
                    if(plan==null||next.Count>plan.Count){plan=next;selectedSeed=seed;}
                    if(next.Count==candidates.Count)break;
                }
                int connected=plan.Count;
                foreach(var item in candidates.Where(item=>!plan.ContainsKey(item)))
                    plan[item]=item.poses.Where(p=>p.contact).OrderBy(p=>plan.Values.Min(other=>Distance(other,p))).ThenBy(p=>p.frame).First();
                var reference=selectedSeed.pose;
                var report=new Report{reference=selectedSeed.owner.motion.id+" frame "+reference.frame+"; connected "+connected+"/"+candidates.Count};
                Dictionary<string,float> branches=null;
                foreach(var item in candidates)
                {
                    item.chosen=plan[item];
                    report.choices.Add(new Choice{id=item.motion.id,frame=item.chosen.frame,frames=item.poses.Count,distanceToReference=Mathf.Sqrt((float)Distance(reference,item.chosen))});
                    Rephase(item.clip,item.chosen.frame,item.poses.Count,item.chosen.origin/animator.humanScale);
                    foreach(var channel in armCurves)
                    {
                        int frames=item.poses.Count;var values=new float[frames+1];
                        for(int i=0;i<=frames;i++)values[i]=IsGaze(channel.binding.propertyName)?0:channel.curve.Evaluate(i/(float)frames*armDuration);
                        HumanoidCurveContinuity.Unwrap(channel.binding.propertyName,values);
                        AnimationUtility.SetEditorCurve(item.clip,channel.binding,Curve(values));
                    }
                    if(branches==null)branches=HumanoidCurveContinuity.Reference(item.clip);
                    HumanoidCurveContinuity.AlignBranches(item.clip,branches);
                    foreach(var binding in AnimationUtility.GetCurveBindings(item.clip).Where(b=>IsArm(b.propertyName)))
                    {
                        var original=AnimationUtility.GetEditorCurve(item.clip,binding);
                        var bounded=new AnimationCurve(original.keys.Select(k=>new Keyframe(k.time,Mathf.Clamp(k.value,-.97f,.97f))).ToArray());
                        for(int k=0;k<bounded.length;k++){AnimationUtility.SetKeyLeftTangentMode(bounded,k,AnimationUtility.TangentMode.ClampedAuto);AnimationUtility.SetKeyRightTangentMode(bounded,k,AnimationUtility.TangentMode.ClampedAuto);}
                        AnimationUtility.SetEditorCurve(item.clip,binding,bounded);
                    }
                    foreach(var binding in AnimationUtility.GetCurveBindings(item.clip).Where(b=>AnatomicalPoseCleanup.IsFinger(b.propertyName)))
                        AnimationUtility.SetEditorCurve(item.clip,binding,AnimationCurve.Constant(0,item.clip.length,binding.propertyName.Contains("Spread")?0:.35f));
                    var old=item.motion.supportHeights;var shifted=new List<float>();
                    for(int frame=0;frame<=item.poses.Count;frame++)shifted.Add(old[(frame+item.chosen.frame)%item.poses.Count]);
                    item.motion.supportHeights=shifted;
                    item.motion.reviewNotes+=" Contact phase rotated by "+item.chosen.frame+" frames at 30fps. Symmetric offline smoothing, eight-count retiming, authored modest arm counter-swing, neutral gaze and stabilized ankles adapt captured Charleston hip/knee step patterns for practice. Transitions remain unapproved.";
                    item.poses.Clear();Sample(item,dancer,animator,skin,mesh,sole,joints,feet);item.chosen=item.poses[0];
                }
                foreach(var from in candidates)foreach(var to in candidates)
                {
                    var a=from.chosen;var b=to.chosen;float foot=0,hand=0,angle=0;double sum=0;string angleJoint="";
                    for(int i=0;i<Joints.Length;i++)
                    {
                        float delta=Vector3.Distance(a.points[i],b.points[i]);sum+=delta*delta;
                        if(Joints[i].ToString().Contains("Foot")||Joints[i].ToString().Contains("Toes"))foot=Math.Max(foot,delta);
                        if(Joints[i]==HumanBodyBones.LeftHand||Joints[i]==HumanBodyBones.RightHand)hand=Math.Max(hand,delta);
                        float jointAngle=Quaternion.Angle(a.rotations[i],b.rotations[i]);if(jointAngle>angle){angle=jointAngle;angleJoint=Joints[i].ToString();}
                    }
                    float rms=Mathf.Sqrt((float)(sum/Joints.Length));
                    report.connections.Add(new Connection{from=from.motion.id,to=to.motion.id,rmsMetres=rms,maxFootMetres=foot,maxHandMetres=hand,maxAngleDegrees=angle,maxAngleJoint=angleJoint,
                        candidate=rms<.12f&&foot<.12f&&hand<.28f&&angle<55});
                }
                Directory.CreateDirectory("Builds/Windows/MotionPhase");
                File.WriteAllText("Builds/Windows/MotionPhase/review.json",JsonUtility.ToJson(report,true));
                Directory.CreateDirectory("Assets/StreamingAssets/MotionCredits");
                File.WriteAllText("Assets/StreamingAssets/MotionCredits/phase-review.json",JsonUtility.ToJson(report,true));
                Debug.Log("CONTACT PHASES ALIGNED: "+candidates.Count+", candidate connections="+report.connections.Count(c=>c.candidate)+"; NOT approved");
            }
            finally{UnityEngine.Object.DestroyImmediate(mesh);UnityEngine.Object.DestroyImmediate(dancer);}
        }
        static double Distance(Pose a,Pose b)
        {
            double sum=0,weight=0;
            for(int i=0;i<Joints.Length;i++)
            {
                string name=Joints[i].ToString();double w=name.Contains("Foot")||name.Contains("Toes")?4:name.Contains("Hand")?1.5:1;
                if(name.Contains("Arm")||name.Contains("Hand"))continue;
                double angle=Quaternion.Angle(a.rotations[i],b.rotations[i])*Mathf.Deg2Rad*(name.Contains("Foot")||name.Contains("Toes")?.15:.07);
                sum+=w*((a.points[i]-b.points[i]).sqrMagnitude+angle*angle);weight+=w;
            }
            return sum/weight;
        }
        static Dictionary<Candidate,Pose> Grow(List<Candidate> candidates,Seed seed)
        {
            var plan=new Dictionary<Candidate,Pose>{{seed.owner,seed.pose}};
            while(plan.Count<candidates.Count)
            {
                Candidate next=null;Pose chosen=null;double best=double.PositiveInfinity;
                foreach(var item in candidates.Where(item=>!plan.ContainsKey(item)))foreach(var pose in item.poses.Where(p=>p.contact))foreach(var previous in plan.Values)
                {
                    if(!Compatible(previous,pose))continue;
                    double score=Distance(previous,pose);
                    if(score<best){best=score;next=item;chosen=pose;}
                }
                if(next==null)break;plan.Add(next,chosen);
            }
            return plan;
        }
        static bool Compatible(Pose a,Pose b)
        {
            double sum=0;float foot=0,hand=0,angle=0;
            for(int i=0;i<Joints.Length;i++)
            {
                float delta=Vector3.Distance(a.points[i],b.points[i]);sum+=delta*delta;
                string name=Joints[i].ToString();
                if(name.Contains("Foot")||name.Contains("Toes"))foot=Math.Max(foot,delta);
                if(name.Contains("Hand"))hand=Math.Max(hand,delta);
                angle=Math.Max(angle,Quaternion.Angle(a.rotations[i],b.rotations[i]));
            }
            return sum/Joints.Length<.12f*.12f&&foot<.12f&&hand<.28f&&angle<55;
        }
        static bool IsGaze(string name)=>name.StartsWith("Head ",StringComparison.Ordinal)||name.StartsWith("Neck ",StringComparison.Ordinal);
        static bool IsArm(string name)=>IsGaze(name)||new[]{"Left Shoulder","Right Shoulder","Left Arm","Right Arm","Left Forearm","Right Forearm","Left Hand","Right Hand"}.Any(prefix=>name.StartsWith(prefix,StringComparison.Ordinal));
        static void Sample(Candidate candidate,GameObject dancer,Animator animator,SkinnedMeshRenderer skin,Mesh mesh,int[] sole,Transform[] joints,Transform[] feet,AnimationClip overrideClip=null)
        {
            var graph=PlayableGraph.Create("Contact phase review");graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
            try
            {
                var playable=AnimationClipPlayable.Create(graph,overrideClip?overrideClip:candidate.clip);playable.SetSpeed(0);playable.SetApplyFootIK(false);
                var output=AnimationPlayableOutput.Create(graph,"Review",animator);output.SetSourcePlayable(playable);graph.Play();
                int frames=Mathf.RoundToInt(candidate.clip.length*30);
                for(int frame=0;frame<frames;frame++)
                {
                    dancer.transform.position=Vector3.zero;playable.SetTime(frame/30.0);graph.Evaluate(0);
                    skin.BakeMesh(mesh);var vertices=mesh.vertices;float low=sole.Min(i=>skin.transform.TransformPoint(vertices[i]).y);
                    float height=candidate.motion.supportHeights[Math.Min(frame,candidate.motion.supportHeights.Count-1)];
                    dancer.transform.position+=Vector3.up*(height-low);var center=joints[0].position;center.y=0;
                    candidate.poses.Add(new Pose{frame=frame,origin=center,points=joints.Select(t=>t.position-center).ToArray(),rotations=joints.Select(t=>t.rotation).ToArray(),contact=height<.04f&&Math.Abs(feet[0].position.y-feet[1].position.y)<.12f});
                }
                if(!candidate.poses.Any(p=>p.contact))foreach(var p in candidate.poses)p.contact=true;
            }
            finally{graph.Destroy();}
        }
        static AnimationCurve Curve(float[] values)
        {
            return HumanoidCurveContinuity.Curve(values);
        }
        static void Rephase(AnimationClip clip,int shift,int frames,Vector3 origin)
        {
            foreach(var binding in AnimationUtility.GetCurveBindings(clip))
            {
                var curve=AnimationUtility.GetEditorCurve(clip,binding);var values=new float[frames+1];
                float offset=binding.propertyName=="RootT.x"?origin.x:binding.propertyName=="RootT.z"?origin.z:0;
                for(int i=0;i<=frames;i++)values[i]=curve.Evaluate(((i+shift)%frames)/30f)-offset;
                HumanoidCurveContinuity.Unwrap(binding.propertyName,values);
                AnimationUtility.SetEditorCurve(clip,binding,Curve(values));
            }
            clip.EnsureQuaternionContinuity();EditorUtility.SetDirty(clip);
        }
    }
}
