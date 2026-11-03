using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace SongChoreography
{
    // Body-aware, length-preserving arm IK, applied AFTER clip blending.
    // It is evaluated from the authored pose, without a time-history filter,
    // so seeks, slow playback and editor samples use identical correction.
    public sealed class ArmBodyAvoidance : IDisposable
    {
        sealed class Arm
        {
            public Transform upper,lower,hand;public Transform[] fingers;public bool left;
            public Vector3[] offsets;public float[] radii;
            public readonly List<SkinPoint> handSkin=new List<SkinPoint>(),forearmSkin=new List<SkinPoint>();
            public Vector3 authoredWrist;public Quaternion authoredRotation;
            public Vector3 authoredLocalWrist;
        }
        sealed class SkinData
        {
            public Transform[] bones;public Matrix4x4[] bind,matrices;public Vector3[] vertices;public BoneWeight[] weights;public Matrix4x4 reference;
            public int[] contactTriangles,contactVertices;public Vector3[] contactWorld;
            public TriangleSurfaceTree contactTree;
#if UNITY_EDITOR
            public int crossChecks;
#endif
            public void Refresh(){for(int i=0;i<bones.Length;i++)matrices[i]=bones[i].localToWorldMatrix*bind[i];}
            public Vector3 Point(int index)
            {var w=weights[index];var v=vertices[index];return matrices[w.boneIndex0].MultiplyPoint3x4(v)*w.weight0+matrices[w.boneIndex1].MultiplyPoint3x4(v)*w.weight1+matrices[w.boneIndex2].MultiplyPoint3x4(v)*w.weight2+matrices[w.boneIndex3].MultiplyPoint3x4(v)*w.weight3;}
        }
        sealed class SkinPoint {public SkinData skin;public int index;}
        readonly Animator animator;readonly Arm[] arms;readonly BodyEnvelope surface;readonly SkinnedBodySurface exactSurface;
        readonly List<SkinData> skins=new List<SkinData>();
        bool deepContact;
        public float LastWristCorrection{get;private set;}
        public int LastIterations{get;private set;}
        public ArmBodyAvoidance(GameObject actor,Animator animator,bool exact=false)
        {
            this.animator=animator;if(exact)exactSurface=new SkinnedBodySurface(actor,animator);else surface=new BodyEnvelope(actor,animator);
            arms=new[]{MakeArm(animator,true),MakeArm(animator,false)};
            var contactBones=new HashSet<Transform>(arms.SelectMany(a=>a.hand.GetComponentsInChildren<Transform>()));foreach(var arm in arms)contactBones.Add(arm.lower);
            foreach(var renderer in actor.GetComponentsInChildren<SkinnedMeshRenderer>())
            {
                var mesh=renderer.sharedMesh;var data=new SkinData{bones=renderer.bones,bind=mesh.bindposes,vertices=mesh.vertices,weights=mesh.boneWeights,matrices=new Matrix4x4[renderer.bones.Length],reference=renderer.transform.localToWorldMatrix};data.Refresh();skins.Add(data);
                if(exact)
                {
                    var ids=new HashSet<int>(Enumerable.Range(0,data.bones.Length).Where(i=>contactBones.Contains(data.bones[i])));var selected=new bool[data.weights.Length];for(int i=0;i<selected.Length;i++){var w=data.weights[i];selected[i]=(ids.Contains(w.boneIndex0)?w.weight0:0)+(ids.Contains(w.boneIndex1)?w.weight1:0)+(ids.Contains(w.boneIndex2)?w.weight2:0)+(ids.Contains(w.boneIndex3)?w.weight3:0)>=.6f;}
                    var triangles=mesh.triangles;var contacts=new List<int>();for(int i=0;i<triangles.Length;i+=3)if(selected[triangles[i]]&&selected[triangles[i+1]]&&selected[triangles[i+2]]){contacts.Add(triangles[i]);contacts.Add(triangles[i+1]);contacts.Add(triangles[i+2]);}data.contactTriangles=contacts.ToArray();data.contactVertices=contacts.Distinct().ToArray();data.contactWorld=new Vector3[data.vertices.Length];foreach(int i in data.contactVertices)data.contactWorld[i]=data.Point(i);data.contactTree=new TriangleSurfaceTree(data.contactWorld,data.contactTriangles);
                }
                foreach(var arm in arms)
                {
                    var hands=new HashSet<Transform>(arm.hand.GetComponentsInChildren<Transform>());var handIds=new HashSet<int>(Enumerable.Range(0,data.bones.Length).Where(i=>hands.Contains(data.bones[i])));int forearm=Array.IndexOf(data.bones,arm.lower);
                    var chosenHand=new Dictionary<int,List<Vector3>>();var chosenForearm=new List<Vector3>();
                    for(int i=0;i<data.weights.Length;i++)
                    {
                        var w=data.weights[i];float hand=(handIds.Contains(w.boneIndex0)?w.weight0:0)+(handIds.Contains(w.boneIndex1)?w.weight1:0)+(handIds.Contains(w.boneIndex2)?w.weight2:0)+(handIds.Contains(w.boneIndex3)?w.weight3:0);
                        float lower=(w.boneIndex0==forearm?w.weight0:0)+(w.boneIndex1==forearm?w.weight1:0)+(w.boneIndex2==forearm?w.weight2:0)+(w.boneIndex3==forearm?w.weight3:0);
                        if(hand<.6f&&lower<.65f)continue;var point=data.reference.MultiplyPoint3x4(data.vertices[i]);
                        if(hand>=.6f)
                        {
                            int dominant=w.boneIndex0;if(!chosenHand.TryGetValue(dominant,out var group)){group=new List<Vector3>();chosenHand.Add(dominant,group);}
                            if(group.All(p=>(p-point).sqrMagnitude>.000324f)){group.Add(point);arm.handSkin.Add(new SkinPoint{skin=data,index=i});}
                        }
                        else if(lower>=.65f&&chosenForearm.All(p=>(p-point).sqrMagnitude>.0004f)){chosenForearm.Add(point);arm.forearmSkin.Add(new SkinPoint{skin=data,index=i});}
                    }
                }
            }
        }
        static Arm MakeArm(Animator animator,bool left)
        {
            var fingers=new List<Transform>();int first=(int)(left?HumanBodyBones.LeftThumbProximal:HumanBodyBones.RightThumbProximal);
            for(int i=first;i<first+15;i++){var bone=animator.GetBoneTransform((HumanBodyBones)i);if(bone)fingers.Add(bone);}
            return new Arm{left=left,upper=animator.GetBoneTransform(left?HumanBodyBones.LeftUpperArm:HumanBodyBones.RightUpperArm),lower=animator.GetBoneTransform(left?HumanBodyBones.LeftLowerArm:HumanBodyBones.RightLowerArm),hand=animator.GetBoneTransform(left?HumanBodyBones.LeftHand:HumanBodyBones.RightHand),fingers=fingers.ToArray(),offsets=new Vector3[fingers.Count+1],radii=new float[fingers.Count+1]};
        }
        static void Solve(Arm arm,Vector3 target,Vector3 pole,Quaternion handRotation,bool exact=false)
        {
            var shoulder=arm.upper.position;float upperLength=Vector3.Distance(shoulder,arm.lower.position),lowerLength=Vector3.Distance(arm.lower.position,arm.hand.position);var direction=target-shoulder;float distance=Mathf.Clamp(direction.magnitude,Mathf.Abs(upperLength-lowerLength)+(exact?.00001f:.001f),exact?upperLength+lowerLength-.00001f:(upperLength+lowerLength)*.997f);direction=direction.sqrMagnitude>1e-8f?direction.normalized:Vector3.down;target=shoulder+direction*distance;
            var bend=Vector3.ProjectOnPlane(pole-shoulder,direction);if(bend.sqrMagnitude<1e-8f)bend=Vector3.ProjectOnPlane(Vector3.forward,direction);if(bend.sqrMagnitude<1e-8f)bend=Vector3.ProjectOnPlane(Vector3.right,direction);bend.Normalize();
            float along=(upperLength*upperLength+distance*distance-lowerLength*lowerLength)/(2*distance);float height=Mathf.Sqrt(Mathf.Max(0,upperLength*upperLength-along*along));var elbow=shoulder+direction*along+bend*height;
            arm.upper.rotation=Quaternion.FromToRotation(arm.lower.position-shoulder,elbow-shoulder)*arm.upper.rotation;
            arm.lower.rotation=Quaternion.FromToRotation(arm.hand.position-arm.lower.position,target-arm.lower.position)*arm.lower.rotation;arm.hand.rotation=handRotation;
        }
        public void Apply(ArmPoseSeed seed=null)
        {
            LastWristCorrection=0;LastIterations=0;if(!(exactSurface!=null?exactSurface.Valid:surface.Valid))return;
            var leftHip=animator.GetBoneTransform(HumanBodyBones.LeftUpperLeg);var rightHip=animator.GetBoneTransform(HumanBodyBones.RightUpperLeg);var right=(rightHip.position-leftHip.position).normalized;
            UpdateSurface();foreach(var skin in skins)skin.Refresh();
            // Seeds retain the current authored wrist even in a free pose.
            foreach(var arm in arms){arm.authoredWrist=arm.hand.position;arm.authoredRotation=arm.hand.rotation;arm.authoredLocalWrist=arm.upper.parent.InverseTransformPoint(arm.hand.position);}
            if(exactSurface!=null&&!HasActualContact())return;
            for(int armIndex=0;armIndex<arms.Length;armIndex++)
            {
                var arm=arms[armIndex];arm.authoredWrist=arm.hand.position;arm.authoredRotation=arm.hand.rotation;arm.authoredLocalWrist=arm.upper.parent.InverseTransformPoint(arm.hand.position);
                if(seed==null)continue;
                float distance=Vector3.Distance(arm.upper.parent.TransformPoint(seed.correctedHand[armIndex]),arm.hand.position);
                float step=Vector3.Distance(arm.upper.parent.TransformPoint(seed.rawHand[armIndex]),arm.hand.position);float alpha=distance>.001f?Mathf.Min(1,(step*1.3f+.025f)/distance):1;
                bool free=true;foreach(var sample in arm.handSkin)if(Query(sample.skin.Point(sample.index)).distance<.012f){free=false;break;}
                if(free)foreach(var sample in arm.forearmSkin)if(Query(sample.skin.Point(sample.index)).distance<.012f){free=false;break;}
                if(!free)alpha=Mathf.Min(alpha,.22f);
                arm.upper.localRotation=Quaternion.Slerp(seed.rotations[armIndex*2],arm.upper.localRotation,alpha);arm.lower.localRotation=Quaternion.Slerp(seed.rotations[armIndex*2+1],arm.lower.localRotation,alpha);arm.hand.rotation=arm.authoredRotation;
            }
            // Refresh the bone envelopes for the evaluated body pose. The
            // independent Editor audit checks the final deformed skin too.
            for(int pass=0;pass<2;pass++)
            {
              if(pass>0)UpdateSurface();bool moved=false;
              foreach(var arm in arms)
              {
                if(!arm.upper||!arm.lower||!arm.hand)continue;
                var rotation=arm.authoredRotation;var side=right*(arm.left?-1:1);
                for(int iteration=0;iteration<(pass==0?7:5);iteration++)
                {
                    LastIterations++;
                    foreach(var skin in skins)skin.Refresh();
                    float worst=0;Vector3 handPush=Escape(arm.hand.position,exactSurface!=null?.003f:.014f,side);
                    foreach(var sample in arm.handSkin){var push=Escape(sample.skin.Point(sample.index),exactSurface!=null?.004f:.02f,side);if(push.sqrMagnitude>handPush.sqrMagnitude)handPush=push;}
                    if(arm.handSkin.Count==0)foreach(var finger in arm.fingers){var push=Escape(finger.position,exactSurface!=null?.004f:.015f,side);if(push.sqrMagnitude>handPush.sqrMagnitude)handPush=push;}
                    worst=handPush.magnitude;
                    if(worst>.0005f)Solve(arm,arm.hand.position+handPush,arm.lower.position,rotation,exactSurface!=null);
                    foreach(var skin in skins)skin.Refresh();
                    Vector3 armPush=Vector3.zero,armPoint=arm.lower.position;
                    if(arm.forearmSkin.Count==0)foreach(float unit in ForearmUnits)
                    {
                        var point=Vector3.Lerp(arm.lower.position,arm.hand.position,unit);var push=Escape(point,exactSurface!=null?.005f:.029f,side);if(push.sqrMagnitude>armPush.sqrMagnitude){armPush=push;armPoint=point;}
                    }
                    foreach(var sample in arm.forearmSkin)
                    {
                        var point=sample.skin.Point(sample.index);var push=Escape(point,exactSurface!=null?.004f:.022f,side);if(push.sqrMagnitude>armPush.sqrMagnitude){armPush=push;armPoint=point;}
                    }
                    worst=Mathf.Max(worst,armPush.magnitude);
                    moved|=worst>.0005f;
                    if(armPush.sqrMagnitude>.00000025f)
                    {
                        var pivot=arm.upper.position;arm.upper.rotation=Quaternion.FromToRotation(armPoint-pivot,armPoint+armPush-pivot)*arm.upper.rotation;arm.hand.rotation=rotation;
                    }
                    if(worst<.002f)break;
                }
                LastWristCorrection=Mathf.Max(LastWristCorrection,Vector3.Distance(arm.authoredWrist,arm.hand.position));
              }
              if(!moved)break;
            }
        }
        public ArmPoseSeed CaptureSeed()
        {
            var result=new ArmPoseSeed();for(int i=0;i<arms.Length;i++)
            {var arm=arms[i];result.rotations[i*2]=arm.upper.localRotation;result.rotations[i*2+1]=arm.lower.localRotation;result.rawHand[i]=arm.authoredLocalWrist;result.correctedHand[i]=arm.upper.parent.InverseTransformPoint(arm.hand.position);}return result;
        }
        static readonly float[] ForearmUnits={.25f,.5f,.75f};
        bool HasActualContact()
        {
            deepContact=false;
            foreach(var skin in skins)
            {
                foreach(int i in skin.contactVertices)skin.contactWorld[i]=skin.Point(i);skin.contactTree.Refit();bool intersects=exactSurface.Intersects(skin.contactTree);
#if UNITY_EDITOR
                if(skin.crossChecks++<3){bool direct=false;for(int i=0;i<skin.contactTriangles.Length;i+=3)if(exactSurface.IntersectsTriangle(skin.contactWorld[skin.contactTriangles[i]],skin.contactWorld[skin.contactTriangles[i+1]],skin.contactWorld[skin.contactTriangles[i+2]])){direct=true;break;}if(direct!=intersects)throw new InvalidOperationException("Skin contact hierarchy disagrees with direct triangle tests.");}
#endif
                if(intersects){deepContact=true;return true;}
            }
            foreach(var arm in arms)
            {
                if(exactSurface.Interior(arm.hand.position).inside||exactSurface.Interior(arm.lower.position).inside){deepContact=true;return true;}
            }
            // A connected finger/forearm entering from outside must cross the
            // tested skin triangles. Wrist/elbow containment additionally
            // catches an entire limb placed inside the body.
            return false;
        }
        void UpdateSurface(){if(exactSurface!=null)exactSurface.Update();else surface.Update();}
        SkinnedBodySurface.Contact Query(Vector3 point)=>exactSurface!=null?exactSurface.Query(point,.03f):surface.Query(point);
        Vector3 Escape(Vector3 point,float radius,Vector3 side)=>exactSurface!=null?exactSurface.EscapeNear(point,radius,side,deepContact):surface.Escape(point,radius,side);
        public void Dispose(){surface?.Dispose();exactSurface?.Dispose();}
    }
}
