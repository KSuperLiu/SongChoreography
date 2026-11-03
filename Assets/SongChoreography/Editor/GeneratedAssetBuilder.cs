using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace SongChoreography.Editor
{
    [Serializable] public class MotionReview
    {
        public string characterId="",characterSignature="",motionId="",motionSignature="",notes="";
    }
    [Serializable] public class TransitionReview
    {
        public string characterId="",characterSignature="",from="",fromSignature="",to="",toSignature="",notes="";
        public float blendBeats=.5f;
    }
    [Serializable] public class ClipSettings
    {
        public string id="",sourceHash="",clipName="";
        public double[] beatTimes=new double[0];
    }
    [Serializable] public class AssetReviews
    {
        public int schemaVersion=2;
        public List<MotionReview> motions=new List<MotionReview>();
        public List<TransitionReview> transitions=new List<TransitionReview>();
        public List<ClipSettings> clips=new List<ClipSettings>();
    }
    public static class GeneratedAssetBuilder
    {
        public const string LibraryPath="Assets/Resources/GeneratedLibrary.asset";
        public static string Root=>Path.GetFullPath(".");
        public static string ReviewsPath=>Path.Combine(Root,"Generation/reviews.json");
        public static AssetReviews Reviews()=>File.Exists(ReviewsPath)?JsonUtility.FromJson<AssetReviews>(File.ReadAllText(ReviewsPath)):new AssetReviews();
        public static void SaveReviews(AssetReviews reviews)
        {Directory.CreateDirectory(Path.GetDirectoryName(ReviewsPath));File.WriteAllText(ReviewsPath,JsonUtility.ToJson(reviews,true));Import();}
        public static string Hash(string value)
        {using(var sha=SHA256.Create())return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(value))).Replace("-","").ToLowerInvariant();}
        static string FileHash(string path)
        {using(var sha=SHA256.Create())using(var stream=File.OpenRead(path))return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-","").ToLowerInvariant();}
        static void CheckSource(string path,string hash)
        {
            if(string.IsNullOrWhiteSpace(path)||!path.Replace('\\','/').StartsWith("Assets/Generated/",StringComparison.Ordinal)||Path.GetExtension(path).ToLowerInvariant()!=".fbx")throw new InvalidDataException("源文件必须是 Assets/Generated 内的 FBX。");
            string full=Choreographer.ResolveLocal(Path.Combine(Root,"Assets/Generated"),path.Replace('\\','/').Substring("Assets/Generated/".Length));
            if(!File.Exists(full)||FileHash(full)!=hash)throw new InvalidDataException("文件缺失或 SHA-256 不匹配："+path);
        }
        static ModelImporter Configure(string path,bool animation)
        {
            var importer=AssetImporter.GetAtPath(path) as ModelImporter;
            if(!importer)throw new InvalidDataException("Unity 无法导入 FBX："+path);
            if(importer.animationType!=ModelImporterAnimationType.Human||importer.avatarSetup!=ModelImporterAvatarSetup.CreateFromThisModel||importer.importAnimation!=animation||!animation&&!importer.isReadable)
            {importer.animationType=ModelImporterAnimationType.Human;importer.avatarSetup=ModelImporterAvatarSetup.CreateFromThisModel;importer.importAnimation=animation;if(!animation)importer.isReadable=true;importer.SaveAndReimport();}
            return importer;
        }
        static GameObject PrepareCharacter(CharacterDefinition definition)
        {
            CheckSource(definition.sourceFile,definition.sha256);var importer=Configure(definition.sourceFile,false);
            string folder=Path.GetDirectoryName(definition.sourceFile).Replace('\\','/');
            string textures=folder+"/Textures";
            if(!Directory.Exists(textures)){Directory.CreateDirectory(textures);importer.ExtractTextures(textures);AssetDatabase.Refresh();importer.SaveAndReimport();}
            foreach(string file in Directory.GetFiles(textures,"*",SearchOption.AllDirectories).Where(p=>Path.GetFileName(p).StartsWith("Normal",StringComparison.OrdinalIgnoreCase)))
            {
                var normal=AssetImporter.GetAtPath(file.Replace('\\','/')) as TextureImporter;
                if(normal&&normal.textureType!=TextureImporterType.NormalMap){normal.textureType=TextureImporterType.NormalMap;normal.SaveAndReimport();}
            }
            var source=AssetDatabase.LoadAssetAtPath<GameObject>(definition.sourceFile);
            if(!source)throw new InvalidDataException("人物模型无法加载。");
            var instance=UnityEngine.Object.Instantiate(source);var root=new GameObject(definition.id);
            try
            {
                root.AddComponent<GeneratedCharacterIdentity>().sourceHash=definition.sha256;
                instance.transform.SetParent(root.transform,false);
                var animator=instance.GetComponentInChildren<Animator>();
                if(!animator||!animator.avatar||!animator.avatar.isValid||!animator.avatar.isHuman)throw new InvalidDataException("人物未形成有效 Humanoid；请在 FBX Rig / Configure 中映射骨骼。");
                // Tripo may export X-forward even with Unity bone naming.
                // Normalize the actual anatomical facing before measuring bounds.
                Vector3 forward=Vector3.zero;
                foreach(var pair in new[]{new[]{HumanBodyBones.LeftFoot,HumanBodyBones.LeftToes},new[]{HumanBodyBones.RightFoot,HumanBodyBones.RightToes}})
                {var foot=animator.GetBoneTransform(pair[0]);var toe=animator.GetBoneTransform(pair[1]);if(foot&&toe)forward+=toe.position-foot.position;}
                forward=Vector3.ProjectOnPlane(forward,Vector3.up);
                if(forward.sqrMagnitude>.000001f)instance.transform.rotation=Quaternion.FromToRotation(forward.normalized,Vector3.forward)*instance.transform.rotation;
                else
                {
                    using(var poseHandler=new HumanPoseHandler(animator.avatar,animator.transform))
                    {var pose=new HumanPose();poseHandler.GetHumanPose(ref pose);instance.transform.rotation=Quaternion.Euler(0,-pose.bodyRotation.eulerAngles.y,0)*instance.transform.rotation;}
                }
                var skins=root.GetComponentsInChildren<SkinnedMeshRenderer>();
                if(skins.Length==0)throw new InvalidDataException("人物没有蒙皮网格。");
                // Imported renderer bounds can be stale after normalization.
                // Bake with scale compensation, then transform into world space.
                var mesh=new Mesh();Bounds bounds;
                try{bounds=SkinBounds(skins,mesh);}finally{UnityEngine.Object.DestroyImmediate(mesh);}
                if(bounds.size.y<.01f||!Choreographer.Finite(bounds.size.y))throw new InvalidDataException("人物尺寸无效。");
                float scale=1.7f/bounds.size.y;instance.transform.localScale*=scale;
                mesh=new Mesh();
                try{bounds=SkinBounds(skins,mesh);}finally{UnityEngine.Object.DestroyImmediate(mesh);}
                instance.transform.position+=new Vector3(-bounds.center.x,-bounds.min.y,-bounds.center.z);
                animator.applyRootMotion=false;animator.cullingMode=AnimatorCullingMode.AlwaysAnimate;
                foreach(var skin in skins)skin.updateWhenOffscreen=true;
                var armPoses=Resources.Load<ArmPoseLibrary>("ArmPoseLibrary");
                definition.height=1.7f;definition.signature=Hash(definition.sha256+AssetDatabase.GetAssetDependencyHash(definition.sourceFile)+"normalized-1.7-facing-v2-playback-"+MotionPlayer.PlaybackRevision+(armPoses?armPoses.signature:""));
                string prefab=folder+"/"+definition.id+".prefab";
                return PrefabUtility.SaveAsPrefabAsset(root,prefab);
            }
            finally{UnityEngine.Object.DestroyImmediate(root);}
        }
        static Bounds SkinBounds(SkinnedMeshRenderer[] skins,Mesh mesh)
        {
            var bounds=new Bounds();bool first=true;
            foreach(var skin in skins)
            {
                skin.BakeMesh(mesh,true);
                foreach(var vertex in mesh.vertices)
                {var world=skin.transform.TransformPoint(vertex);if(first){bounds=new Bounds(world,Vector3.zero);first=false;}else bounds.Encapsulate(world);}
            }
            return bounds;
        }
        static AnimationClip PrepareMotion(MotionDefinition definition,AssetReviews reviews)
        {
            CheckSource(definition.sourceFile,definition.sha256);Configure(definition.sourceFile,true);
            var clips=AssetDatabase.LoadAllAssetsAtPath(definition.sourceFile).OfType<AnimationClip>().Where(a=>!a.name.StartsWith("__preview__",StringComparison.Ordinal)).ToArray();
            var selected=string.IsNullOrWhiteSpace(definition.clipName)?clips:clips.Where(a=>a.name==definition.clipName).ToArray();
            if(selected.Length!=1||!selected[0].isHumanMotion||selected[0].length<=0)throw new InvalidDataException("需选中一个有效 Humanoid clip；请检查 FBX Rig / Animation 和清单 clipName。可用片段："+string.Join(", ",clips.Select(a=>a.name)));
            var clip=selected[0];definition.clipName=clip.name;definition.clipStart=0;definition.clipEnd=clip.length;
            var settings=reviews.clips.FirstOrDefault(a=>a.id==definition.id&&a.sourceHash==definition.sha256&&a.clipName==definition.clipName);
            definition.beatTimes=settings!=null?settings.beatTimes.ToList():Enumerable.Range(0,definition.beats+1).Select(i=>clip.length*i/(double)definition.beats).ToList();
            if(settings!=null&&definition.beatTimes.Count==definition.beats+1){definition.clipStart=definition.beatTimes.First();definition.clipEnd=definition.beatTimes.Last();}
            if(definition.clipEnd>clip.length+.001)throw new InvalidDataException("拍点超过动画时长。");
            definition.reviewedCharacters.Clear();
            definition.signature="";definition.signature=Hash(definition.sha256+AssetDatabase.GetAssetDependencyHash(definition.sourceFile)+JsonUtility.ToJson(definition));
            return clip;
        }
        [MenuItem("Tools/CHOREO AI/Import generated assets")]
        public static void Import()
        {
            Directory.CreateDirectory("Assets/Resources");AssetDatabase.Refresh();
            var library=AssetDatabase.LoadAssetAtPath<GeneratedLibrary>(LibraryPath);
            if(!library){library=ScriptableObject.CreateInstance<GeneratedLibrary>();AssetDatabase.CreateAsset(library,LibraryPath);}
            // Failed reimports cannot leave stale approvals on changed FBXs.
            library.catalog=new AssetCatalog();library.characters=new CharacterAsset[0];library.motions=new MotionAsset[0];library.importIssues=new[]{"素材导入尚未完成。"};EditorUtility.SetDirty(library);AssetDatabase.SaveAssets();
            var catalog=new AssetCatalog();var characters=new List<CharacterAsset>();var motions=new List<MotionAsset>();var issues=new List<string>();
            string path="Generation/library.json";
            var input=File.Exists(path)?JsonUtility.FromJson<AssetCatalog>(File.ReadAllText(path)):new AssetCatalog();
            if(input==null||input.schemaVersion!=2||input.characters==null||input.motions==null)throw new InvalidDataException("需要 schemaVersion 2 的生成素材清单。");
            var ids=input.characters.Select(a=>a.id).Concat(input.motions.Select(a=>a.id)).ToArray();
            if(ids.Any(id=>id==null||!System.Text.RegularExpressions.Regex.IsMatch(id,"^[a-z][a-z0-9-]{1,63}$"))||ids.Distinct().Count()!=ids.Length)throw new InvalidDataException("素材 ID 必须唯一，只能使用小写字母、数字和连字符。");
            var reviews=Reviews();
            if(reviews==null||reviews.schemaVersion!=2)throw new InvalidDataException("复查清单版本错误。");
            var armPoses=Resources.Load<ArmPoseLibrary>("ArmPoseLibrary");
            if(armPoses)
            {
                var valid=armPoses.tracks.Where(t=>input.motions.Any(m=>m.id==t.id&&m.sha256==t.sourceHash)).ToArray();
                if(!input.characters.Any(c=>c.sha256==armPoses.targetSourceHash))valid=new ArmPoseTrack[0];
                if(valid.Length!=armPoses.tracks.Length){armPoses.tracks=valid;armPoses.signature=Hash(JsonUtility.ToJson(armPoses));EditorUtility.SetDirty(armPoses);AssetDatabase.SaveAssets();}
            }
            foreach(var definition in input.characters)
            {
                try{var prefab=PrepareCharacter(definition);catalog.characters.Add(definition);characters.Add(new CharacterAsset{id=definition.id,prefab=prefab});}
                catch(Exception e){issues.Add(definition.id+"："+e.Message);}
            }
            foreach(var definition in input.motions)
            {
                try
                {
                    var clip=PrepareMotion(definition,reviews);
                    foreach(var character in catalog.characters)
                        if(reviews.motions.Any(r=>r.characterId==character.id&&r.characterSignature==character.signature&&r.motionId==definition.id&&r.motionSignature==definition.signature&&!string.IsNullOrWhiteSpace(r.notes)))definition.reviewedCharacters.Add(character.id);
                    Choreographer.ValidateCatalog(new AssetCatalog{characters=catalog.characters,motions=new List<MotionDefinition>{definition}});
                    catalog.motions.Add(definition);motions.Add(new MotionAsset{id=definition.id,clip=clip});
                }
                catch(Exception e){issues.Add(definition.id+"："+e.Message);}
            }
            foreach(var review in reviews.transitions)
            {
                var character=catalog.characters.FirstOrDefault(a=>a.id==review.characterId&&a.signature==review.characterSignature);
                var from=catalog.motions.FirstOrDefault(a=>a.id==review.from&&a.signature==review.fromSignature&&a.reviewedCharacters.Contains(review.characterId));
                var to=catalog.motions.FirstOrDefault(a=>a.id==review.to&&a.signature==review.toSignature&&a.reviewedCharacters.Contains(review.characterId));
                if(character!=null&&from!=null&&to!=null&&!string.IsNullOrWhiteSpace(review.notes))catalog.transitions.Add(new TransitionDefinition{from=from.id,to=to.id,characterId=character.id,blendBeats=review.blendBeats});
            }
            Choreographer.ValidateCatalog(catalog);
            library.catalog=catalog;library.characters=characters.ToArray();library.motions=motions.ToArray();library.importIssues=issues.ToArray();
            EditorUtility.SetDirty(library);AssetDatabase.SaveAssets();
            Debug.Log("GENERATED IMPORT: characters="+characters.Count+", motions="+motions.Count+", issues="+issues.Count);
        }
    }
}
