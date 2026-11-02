using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace SongChoreography
{
    public sealed partial class StudioApp
    {
        [Serializable]class MotionReview
        {
            public string id;
            public int samples;
            public float lowestSole=100,highestSupportSole=-100,loopPositionGap,loopRotationGap;
            public float maxFramePositionGap,maxFrameRotationGap,firstFeetFacingDot;
            public string maxPositionJoint="",maxRotationJoint="";
            public float worstFrameTime;
            public float sourceDuration,practiceDuration,musicBpm;
            public bool approved=false;
        }
        [Serializable]class MotionReviews {public List<MotionReview> clips=new List<MotionReview>();}
        // Development-only visual review command. Preview clips are unapproved
        // and never appear as a publicly selectable dance style.
        IEnumerator MotionPreview()
        {
            yield return null;Guard(()=>UseBuiltin(0));while(loading)yield return null;
            if(!Ready){Debug.LogError("Motion preview music failed: "+error);Application.Quit(2);yield break;}
            playback.Pause();ResetCamera();string folder=Path.Combine(Path.GetDirectoryName(Application.dataPath),"MotionPreview");Directory.CreateDirectory(folder);
            var saved=project.segments;string oldTitle=project.title;
            var reviews=new MotionReviews();
            foreach(var motion in catalog.motions.Where(m=>m.style=="charleston-preview"||m.style=="charleston-review"))
            {
                var clip=Resources.Load<AnimationClip>(motion.resource);
                project.title=motion.name;
                double practiceDuration=motion.beats*60/project.grid.bpm;
                project.segments=new List<DanceSegment>{new DanceSegment{motionId=motion.id,startSeconds=0,endSeconds=practiceDuration,beatCount=motion.beats}};
                var body=characterObject.GetComponentsInChildren<SkinnedMeshRenderer>().OrderByDescending(r=>r.sharedMesh.vertexCount).First();
                var footRoots=new[]{HumanBodyBones.LeftFoot,HumanBodyBones.RightFoot}.Select(b=>diagnosticAnimator.GetBoneTransform(b)).ToArray();
                var weights=body.sharedMesh.boneWeights;
                var footVertices=footRoots.Select(root=>Enumerable.Range(0,weights.Length).Where(i=>
                {
                    var w=weights[i];int[] bones={w.boneIndex0,w.boneIndex1,w.boneIndex2,w.boneIndex3};float[] values={w.weight0,w.weight1,w.weight2,w.weight3};float weight=0;
                    for(int b=0;b<4;b++)if(body.bones[bones[b]]==root||body.bones[bones[b]].IsChildOf(root))weight+=values[b];return weight>.8f;
                }).ToArray()).ToArray();
                var mesh=new Mesh();var review=new MotionReview{id=motion.id,sourceDuration=clip.length,practiceDuration=(float)practiceDuration,musicBpm=(float)project.grid.bpm};
                int total=Mathf.CeilToInt((float)practiceDuration*60);
                Vector3[] startPositions=null,previousPositions=null;Quaternion[] startRotations=null,previousRotations=null;
                var watched=new[]{HumanBodyBones.Hips,HumanBodyBones.Head,HumanBodyBones.LeftUpperLeg,HumanBodyBones.LeftLowerLeg,HumanBodyBones.LeftFoot,HumanBodyBones.LeftToes,HumanBodyBones.RightUpperLeg,HumanBodyBones.RightLowerLeg,HumanBodyBones.RightFoot,HumanBodyBones.RightToes,HumanBodyBones.LeftUpperArm,HumanBodyBones.LeftLowerArm,HumanBodyBones.LeftHand,HumanBodyBones.RightUpperArm,HumanBodyBones.RightLowerArm,HumanBodyBones.RightHand}.Select(b=>diagnosticAnimator.GetBoneTransform(b)).ToArray();
                for(int frame=0;frame<=total;frame++)
                {
                    playback.Seek(Math.Min(practiceDuration-.0001,frame/60.0));body.BakeMesh(mesh);var vertices=mesh.vertices;
                    float left=footVertices[0].Min(i=>body.transform.TransformPoint(vertices[i]).y),right=footVertices[1].Min(i=>body.transform.TransformPoint(vertices[i]).y);
                    review.lowestSole=Math.Min(review.lowestSole,Math.Min(left,right));review.highestSupportSole=Math.Max(review.highestSupportSole,Math.Min(left,right));review.samples++;
                    if(frame==0)
                    {
                        startPositions=watched.Select(b=>b.position).ToArray();startRotations=watched.Select(b=>b.rotation).ToArray();
                        var eyes=characterObject.GetComponentsInChildren<SkinnedMeshRenderer>().First(r=>r.name.Equals("Eyes",StringComparison.OrdinalIgnoreCase));
                        var eyeMesh=new Mesh();eyes.BakeMesh(eyeMesh);Vector3 center=Vector3.zero;foreach(var vertex in eyeMesh.vertices)center+=eyes.transform.TransformPoint(vertex);center/=eyeMesh.vertexCount;Destroy(eyeMesh);
                        Vector3 face=Vector3.ProjectOnPlane(center-diagnosticAnimator.GetBoneTransform(HumanBodyBones.Head).position,Vector3.up).normalized;
                        Vector3 toes=Vector3.zero;foreach(var side in new[]{new[]{HumanBodyBones.LeftFoot,HumanBodyBones.LeftToes},new[]{HumanBodyBones.RightFoot,HumanBodyBones.RightToes}})toes+=Vector3.ProjectOnPlane(diagnosticAnimator.GetBoneTransform(side[1]).position-diagnosticAnimator.GetBoneTransform(side[0]).position,Vector3.up).normalized;
                        review.firstFeetFacingDot=Vector3.Dot(face,toes.normalized);
                    }
                    if(previousPositions!=null)
                    {
                        for(int joint=0;joint<watched.Length;joint++)
                        {
                            float delta=Vector3.Distance(watched[joint].position,previousPositions[joint]);
                            if(delta>review.maxFramePositionGap){review.maxFramePositionGap=delta;review.maxPositionJoint=watched[joint].name;review.worstFrameTime=frame/60f;}
                            float angle=Quaternion.Angle(watched[joint].rotation,previousRotations[joint]);
                            if(angle>review.maxFrameRotationGap){review.maxFrameRotationGap=angle;review.maxRotationJoint=watched[joint].name;}
                        }
                    }
                    previousPositions=watched.Select(b=>b.position).ToArray();previousRotations=watched.Select(b=>b.rotation).ToArray();
                    if(frame==total){review.loopPositionGap=watched.Select((b,i)=>Vector3.Distance(b.position,startPositions[i])).Max();review.loopRotationGap=watched.Select((b,i)=>Quaternion.Angle(b.rotation,startRotations[i])).Max();}
                }
                Destroy(mesh);reviews.clips.Add(review);
                playback.Seek(review.worstFrameTime);yield return null;yield return new WaitForEndOfFrame();
                ScreenCapture.CaptureScreenshot(Path.Combine(folder,motion.id+"-worst-gap.png"));yield return null;
                for(int frame=0;frame<5;frame++)
                {
                    playback.Seek(practiceDuration*(.08+frame*.2));yield return null;yield return new WaitForEndOfFrame();
                    ScreenCapture.CaptureScreenshot(Path.Combine(folder,motion.id+"-"+frame+".png"));yield return null;
                }
                Debug.Log("MOTION PREVIEW RENDERED "+motion.id);
            }
            File.WriteAllText(Path.Combine(folder,"review.json"),JsonUtility.ToJson(reviews,true));
            project.segments=saved;project.title=oldTitle;playback.Seek(0);dirty=false;
            yield return new WaitForSecondsRealtime(.5f);Application.Quit(0);
        }
    }
}
