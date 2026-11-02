using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace SongChoreography.Editor
{
    // Personal data is read only from an ignored local directory. This importer
    // calibrates and constrains pose rotations for each rig; no SMPL meshes.
    public static class PersonalMotionBuilder
    {
        [Serializable]class Pose {public float[] root,rotations;}
        [Serializable]class Label {public string name,style1,style2;}
        [Serializable]class Input
        {
            public int schemaVersion,fps;public bool personalUseOnly;
            public string sourceId,source,license,sourceSha256;public Label sourceLabel;
            public float sourceBpm,startSeconds;public float[] rootDrift,neutralJoints;public Pose[] frames;
        }
        [Serializable]class Review {public int schemaVersion=2;public string sourceSha256;public Entry[] motions;}
        [Serializable]class Entry {public string id,sha256,name,reviewer,reviewNotes;public string[] safeNext;}
        static readonly int[] Parents={-1,0,0,0,1,2,3,4,5,6,7,8,9,9,9,12,13,14,16,17,18,19,20,22,23,20,25,26,20,28,29,20,31,32,20,34,35,21,37,38,21,40,41,21,43,44,21,46,47,21,49,50};
        static HumanBodyBones[] Bones()
        {
            var names=new List<string>{"Hips","LeftUpperLeg","RightUpperLeg","Spine","LeftLowerLeg","RightLowerLeg","Chest","LeftFoot","RightFoot","UpperChest","LeftToes","RightToes","Neck","LeftShoulder","RightShoulder","Head","LeftUpperArm","RightUpperArm","LeftLowerArm","RightLowerArm","LeftHand","RightHand"};
            foreach(string side in new[]{"Left","Right"})foreach(string finger in new[]{"Index","Middle","Little","Ring","Thumb"})foreach(string joint in new[]{"Proximal","Intermediate","Distal"})names.Add(side+finger+joint);
            return names.Select(n=>(HumanBodyBones)Enum.Parse(typeof(HumanBodyBones),n)).ToArray();
        }
        static Quaternion Local(Pose pose,int bone)
        {int i=bone*4;return SourceSkeletonCalibration.ConvertRotation(new Quaternion(pose.rotations[i],pose.rotations[i+1],pose.rotations[i+2],pose.rotations[i+3])).normalized;}
        // SMPL-X is right-handed. Reflect BOTH positions and rotations before
        // applying a heading; a yaw alone cannot change handedness.
        static Vector3 Root(Pose pose)=>SourceSkeletonCalibration.ConvertPosition(new Vector3(pose.root[0],pose.root[1],pose.root[2]));

        [MenuItem("Tools/Song Choreography/Prepare Personal Learning Motions")]
        public static void Prepare()
        {
            const string path="PersonalAssets/FineDance/poses.json";
            if(!File.Exists(path))throw new InvalidDataException("先运行 Scripts/prepare-personal-finedance.py；个人动作不得随公开发行包分发。");
            var input=JsonUtility.FromJson<Input>(File.ReadAllText(path));
            if(input.schemaVersion!=1||!input.personalUseOnly||input.fps!=30||input.frames==null||input.frames.Length<240||input.sourceSha256==null||input.sourceSha256.Length!=64)
                throw new InvalidDataException("个人动作数据版本、来源或使用限制无效。");
            if(input.sourceLabel==null||!string.Equals(input.sourceLabel.style2,"jazz",StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("源标签不是 Jazz；不能自动改名为 New Jazz。");
            if(input.neutralJoints==null||input.neutralJoints.Length!=156)throw new InvalidDataException("缺少原作者中性骨架。请重新运行获取及转换工具；不能再用仅 T 姿态的旧重定向。");
            const string sourceLicense="PersonalAssets/FineDance/LICENSE.txt";
            if(!File.Exists(sourceLicense))throw new InvalidDataException("请将原作者 LICENSE 保存到 PersonalAssets/FineDance/LICENSE.txt，再准备个人动作。");
            var catalog=new MotionCatalog{personalUseOnly=true,sourceNotice="FineDance 真人 Street/Jazz 动捕 · 仅本机非商业个人学习；动作及包含动作的应用不得公开分发。"};
            // A failed rebake must not leave a stale approved catalog pointing to
            // newly changed pose assets in the editor.
            File.WriteAllText("Assets/Resources/PersonalMotionCatalog.json",JsonUtility.ToJson(catalog,true));
            float period=60/input.sourceBpm;
            for(int index=0;index<8;index++)
            {
                string id="personal-finedance-"+input.sourceId+"-"+index;
                float start=input.startSeconds+index*8*period,duration=8*period;
                Bake(input,start,duration,id,"female");Bake(input,start,duration,id,"male");
                catalog.motions.Add(new MotionDefinition{id=id,name="待复查 · 街爵士八拍 "+(index+1),resource="Motions/"+id,
                    style="new-jazz-review",beats=8,difficulty=1,energy=.35f+index*.05f,personalUseOnly=true,
                    approved=false,localRigMotion=true,sourceNext=index<7?"personal-finedance-"+input.sourceId+"-"+(index+1):"",groundToFloor=true,supportFps=30,supportHeights=new List<float>{.008f,.008f},transitionBeats=.6f,
                    source=input.source+" / "+input.sourceId+" / original label: "+input.sourceLabel.style1+" / "+input.sourceLabel.style2+" / "+start.ToString("R",System.Globalization.CultureInfo.InvariantCulture)+"s",
                    license=input.license});
            }
            AssetDatabase.SaveAssets();AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            File.WriteAllText("Assets/Resources/PersonalMotionCatalog.json",JsonUtility.ToJson(catalog,true));
            foreach(var motion in catalog.motions)AnatomyAssetGate.Validate(motion);
            var hashes=catalog.motions.Select(m=>new Entry{id=m.id,sha256=MocapReviewGate.Digest(m)}).ToArray();
            File.WriteAllText("PersonalAssets/FineDance/clip-hashes.json",JsonUtility.ToJson(new Review{sourceSha256=input.sourceSha256,motions=hashes},true));
            string approval="PersonalAssets/FineDance/reviewed.json";
            if(!File.Exists(approval))approval="Assets/SongChoreography/Editor/PersonalMotionReview/finedance-"+input.sourceId+".json";
            if(File.Exists(approval)&&JsonUtility.FromJson<Review>(File.ReadAllText(approval)).schemaVersion<2)
                Debug.LogWarning("旧批准未包含关节轴与范围复查，已失效；保持个人动作未批准。");
            else if(File.Exists(approval))
            {
                var review=JsonUtility.FromJson<Review>(File.ReadAllText(approval));
                if(review.schemaVersion!=2||review.sourceSha256!=input.sourceSha256||review.motions==null||review.motions.Length!=8||review.motions.Select(m=>m.id).Distinct().Count()!=8)throw new InvalidDataException("个人动作批准记录与源数据不符。");
                foreach(var item in review.motions)
                {
                    var motion=catalog.motions.Single(m=>m.id==item.id);
                    if(hashes.Single(h=>h.id==item.id).sha256!=item.sha256||string.IsNullOrWhiteSpace(item.reviewer)||string.IsNullOrWhiteSpace(item.reviewNotes)||item.safeNext==null||item.safeNext.Length==0||item.safeNext.Any(id=>!catalog.motions.Any(m=>m.id==id)))
                        throw new InvalidDataException("个人动作已变更或复查信息不完整："+item.id);
                    motion.name=item.name;motion.style=DanceStyles.NewJazz;motion.approved=true;motion.reviewer=item.reviewer;motion.reviewNotes=item.reviewNotes;motion.safeNext=item.safeNext.ToList();
                }
            }
            File.WriteAllText("Assets/Resources/PersonalMotionCatalog.json",JsonUtility.ToJson(catalog,true));
            Directory.CreateDirectory("Assets/StreamingAssets/PersonalMotionCredits");
            File.Copy(sourceLicense,"Assets/StreamingAssets/PersonalMotionCredits/LICENSE.txt",true);
            File.WriteAllText("Assets/StreamingAssets/PersonalMotionCredits/NOTICE.txt",catalog.sourceNotice+"\n"+input.source+"\n"+input.license+"\nOriginal source tag: "+input.sourceLabel.style1+" / "+input.sourceLabel.style2);
            AssetDatabase.SaveAssets();AssetDatabase.Refresh();Debug.Log("PERSONAL MOTIONS PREPARED; approved="+catalog.motions.Count(m=>m.approved));
        }

        static void Bake(Input input,float start,float duration,string id,string sex)
        {
            var source=AssetDatabase.LoadAssetAtPath<GameObject>(CharacterAssetBuilder.Art+"Superhero_"+(sex=="male"?"Male":"Female")+"_FullBody.fbx");
            var rig=UnityEngine.Object.Instantiate(source);var animator=rig.GetComponent<Animator>();animator.enabled=false;
            var bones=Bones().Select(b=>animator.GetBoneTransform(b)).ToArray();
            var neutral=Enumerable.Range(0,52).Select(i=>new Vector3(input.neutralJoints[i*3],input.neutralJoints[i*3+1],-input.neutralJoints[i*3+2])).ToArray();
            var rest=SourceSkeletonCalibration.Solve(animator,bones,neutral);
            var all=rig.GetComponentsInChildren<Transform>();var restPositions=all.Select(t=>t.localPosition).ToArray();var restRotations=all.Select(t=>t.localRotation).ToArray();
            Quaternion heading=Quaternion.Euler(0,180,0);
            var hips=bones[0];Vector3 initialHips=hips.position;Vector3 origin=Root(input.frames[Mathf.RoundToInt(input.startSeconds*input.fps)]);
            int count=Mathf.CeilToInt(duration*30);
            var localSamples=Enumerable.Range(0,52*4+3).Select(_=>new float[count+1]).ToArray();
            Quaternion[] previousWorld=null;float rawStep=0;int rawJoint=0;
            {
                for(int frame=0;frame<=count;frame++)
                {
                    for(int i=0;i<all.Length;i++){all[i].localPosition=restPositions[i];all[i].localRotation=restRotations[i];}
                    float time=(start+Mathf.Min(duration,frame/30f))*input.fps;int a=Mathf.FloorToInt(time),b=Math.Min(a+1,input.frames.Length-1);float mix=time-a;
                    if(a<0||a>=input.frames.Length||b>=input.frames.Length)throw new InvalidDataException("个人动作时间越界。");
                    var world=new Quaternion[52];
                    for(int joint=0;joint<52;joint++)
                    {
                        Func<int,Quaternion> filtered=f=>Quaternion.Slerp(Quaternion.Slerp(Local(input.frames[Math.Max(0,f-1)],joint),Local(input.frames[Math.Min(input.frames.Length-1,f+1)],joint),.5f),Local(input.frames[f],joint),.5f);
                        Quaternion q=Quaternion.Slerp(filtered(a),filtered(b),mix);
                        world[joint]=Parents[joint]<0?q:world[Parents[joint]]*q;
                        if(bones[joint])bones[joint].rotation=heading*world[joint]*rest[joint];
                    }
                    Vector3 drift=input.rootDrift!=null&&input.rootDrift.Length==3?new Vector3(input.rootDrift[0],0,-input.rootDrift[2]):Vector3.zero;
                    float progress=(start+Mathf.Min(duration,frame/30f)-input.startSeconds)/(64*60/input.sourceBpm);
                    hips.position=initialHips+heading*(Vector3.Lerp(Root(input.frames[a]),Root(input.frames[b]),mix)-origin-drift*progress);
                    if(previousWorld!=null)for(int j=0;j<22;j++)if(bones[j]&&Quaternion.Angle(previousWorld[j],bones[j].rotation)>rawStep){rawStep=Quaternion.Angle(previousWorld[j],bones[j].rotation);rawJoint=j;}
                    previousWorld=bones.Select(t=>t?t.rotation:Quaternion.identity).ToArray();
                    for(int j=0;j<52;j++)
                    {
                        Quaternion q=bones[j]?bones[j].localRotation:Quaternion.identity;
                        if(frame>0&&q.x*localSamples[j*4][frame-1]+q.y*localSamples[j*4+1][frame-1]+q.z*localSamples[j*4+2][frame-1]+q.w*localSamples[j*4+3][frame-1]<0)q=new Quaternion(-q.x,-q.y,-q.z,-q.w);
                        localSamples[j*4][frame]=q.x;localSamples[j*4+1][frame]=q.y;localSamples[j*4+2][frame]=q.z;localSamples[j*4+3][frame]=q.w;
                    }
                    localSamples[208][frame]=hips.localPosition.x;localSamples[209][frame]=hips.localPosition.y;localSamples[210][frame]=hips.localPosition.z;
                }
            }
            Debug.Log("PERSONAL CAPTURE STEP "+id+": "+rawStep+" degrees, joint="+rawJoint);
            AnatomicalPoseCleanup.Apply(animator,bones,localSamples,count);
            // These are source-contiguous phrases, not artificially looped gestures.
            var clip=new AnimationClip{name=id,frameRate=30};
            Func<float[],AnimationCurve> curve=values=>new AnimationCurve(values.Select((v,i)=>
            {float slope=i==0?(values[1]-v)*30:i==count?(v-values[i-1])*30:(values[i+1]-values[i-1])*15;return new Keyframe(i/30f,v,slope,slope);}).ToArray());
            // The complex wrist/ankle poses cross Mecanim muscle decomposition
            // poles. Exact local quaternion tracks preserve the solved motion
            // on the two supplied characters, which share this bone hierarchy.
            for(int j=0;j<52;j++)if(bones[j])for(int axis=0;axis<4;axis++)clip.SetCurve(AnimationUtility.CalculateTransformPath(bones[j],rig.transform),typeof(Transform),"localRotation."+"xyzw"[axis],curve(localSamples[j*4+axis]));
            for(int axis=0;axis<3;axis++)clip.SetCurve(AnimationUtility.CalculateTransformPath(hips,rig.transform),typeof(Transform),"localPosition."+"xyz"[axis],curve(localSamples[208+axis]));
            clip.EnsureQuaternionContinuity();var settings=AnimationUtility.GetAnimationClipSettings(clip);settings.loopTime=false;settings.loopBlend=false;
            settings.keepOriginalOrientation=settings.keepOriginalPositionY=settings.keepOriginalPositionXZ=true;
            settings.loopBlendOrientation=settings.loopBlendPositionY=settings.loopBlendPositionXZ=true;AnimationUtility.SetAnimationClipSettings(clip,settings);
            string path="Assets/Resources/Motions/"+id+(sex=="male"?"-male":"")+".anim";var existing=AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
            if(existing){EditorUtility.CopySerialized(clip,existing);UnityEngine.Object.DestroyImmediate(clip);}else AssetDatabase.CreateAsset(clip,path);
            string rigPath="Assets/Resources/Motions/"+id+"-rig"+(sex=="male"?"-male":"")+".asset";
            var rigData=AssetDatabase.LoadAssetAtPath<LocalRigMotionAsset>(rigPath);bool fresh=!rigData;
            if(fresh)rigData=ScriptableObject.CreateInstance<LocalRigMotionAsset>();
            rigData.schemaVersion=2;rigData.characterId=sex;rigData.paths=bones.Select(t=>AnimationUtility.CalculateTransformPath(t,rig.transform)).ToArray();
            rigData.frameCount=count+1;rigData.duration=duration;rigData.rotations=new float[(count+1)*208];rigData.hips=new Vector3[count+1];
            for(int f=0;f<=count;f++)
            {
                for(int channelIndex=0;channelIndex<208;channelIndex++)rigData.rotations[f*208+channelIndex]=localSamples[channelIndex][f];
                rigData.hips[f]=new Vector3(localSamples[208][f],localSamples[209][f],localSamples[210][f]);
            }
            if(fresh)AssetDatabase.CreateAsset(rigData,rigPath);else EditorUtility.SetDirty(rigData);
            UnityEngine.Object.DestroyImmediate(rig);
        }
    }
}
