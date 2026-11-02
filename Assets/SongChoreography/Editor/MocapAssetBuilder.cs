using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace SongChoreography.Editor
{
    // Development-only Acclaim retargeting. Generated clips are Humanoid muscle
    // curves; raw motion files are never loaded by the shipped application.
    public static class MocapAssetBuilder
    {
        [Serializable]class Bone {public string name;public int parent;public float[] offset;}
        [Serializable]class Frame {public float[] root,rotations,positions;}
        [Serializable]class Data {public int schemaVersion,fps;public string source;public Bone[] bones;public Frame[] frames;}
        // Acclaim's right-handed coordinates must be reflected before applying a
        // heading rotation to Unity's left-handed character. A yaw alone would
        // make the avatar face forward while its captured feet point backward.
        static Vector3 V(float[] values,int index=0)=>new Vector3(values[index],values[index+1],-values[index+2]);
        static Quaternion Q(float[] values,int index)=>new Quaternion(-values[index],-values[index+1],values[index+2],values[index+3]);

        [MenuItem("Tools/Song Choreography/Bake CMU Preview")]
        public static void PreparePreview()
        {
            HumanoidCurveContinuity.Validate();
            const string input="Assets/SongChoreography/Editor/MocapData";
            if(!Directory.Exists(input))return;
            string catalogPath="Assets/Resources/MotionCatalog.json";
            var catalog=JsonUtility.FromJson<MotionCatalog>(File.ReadAllText(catalogPath));
            catalog.motions.RemoveAll(m=>m.id.StartsWith("cmu-preview-",StringComparison.Ordinal)||m.id.StartsWith("charleston-review-",StringComparison.Ordinal));
            foreach(string file in Directory.GetFiles(input,"93_*.json"))
            {
                var data=JsonUtility.FromJson<Data>(File.ReadAllText(file));
                if(data.schemaVersion!=1||data.fps!=30||data.frames.Length<60)throw new InvalidDataException("Invalid Acclaim intermediate file: "+file);
                string id="cmu-preview-"+data.source;var support=Bake(data,id);
                catalog.motions.Add(new MotionDefinition{id=id,name="Charleston 来源预览 "+data.source,resource="Motions/"+id,style="charleston-preview",beats=8,difficulty=1,approved=false,
                    groundToFloor=true,supportHeights=support,source="https://mocap.cs.cmu.edu/search.php?subjectnumber=93 / "+data.source,license="CMU: use in products permitted; no direct resale of motion data",reviewNotes="Source preview only; retargeting, beat trimming and transitions not yet approved."});
                // Candidate windows were selected by endpoint pose and root displacement
                // metrics. They remain unapproved until viewed and contact-checked.
                int begin=data.source=="93_03"?7:data.source=="93_04"?13:data.source=="93_05"?9:35;
                int end=data.source=="93_03"?95:data.source=="93_04"?86:data.source=="93_05"?83:104;
                var cut=new Data{schemaVersion=1,fps=data.fps,source=data.source,bones=data.bones,frames=data.frames.Skip(begin).Take(end-begin+1).ToArray()};
                foreach(bool mirror in new[]{false,true})
                {
                    string candidate="charleston-review-"+data.source+(mirror?"-mirror":"");var candidateSupport=Bake(cut,candidate,true,mirror);
                    catalog.motions.Add(new MotionDefinition{id=candidate,name="Charleston 候选 "+data.source+(mirror?" 左右变体":""),resource="Motions/"+candidate,style="charleston-review",beats=8,difficulty=1,approved=false,
                        transitionBeats=.5f,groundToFloor=true,supportHeights=candidateSupport,source="https://mocap.cs.cmu.edu/search.php?subjectnumber=93 / "+data.source+" frames "+(begin*4+1)+"-"+(end*4+1)+(mirror?" / mirrored":""),license="CMU: use in products permitted; no direct resale of motion data",reviewNotes="Cyclic editing candidate; NOT approved for public choreography. Validate beat labels, foot contact and every allowed connection."});
                }
            }
            AssetDatabase.SaveAssets();AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            MotionPhaseBuilder.Align(catalog);
            MocapReviewGate.Apply(catalog);
            File.WriteAllText(catalogPath,JsonUtility.ToJson(catalog,true));
            Directory.CreateDirectory("Assets/StreamingAssets/MotionCredits");
            foreach(string name in new[]{"SOURCE.json","CREDITS.txt"})File.Copy("Samples/MotionSources/CMU93/"+name,"Assets/StreamingAssets/MotionCredits/"+name,true);
            AssetDatabase.SaveAssets();AssetDatabase.Refresh();
        }

        static List<float> Bake(Data data,string id,bool periodic=false,bool mirror=false)
        {
            var source=AssetDatabase.LoadAssetAtPath<GameObject>(CharacterAssetBuilder.Art+"Superhero_Female_FullBody.fbx");
            var rig=UnityEngine.Object.Instantiate(source);var animator=rig.GetComponent<Animator>();animator.enabled=false;
            var anatomy=new AnatomicalRetargeter(animator);
            var indices=data.bones.Select((b,i)=>new{b.name,i}).ToDictionary(x=>x.name,x=>x.i);
            var hips=animator.GetBoneTransform(HumanBodyBones.Hips);
            float targetLeg=Vector3.Distance(animator.GetBoneTransform(HumanBodyBones.LeftUpperLeg).position,animator.GetBoneTransform(HumanBodyBones.LeftLowerLeg).position)+Vector3.Distance(animator.GetBoneTransform(HumanBodyBones.LeftLowerLeg).position,animator.GetBoneTransform(HumanBodyBones.LeftFoot).position);
            float sourceLeg=V(data.bones[indices["lfemur"]].offset).magnitude+V(data.bones[indices["ltibia"]].offset).magnitude;
            float scale=targetLeg/sourceLeg;
            var first=data.frames[0];
            Vector3 sourceLeft=Vector3.ProjectOnPlane(V(first.positions,indices["lfemur"]*3)-V(first.positions,indices["rfemur"]*3),Vector3.up).normalized;
            Quaternion orientation=Quaternion.FromToRotation(sourceLeft,Vector3.left);
            Vector3 origin=V(first.root);origin.y=0;
            var samples=Enumerable.Range(0,HumanTrait.MuscleCount+7).Select(_=>new float[data.frames.Length]).ToArray();
            var pose=new HumanPose{muscles=new float[HumanTrait.MuscleCount]};
            var body=rig.GetComponentsInChildren<SkinnedMeshRenderer>().OrderByDescending(r=>r.sharedMesh.vertexCount).First();
            var mesh=new Mesh();var weights=body.sharedMesh.boneWeights;
            var feet=new[]{HumanBodyBones.LeftFoot,HumanBodyBones.RightFoot}.Select(b=>animator.GetBoneTransform(b)).ToArray();
            var footVertices=Enumerable.Range(0,weights.Length).Where(i=>
            {
                var w=weights[i];int[] bones={w.boneIndex0,w.boneIndex1,w.boneIndex2,w.boneIndex3};float[] values={w.weight0,w.weight1,w.weight2,w.weight3};float weight=0;
                for(int b=0;b<4;b++)if(feet.Any(root=>body.bones[bones[b]]==root||body.bones[bones[b]].IsChildOf(root)))weight+=values[b];return weight>.8f;
            }).ToArray();
            var sourceSupport=data.frames.Select(frame=>new[]{"lfoot","ltoes","rfoot","rtoes"}.Select(name=>V(frame.positions,indices[name]*3).y).Min()).ToArray();
            var sortedSupport=sourceSupport.OrderBy(y=>y).ToArray();float sourceFloor=sortedSupport[Mathf.FloorToInt((sortedSupport.Length-1)*.1f)];
            var last=data.frames[data.frames.Length-1];
            var desiredHeights=new List<float>();
            var sourcePoses=SourcePoses(data,origin,orientation,scale,periodic);
            var leftPlanes=LegPlanes(sourcePoses,indices["lfemur"],indices["ltibia"],indices["lfoot"],periodic);
            var rightPlanes=LegPlanes(sourcePoses,indices["rfemur"],indices["rtibia"],indices["rfoot"],periodic);
            var watched=new[]{HumanBodyBones.Hips,HumanBodyBones.LeftUpperLeg,HumanBodyBones.LeftLowerLeg,HumanBodyBones.LeftFoot,HumanBodyBones.RightUpperLeg,HumanBodyBones.RightLowerLeg,HumanBodyBones.RightFoot}.Select(b=>animator.GetBoneTransform(b)).ToArray();
            Quaternion[] previousWorld=null;Quaternion previousBody=Quaternion.identity;float worldStep=0,bodyStep=0;string worldJoint="";int worldFrame=0;
            using(var handler=new HumanPoseHandler(animator.avatar,rig.transform))
            {
                for(int index=0;index<data.frames.Length;index++)
                {
                    float phase=index/(float)(data.frames.Length-1),ease=phase*phase*(3-2*phase);
                    var sourcePositions=sourcePoses[index];
                    hips.position=sourcePositions[0];
                    anatomy.Apply(name=>sourcePositions[indices[name]],periodic,phase,leftPlanes[index],rightPlanes[index]);
                    // Preserve source hops while grounding the actual skinned sole. Ankle
                    // height alone cannot tell whether a pointed toe penetrates the floor.
                    float support=sourceSupport[index]-sourceFloor;
                    if(periodic)support-=(sourceSupport[sourceSupport.Length-1]-sourceSupport[0])*ease;
                    float desiredSupport=.01f+Mathf.Max(0,support)*scale;
                    desiredHeights.Add(desiredSupport);
                    body.BakeMesh(mesh);var vertices=mesh.vertices;
                    float actualSupport=footVertices.Min(v=>body.transform.TransformPoint(vertices[v]).y);
                    hips.position+=Vector3.up*(desiredSupport-actualSupport);
                    handler.GetHumanPose(ref pose);
                    if(previousWorld!=null)
                    {
                        for(int bone=0;bone<watched.Length;bone++)
                        {float delta=Quaternion.Angle(previousWorld[bone],watched[bone].rotation);if(delta>worldStep){worldStep=delta;worldJoint=watched[bone].name;worldFrame=index;}}
                        bodyStep=Math.Max(bodyStep,Quaternion.Angle(previousBody,pose.bodyRotation));
                    }
                    previousWorld=watched.Select(t=>t.rotation).ToArray();previousBody=pose.bodyRotation;
                    for(int muscle=0;muscle<HumanTrait.MuscleCount;muscle++)samples[muscle][index]=pose.muscles[muscle];
                    float[] root={pose.bodyPosition.x,pose.bodyPosition.y,pose.bodyPosition.z,pose.bodyRotation.x,pose.bodyRotation.y,pose.bodyRotation.z,pose.bodyRotation.w};
                    for(int k=0;k<7;k++)samples[HumanTrait.MuscleCount+k][index]=root[k];
                }
            }
            UnityEngine.Object.DestroyImmediate(mesh);
            int rootStart=HumanTrait.MuscleCount;
            for(int frame=1;frame<data.frames.Length;frame++)
            {
                float dot=0;for(int k=3;k<7;k++)dot+=samples[rootStart+k][frame-1]*samples[rootStart+k][frame];
                if(dot<0)for(int k=3;k<7;k++)samples[rootStart+k][frame]*=-1;
            }
            for(int frame=0;frame<data.frames.Length;frame++)
            {
                float length=0;for(int k=3;k<7;k++)length+=samples[rootStart+k][frame]*samples[rootStart+k][frame];length=Mathf.Sqrt(length);
                for(int k=3;k<7;k++)samples[rootStart+k][frame]/=length;
            }
            if(mirror)
            {
                var originalSamples=samples.Select(channel=>(float[])channel.Clone()).ToArray();
                for(int muscle=0;muscle<HumanTrait.MuscleCount;muscle++)
                {
                    string name=HumanTrait.MuscleName[muscle];string opposite=name.StartsWith("Left ")?"Right "+name.Substring(5):name.StartsWith("Right ")?"Left "+name.Substring(6):name;
                    int other=Array.IndexOf(HumanTrait.MuscleName,opposite);if(other<0)throw new InvalidOperationException("Missing mirrored muscle: "+name);
                    float sign=name==opposite&&name.Contains("Left-Right")?-1:1;
                    for(int frame=0;frame<data.frames.Length;frame++)samples[muscle][frame]=originalSamples[other][frame]*sign;
                }
                foreach(int channel in new[]{rootStart,rootStart+4,rootStart+5})for(int frame=0;frame<data.frames.Length;frame++)samples[channel][frame]*=-1;
            }
            // HumanPose returns axial angles on a +/-180 degree branch. Those
            // equivalent poses must not interpolate through zero at a wrap.
            for(int muscle=0;muscle<HumanTrait.MuscleCount;muscle++)HumanoidCurveContinuity.Unwrap(HumanTrait.MuscleName[muscle],samples[muscle]);
            if(periodic)
            {
                // Symmetric offline filtering removes abrupt captured kick changes
                // without shifting their timing. Angles are already unwrapped, so
                // filtering never averages +180 with -180 through zero.
                float sigma=data.source=="93_03"?2:1.25f;
                foreach(var channel in samples)HumanoidCurveContinuity.SmoothCycle(channel,sigma);
                var support=desiredHeights.ToArray();HumanoidCurveContinuity.SmoothCycle(support,sigma);desiredHeights=support.ToList();
                for(int frame=0;frame<data.frames.Length;frame++)
                {
                    float length=0;for(int k=3;k<7;k++)length+=samples[rootStart+k][frame]*samples[rootStart+k][frame];length=Mathf.Sqrt(length);
                    for(int k=3;k<7;k++)samples[rootStart+k][frame]/=length;
                }
            }
            float largestStep=0;string largestMuscle="";int largestFrame=0;
            for(int muscle=0;muscle<HumanTrait.MuscleCount;muscle++)for(int frame=1;frame<data.frames.Length;frame++)
            {float step=Math.Abs(samples[muscle][frame]-samples[muscle][frame-1]);if(step>largestStep){largestStep=step;largestMuscle=HumanTrait.MuscleName[muscle];largestFrame=frame;}}
            Debug.Log("MOCAP MUSCLE STEP "+id+": "+largestMuscle+" = "+largestStep+" at "+largestFrame/30f+"s");
            Debug.Log("MOCAP TARGET STEP "+id+": "+worldJoint+" = "+worldStep+" deg at "+worldFrame/30f+"s; body="+bodyStep);
            var curves=samples.Select(channel=>new AnimationCurve(channel.Select((value,frame)=>
            {
                int end=channel.Length-1;
                float slope=frame==0?(channel[1]-channel[0])*data.fps:frame==end?(channel[end]-channel[end-1])*data.fps:(channel[frame+1]-channel[frame-1])*data.fps*.5f;
                if(periodic&&(frame==0||frame==end))slope=(channel[1]-channel[0]+channel[end]-channel[end-1])*data.fps*.5f;
                return new Keyframe(frame/(float)data.fps,value,slope,slope);
            }).ToArray())).ToArray();
            var clip=new AnimationClip{name=id,frameRate=data.fps};
            for(int muscle=0;muscle<HumanTrait.MuscleCount;muscle++)clip.SetCurve("",typeof(Animator),HumanTrait.MuscleName[muscle],curves[muscle]);
            string[] channels={"RootT.x","RootT.y","RootT.z","RootQ.x","RootQ.y","RootQ.z","RootQ.w"};
            for(int k=0;k<7;k++)clip.SetCurve("",typeof(Animator),channels[k],curves[HumanTrait.MuscleCount+k]);
            clip.EnsureQuaternionContinuity();
            // Music time is sampled directly with root motion disabled on Animator.
            // Bake these authored body translations/rotation into the pose; otherwise
            // Mecanim extracts and discards the grounding and source hop corrections.
            var settings=AnimationUtility.GetAnimationClipSettings(clip);
            settings.loopTime=periodic;settings.loopBlend=false;
            settings.keepOriginalOrientation=settings.keepOriginalPositionY=settings.keepOriginalPositionXZ=true;
            settings.loopBlendOrientation=settings.loopBlendPositionY=settings.loopBlendPositionXZ=true;
            AnimationUtility.SetAnimationClipSettings(clip,settings);
            string path="Assets/Resources/Motions/"+id+".anim";var old=AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
            if(old){EditorUtility.CopySerialized(clip,old);UnityEngine.Object.DestroyImmediate(clip);}else AssetDatabase.CreateAsset(clip,path);
            UnityEngine.Object.DestroyImmediate(rig);Debug.Log("CMU PREVIEW BAKED "+id+", seconds="+((data.frames.Length-1)/(float)data.fps)+", scale="+scale);
            return desiredHeights;
        }
        static Vector3[][] SourcePoses(Data data,Vector3 origin,Quaternion orientation,float scale,bool periodic)
        {
            var result=new Vector3[data.frames.Length][];var first=data.frames[0];var last=data.frames[data.frames.Length-1];
            for(int frame=0;frame<data.frames.Length;frame++)
            {
                float t=frame/(float)(data.frames.Length-1),ease=t*t*(3-2*t);
                var rotations=new Quaternion[data.bones.Length];var positions=new Vector3[data.bones.Length];
                Vector3 root=V(data.frames[frame].root)-origin;if(periodic)root-=(V(last.root)-V(first.root))*ease;
                for(int bone=0;bone<data.bones.Length;bone++)
                {
                    Quaternion rotation=Q(data.frames[frame].rotations,bone*4);
                    if(periodic)
                    {
                        Quaternion difference=Q(last.rotations,bone*4)*Quaternion.Inverse(Q(first.rotations,bone*4));
                        rotation=Quaternion.Inverse(Quaternion.Slerp(Quaternion.identity,difference,ease))*rotation;
                    }
                    rotations[bone]=rotation;int parent=data.bones[bone].parent;
                    positions[bone]=parent<0?root:positions[parent]+rotations[parent]*V(data.bones[parent].offset);
                }
                result[frame]=positions.Select(p=>orientation*p*scale).ToArray();
            }
            return result;
        }
        static Vector3[] LegPlanes(Vector3[][] poses,int upper,int lower,int end,bool periodic)
        {
            int count=periodic?poses.Length-1:poses.Length;var normals=new Vector3[count];var weights=new float[count];var directions=new Vector3[count];
            for(int i=0;i<count;i++)
            {
                var u=(poses[i][lower]-poses[i][upper]).normalized;var v=(poses[i][end]-poses[i][lower]).normalized;
                var cross=Vector3.Cross(u,v);directions[i]=u;weights[i]=cross.sqrMagnitude;normals[i]=cross.normalized;
            }
            int anchor=Array.IndexOf(weights,weights.Max());Vector3 previous=normals[anchor];
            for(int step=0;step<count;step++)
            {
                int index=periodic?(anchor+step)%count:step;Vector3 n=normals[index];
                if(n.sqrMagnitude<.5f)n=previous;if(Vector3.Dot(n,previous)<0)n=-n;
                normals[index]=n;previous=n;
            }
            var filtered=new Vector3[poses.Length];
            for(int i=0;i<count;i++)
            {
                Vector3 sum=Vector3.zero;
                for(int offset=-4;offset<=4;offset++)
                {
                    int index=periodic?(i+offset+count)%count:Mathf.Clamp(i+offset,0,count-1);
                    sum+=normals[index]*(Mathf.Exp(-offset*offset/8f)*Mathf.Max(.001f,weights[index]));
                }
                sum=Vector3.ProjectOnPlane(sum,directions[i]);filtered[i]=sum.sqrMagnitude>.000001f?sum.normalized:normals[i];
            }
            // A weighted sum can cross zero near a straight knee, even when its
            // input normals were unrolled. Resolve the filtered plane's branch
            // against parallel transport, after projection onto the moving thigh.
            // A sign change here would turn a positive bend into a 180-degree kick.
            previous=filtered[anchor];
            for(int step=1;step<count;step++)
            {
                int index=periodic?(anchor+step)%count:step;
                Vector3 transported=Vector3.ProjectOnPlane(previous,directions[index]);
                if(Vector3.Dot(filtered[index],transported)<0)filtered[index]=-filtered[index];
                previous=filtered[index];
            }
            if(periodic)filtered[filtered.Length-1]=filtered[0];return filtered;
        }
    }
}
