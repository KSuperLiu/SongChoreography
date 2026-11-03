using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace SongChoreography.Editor
{
    public static class PracticeTransitionCapture
    {
        [Serializable] public class Pair {public string from,to;public float blendBeats=1;}
        [Serializable] public class Plan {public Pair[] edges=new Pair[0];public string output=".generation/transition-review-100bpm";public float bpm=100;}
        [MenuItem("Tools/CHOREO AI/Review/Capture practice transitions")]
        public static void Run()
        {
            var plan=JsonUtility.FromJson<Plan>(File.ReadAllText(".generation/transition-review-plan.json"));
            if(plan.edges.Length==0||!Choreographer.Finite(plan.bpm)||plan.bpm<40||plan.bpm>240)throw new InvalidDataException("需要有效的连接复查计划。");
            string output=Choreographer.ResolveLocal(GeneratedAssetBuilder.Root,plan.output);Directory.CreateDirectory(output);
            var lib=AssetDatabase.LoadAssetAtPath<GeneratedLibrary>(GeneratedAssetBuilder.LibraryPath);
            var character=lib.catalog.characters[0];var report=new MotionReviewCapture.Report{characterId=character.id,characterSignature=character.signature};
            foreach(var pair in plan.edges)
            {
                if(!Choreographer.Finite(pair.blendBeats)||pair.blendBeats<=0||pair.blendBeats>1)throw new InvalidDataException("连接混合长度须大于零且不超过一拍。");
                var from=lib.catalog.motions.First(m=>m.id==pair.from);var to=lib.catalog.motions.First(m=>m.id==pair.to);
                var preview=new PreviewRenderUtility();MotionPlayer player=null;var mesh=new Mesh();Material mat=null;
                try
                {
                    var actor=UnityEngine.Object.Instantiate(lib.characters[0].prefab);preview.AddSingleGO(actor);player=new MotionPlayer(actor);var animator=actor.GetComponentInChildren<Animator>();
                    var floor=GameObject.CreatePrimitive(PrimitiveType.Plane);UnityEngine.Object.DestroyImmediate(floor.GetComponent<Collider>());mat=new Material(Shader.Find("Standard")){color=new Color(.16f,.18f,.21f)};floor.GetComponent<Renderer>().sharedMaterial=mat;preview.AddSingleGO(floor);
                    preview.camera.nearClipPlane=.01f;preview.camera.farClipPlane=30;preview.camera.fieldOfView=35;preview.camera.backgroundColor=new Color(.055f,.075f,.1f);preview.camera.clearFlags=CameraClearFlags.SolidColor;
                    preview.lights[0].intensity=1.2f;preview.lights[0].transform.rotation=Quaternion.Euler(35,145,0);preview.lights[1].intensity=.7f;preview.lights[1].transform.rotation=Quaternion.Euler(25,-35,0);preview.ambientColor=Color.gray;
                    float blend=60/plan.bpm*pair.blendBeats,length=.3f+blend+.6f;
                    var outgoingSegment=new DanceSegment{startSeconds=0,endSeconds=60*from.beats/plan.bpm,beatCount=from.beats};
                    var incomingSegment=new DanceSegment{startSeconds=0,endSeconds=60*to.beats/plan.bpm,beatCount=to.beats};
                    var item=new MotionReviewCapture.ClipReport{id=from.id+"--"+to.id,name=from.name+" → "+to.name,signature=from.signature+":"+to.signature,length=length};var vertices=new List<Vector3>();
                    System.Action<float> sample=t=>{
                        double incoming=t-.3;
                        if(incoming<0)player.Sample(lib.Motion(from.id).clip,Choreographer.MotionTime(from,outgoingSegment,outgoingSegment.endSeconds+incoming));
                        else if(incoming<blend)player.Sample(lib.Motion(to.id).clip,Choreographer.MotionTime(to,incomingSegment,incoming),lib.Motion(from.id).clip,from.clipEnd,Mathf.SmoothStep(0,1,(float)(incoming/blend)));
                        else player.Sample(lib.Motion(to.id).clip,Choreographer.MotionTime(to,incomingSegment,incoming));
                    };
                    for(int i=0;i<=Mathf.CeilToInt(length*30);i++){float t=Mathf.Min(length,i/30f);sample(t);item.frames.Add(MotionReviewCapture.Measure(animator,actor,t,mesh,vertices));}
                    string folder=Path.Combine(output,item.id);Directory.CreateDirectory(folder);
                    for(int i=0;i<16;i++){sample(i*length/16);MotionReviewCapture.Capture(preview,Path.Combine(folder,"front-"+i.ToString("00")+".png"),0,actor);if(i%2==0)MotionReviewCapture.Capture(preview,Path.Combine(folder,"side-"+i.ToString("00")+".png"),1,actor);}
                    report.clips.Add(item);
                }
                finally{player?.Dispose();preview.Cleanup();UnityEngine.Object.DestroyImmediate(mesh);if(mat)UnityEngine.Object.DestroyImmediate(mat);}
            }
            File.WriteAllText(Path.Combine(output,"trajectories.json"),JsonUtility.ToJson(report));
            File.WriteAllText(Path.Combine(output,"plan.json"),JsonUtility.ToJson(plan,true));
            Debug.Log("PRACTICE TRANSITIONS: captured "+report.clips.Count+" directed transitions at "+plan.bpm+" BPM. No approvals recorded.");
        }
    }
}
