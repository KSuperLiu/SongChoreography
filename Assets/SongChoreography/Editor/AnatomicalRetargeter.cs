using System;
using System.Linq;
using UnityEngine;

namespace SongChoreography.Editor
{
    // Retarget limb directions and bend planes through a valid Humanoid reference
    // pose. Copying source axial rotations directly can overtwist a different rig.
    public sealed class AnatomicalRetargeter
    {
        sealed class Chain
        {
            public Transform upper,lower,end;
            public Quaternion upperOffset,lowerOffset,endRelative;
            readonly bool leg;
            Vector3 previousNormal;
            public Chain(Transform a,Transform b,Transform c,bool isLeg=false)
            {
                leg=isLeg;
                upper=a;lower=b;end=c;Vector3 u=(b.position-a.position).normalized,v=(c.position-b.position).normalized;
                Vector3 normal=Vector3.Cross(u,v).normalized;
                if(normal.sqrMagnitude<.5f)throw new InvalidOperationException("Humanoid reference limb must be bent.");
                upperOffset=Quaternion.Inverse(Quaternion.LookRotation(u,normal))*a.rotation;
                lowerOffset=Quaternion.Inverse(Quaternion.LookRotation(v,normal))*b.rotation;
                endRelative=Quaternion.Inverse(b.rotation)*c.rotation;
            }
            public void Apply(Vector3 u,Vector3 v,Vector3 filteredPlane=default(Vector3))
            {
                u.Normalize();v.Normalize();float bend=Mathf.Clamp(Mathf.Acos(Mathf.Clamp(Vector3.Dot(u,v),-1,1))*Mathf.Rad2Deg,2,leg?135:145);Vector3 normal=Vector3.Cross(u,v);
                if(filteredPlane.sqrMagnitude>.5f)normal=Vector3.ProjectOnPlane(filteredPlane,u).normalized;
                else
                {
                    if(normal.sqrMagnitude<.0001f&&previousNormal.sqrMagnitude>.5f)normal=previousNormal;
                    else normal.Normalize();
                    if(previousNormal.sqrMagnitude>.5f&&Vector3.Dot(normal,previousNormal)<0)normal=-normal;
                }
                if(normal.sqrMagnitude<.5f)normal=Vector3.Cross(u,Vector3.forward).normalized;
                previousNormal=normal;
                // Keep the bend positive after resolving a pole flip near a straight
                // limb. Flipping the plane alone would make the joint hyperextend.
                v=Quaternion.AngleAxis(bend,normal)*u;
                upper.rotation=Quaternion.LookRotation(u,normal)*upperOffset;
                lower.rotation=Quaternion.LookRotation(v,normal)*lowerOffset;
                end.rotation=lower.rotation*endRelative;
            }
        }
        readonly Chain leftArm,rightArm,leftLeg,rightLeg;
        readonly Transform hips,spine,chest,upperChest,neck,head,leftShoulder,rightShoulder;
        readonly Quaternion hipsOffset,spineOffset,chestOffset,upperChestOffset,neckOffset,headOffset,leftShoulderOffset,rightShoulderOffset;
        readonly Transform leftFoot,rightFoot,leftToes,rightToes;
        readonly Quaternion leftFootOffset,rightFootOffset,leftToesRelative,rightToesRelative;
        public AnatomicalRetargeter(Animator animator)
        {
            var transforms=animator.GetComponentsInChildren<Transform>();
            var positions=transforms.Select(t=>t.localPosition).ToArray();var rotations=transforms.Select(t=>t.localRotation).ToArray();
            using(var handler=new HumanPoseHandler(animator.avatar,animator.transform))
            {
                var pose=new HumanPose{muscles=new float[HumanTrait.MuscleCount]};handler.GetHumanPose(ref pose);
                // The FBX bind transforms can encode axial rotations outside the
                // normalized Humanoid range. Calibrate from an actual neutral
                // Humanoid pose, rather than inheriting those bind-pose twists.
                Array.Clear(pose.muscles,0,pose.muscles.Length);
                foreach(string name in new[]{"Left Forearm Stretch","Right Forearm Stretch","Left Lower Leg Stretch","Right Lower Leg Stretch"})
                {
                    int index=Array.IndexOf(HumanTrait.MuscleName,name);if(index<0)throw new InvalidOperationException("Missing reference muscle: "+name);
                    pose.muscles[index]=-.5f;
                }
                handler.SetHumanPose(ref pose);
                var check=new HumanPose{muscles=new float[HumanTrait.MuscleCount]};handler.GetHumanPose(ref check);
                Debug.Log("HUMANOID REFERENCE LEG TWIST "+check.muscles[Array.IndexOf(HumanTrait.MuscleName,"Left Upper Leg Twist In-Out")]+", "+check.muscles[Array.IndexOf(HumanTrait.MuscleName,"Right Upper Leg Twist In-Out")]);
                hips=animator.GetBoneTransform(HumanBodyBones.Hips);spine=animator.GetBoneTransform(HumanBodyBones.Spine);chest=animator.GetBoneTransform(HumanBodyBones.Chest);
                upperChest=animator.GetBoneTransform(HumanBodyBones.UpperChest);neck=animator.GetBoneTransform(HumanBodyBones.Neck);head=animator.GetBoneTransform(HumanBodyBones.Head);
                leftShoulder=animator.GetBoneTransform(HumanBodyBones.LeftShoulder);rightShoulder=animator.GetBoneTransform(HumanBodyBones.RightShoulder);
                var leftUpperLeg=animator.GetBoneTransform(HumanBodyBones.LeftUpperLeg);var rightUpperLeg=animator.GetBoneTransform(HumanBodyBones.RightUpperLeg);
                var leftUpperArm=animator.GetBoneTransform(HumanBodyBones.LeftUpperArm);var rightUpperArm=animator.GetBoneTransform(HumanBodyBones.RightUpperArm);
                Vector3 shoulderLine=leftUpperArm.position-rightUpperArm.position;
                hipsOffset=Quaternion.Inverse(BodyFrame(leftUpperLeg.position-rightUpperLeg.position,chest.position-hips.position))*hips.rotation;
                spineOffset=Quaternion.Inverse(BodyFrame(shoulderLine,chest.position-spine.position))*spine.rotation;
                chestOffset=Quaternion.Inverse(BodyFrame(shoulderLine,neck.position-chest.position))*chest.rotation;
                upperChestOffset=Quaternion.Inverse(BodyFrame(shoulderLine,neck.position-upperChest.position))*upperChest.rotation;
                neckOffset=Quaternion.Inverse(BodyFrame(shoulderLine,head.position-neck.position))*neck.rotation;
                headOffset=Quaternion.Inverse(BodyFrame(shoulderLine,head.position-neck.position))*head.rotation;
                Vector3 up=(head.position-hips.position).normalized;
                leftShoulderOffset=Quaternion.Inverse(Quaternion.LookRotation(leftUpperArm.position-leftShoulder.position,up))*leftShoulder.rotation;
                rightShoulderOffset=Quaternion.Inverse(Quaternion.LookRotation(rightUpperArm.position-rightShoulder.position,up))*rightShoulder.rotation;
                leftArm=new Chain(animator.GetBoneTransform(HumanBodyBones.LeftUpperArm),animator.GetBoneTransform(HumanBodyBones.LeftLowerArm),animator.GetBoneTransform(HumanBodyBones.LeftHand));
                rightArm=new Chain(animator.GetBoneTransform(HumanBodyBones.RightUpperArm),animator.GetBoneTransform(HumanBodyBones.RightLowerArm),animator.GetBoneTransform(HumanBodyBones.RightHand));
                leftLeg=new Chain(animator.GetBoneTransform(HumanBodyBones.LeftUpperLeg),animator.GetBoneTransform(HumanBodyBones.LeftLowerLeg),animator.GetBoneTransform(HumanBodyBones.LeftFoot),true);
                rightLeg=new Chain(animator.GetBoneTransform(HumanBodyBones.RightUpperLeg),animator.GetBoneTransform(HumanBodyBones.RightLowerLeg),animator.GetBoneTransform(HumanBodyBones.RightFoot),true);
                leftFoot=leftLeg.end;rightFoot=rightLeg.end;leftToes=animator.GetBoneTransform(HumanBodyBones.LeftToes);rightToes=animator.GetBoneTransform(HumanBodyBones.RightToes);
                leftFootOffset=Quaternion.Inverse(Quaternion.LookRotation(leftToes.position-leftFoot.position,Vector3.up))*leftFoot.rotation;
                rightFootOffset=Quaternion.Inverse(Quaternion.LookRotation(rightToes.position-rightFoot.position,Vector3.up))*rightFoot.rotation;
                leftToesRelative=Quaternion.Inverse(leftFoot.rotation)*leftToes.rotation;rightToesRelative=Quaternion.Inverse(rightFoot.rotation)*rightToes.rotation;
            }
            for(int i=0;i<transforms.Length;i++){transforms[i].localPosition=positions[i];transforms[i].localRotation=rotations[i];}
        }
        public void Apply(Func<string,Vector3> p,bool practice=false,float phase=0,Vector3 leftPlane=default(Vector3),Vector3 rightPlane=default(Vector3))
        {
            Vector3 shoulders=p("lhumerus")-p("rhumerus"),up=(p("head")-p("root")).normalized;
            hips.rotation=BodyFrame(p("lfemur")-p("rfemur"),p("thorax")-p("root"))*hipsOffset;
            spine.rotation=BodyFrame(shoulders,p("upperback")-p("lowerback"))*spineOffset;
            chest.rotation=BodyFrame(shoulders,p("lowerneck")-p("upperback"))*chestOffset;
            upperChest.rotation=BodyFrame(shoulders,p("upperneck")-p("thorax"))*upperChestOffset;
            neck.rotation=BodyFrame(shoulders,p("head")-p("lowerneck"))*neckOffset;
            head.rotation=BodyFrame(shoulders,p("head")-p("lowerneck"))*headOffset;
            leftShoulder.rotation=Quaternion.LookRotation(p("lhumerus")-p("lclavicle"),up)*leftShoulderOffset;
            rightShoulder.rotation=Quaternion.LookRotation(p("rhumerus")-p("rclavicle"),up)*rightShoulderOffset;
            if(practice)ApplyPracticeArms(phase);
            else
            {
                leftArm.Apply(p("lradius")-p("lhumerus"),p("lwrist")-p("lradius"));
                rightArm.Apply(p("rradius")-p("rhumerus"),p("rwrist")-p("rradius"));
            }
            leftLeg.Apply(p("ltibia")-p("lfemur"),p("lfoot")-p("ltibia"),leftPlane);
            rightLeg.Apply(p("rtibia")-p("rfemur"),p("rfoot")-p("rtibia"),rightPlane);
            if(!practice)
            {
                leftFoot.rotation=Quaternion.LookRotation(p("ltoes")-p("lfoot"),Vector3.up)*leftFootOffset;
                rightFoot.rotation=Quaternion.LookRotation(p("rtoes")-p("rfoot"),Vector3.up)*rightFootOffset;
            }
            // Practice edits retain the calibrated neutral ankle articulation.
            // The source toe channels are noisy and can cross a 90-degree pole;
            // captured hip/knee directions still supply the actual dance steps.
            leftToes.rotation=leftFoot.rotation*leftToesRelative;rightToes.rotation=rightFoot.rotation*rightToesRelative;
        }
        static Quaternion BodyFrame(Vector3 left,Vector3 up)
        {
            up.Normalize();left=Vector3.ProjectOnPlane(left,up).normalized;
            return Quaternion.LookRotation(Vector3.Cross(left,up),up);
        }
        public void ApplyPracticeArms(float phase)
        {
            // A repeatable, modest counter-swing for the beginner practice edit.
            // Source Charleston footwork is retained; hand styling is authored.
            float swing=Mathf.Sin(phase*Mathf.PI*2)*.32f;
            var left=new Vector3(-.12f,-.96f,swing).normalized;
            var right=new Vector3(.12f,-.96f,-swing).normalized;
            leftArm.Apply(left,Quaternion.AngleAxis(-30,Vector3.right)*left);
            rightArm.Apply(right,Quaternion.AngleAxis(-30,Vector3.right)*right);
        }
    }
}
