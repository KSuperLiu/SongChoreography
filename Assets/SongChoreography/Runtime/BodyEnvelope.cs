using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace SongChoreography
{
    // Conservative convex skin envelopes attached to body bones. Each envelope
    // is fitted from mesh bind positions, rather than hard-coded body radii.
    // Runtime queries use 26 support planes; the Editor audits the actual mesh.
    public sealed class BodyEnvelope : IDisposable
    {
        sealed class Volume
        {
            public Transform bone;public float[] support=Enumerable.Repeat(float.NegativeInfinity,26).ToArray();
            public Vector3[] normals=new Vector3[26];public float[] distances=new float[26];public int points;
        }
        readonly List<Volume> volumes=new List<Volume>();
        static readonly Vector3[] Directions=MakeDirections();
        static Vector3[] MakeDirections()
        {
            var result=new List<Vector3>();for(int x=-1;x<=1;x++)for(int y=-1;y<=1;y++)for(int z=-1;z<=1;z++)if(x!=0||y!=0||z!=0)result.Add(new Vector3(x,y,z).normalized);return result.ToArray();
        }
        public bool Valid=>volumes.Count>0;
        public BodyEnvelope(GameObject actor,Animator animator)
        {
            var bones=new HashSet<Transform>(new[]{HumanBodyBones.Hips,HumanBodyBones.Spine,HumanBodyBones.Chest,HumanBodyBones.UpperChest,HumanBodyBones.Neck,HumanBodyBones.Head,HumanBodyBones.LeftUpperLeg,HumanBodyBones.RightUpperLeg,HumanBodyBones.LeftLowerLeg,HumanBodyBones.RightLowerLeg,HumanBodyBones.LeftFoot,HumanBodyBones.RightFoot,HumanBodyBones.LeftToes,HumanBodyBones.RightToes}.Select(animator.GetBoneTransform).Where(t=>t));
            var map=new Dictionary<Transform,Volume>();
            foreach(var skin in actor.GetComponentsInChildren<SkinnedMeshRenderer>())
            {
                var mesh=skin.sharedMesh;var vertices=mesh.vertices;var weights=mesh.boneWeights;var bind=mesh.bindposes;
                for(int i=0;i<weights.Length;i++)
                {
                    var w=weights[i];int[] indices={w.boneIndex0,w.boneIndex1,w.boneIndex2,w.boneIndex3};float[] amounts={w.weight0,w.weight1,w.weight2,w.weight3};float sum=0;
                    for(int j=0;j<4;j++)if(bones.Contains(skin.bones[indices[j]]))sum+=amounts[j];if(sum<.65f)continue;
                    for(int j=0;j<4;j++)
                    {
                        var bone=skin.bones[indices[j]];if(amounts[j]<.05f||!bones.Contains(bone))continue;
                        if(!map.TryGetValue(bone,out var volume)){volume=new Volume{bone=bone};map.Add(bone,volume);}
                        var point=bind[indices[j]].MultiplyPoint3x4(vertices[i]);for(int p=0;p<Directions.Length;p++)volume.support[p]=Mathf.Max(volume.support[p],Vector3.Dot(point,Directions[p]));volume.points++;
                    }
                }
            }
            volumes.AddRange(map.Values.Where(v=>v.points>=12));Update();
        }
        public void Update()
        {
            foreach(var v in volumes)
            {
                var matrix=v.bone.localToWorldMatrix;var normalMatrix=matrix.inverse.transpose;
                for(int i=0;i<Directions.Length;i++)
                {
                    var normal=normalMatrix.MultiplyVector(Directions[i]).normalized;v.normals[i]=normal;
                    v.distances[i]=Vector3.Dot(normal,matrix.MultiplyPoint3x4(Directions[i]*v.support[i]))+.004f;
                }
            }
        }
        public SkinnedBodySurface.Contact Query(Vector3 point)
        {
            float nearest=float.PositiveInfinity;Vector3 n=Vector3.zero;bool inside=false;
            foreach(var v in volumes)
            {
                float distance=float.NegativeInfinity;int plane=0;
                for(int i=0;i<Directions.Length;i++){float value=Vector3.Dot(point,v.normals[i])-v.distances[i];if(value>distance){distance=value;plane=i;}}
                if(distance<0){if(!inside||distance<nearest){inside=true;nearest=distance;n=v.normals[plane];}}
                else if(!inside&&distance<nearest){nearest=distance;n=v.normals[plane];}
            }
            return new SkinnedBodySurface.Contact{found=Valid,inside=inside,distance=nearest,normal=n,point=point-n*nearest};
        }
        float Exit(Vector3 point,Vector3 direction,float margin)
        {
            float result=0;
            for(int pass=0;pass<4;pass++)
            {
                bool changed=false;
                foreach(var v in volumes)
                {
                    float lo=0,hi=float.PositiveInfinity;
                    for(int i=0;i<Directions.Length;i++)
                    {
                        float numerator=v.distances[i]+margin-Vector3.Dot(point,v.normals[i]);float denominator=Vector3.Dot(direction,v.normals[i]);
                        if(Mathf.Abs(denominator)<1e-7f){if(numerator<0){hi=-1;break;}continue;}
                        float t=numerator/denominator;if(denominator>0)hi=Mathf.Min(hi,t);else lo=Mathf.Max(lo,t);
                    }
                    if(hi>=lo&&lo<=result+.001f&&hi>result&&hi<.5f){result=hi+.001f;changed=true;}
                }
                if(!changed)break;
            }
            return result;
        }
        public Vector3 Escape(Vector3 point,float radius,Vector3 preferredSide)
        {
            float nearest=float.PositiveInfinity;Vector3 normal=Vector3.zero;bool inside=false,found=false;Vector3 weightedNormal=Vector3.zero;float weightSum=0;
            foreach(var volume in volumes)
            {
                float distance=float.NegativeInfinity;int plane=0;bool excluded=false;
                for(int i=0;i<Directions.Length;i++)
                {
                    float value=Vector3.Dot(point,volume.normals[i])-volume.distances[i];if(value>=radius){excluded=true;break;}if(value>distance){distance=value;plane=i;}
                }
                if(excluded)continue;found=true;
                // Rounded support-plane gradients avoid switching abruptly
                // between two faces or overlapping bone envelopes.
                var smooth=Vector3.zero;
                for(int i=0;i<Directions.Length;i++)
                {float value=Vector3.Dot(point,volume.normals[i])-volume.distances[i];float w=Mathf.Max(0,1-(distance-value)/.035f);smooth+=volume.normals[i]*(w*w);}
                float weight=(radius-distance)*(radius-distance);weightedNormal+=(smooth.sqrMagnitude>.00001f?smooth.normalized:volume.normals[plane])*weight;weightSum+=weight;
                if(distance<0){if(!inside||distance<nearest){inside=true;nearest=distance;normal=volume.normals[plane];}}
                else if(!inside&&distance<nearest){nearest=distance;normal=volume.normals[plane];}
            }
            var contact=new SkinnedBodySurface.Contact{found=found,inside=inside,distance=nearest,normal=normal};if(!contact.found||contact.distance>=radius)return Vector3.zero;
            var direction=weightedNormal.sqrMagnitude>.0000001f?weightedNormal.normalized:contact.normal;
            if(contact.inside&&contact.distance<-.025f&&preferredSide.sqrMagnitude>.1f)
            {
                direction=Vector3.Slerp(direction,preferredSide.normalized,Mathf.SmoothStep(0,1,Mathf.InverseLerp(.025f,.065f,-contact.distance))).normalized;
            }
            float exit=Exit(point,direction,radius+.001f);var push=direction*(exit>0?exit:radius-contact.distance+.001f);
            return Vector3.ClampMagnitude(push,.22f);
        }
        public void Dispose(){}
    }
}
