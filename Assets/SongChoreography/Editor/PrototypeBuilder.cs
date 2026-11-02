using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using SongChoreography;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class PrototypeBuilder
{
    const string Scene="Assets/Scenes/Practice.unity";
    [MenuItem("Tools/Song Choreography/Prepare Prototype")]
    public static void Prepare()
    {
        Directory.CreateDirectory("Assets/Resources/Motions");Directory.CreateDirectory("Assets/Scenes");
        var names=new[]{"节奏摆动","肩部测试","重心测试","手臂展开","屈膝测试","上身转动","交替抬臂","组合测试"};
        var catalog=new MotionCatalog();
        SongChoreography.Editor.CharacterAssetBuilder.Prepare();
        for(int index=0;index<8;index++)
        {
            string id="diagnostic-"+index,path="Assets/Resources/Motions/"+id+".anim";
            GenerateDiagnostic(index,id,path);
            catalog.motions.Add(new MotionDefinition{id=id,name=names[index],resource="Motions/"+id,beats=8,style="diagnostic",energy=.2f+.08f*index,
                source="Original procedural diagnostic curves / PrototypeBuilder.cs",license="CC0-1.0 (original diagnostic assets)",approved=false,
                reviewNotes="Technical playback fixture only. Not New Jazz; transitions have NOT passed choreographic review.",safeNext=Enumerable.Range(0,8).Select(i=>"diagnostic-"+i).ToList()});
        }
        // Leave an existing catalog intact so asset reviewers can add real motions without data loss.
        if(!File.Exists("Assets/Resources/MotionCatalog.json"))File.WriteAllText("Assets/Resources/MotionCatalog.json",JsonUtility.ToJson(catalog,true));
        SongChoreography.Editor.MocapAssetBuilder.PreparePreview();
        foreach(var pair in new[]{new{shader="Standard",name="RuntimeStandard"},new{shader="Sprites/Default",name="RuntimeLines"}})
            if(!File.Exists("Assets/Resources/"+pair.name+".mat"))AssetDatabase.CreateAsset(new Material(Shader.Find(pair.shader)),"Assets/Resources/"+pair.name+".mat");
        string analyzer="Assets/StreamingAssets/Analyzer";Directory.CreateDirectory(analyzer);
        File.Copy("Analyzer/analyzer.py",analyzer+"/analyzer.py",true);File.Copy("Analyzer/requirements.txt",analyzer+"/requirements.txt",true);
        if(Directory.Exists("Samples/Demo"))
        {
            Directory.CreateDirectory("Assets/StreamingAssets/Demo");
            foreach(var f in Directory.GetFiles("Samples/Demo"))File.Copy(f,"Assets/StreamingAssets/Demo/"+Path.GetFileName(f),true);
        }
        if(!File.Exists(Scene))
        {
            var scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
            EditorSceneManager.SaveScene(scene,Scene);
        }
        EditorBuildSettings.scenes=new[]{new EditorBuildSettingsScene(Scene,true)};
        PlayerSettings.companyName="SongChoreography";PlayerSettings.productName="Choreo Practice Studio";
        PlayerSettings.bundleVersion="0.4.1";
        PlayerSettings.defaultScreenWidth=1600;PlayerSettings.defaultScreenHeight=900;
        PlayerSettings.fullScreenMode=FullScreenMode.Windowed;PlayerSettings.resizableWindow=true;PlayerSettings.runInBackground=true;
        PlayerSettings.SetScriptingBackend(BuildTargetGroup.Standalone,ScriptingImplementation.Mono2x);
        PlayerSettings.colorSpace=ColorSpace.Linear;QualitySettings.vSyncCount=1;
        AssetDatabase.SaveAssets();AssetDatabase.Refresh();
        Debug.Log("PROTOTYPE PREPARED");
    }
    static void GenerateDiagnostic(int index,string id,string path)
    {
        var source=AssetDatabase.LoadAssetAtPath<GameObject>(SongChoreography.Editor.CharacterAssetBuilder.Art+"Superhero_Female_FullBody.fbx");
        var rig=UnityEngine.Object.Instantiate(source);var animator=rig.GetComponent<Animator>();animator.enabled=false;
        var transforms=rig.GetComponentsInChildren<Transform>();var positions=transforms.Select(t=>t.localPosition).ToArray();var rotations=transforms.Select(t=>t.localRotation).ToArray();
        var spine=animator.GetBoneTransform(HumanBodyBones.Spine);var chest=animator.GetBoneTransform(HumanBodyBones.Chest);
        var curves=Enumerable.Range(0,HumanTrait.MuscleCount+7).Select(_=>new AnimationCurve()).ToArray();
        var pose=new HumanPose{muscles=new float[HumanTrait.MuscleCount]};
        // Derive muscle values and body coordinates from actual, anatomically simple
        // bone poses. Guessing normalized muscles can fold knees or flip the body.
        using(var handler=new HumanPoseHandler(animator.avatar,rig.transform))
        {
            for(int frame=0;frame<=240;frame++)
            {
                float t=frame/60f,phase=t*Mathf.PI;
                for(int b=0;b<transforms.Length;b++){transforms[b].localPosition=positions[b];transforms[b].localRotation=rotations[b];}
                spine.rotation=Quaternion.AngleAxis(3*Mathf.Sin(phase*.5f+index),Vector3.forward)*spine.rotation;
                chest.rotation=Quaternion.AngleAxis(5*Mathf.Sin(phase*.5f+index*.4f),Vector3.up)*chest.rotation;
                foreach(string side in new[]{"Left","Right"})
                {
                    bool left=side=="Left";
                    var upper=animator.GetBoneTransform(left?HumanBodyBones.LeftUpperArm:HumanBodyBones.RightUpperArm);
                    var lower=animator.GetBoneTransform(left?HumanBodyBones.LeftLowerArm:HumanBodyBones.RightLowerArm);
                    var hand=animator.GetBoneTransform(left?HumanBodyBones.LeftHand:HumanBodyBones.RightHand);
                    float sign=Mathf.Sign(upper.position.x),wave=Mathf.Sin(phase+sign*index*.4f);
                    Vector3 upperDirection=new Vector3(sign*(.25f+.12f*wave),-.94f,-.08f).normalized;
                    upper.rotation=Quaternion.FromToRotation(lower.position-upper.position,upperDirection)*upper.rotation;
                    Vector3 lowerDirection=new Vector3(sign*.14f,-.93f,-.25f-.08f*Mathf.Cos(phase+index)).normalized;
                    lower.rotation=Quaternion.FromToRotation(hand.position-lower.position,lowerDirection)*lower.rotation;
                }
                handler.GetHumanPose(ref pose);
                for(int m=0;m<HumanTrait.MuscleCount;m++)curves[m].AddKey(t,pose.muscles[m]);
                float[] root={pose.bodyPosition.x,pose.bodyPosition.y,pose.bodyPosition.z,pose.bodyRotation.x,pose.bodyRotation.y,pose.bodyRotation.z,pose.bodyRotation.w};
                for(int r=0;r<7;r++)curves[HumanTrait.MuscleCount+r].AddKey(t,root[r]);
            }
        }
        UnityEngine.Object.DestroyImmediate(rig);
        var clip=AssetDatabase.LoadAssetAtPath<AnimationClip>(path);bool fresh=!clip;
        if(fresh)clip=new AnimationClip{name=id};else clip.ClearCurves();
        clip.frameRate=60;clip.wrapMode=WrapMode.Loop;
        for(int m=0;m<HumanTrait.MuscleCount;m++)clip.SetCurve("",typeof(Animator),HumanTrait.MuscleName[m],curves[m]);
        var rootNames=new[]{"RootT.x","RootT.y","RootT.z","RootQ.x","RootQ.y","RootQ.z","RootQ.w"};
        for(int r=0;r<7;r++)clip.SetCurve("",typeof(Animator),rootNames[r],curves[HumanTrait.MuscleCount+r]);
        if(fresh)AssetDatabase.CreateAsset(clip,path);else EditorUtility.SetDirty(clip);
    }
    [MenuItem("Tools/Song Choreography/Build Windows Prototype")]
    public static void Build()
    {
        Prepare();
        ValidateAssets();
        ValidateMusicLibrary();
        Directory.CreateDirectory("Builds/Windows");
        var report=BuildPipeline.BuildPlayer(new BuildPlayerOptions{scenes=new[]{Scene},locationPathName="Builds/Windows/ChoreoStudio.exe",target=BuildTarget.StandaloneWindows64,options=BuildOptions.None});
        if(report.summary.result!=BuildResult.Succeeded)throw new Exception("Build failed: "+report.summary.result);
        Debug.Log("PROTOTYPE BUILD SUCCEEDED: "+report.summary.totalSize+" bytes");
    }
    [MenuItem("Tools/Song Choreography/Validate Motion Assets")]
    public static void ValidateAssets()
    {
        var catalog=JsonUtility.FromJson<MotionCatalog>(File.ReadAllText("Assets/Resources/MotionCatalog.json"));
        Choreographer.Eligible(catalog,"diagnostic");
        foreach(var m in catalog.motions)
        {
            var clip=Resources.Load<AnimationClip>(m.resource);
            if(!clip||!clip.isHumanMotion||clip.length<=0)throw new Exception("Missing or non-Humanoid motion: "+m.id);
            foreach(var next in m.safeNext)if(!catalog.motions.Any(x=>x.id==next))throw new Exception("Unknown safeNext: "+next);
        }
        foreach(string id in new[]{"female","male"})
        {
            var mannequin=DancerCharacters.Create(id);var avatar=mannequin.GetComponentInChildren<Animator>().avatar;
            bool valid=avatar&&avatar.isValid&&avatar.isHuman&&mannequin.GetComponentsInChildren<SkinnedMeshRenderer>().Length>=3;
            UnityEngine.Object.DestroyImmediate(mannequin);
            if(!valid)throw new Exception("Complete character is invalid: "+id);
        }
        Debug.Log("ASSET VALIDATION PASSED. Approved New Jazz: "+Choreographer.Eligible(catalog,"new-jazz").Count);
    }
    static void ValidateMusicLibrary()
    {
        const string folder="Assets/StreamingAssets/MusicLibrary";
        string path=Path.Combine(folder,"catalog.json");
        if(!File.Exists(path))throw new Exception("Builtin music not prepared. Run: python Scripts/prepare-music.py");
        var library=JsonUtility.FromJson<BuiltinMusicLibrary>(File.ReadAllText(path));
        if(library.schemaVersion!=1||library.tracks.Count<3)throw new Exception("At least three licensed music examples are required.");
        foreach(var track in library.tracks)
        {
            string root=Choreographer.ResolveLocal(folder,track.id);
            foreach(string file in new[]{"analysis.json","normal.wav","slow075.wav","slow050.wav","CREDITS.txt"})
                if(!File.Exists(Path.Combine(root,file)))throw new Exception("Missing builtin music file: "+track.id+"/"+file);
            Choreographer.ValidateAnalysis(JsonUtility.FromJson<MusicAnalysis>(File.ReadAllText(Path.Combine(root,"analysis.json"))));
            if(string.IsNullOrEmpty(track.artist)||string.IsNullOrEmpty(track.licenseUrl)||track.sha256.Length!=64)throw new Exception("Incomplete music attribution: "+track.id);
        }
        Debug.Log("BUILTIN MUSIC VALIDATION PASSED: "+library.tracks.Count);
    }
}
