using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;
using UnityEngine.Rendering;

namespace SongChoreography.Editor
{
    // Diagnostic output is local only: it includes restricted sampled poses.
    public static class MotionAnatomyAudit
    {
        [Serializable]class Range {public string name;public float min=100,max=-100;public int minFrame,maxFrame;}
        [Serializable]class ClipAudit {public string character,id;public int frames,worstFrame;public string worstMuscle;public float largestNormalizedMuscle;public Range[] ranges;}
        [Serializable]class Report {public string scope="Diagnostic only; normalized Avatar ranges are not an anatomical certification";public List<ClipAudit> clips=new List<ClipAudit>();}
        [Serializable]class BoneData {public string name,path;public float[] position,rotation;}
        [Serializable]class SkeletonData {public BoneData[] bones;}
        static float[] V(Vector3 p)=>new[]{p.x,p.y,p.z};
        static float[] Q(Quaternion q)=>new[]{q.x,q.y,q.z,q.w};
        public static void PrepareAndRun(){PersonalMotionBuilder.Prepare();Run();}
        public static void PrepareBothAndRun(){MocapAssetBuilder.PreparePreview();PrepareAndRun();}
        public static void PrepareGatesAndTest(){MocapAssetBuilder.PreparePreview();PersonalMotionBuilder.Prepare();SelfTest();}
        public static void Comparison()
        {
            const string temporary="Assets/SongChoreography/Editor/LocalAnatomyRegression.asset";
            if(File.Exists(temporary))throw new Exception("Preserved existing local fixture.");
            try
            {
                File.Copy("PersonalAssets/AnatomyRegression/old-050-rig.asset",temporary);AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
                string folder=Path.GetFullPath("TestResults/AnatomyComparison");Directory.CreateDirectory(folder);
                foreach(string version in new[]{"before","after"})
                {
                    var data=version=="before"?AssetDatabase.LoadAssetAtPath<LocalRigMotionAsset>(temporary):Resources.Load<LocalRigMotionAsset>("Motions/personal-finedance-001-0-rig");
                    var character=DancerCharacters.Create("female");var animator=character.GetComponentInChildren<Animator>();animator.enabled=false;int frame=19;
                    for(int j=0;j<52;j++){int k=frame*208+j*4;var bone=animator.transform.Find(data.paths[j]);bone.localRotation=new Quaternion(data.rotations[k],data.rotations[k+1],data.rotations[k+2],data.rotations[k+3]);if(j==0)bone.localPosition=data.hips[frame];}
                    Capture(character,Path.Combine(folder,version));UnityEngine.Object.DestroyImmediate(character);
                }
            }
            finally{AssetDatabase.DeleteAsset(temporary);}
        }
        [Serializable]class SelfTests {public bool reflectionPreservesPhysicalRotation,deliberateReverseWristRejected,wrongCharacterTrackRejected,old050TrackRejected;}
        public static void SelfTest()
        {
            var result=new SelfTests();
            for(int i=0;i<32;i++)
            {
                var q=Quaternion.Euler(i*11-90,i*17-140,i*7-60);var v=new Vector3(.3f+i*.01f,-.6f,.8f);
                if(Vector3.Distance(SourceSkeletonCalibration.ConvertPosition(q*v),SourceSkeletonCalibration.ConvertRotation(q)*SourceSkeletonCalibration.ConvertPosition(v))>.00001f)throw new Exception("Coordinate reflection changed the physical rotation.");
            }
            result.reflectionPreservesPhysicalRotation=true;
            var character=DancerCharacters.Create("female");var animator=character.GetComponentInChildren<Animator>();animator.enabled=false;
            using(var handler=new HumanPoseHandler(animator.avatar,animator.transform))
            {
                var pose=new HumanPose{muscles=new float[HumanTrait.MuscleCount]};handler.GetHumanPose(ref pose);Array.Clear(pose.muscles,0,pose.muscles.Length);handler.SetHumanPose(ref pose);
                var wrist=animator.GetBoneTransform(HumanBodyBones.LeftHand);var neutral=wrist.localRotation;
                pose.muscles[Array.IndexOf(HumanTrait.MuscleName,"Left Hand Down-Up")]=1;handler.SetHumanPose(ref pose);var limit=wrist.localRotation;
                pose.muscles[Array.IndexOf(HumanTrait.MuscleName,"Left Hand Down-Up")]=0;handler.SetHumanPose(ref pose);wrist.localRotation=Quaternion.SlerpUnclamped(neutral,limit,1.8f);
                using(var probe=new JointRangeProbe(animator))result.deliberateReverseWristRejected=!probe.Sample();
            }
            UnityEngine.Object.DestroyImmediate(character);
            if(!result.deliberateReverseWristRejected)throw new Exception("Range gate accepted a deliberately invalid wrist.");
            var female=Resources.Load<LocalRigMotionAsset>("Motions/personal-finedance-001-1-rig");
            result.wrongCharacterTrackRejected=RejectedOnRig(female,"male");
            if(!result.wrongCharacterTrackRejected)throw new Exception("Wrong-character regression was not detected.");
            const string legacy="PersonalAssets/AnatomyRegression/old-050-rig.asset",temporary="Assets/SongChoreography/Editor/LocalAnatomyRegression.asset";
            if(File.Exists(legacy))
            {
                if(File.Exists(temporary))throw new Exception("A local regression fixture already exists; it was preserved.");
                try{File.Copy(legacy,temporary);AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);result.old050TrackRejected=RejectedOnRig(AssetDatabase.LoadAssetAtPath<LocalRigMotionAsset>(temporary),"female");}
                finally{AssetDatabase.DeleteAsset(temporary);}
                if(!result.old050TrackRejected)throw new Exception("The gate accepted the actual faulty 0.5.0 track.");
            }
            Directory.CreateDirectory("TestResults");File.WriteAllText("TestResults/anatomy-self-tests.json",JsonUtility.ToJson(result,true));Debug.Log("ANATOMY SELF TESTS PASSED");
        }
        static bool RejectedOnRig(LocalRigMotionAsset data,string sex)
        {
            var character=DancerCharacters.Create(sex);var animator=character.GetComponentInChildren<Animator>();animator.enabled=false;
            try
            {
                var bones=data.paths.Select(p=>animator.transform.Find(p)).ToArray();
                using(var probe=new JointRangeProbe(animator))for(int frame=0;frame<data.frameCount;frame++)
                {
                    for(int j=0;j<52;j++){int k=frame*208+j*4;bones[j].localRotation=new Quaternion(data.rotations[k],data.rotations[k+1],data.rotations[k+2],data.rotations[k+3]);}bones[0].localPosition=data.hips[frame];
                    if(!probe.Sample())return true;
                }
                return false;
            }
            finally{UnityEngine.Object.DestroyImmediate(character);}
        }

