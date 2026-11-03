using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

namespace SongChoreography
{
    // All poses, including editor previews, use the same Humanoid graph. There
    // are no source-specific joint indices or fabricated fallback animations.
    public sealed class MotionPlayer : IDisposable
    {
        public const int PlaybackRevision=6;
        PlayableGraph graph;
        AnimationMixerPlayable mixer;
        AnimationClipPlayable current,previous;
        AnimationClip currentClip,previousClip;
        readonly Animator animator;
        readonly Transform root;
        readonly Vector3 initialPosition;
        readonly SkinnedMeshRenderer[] skins;
        readonly Mesh baked;
        readonly ArmBodyAvoidance armAvoidance,exactArmAvoidance;
        readonly Transform armAnchor;
        readonly ArmPoseLibrary armPoses;ArmPoseSeed seed;AnimationClip seedClip;double seedTime;
        readonly bool guardBakedArms,exactUncachedArms;
        public bool BakeContinuity;
        public Quaternion[] SourceArmRotations{get;}=new Quaternion[4];
        public Vector3[] SourceArmPositions{get;}=new Vector3[4];
        float lastArmCorrection;public float LastArmCorrection=>lastArmCorrection;
        public int LastArmIterations=>Mathf.Max(armAvoidance?.LastIterations??0,exactArmAvoidance?.LastIterations??0);
        bool poseInitialized;
        readonly List<Vector3> vertices=new List<Vector3>();
        public MotionPlayer(GameObject character,bool correctArms=true,bool useBakedArms=true,bool guardBakedArms=true,bool exactUncachedArms=false)
        {
            this.guardBakedArms=guardBakedArms;this.exactUncachedArms=exactUncachedArms;
            root=character.transform;initialPosition=root.position;
            animator=character.GetComponentInChildren<Animator>();
            if(!animator||!animator.avatar||!animator.avatar.isHuman||!animator.avatar.isValid)throw new InvalidOperationException("人物 Humanoid 骨骼无效，请在素材工作台修正。");
            animator.applyRootMotion=false;animator.cullingMode=AnimatorCullingMode.AlwaysAnimate;
            skins=character.GetComponentsInChildren<SkinnedMeshRenderer>();
            if(correctArms&&Application.isPlaying)foreach(var skin in skins)
                if(skin.sharedMesh&&!skin.sharedMesh.isReadable)throw new InvalidOperationException("人物资源尚未完成避让配置，请停止运行并重新导入生成素材。");
            foreach(var skin in skins)skin.updateWhenOffscreen=true;
            baked=new Mesh();baked.MarkDynamic();
            if(correctArms&&skins.Length>0)armAvoidance=new ArmBodyAvoidance(character,animator);
            if(correctArms&&useBakedArms)armPoses=Resources.Load<ArmPoseLibrary>("ArmPoseLibrary");
            if(correctArms&&exactUncachedArms&&skins.Length>0)exactArmAvoidance=new ArmBodyAvoidance(character,animator,true);
            armAnchor=ArmPoseLibrary.Anchor(animator);
            graph=PlayableGraph.Create("Generated dance");graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
            mixer=AnimationMixerPlayable.Create(graph,2);
            var output=AnimationPlayableOutput.Create(graph,"Humanoid pose",animator);output.SetSourcePlayable(mixer);graph.Play();
        }
        void Channel(int index,AnimationClip clip,double time)
        {
            var existing=index==0?currentClip:previousClip;
            if(existing!=clip)
            {
                var old=index==0?current:previous;
                if(old.IsValid()){mixer.DisconnectInput(index);graph.DestroyPlayable(old);}
                var playable=AnimationClipPlayable.Create(graph,clip);playable.SetSpeed(0);
                playable.SetApplyFootIK(false);playable.SetApplyPlayableIK(false);
                graph.Connect(playable,0,mixer,index);
                if(index==0){current=playable;currentClip=clip;}else{previous=playable;previousClip=clip;}
            }
            (index==0?current:previous).SetTime(Math.Max(0,Math.Min(clip.length-.00001,time)));
        }
        public void Sample(AnimationClip clip,double time,AnimationClip outgoing=null,double outgoingTime=0,float incomingWeight=1)
        {
            if(!clip||!clip.isHumanMotion)throw new InvalidOperationException("需要有效的人形骨骼动画。");
            if(!poseInitialized)
            {
                // Static FBX previews have an anatomical facing correction. A
                // Humanoid clip supplies canonical +Z facing itself, so remove
                // that presentation yaw once, preserving authored turns inside
                // the clip and the outer character's rotation.
                float yaw=animator.transform.eulerAngles.y;
                animator.transform.rotation=Quaternion.Euler(0,Mathf.DeltaAngle(yaw,root.eulerAngles.y),0)*animator.transform.rotation;
                poseInitialized=true;
            }
            root.position=initialPosition;
            Channel(0,clip,time);mixer.SetInputWeight(0,outgoing?Mathf.Clamp01(incomingWeight):1);
            if(outgoing){Channel(1,outgoing,outgoingTime);mixer.SetInputWeight(1,1-Mathf.Clamp01(incomingWeight));}
            else mixer.SetInputWeight(1,0);
            graph.Evaluate(0);
            SourceArmRotations[0]=animator.GetBoneTransform(HumanBodyBones.LeftUpperArm).localRotation;SourceArmRotations[1]=animator.GetBoneTransform(HumanBodyBones.LeftLowerArm).localRotation;SourceArmRotations[2]=animator.GetBoneTransform(HumanBodyBones.RightUpperArm).localRotation;SourceArmRotations[3]=animator.GetBoneTransform(HumanBodyBones.RightLowerArm).localRotation;
            var left=animator.GetBoneTransform(HumanBodyBones.LeftHand);var right=animator.GetBoneTransform(HumanBodyBones.RightHand);var leftSource=left.position;var rightSource=right.position;
            var anchorMatrix=armAnchor.worldToLocalMatrix;SourceArmPositions[0]=anchorMatrix.MultiplyPoint3x4(left.position);SourceArmPositions[1]=anchorMatrix.MultiplyPoint3x4(animator.GetBoneTransform(HumanBodyBones.LeftLowerArm).position);SourceArmPositions[2]=anchorMatrix.MultiplyPoint3x4(right.position);SourceArmPositions[3]=anchorMatrix.MultiplyPoint3x4(animator.GetBoneTransform(HumanBodyBones.RightLowerArm).position);
            bool positionBased=false;bool bakedPoseApplied=armPoses!=null&&armPoses.Apply(animator,clip,time,outgoing,outgoingTime,incomingWeight,out positionBased);
            if(BakeContinuity&&(!ReferenceEquals(seedClip,clip)||time<seedTime||time-seedTime>.1))seed=null;
            if(guardBakedArms||!bakedPoseApplied){if(exactUncachedArms)exactArmAvoidance?.Apply(BakeContinuity?seed:null);else armAvoidance?.Apply(BakeContinuity?seed:null);}
            if(BakeContinuity&&armAvoidance!=null){seed=(exactUncachedArms?exactArmAvoidance:armAvoidance).CaptureSeed();seedClip=clip;seedTime=time;}
            lastArmCorrection=Mathf.Max(Vector3.Distance(left.position,leftSource),Vector3.Distance(right.position,rightSource));
            // Correct penetration only. Keeping genuine airborne height avoids
            // pinning a jump or lifted pose to the floor every frame.
            float min=float.PositiveInfinity;
            foreach(var skin in skins)
            {skin.BakeMesh(baked,true);baked.GetVertices(vertices);var matrix=skin.transform.localToWorldMatrix;foreach(var v in vertices)min=Mathf.Min(min,matrix.MultiplyPoint3x4(v).y);}
            if(min<.005f)root.position+=Vector3.up*(.005f-min);
        }
        public void Dispose()
        {if(graph.IsValid())graph.Destroy();armAvoidance?.Dispose();exactArmAvoidance?.Dispose();if(baked){if(Application.isPlaying)UnityEngine.Object.Destroy(baked);else UnityEngine.Object.DestroyImmediate(baked);}}
    }
}
