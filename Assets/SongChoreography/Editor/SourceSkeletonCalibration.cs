using System;
using System.Linq;
using UnityEngine;

namespace SongChoreography.Editor
{
    // Match anatomical frames, not merely the character's bind quaternions.
    // Source joint positions stay in the locally licensed input, never in code.
    public static class SourceSkeletonCalibration
    {
        public static Vector3 ConvertPosition(Vector3 p)=>new Vector3(p.x,p.y,-p.z);
        public static Quaternion ConvertRotation(Quaternion q)=>new Quaternion(-q.x,-q.y,q.z,q.w);
        static Quaternion Frame(Vector3 direction,Vector3 across)
        {
            direction.Normalize();across=Vector3.ProjectOnPlane(across,direction).normalized;
            if(direction.sqrMagnitude<.9f||across.sqrMagnitude<.9f)throw new InvalidOperationException("Degenerate anatomical calibration frame.");
            return Quaternion.LookRotation(direction,across);
        }
        public static Quaternion[] Solve(Animator animator,Transform[] bones,Vector3[] source)
        {
            if(source==null||source.Length!=52||bones.Length!=52||bones.Any(b=>!b))throw new InvalidOperationException("52 mapped source and target joints are required.");
            if(source.Any(p=>!Choreographer.Finite(p.x)||!Choreographer.Finite(p.y)||!Choreographer.Finite(p.z)))throw new InvalidOperationException("Source anatomical joints must be finite.");
            var all=animator.GetComponentsInChildren<Transform>();var positions=all.Select(t=>t.localPosition).ToArray();var rotations=all.Select(t=>t.localRotation).ToArray();
            var bind=bones.Select(t=>t.rotation).ToArray();
            var tipLocal=bones.Select(t=>t.InverseTransformDirection(t.position-t.parent.position)).ToArray();
            try
            {
                using(var handler=new HumanPoseHandler(animator.avatar,animator.transform))
                {
                    var pose=new HumanPose{muscles=new float[HumanTrait.MuscleCount]};handler.GetHumanPose(ref pose);Array.Clear(pose.muscles,0,pose.muscles.Length);
                    foreach(string name in new[]{"Left Forearm Stretch","Right Forearm Stretch","Left Lower Leg Stretch","Right Lower Leg Stretch"})pose.muscles[Array.IndexOf(HumanTrait.MuscleName,name)]=-.5f;
                    handler.SetHumanPose(ref pose);
                    for(int i=0;i<all.Length;i++)
                    {
                        float shift=Vector3.Distance(all[i].localPosition,positions[i]);
                        if(shift>.001f)Debug.Log("ANATOMY_REFERENCE_TRANSLATION "+all[i].name+" "+shift);
                        all[i].localPosition=positions[i];
                    }
                    var target=bones.Select(t=>t.position).ToArray();var result=new Quaternion[52];
                    Vector3 sourceAcross=source[16]-source[17],targetAcross=target[16]-target[17];
                    Vector3 sourceUp=source[15]-source[0],targetUp=target[15]-target[0];
                    Action<int,Vector3,Vector3,Vector3,Vector3> set=(i,sd,sa,td,ta)=>result[i]=Frame(sd,sa)*Quaternion.Inverse(Frame(td,ta))*bones[i].rotation;
                    foreach(var item in new[]{new[]{0,3},new[]{3,6},new[]{6,9},new[]{9,12},new[]{12,15}})set(item[0],source[item[1]]-source[item[0]],sourceAcross,target[item[1]]-target[item[0]],targetAcross);
                    set(15,source[15]-source[12],sourceAcross,target[15]-target[12],targetAcross);
                    foreach(var chain in new[]{new[]{1,4,7},new[]{2,5,8},new[]{16,18,20},new[]{17,19,21}})
                    {
                        int a=chain[0],b=chain[1],c=chain[2];
                        Vector3 su=source[b]-source[a],sv=source[c]-source[b],tu=target[b]-target[a],tv=target[c]-target[b];
                        // Neutral SMPL-X limbs are almost straight and include
                        // valgus/carry angles. Their geometric cross product is
                        // NOT the hinge axis (especially at the knee).
                        Vector3 sn=a<3?Vector3.left:a==16?Vector3.up:Vector3.down;
                        Vector3 tn=Vector3.Cross(tu,tv);
                        set(a,su,sn,tu,tn);set(b,sv,sn,tv,tn);
                    }
                    foreach(var pair in new[]{new[]{13,16},new[]{14,17}})set(pair[0],source[pair[1]]-source[pair[0]],sourceUp,target[pair[1]]-target[pair[0]],targetUp);
                    foreach(var pair in new[]{new[]{7,10},new[]{8,11}})
                    {
                        int foot=pair[0],toe=pair[1];Vector3 soleUp=bones[foot].rotation*Quaternion.Inverse(bind[foot])*Vector3.up;
                        set(foot,source[toe]-source[foot],Vector3.up,target[toe]-target[foot],soleUp);
                        result[toe]=result[foot]*Quaternion.Inverse(bones[foot].rotation)*bones[toe].rotation;
                    }
                    foreach(int side in new[]{0,1})
                    {
                        int hand=20+side,first=22+side*15,index=first,middle=first+3,pinky=first+6;
                        Vector3 sa=source[index]-source[pinky],ta=target[index]-target[pinky];
                        set(hand,source[middle]-source[hand],sa,target[middle]-target[hand],ta);
                        for(int finger=0;finger<5;finger++)for(int joint=0;joint<3;joint++)
                        {
                            int i=first+finger*3+joint;
                            Vector3 sd=joint<2?source[i+1]-source[i]:source[i]-source[i-1];
                            Vector3 td=joint<2?target[i+1]-target[i]:bones[i].rotation*tipLocal[i];
                            set(i,sd,sa,td,ta);
                        }
                    }
                    // Torso bind frames already share the source's
                    // upright axes. Do not infer axial roll from the reference
                    // muscle pose's curved spine or elevated shoulder line.
                    foreach(int i in new[]{0,3,6,9,12,15})result[i]=bind[i];
                    return result;
                }
            }
            finally{for(int i=0;i<all.Length;i++){all[i].localPosition=positions[i];all[i].localRotation=rotations[i];}}
        }
    }
}
