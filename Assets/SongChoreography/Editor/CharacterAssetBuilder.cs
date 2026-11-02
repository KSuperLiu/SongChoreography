using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace SongChoreography.Editor
{
    public static class CharacterAssetBuilder
    {
        public const string Art="Assets/CharacterArt/Quaternius/";
        public static void Prepare()
        {
            Directory.CreateDirectory("Assets/Resources/Characters");AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            Build("female","Female","Hair_Buns");Build("male","Male","Hair_SimpleParted");
            Directory.CreateDirectory("Assets/StreamingAssets/CharacterCredits");
            File.Copy(Art+"License_Standard.txt","Assets/StreamingAssets/CharacterCredits/License_Standard.txt",true);
            File.Copy(Art+"SOURCE.json","Assets/StreamingAssets/CharacterCredits/SOURCE.json",true);
            AssetDatabase.SaveAssets();AssetDatabase.Refresh();
        }
        static Material Material(string name,string texture,bool outfit=false,bool cutout=false)
        {
            string path="Assets/Resources/Characters/"+name+".mat";
            var material=AssetDatabase.LoadAssetAtPath<Material>(path);
            if(!material){material=new Material(Shader.Find(outfit?"Choreo/Practice Outfit":"Standard"));AssetDatabase.CreateAsset(material,path);}
            material.mainTexture=AssetDatabase.LoadAssetAtPath<Texture2D>(Art+texture);
            if(!material.mainTexture)throw new Exception("Missing character texture: "+texture);
            if(!outfit){material.color=Color.white;material.SetFloat("_Glossiness",.22f);}
            if(cutout)
            {
                material.SetFloat("_Mode",1);material.SetFloat("_Cutoff",.35f);material.EnableKeyword("_ALPHATEST_ON");
                material.SetOverrideTag("RenderType","TransparentCutout");material.renderQueue=2450;
            }
            EditorUtility.SetDirty(material);return material;
        }
        static void Build(string id,string sex,string hairstyle)
        {
            string path=Art+"Superhero_"+sex+"_FullBody.fbx";
            if(!File.Exists(path))throw new Exception("Run python Scripts/prepare-character.py before building.");
            var importer=(ModelImporter)AssetImporter.GetAtPath(path);
            importer.animationType=ModelImporterAnimationType.Human;importer.avatarSetup=ModelImporterAvatarSetup.CreateFromThisModel;
            importer.importAnimation=false;importer.isReadable=true;importer.optimizeGameObjects=false;importer.SaveAndReimport();
            var model=AssetDatabase.LoadAssetAtPath<GameObject>(path);var instance=UnityEngine.Object.Instantiate(model);
            instance.name="Practice "+sex;
            var animator=instance.GetComponent<Animator>();
            if(!animator||!animator.avatar||!animator.avatar.isValid||!animator.avatar.isHuman)throw new Exception("Invalid Humanoid character: "+id);
            animator.applyRootMotion=false;animator.cullingMode=AnimatorCullingMode.AlwaysAnimate;
            var skin=Material(id+"-training",id=="female"?"T_Superhero_Female_Light_BaseColor.png":"T_Superhero_Male_Ligh.png",true);
            var eyes=Material("eyes","T_Eye_Brown.png");var hair1=Material(id+"-hair1","T_Hair_1_BaseColor.png",false,true);var hair2=Material(id+"-hair2","T_Hair_2_BaseColor.png",false,true);
            hair1.color=hair2.color=id=="female"?new Color(.19f,.12f,.09f):new Color(.40f,.24f,.13f);
            var exposed=new System.Collections.Generic.HashSet<Transform>();
            foreach(HumanBodyBones bone in Enum.GetValues(typeof(HumanBodyBones)))
            {
                string n=bone.ToString();
                if(bone==HumanBodyBones.Head||bone==HumanBodyBones.Neck||n.Contains("UpperArm")||n.Contains("LowerArm")||n.Contains("Hand")||n.Contains("Thumb")||n.Contains("Index")||n.Contains("Middle")||n.Contains("Ring")||n.Contains("Little")||n.Contains("Foot")||n.Contains("Toes"))
                {var t=animator.GetBoneTransform(bone);if(t)exposed.Add(t);}
            }
            var sourceRenderers=instance.GetComponentsInChildren<SkinnedMeshRenderer>();
            var body=sourceRenderers.First(r=>r.name.ToLowerInvariant().Contains("superhero"));
            var sourceMesh=body.sharedMesh;var mesh=UnityEngine.Object.Instantiate(sourceMesh);mesh.name=id+" training mesh";
            var world=mesh.vertices.Select(v=>body.transform.TransformPoint(v)).ToArray();float min=world.Min(v=>v.y),height=world.Max(v=>v.y)-min;
            var colors=new Color[world.Length];var weights=mesh.boneWeights;
            var bareBones=body.bones.Select(b=>exposed.Any(root=>b==root||b.IsChildOf(root))).ToArray();
            for(int i=0;i<colors.Length;i++)
            {
                var w=weights[i];float bare=0;
                int[] indices={w.boneIndex0,w.boneIndex1,w.boneIndex2,w.boneIndex3};float[] values={w.weight0,w.weight1,w.weight2,w.weight3};
                for(int b=0;b<4;b++)if(indices[b]<bareBones.Length&&bareBones[indices[b]])bare+=values[b];
                float y=(world[i].y-min)/height;
                Color color=y<.48f?new Color(.045f,.065f,.095f):new Color(.07f,.44f,.43f);
                color.a=1-Mathf.Clamp01(bare);colors[i]=color;
            }
            mesh.colors=colors;
            string meshPath="Assets/Resources/Characters/"+id+"-body.asset";
            var existing=AssetDatabase.LoadAssetAtPath<Mesh>(meshPath);
            if(existing){EditorUtility.CopySerialized(mesh,existing);UnityEngine.Object.DestroyImmediate(mesh);mesh=existing;}else AssetDatabase.CreateAsset(mesh,meshPath);
            body.sharedMesh=mesh;
            foreach(var renderer in sourceRenderers)
            {
                renderer.sharedMaterial=renderer==body?skin:renderer.name.ToLowerInvariant().Contains("eye")&&!renderer.name.ToLowerInvariant().Contains("brow")?eyes:hair1;
                renderer.updateWhenOffscreen=true;
            }
            var hairImporter=(ModelImporter)AssetImporter.GetAtPath(Art+hairstyle+".fbx");
            if(hairImporter&&!hairImporter.isReadable){hairImporter.isReadable=true;hairImporter.SaveAndReimport();}
            var hairModel=AssetDatabase.LoadAssetAtPath<GameObject>(Art+hairstyle+".fbx");
            if(!hairModel)throw new Exception("Missing hairstyle.");
            var hairObject=UnityEngine.Object.Instantiate(hairModel);hairObject.name=hairstyle;
            hairObject.transform.position+=Vector3.up*.035f;
            // Fit the short quiff to each skull: the male and female heads differ in height.
            var hairRenderers=hairObject.GetComponentsInChildren<Renderer>();
            var hairScale=hairObject.transform.localScale;hairScale.y*=id=="female"?.82f:.5f;hairObject.transform.localScale=hairScale;
            hairObject.transform.position+=Vector3.up*(world.Max(v=>v.y)+(id=="female"?.085f:.045f)-hairRenderers.Max(r=>r.bounds.max.y));
            hairObject.transform.SetParent(animator.GetBoneTransform(HumanBodyBones.Head),true);
            foreach(var renderer in hairObject.GetComponentsInChildren<Renderer>())renderer.sharedMaterial=renderer.sharedMaterial&&renderer.sharedMaterial.name.Contains("Hair_2")?hair2:hair1;
            var wrapper=new GameObject("Practice "+sex);
            instance.transform.SetParent(wrapper.transform,false);
            PrefabUtility.SaveAsPrefabAsset(wrapper,"Assets/Resources/Characters/"+id+".prefab");
            UnityEngine.Object.DestroyImmediate(wrapper);
            Debug.Log("COMPLETE CHARACTER PREPARED: "+id+", vertices="+sourceMesh.vertexCount+", Humanoid=true");
        }
        [MenuItem("Tools/Song Choreography/Inspect Character Sources")]
        public static void Inspect()
        {
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            foreach(string sex in new[]{"Female","Male"})
            {
                string path=Art+"Superhero_"+sex+"_FullBody.fbx";
                if(!File.Exists(path)){Debug.LogError("MISSING "+path);continue;}
                var importer=(ModelImporter)AssetImporter.GetAtPath(path);
                importer.animationType=ModelImporterAnimationType.Human;importer.avatarSetup=ModelImporterAvatarSetup.CreateFromThisModel;
                importer.importAnimation=false;importer.isReadable=true;importer.SaveAndReimport();
                var model=AssetDatabase.LoadAssetAtPath<GameObject>(path);
                Debug.Log("CHARACTER BONES "+sex+": "+string.Join(",",model.GetComponentsInChildren<Transform>().Select(t=>t.name)));
                Debug.Log("CHARACTER MATERIALS "+sex+": "+string.Join(",",model.GetComponentsInChildren<Renderer>().SelectMany(r=>r.sharedMaterials).Select(m=>m?m.name:"null")));
                var avatar=AssetDatabase.LoadAllAssetsAtPath(path).OfType<Avatar>().FirstOrDefault();
                Debug.Log("CHARACTER AVATAR "+sex+": "+(avatar&&avatar.isHuman&&avatar.isValid));
                foreach(var mesh in model.GetComponentsInChildren<SkinnedMeshRenderer>())Debug.Log("CHARACTER MESH "+mesh.name+" vertices="+mesh.sharedMesh.vertexCount+" bounds="+mesh.bounds);
            }
        }
    }
}
