using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace SongChoreography
{
    // QA of the configured Humanoid ranges; not a medical/anatomical certification.
    public sealed class JointRangeProbe : IDisposable
    {
        public const float Tolerance=1.02f;
        readonly HumanPoseHandler handler;
        sealed class Bend {public string name;public Transform upper,joint,end;public Vector3 axis;public float maximum;}
        readonly List<Bend> bends=new List<Bend>();
        HumanPose pose=new HumanPose{muscles=new float[HumanTrait.MuscleCount]};
        public float Maximum {get;private set;}
        public string WorstMuscle {get;private set;}="";
        public int Samples {get;private set;}
        public float MinimumBendDegrees {get;private set;}=180;
        public string FailureReason {get;private set;}="";
        public bool Passed {get;private set;}=true;
        public JointRangeProbe(Animator animator)
        {
            handler=new HumanPoseHandler(animator.avatar,animator.transform);
            var reference=UnityEngine.Object.Instantiate(animator.gameObject);reference.hideFlags=HideFlags.HideAndDontSave;
            foreach(var renderer in reference.GetComponentsInChildren<Renderer>())renderer.enabled=false;
            var referenceAnimator=reference.GetComponent<Animator>();referenceAnimator.enabled=false;
            try
            {
                using(var calibrator=new HumanPoseHandler(referenceAnimator.avatar,referenceAnimator.transform))
                {
                    var calibration=new HumanPose{muscles=new float[HumanTrait.MuscleCount]};calibrator.GetHumanPose(ref calibration);Array.Clear(calibration.muscles,0,calibration.muscles.Length);
                    for(int i=0;i<calibration.muscles.Length;i++)if(HumanTrait.MuscleName[i].Contains("Stretched")||HumanTrait.MuscleName[i].Contains("Forearm Stretch")||HumanTrait.MuscleName[i].Contains("Lower Leg Stretch"))calibration.muscles[i]=-.5f;
                    calibrator.SetHumanPose(ref calibration);
                }
                Func<Transform,Transform> live=t=>
                {var names=new List<string>();while(t!=referenceAnimator.transform){names.Add(t.name);t=t.parent;}names.Reverse();return animator.transform.Find(string.Join("/",names));};
                Func<string,Transform> bone=name=>referenceAnimator.GetBoneTransform((HumanBodyBones)Enum.Parse(typeof(HumanBodyBones),name));
                foreach(string side in new[]{"Left","Right"})
                {
                    Add(side+" elbow",bone(side+"UpperArm"),bone(side+"LowerArm"),bone(side+"Hand"),155,live);
                    Add(side+" knee",bone(side+"UpperLeg"),bone(side+"LowerLeg"),bone(side+"Foot"),150,live);
                    foreach(string finger in new[]{"Thumb","Index","Middle","Ring","Little"})
                    {
                        var a=bone(side+finger+"Proximal");var b=bone(side+finger+"Intermediate");var c=bone(side+finger+"Distal");
                        if(finger!="Thumb")Add(side+" "+finger+" MCP",bone(side+"Hand"),a,b,110,live);
                        Add(side+" "+finger+" middle",a,b,c,120,live);
                        if(c.childCount>0)Add(side+" "+finger+" distal",b,c,c.GetChild(0),100,live);
                    }
                }
            }
            finally{if(Application.isPlaying)UnityEngine.Object.Destroy(reference);else UnityEngine.Object.DestroyImmediate(reference);}
        }
        void Add(string name,Transform upper,Transform joint,Transform end,float maximum,Func<Transform,Transform> live)
        {
            Vector3 axis=Vector3.Cross(joint.position-upper.position,end.position-joint.position).normalized;
            if(axis.sqrMagnitude<.9f)throw new InvalidOperationException("Cannot calibrate hinge: "+name);
            bends.Add(new Bend{name=name,upper=live(upper),joint=live(joint),end=live(end),axis=Quaternion.Inverse(upper.rotation)*axis,maximum=maximum});
        }
        public bool Sample()
        {
            handler.GetHumanPose(ref pose);bool valid=true;Samples++;
            for(int i=0;i<pose.muscles.Length;i++)
            {
                float value=Mathf.Abs(pose.muscles[i]);
                if(float.IsNaN(value)||float.IsInfinity(value)){valid=false;WorstMuscle=HumanTrait.MuscleName[i];FailureReason="Non-finite muscle: "+WorstMuscle;}
                else {if(value>Maximum){Maximum=value;WorstMuscle=HumanTrait.MuscleName[i];}if(value>Tolerance){valid=false;FailureReason=HumanTrait.MuscleName[i]+" normalized="+value;}}
            }
            foreach(var bend in bends)
            {
                Vector3 axis=bend.upper.rotation*bend.axis;
                Vector3 u=Vector3.ProjectOnPlane(bend.joint.position-bend.upper.position,axis).normalized,v=Vector3.ProjectOnPlane(bend.end.position-bend.joint.position,axis).normalized;
                float angle=Vector3.SignedAngle(u,v,axis);MinimumBendDegrees=Math.Min(MinimumBendDegrees,angle);
                if(float.IsNaN(angle)||angle< -3||angle>bend.maximum+3){valid=false;FailureReason=bend.name+" signed bend="+angle+" degrees";}
            }
            Passed&=valid;return valid;
        }
        public void Dispose()=>handler.Dispose();
    }
}
