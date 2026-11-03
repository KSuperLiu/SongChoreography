using System;
using System.Linq;
using UnityEngine;

namespace SongChoreography
{
    [Serializable] public class ArmPoseTrack
    {
        public string id,sourceHash;public AnimationClip clip;public float fps=30;
        public Quaternion[] leftUpper,leftLower,rightUpper,rightLower;
        public Vector3[] leftHandTarget,leftElbowTarget,rightHandTarget,rightElbowTarget;
        public bool HasPositionTargets=>leftHandTarget!=null&&leftHandTarget.Length>0&&leftElbowTarget!=null&&leftElbowTarget.Length==leftHandTarget.Length&&rightHandTarget!=null&&rightHandTarget.Length==leftHandTarget.Length&&rightElbowTarget!=null&&rightElbowTarget.Length==leftHandTarget.Length;
        public Quaternion At(Quaternion[] values,double time)
        {float frame=Mathf.Clamp((float)time*fps,0,values.Length-1);int first=Mathf.Min(values.Length-1,(int)frame),next=Mathf.Min(values.Length-1,first+1);return Quaternion.Slerp(values[first],values[next],frame-first);}
        public Vector3 At(Vector3[] values,double time)
        {
            float frame=Mathf.Clamp((float)time*fps,0,values.Length-1);int i=Mathf.Min(values.Length-1,(int)frame),j=Mathf.Min(values.Length-1,i+1);var a=values[Mathf.Max(0,i-1)];var b=values[i];var c=values[j];var d=values[Mathf.Min(values.Length-1,j+1)];float t=frame-i;
            var result=Vector3.zero;for(int axis=0;axis<3;axis++){float slope=c[axis]-b[axis],before=b[axis]-a[axis],after=d[axis]-c[axis];float m0=before*slope>0?2*before*slope/(before+slope):0,m1=slope*after>0?2*slope*after/(slope+after):0;float t2=t*t,t3=t2*t;result[axis]=(2*t3-3*t2+1)*b[axis]+(t3-2*t2+t)*m0+(-2*t3+3*t2)*c[axis]+(t3-t2)*m1;}return result;
        }
    }
    public sealed class ArmPoseLibrary : ScriptableObject
    {
        public int revision=1;public Avatar avatar;public string targetSourceHash="",signature="";public ArmPoseTrack[] tracks=new ArmPoseTrack[0];
        public static Transform Anchor(Animator animator)=>animator.GetBoneTransform(HumanBodyBones.Chest)??animator.GetBoneTransform(HumanBodyBones.Spine)??animator.GetBoneTransform(HumanBodyBones.Hips);
        public bool Apply(Animator animator,AnimationClip clip,double time,AnimationClip outgoing,double outgoingTime,float weight,out bool positionBased)
        {
            positionBased=false;
            var identity=animator.GetComponentInParent<GeneratedCharacterIdentity>();
            if(signature=="baking"||!identity||string.IsNullOrEmpty(targetSourceHash)||identity.sourceHash!=targetSourceHash||animator.avatar!=avatar)return false;
            var incoming=tracks.FirstOrDefault(t=>t.clip==clip);var previous=outgoing?tracks.FirstOrDefault(t=>t.clip==outgoing):null;if(incoming==null||outgoing&&previous==null)return false;
            var leftHand=animator.GetBoneTransform(HumanBodyBones.LeftHand);var rightHand=animator.GetBoneTransform(HumanBodyBones.RightHand);var leftRotation=leftHand.rotation;var rightRotation=rightHand.rotation;
            if(incoming.HasPositionTargets&&(previous==null||previous.HasPositionTargets))
            {
                // Positions constrain two segment directions, but do not
                // determine roll. Replay the baked orientation as the frame
                // before aligning those directions, so forearm skin retains
                // the twist that was checked during baking.
                var poseBones=new[]{HumanBodyBones.LeftUpperArm,HumanBodyBones.LeftLowerArm,HumanBodyBones.RightUpperArm,HumanBodyBones.RightLowerArm};
                var poseValues=new[]{incoming.leftUpper,incoming.leftLower,incoming.rightUpper,incoming.rightLower};
                var oldValues=previous==null?null:new[]{previous.leftUpper,previous.leftLower,previous.rightUpper,previous.rightLower};
                for(int i=0;i<4;i++){var rotation=incoming.At(poseValues[i],time);if(previous!=null)rotation=Quaternion.Slerp(previous.At(oldValues[i],outgoingTime),rotation,weight);animator.GetBoneTransform(poseBones[i]).localRotation=rotation;}
                var anchor=Anchor(animator);var targets=new[]{incoming.leftHandTarget,incoming.leftElbowTarget,incoming.rightHandTarget,incoming.rightElbowTarget};var priorTargets=previous==null?null:new[]{previous.leftHandTarget,previous.leftElbowTarget,previous.rightHandTarget,previous.rightElbowTarget};var points=new Vector3[4];
                for(int i=0;i<4;i++){var p=incoming.At(targets[i],time);if(previous!=null)p=Vector3.Lerp(previous.At(priorTargets[i],outgoingTime),p,weight);points[i]=anchor.TransformPoint(p);}
                Solve(animator.GetBoneTransform(HumanBodyBones.LeftUpperArm),animator.GetBoneTransform(HumanBodyBones.LeftLowerArm),leftHand,points[0],points[1],anchor.right*-1,leftRotation);
                Solve(animator.GetBoneTransform(HumanBodyBones.RightUpperArm),animator.GetBoneTransform(HumanBodyBones.RightLowerArm),rightHand,points[2],points[3],anchor.right,rightRotation);positionBased=true;return true;
            }
            var bones=new[]{HumanBodyBones.LeftUpperArm,HumanBodyBones.LeftLowerArm,HumanBodyBones.RightUpperArm,HumanBodyBones.RightLowerArm};var values=new[]{incoming.leftUpper,incoming.leftLower,incoming.rightUpper,incoming.rightLower};var prior=previous==null?null:new[]{previous.leftUpper,previous.leftLower,previous.rightUpper,previous.rightLower};
            for(int i=0;i<bones.Length;i++){var rotation=incoming.At(values[i],time);if(previous!=null)rotation=Quaternion.Slerp(previous.At(prior[i],outgoingTime),rotation,weight);animator.GetBoneTransform(bones[i]).localRotation=rotation;}
            leftHand.rotation=leftRotation;rightHand.rotation=rightRotation;positionBased=incoming.HasPositionTargets||(previous!=null&&previous.HasPositionTargets);return true;
        }
        static void Solve(Transform upper,Transform lower,Transform hand,Vector3 target,Vector3 pole,Vector3 fallback,Quaternion wrist)
        {
            // Interpolate the two segment directions directly. An analytic
            // elbow pole becomes ambiguous when a smoothed hand path crosses
            // the shoulder/elbow line; direction replay has no such branch.
            var upperDirection=pole-upper.position;var lowerDirection=target-pole;
            if(upperDirection.sqrMagnitude>1e-10f)upper.rotation=Quaternion.FromToRotation(lower.position-upper.position,upperDirection)*upper.rotation;
            if(lowerDirection.sqrMagnitude>1e-10f)lower.rotation=Quaternion.FromToRotation(hand.position-lower.position,lowerDirection)*lower.rotation;
            hand.rotation=wrist;
        }
    }
    public sealed class ArmPoseSeed
    {
        public readonly Quaternion[] rotations=new Quaternion[4];
        public readonly Vector3[] rawHand=new Vector3[2],correctedHand=new Vector3[2];
    }
}
