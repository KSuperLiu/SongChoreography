using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace SongChoreography.Editor
{
    // Reproducible evidence for artistic review, using the actual playback graph.
    // Capturing a candidate never records an approval.
    public static class MotionReviewCapture
    {
        [Serializable] public class Plan
        {
            public string[] motions=new string[0];
            public string output=".generation/motion-review";
            public bool mapped;
            public float bpm=100;
        }
        [Serializable] public class Frame
        {
            public float time,minY,leftKneeAngle,rightKneeAngle;
            public Vector3 hips,head,leftFoot,rightFoot,leftToe,rightToe,leftHand,rightHand;
        }
        [Serializable] public class ClipReport
        {public string id,name,signature;public float length;public List<Frame> frames=new List<Frame>();}
        [Serializable] public class Report
        {public string characterId,characterSignature;public List<ClipReport> clips=new List<ClipReport>();}
        static Vector3 Bone(Animator a,HumanBodyBones bone)=>a.GetBoneTransform(bone).position;
        static float Knee(Animator a,bool left)
        {
            var hip=Bone(a,left?HumanBodyBones.LeftUpperLeg:HumanBodyBones.RightUpperLeg);
            var knee=Bone(a,left?HumanBodyBones.LeftLowerLeg:HumanBodyBones.RightLowerLeg);
            var foot=Bone(a,left?HumanBodyBones.LeftFoot:HumanBodyBones.RightFoot);
            return Vector3.Angle(hip-knee,foot-knee);
        }
        internal static Frame Measure(Animator a,GameObject actor,float time,Mesh mesh,List<Vector3> vertices)
        {
            float min=float.PositiveInfinity;
            foreach(var skin in actor.GetComponentsInChildren<SkinnedMeshRenderer>())
            {skin.BakeMesh(mesh,true);mesh.GetVertices(vertices);var matrix=skin.transform.localToWorldMatrix;foreach(var vertex in vertices)min=Mathf.Min(min,matrix.MultiplyPoint3x4(vertex).y);}
            return new Frame{time=time,minY=min,hips=Bone(a,HumanBodyBones.Hips),head=Bone(a,HumanBodyBones.Head),
                leftFoot=Bone(a,HumanBodyBones.LeftFoot),rightFoot=Bone(a,HumanBodyBones.RightFoot),
                leftToe=Bone(a,HumanBodyBones.LeftToes),rightToe=Bone(a,HumanBodyBones.RightToes),
                leftHand=Bone(a,HumanBodyBones.LeftHand),rightHand=Bone(a,HumanBodyBones.RightHand),
                leftKneeAngle=Knee(a,true),rightKneeAngle=Knee(a,false)};
        }
        internal static void Capture(PreviewRenderUtility preview,string path,int view,GameObject actor)
        {
            preview.camera.transform.position=Quaternion.Euler(0,view==0?0:90,0)*new Vector3(0,0,3.8f)+Vector3.up*.92f;
            preview.camera.transform.LookAt(Vector3.up*.88f);
            // Multiple poses are rendered inside one Editor frame. Snapshot
            // CPU skinning so the GPU's per-frame skin cache cannot lag behind
            // the pose measured above. Live verification uses the real renderer.
            var skins=actor.GetComponentsInChildren<SkinnedMeshRenderer>();var snapshots=new List<GameObject>();var meshes=new List<Mesh>();
            try
            {
                foreach(var skin in skins)
                {
                    var mesh=new Mesh();skin.BakeMesh(mesh,true);meshes.Add(mesh);
                    var snapshot=new GameObject("Review skin snapshot");snapshot.transform.SetParent(skin.transform,false);
                    snapshot.AddComponent<MeshFilter>().sharedMesh=mesh;snapshot.AddComponent<MeshRenderer>().sharedMaterials=skin.sharedMaterials;
                    snapshots.Add(snapshot);skin.forceRenderingOff=true;
                }
                preview.BeginStaticPreview(new Rect(0,0,320,400));preview.Render();
                var texture=preview.EndStaticPreview();File.WriteAllBytes(path,texture.EncodeToPNG());UnityEngine.Object.DestroyImmediate(texture);
            }
            finally{foreach(var skin in skins)skin.forceRenderingOff=false;foreach(var snapshot in snapshots)UnityEngine.Object.DestroyImmediate(snapshot);foreach(var mesh in meshes)UnityEngine.Object.DestroyImmediate(mesh);}
        }
        [MenuItem("Tools/CHOREO AI/Review/Capture candidates")]
        public static void Run()
        {
            var plan=JsonUtility.FromJson<Plan>(File.ReadAllText(".generation/motion-review-plan.json"));
            var library=AssetDatabase.LoadAssetAtPath<GeneratedLibrary>(GeneratedAssetBuilder.LibraryPath);
            string output=Choreographer.ResolveLocal(GeneratedAssetBuilder.Root,plan.output);
            Directory.CreateDirectory(output);
            var character=library.catalog.characters[0];var report=new Report{characterId=character.id,characterSignature=character.signature};
            foreach(string id in plan.motions)
            {
                var definition=library.catalog.motions.First(m=>m.id==id);var clip=library.Motion(id).clip;
                var preview=new PreviewRenderUtility();Material floorMaterial=null;MotionPlayer player=null;var mesh=new Mesh();
                try
                {
                    var actor=UnityEngine.Object.Instantiate(library.characters[0].prefab);preview.AddSingleGO(actor);player=new MotionPlayer(actor);
                    var animator=actor.GetComponentInChildren<Animator>();
                    var floor=GameObject.CreatePrimitive(PrimitiveType.Plane);UnityEngine.Object.DestroyImmediate(floor.GetComponent<Collider>());
                    floorMaterial=new Material(Shader.Find("Standard")){color=new Color(.16f,.18f,.21f)};
                    floor.GetComponent<Renderer>().sharedMaterial=floorMaterial;floor.transform.localScale=Vector3.one*.4f;preview.AddSingleGO(floor);
                    preview.camera.nearClipPlane=.01f;preview.camera.farClipPlane=30;preview.camera.fieldOfView=35;
                    preview.camera.backgroundColor=new Color(.055f,.075f,.1f);preview.camera.clearFlags=CameraClearFlags.SolidColor;
                    preview.lights[0].intensity=1.2f;preview.lights[0].transform.rotation=Quaternion.Euler(35,145,0);
                    preview.lights[1].intensity=.7f;preview.lights[1].transform.rotation=Quaternion.Euler(25,-35,0);preview.ambientColor=Color.gray;
                    float length=plan.mapped?60*definition.beats/plan.bpm:clip.length;
                    var item=new ClipReport{id=id,name=definition.name,signature=definition.signature,length=length};
                    var segment=new DanceSegment{startSeconds=0,endSeconds=length,beatCount=definition.beats};
                    var vertices=new List<Vector3>();
                    for(int i=0;i<=Mathf.CeilToInt(length*30);i++)
                    {
                        float t=Mathf.Min(length,i/30f);double source=plan.mapped?Choreographer.MotionTime(definition,segment,t):t;
                        player.Sample(clip,source);item.frames.Add(Measure(animator,actor,t,mesh,vertices));
                    }
                    string folder=Path.Combine(output,id);Directory.CreateDirectory(folder);
                    for(int i=0;i<16;i++)
                    {
                        float t=length*i/16;player.Sample(clip,plan.mapped?Choreographer.MotionTime(definition,segment,t):t);
                        Capture(preview,Path.Combine(folder,"front-"+i.ToString("00")+".png"),0,actor);
                        if(i%2==0)Capture(preview,Path.Combine(folder,"side-"+i.ToString("00")+".png"),1,actor);
                    }
                    report.clips.Add(item);
                }
                finally{player?.Dispose();preview.Cleanup();UnityEngine.Object.DestroyImmediate(mesh);if(floorMaterial)UnityEngine.Object.DestroyImmediate(floorMaterial);}
            }
            File.WriteAllText(Path.Combine(output,"trajectories.json"),JsonUtility.ToJson(report));
            Debug.Log("MOTION REVIEW CAPTURE: "+report.clips.Count+" candidates; no approvals recorded; "+output);
        }
    }
}