        public static void Run()
        {
            string folder=Path.GetFullPath("TestResults/AnatomyAudit");Directory.CreateDirectory(folder);
            var shared=Resources.Load<TextAsset>("MotionCatalog");
            var catalog=JsonUtility.FromJson<MotionCatalog>(shared.text);
            var personal=Resources.Load<TextAsset>("PersonalMotionCatalog");
            if(personal)catalog=PersonalMotionPolicy.Merge(catalog,JsonUtility.FromJson<MotionCatalog>(personal.text));
            var report=new Report();
            foreach(string sex in new[]{"female","male"})
            {
                var character=DancerCharacters.Create(sex);var animator=character.GetComponentInChildren<Animator>();animator.enabled=false;
                var all=animator.GetComponentsInChildren<Transform>();var bindP=all.Select(t=>t.localPosition).ToArray();var bindQ=all.Select(t=>t.localRotation).ToArray();
                File.WriteAllText(Path.Combine(folder,sex+"-bind.json"),JsonUtility.ToJson(new SkeletonData{bones=all.Select(t=>new BoneData{name=t.name,path=AnimationUtility.CalculateTransformPath(t,animator.transform),position=V(t.position),rotation=Q(t.rotation)}).ToArray()},true));
                using(var handler=new HumanPoseHandler(animator.avatar,animator.transform))
                {
                    var pose=new HumanPose{muscles=new float[HumanTrait.MuscleCount]};
                    foreach(var definition in catalog.motions.Where(m=>m.personalUseOnly||m.style==DanceStyles.Charleston||m.style=="charleston-review"))
                    {
                        var clip=Resources.Load<AnimationClip>(definition.resource);
                        var data=definition.localRigMotion?Resources.Load<LocalRigMotionAsset>(definition.resource+"-rig"+(sex=="male"?"-male":"")):null;
                        PlayableGraph graph=default(PlayableGraph);AnimationClipPlayable player=default(AnimationClipPlayable);
                        if(!data)
                        {animator.enabled=true;graph=PlayableGraph.Create("Anatomy diagnostic");graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);player=AnimationClipPlayable.Create(graph,clip);player.SetApplyFootIK(definition.footIK);player.SetApplyPlayableIK(false);player.SetSpeed(0);var output=AnimationPlayableOutput.Create(graph,"pose",animator);output.SetSourcePlayable(player);graph.Play();}
                        else animator.enabled=false;
                        int frames=data?data.frameCount:Mathf.CeilToInt(clip.length*60)+1;
                        var audit=new ClipAudit{character=sex,id=definition.id,frames=frames,ranges=HumanTrait.MuscleName.Select(n=>new Range{name=n}).ToArray()};
                        Action<int> sample=frame=>
                        {
                            if(data)
                            {
                                for(int i=0;i<all.Length;i++){all[i].localPosition=bindP[i];all[i].localRotation=bindQ[i];}
                                for(int j=0;j<52;j++)
                                {int k=frame*208+j*4;var bone=animator.transform.Find(data.paths[j]);bone.localRotation=new Quaternion(data.rotations[k],data.rotations[k+1],data.rotations[k+2],data.rotations[k+3]);if(j==0)bone.localPosition=data.hips[frame];}
                            }
                            else{player.SetTime(Mathf.Min(clip.length-.0001f,frame/60f));graph.Evaluate(0);}
                        };
                        for(int frame=0;frame<frames;frame++)
                        {
                            sample(frame);handler.GetHumanPose(ref pose);
                            for(int m=0;m<pose.muscles.Length;m++)
                            {
                                var range=audit.ranges[m];float value=pose.muscles[m];
                                if(value<range.min){range.min=value;range.minFrame=frame;}if(value>range.max){range.max=value;range.maxFrame=frame;}
                                if(Mathf.Abs(value)>audit.largestNormalizedMuscle){audit.largestNormalizedMuscle=Mathf.Abs(value);audit.worstFrame=frame;audit.worstMuscle=range.name;}
                            }
                        }
                        report.clips.Add(audit);sample(audit.worstFrame);
                        string prefix=Path.Combine(folder,sex+"-"+definition.id+"-f"+audit.worstFrame);
                        Capture(character,prefix);
                        File.WriteAllText(prefix+"-bones.json",JsonUtility.ToJson(new SkeletonData{bones=all.Select(t=>new BoneData{name=t.name,path=AnimationUtility.CalculateTransformPath(t,animator.transform),position=V(t.position),rotation=Q(t.rotation)}).ToArray()},true));
                        Debug.Log("ANATOMY_DIAGNOSTIC "+sex+" / "+definition.id+" / "+audit.worstMuscle+" = "+audit.largestNormalizedMuscle);
                        if(graph.IsValid())graph.Destroy();animator.enabled=false;
                    }
                }
                UnityEngine.Object.DestroyImmediate(character);
            }
            File.WriteAllText(Path.Combine(folder,"ranges.json"),JsonUtility.ToJson(report,true));
            Debug.Log("ANATOMY AUDIT COMPLETE: "+folder);
        }
        static void Capture(GameObject character,string prefix)
        {
            var animator=character.GetComponentInChildren<Animator>();
            // Freeze CPU-baked meshes: the batch editor does not advance the
            // renderer's GPU skinning every time diagnostic transforms change.
            var snapshots=new List<GameObject>();var skins=character.GetComponentsInChildren<SkinnedMeshRenderer>();
            foreach(var skin in skins)
            {var mesh=new Mesh();skin.BakeMesh(mesh);var snapshot=new GameObject("Posed mesh");snapshot.transform.SetParent(skin.transform,false);snapshot.AddComponent<MeshFilter>().sharedMesh=mesh;snapshot.AddComponent<MeshRenderer>().sharedMaterials=skin.sharedMaterials;snapshots.Add(snapshot);skin.enabled=false;}
            var cameraObject=new GameObject("Anatomy diagnostic camera");var camera=cameraObject.AddComponent<Camera>();
            camera.backgroundColor=new Color(.025f,.035f,.05f);camera.clearFlags=CameraClearFlags.SolidColor;camera.orthographic=true;camera.nearClipPlane=.01f;camera.farClipPlane=20;
            var lightObject=new GameObject("Anatomy diagnostic light");var light=lightObject.AddComponent<Light>();light.type=LightType.Directional;light.intensity=1.4f;lightObject.transform.rotation=Quaternion.Euler(35,-25,0);
            var previousAmbient=RenderSettings.ambientLight;var previousMode=RenderSettings.ambientMode;RenderSettings.ambientMode=AmbientMode.Flat;RenderSettings.ambientLight=Color.white;
            var texture=new RenderTexture(900,900,24);camera.targetTexture=texture;
            foreach(string region in new[]{"body","left-hand","right-hand"})foreach(int view in new[]{0,90,180,270})
            {
                Vector3 target=region=="body"?animator.GetBoneTransform(HumanBodyBones.Hips).position+Vector3.up*.15f:animator.GetBoneTransform(region=="left-hand"?HumanBodyBones.LeftHand:HumanBodyBones.RightHand).position;
                camera.orthographicSize=region=="body"?1.05f:.19f;cameraObject.transform.position=target+Quaternion.Euler(0,view,0)*new Vector3(0,.10f,3);cameraObject.transform.LookAt(target);
                camera.Render();var previous=RenderTexture.active;RenderTexture.active=texture;var image=new Texture2D(900,900,TextureFormat.RGB24,false);image.ReadPixels(new Rect(0,0,900,900),0,0);image.Apply();RenderTexture.active=previous;
                File.WriteAllBytes(prefix+"-"+region+"-"+view+".png",image.EncodeToPNG());UnityEngine.Object.DestroyImmediate(image);
            }
            RenderSettings.ambientLight=previousAmbient;RenderSettings.ambientMode=previousMode;camera.targetTexture=null;UnityEngine.Object.DestroyImmediate(texture);UnityEngine.Object.DestroyImmediate(cameraObject);UnityEngine.Object.DestroyImmediate(lightObject);
            foreach(var snapshot in snapshots){UnityEngine.Object.DestroyImmediate(snapshot.GetComponent<MeshFilter>().sharedMesh);UnityEngine.Object.DestroyImmediate(snapshot);}foreach(var skin in skins)skin.enabled=true;
        }
    }
}
