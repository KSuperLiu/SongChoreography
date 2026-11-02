using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.Rendering;

namespace SongChoreography.Editor
{
    // Calls the shipped player, including its blending and grounding, without
    // opening a foreground app or accepting keyboard/mouse input.
    public static class PlaybackAnatomyReview
    {
        [Serializable]class Edge {public string from,to;}
        [Serializable]class Selection {public List<Edge> edges=new List<Edge>();}
        [Serializable]class Entry {public string id;public string[] safeNext;}
        [Serializable]class Review {public Entry[] motions;}
        [Serializable]class Case
        {
            public string from,to,character,board,worstJoint;public bool passed,jointRangesPassed;
            public int samples;public float maxNormalizedJoint,minimumBendDegrees,lowestMesh=100,maxFramePosition,maxFrameAngle,cutPositionGap;
        }
        [Serializable]class Report {public int schemaVersion=1;public string version="0.5.1";public List<Case> cases=new List<Case>();}
        static List<Edge> Edges(string path)=>JsonUtility.FromJson<Review>(File.ReadAllText(path)).motions.SelectMany(m=>m.safeNext.Select(id=>new Edge{from=m.id,to=id})).ToList();
        public static void PrepareAndRun(){PersonalMotionBuilder.Prepare();Run();}
        public static void Run()
        {
            string output=Path.GetFullPath("TestResults/PlaybackAnatomyReview");Directory.CreateDirectory(output);
            var catalog=PersonalMotionPolicy.Merge(JsonUtility.FromJson<MotionCatalog>(Resources.Load<TextAsset>("MotionCatalog").text),JsonUtility.FromJson<MotionCatalog>(Resources.Load<TextAsset>("PersonalMotionCatalog").text));
            var privateMotions=catalog.motions.Where(m=>m.personalUseOnly).ToArray();
            var proposed=Edges("Assets/SongChoreography/Editor/PersonalMotionReview/finedance-001.json");
            if(File.Exists("PersonalAssets/AnatomySelectedEdges.json"))proposed=JsonUtility.FromJson<Selection>(File.ReadAllText("PersonalAssets/AnatomySelectedEdges.json")).edges;
            var shared=Edges("Assets/SongChoreography/Editor/MocapData/reviewed-charleston.json");
            var edges=privateMotions.SelectMany(a=>privateMotions.Where(b=>a.id!=b.id).Select(b=>new Edge{from=a.id,to=b.id})).Concat(shared).ToList();
            foreach(var motion in catalog.motions.Where(m=>m.personalUseOnly||m.id.StartsWith("charleston-review-")))motion.safeNext=edges.Where(e=>e.from==motion.id).Select(e=>e.to).ToList();
            var report=new Report();
            foreach(string sex in new[]{"female","male"})
            {
                var character=DancerCharacters.Create(sex);var animator=character.GetComponentInChildren<Animator>();
                var host=new GameObject("Offscreen playback QA");var player=host.AddComponent<DancePlayback>();player.Initialize(animator,catalog,sex);
                var project=new DanceProject{analysis=new MusicAnalysis{durationSeconds=40,bpm=101.3},grid=new GridSettings{manual=true,bpm=101.3},loopEnd=40};
                player.SetProject(project,AudioClip.Create("QA normal",4800,1,48000,false),AudioClip.Create("QA 075",4800,1,48000,false),AudioClip.Create("QA 050",4800,1,48000,false));
                var skin=character.GetComponentsInChildren<SkinnedMeshRenderer>().OrderByDescending(r=>r.sharedMesh.vertexCount).First();var mesh=new Mesh();
                var watched=Enum.GetValues(typeof(HumanBodyBones)).Cast<HumanBodyBones>().Where(b=>b!=HumanBodyBones.LastBone).Select(animator.GetBoneTransform).Where(b=>b).ToArray();
                try
                {
                    foreach(var edge in edges)
                    {
                        double cut=8*60/101.3;project.segments=new List<DanceSegment>{new DanceSegment{motionId=edge.from,startSeconds=0,endSeconds=cut,beatCount=8},new DanceSegment{motionId=edge.to,startSeconds=cut,endSeconds=cut*2,beatCount=8}};
                        var item=new Case{from=edge.from,to=edge.to,character=sex};
                        player.Seek(cut-.00001);var before=watched.Select(t=>t.position).ToArray();player.Seek(cut);item.cutPositionGap=watched.Select((t,i)=>Vector3.Distance(t.position,before[i])).Max();
                        Vector3[] previous=null;Quaternion[] rotation=null;
                        using(var probe=new JointRangeProbe(animator))
                        {
                            for(double time=cut-.15;time<=cut+.65;time+=1.0/120)
                            {
                                player.Seek(time);probe.Sample();skin.BakeMesh(mesh);item.lowestMesh=Math.Min(item.lowestMesh,mesh.vertices.Min(v=>skin.transform.TransformPoint(v).y));item.samples++;
                                if(previous!=null){item.maxFramePosition=Math.Max(item.maxFramePosition,watched.Select((t,i)=>Vector3.Distance(t.position,previous[i])).Max());item.maxFrameAngle=Math.Max(item.maxFrameAngle,watched.Select((t,i)=>Quaternion.Angle(t.rotation,rotation[i])).Max());}
                                previous=watched.Select(t=>t.position).ToArray();rotation=watched.Select(t=>t.rotation).ToArray();
                            }
                            item.jointRangesPassed=probe.Passed;item.maxNormalizedJoint=probe.Maximum;item.minimumBendDegrees=probe.MinimumBendDegrees;item.worstJoint=probe.Passed?probe.WorstMuscle:probe.FailureReason;
                        }
                        item.passed=item.jointRangesPassed&&item.lowestMesh>=-.005f&&item.maxFramePosition<.05f&&item.maxFrameAngle<12&&item.cutPositionGap<.003f;
                        bool selected=shared.Concat(proposed).Any(e=>e.from==edge.from&&e.to==edge.to);
                        if(selected)
                        {item.board=sex+"-"+edge.from+"--"+edge.to+".png";Board(character,player,cut,Path.Combine(output,item.board));}
                        report.cases.Add(item);Debug.Log("PLAYBACK_ANATOMY "+sex+" / "+edge.from+" -> "+edge.to+" / "+item.passed+" / normalized="+item.maxNormalizedJoint+" / bend="+item.minimumBendDegrees);
                    }
                }
                finally{UnityEngine.Object.DestroyImmediate(mesh);UnityEngine.Object.DestroyImmediate(host);UnityEngine.Object.DestroyImmediate(character);}
            }
            File.WriteAllText(Path.Combine(output,"results.json"),JsonUtility.ToJson(report,true));
        }
        static void Board(GameObject character,DancePlayback player,double cut,string path)
        {
            var cameraObject=new GameObject("Review camera");var camera=cameraObject.AddComponent<Camera>();camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.04f,.055f,.075f);camera.orthographic=true;camera.orthographicSize=1.05f;camera.nearClipPlane=.01f;
            var texture=new RenderTexture(500,320,24);camera.targetTexture=texture;
            var board=new Texture2D(1500,640,TextureFormat.RGB24,false);var ambient=RenderSettings.ambientLight;var mode=RenderSettings.ambientMode;RenderSettings.ambientMode=AmbientMode.Flat;RenderSettings.ambientLight=Color.white;
            var lightObject=new GameObject("Review light");var light=lightObject.AddComponent<Light>();light.type=LightType.Directional;light.intensity=.8f;lightObject.transform.rotation=Quaternion.Euler(30,145,0);
            var skins=character.GetComponentsInChildren<SkinnedMeshRenderer>();var snapshots=new List<GameObject>();
            foreach(var skin in skins){var copy=new GameObject("Baked pose");copy.transform.SetParent(skin.transform,false);copy.AddComponent<MeshFilter>().sharedMesh=new Mesh();copy.AddComponent<MeshRenderer>().sharedMaterials=skin.sharedMaterials;skin.enabled=false;snapshots.Add(copy);}
            try
            {
                float[] times={-.12f,0,.06f,.15f,.30f,.48f};
                for(int frame=0;frame<times.Length;frame++)
                {
                    player.Seek(cut+times[frame]);for(int i=0;i<skins.Length;i++)skins[i].BakeMesh(snapshots[i].GetComponent<MeshFilter>().sharedMesh);
                    Vector3 center=new Vector3(0,.92f,0);cameraObject.transform.position=center+new Vector3(0,.08f,4);cameraObject.transform.LookAt(center);camera.Render();var previous=RenderTexture.active;RenderTexture.active=texture;
                    var image=new Texture2D(500,320,TextureFormat.RGB24,false);image.ReadPixels(new Rect(0,0,500,320),0,0);image.Apply();RenderTexture.active=previous;board.SetPixels(frame%3*500,(1-frame/3)*320,500,320,image.GetPixels());UnityEngine.Object.DestroyImmediate(image);
                }
                board.Apply();File.WriteAllBytes(path,board.EncodeToPNG());
            }
            finally
            {
                foreach(var snapshot in snapshots){UnityEngine.Object.DestroyImmediate(snapshot.GetComponent<MeshFilter>().sharedMesh);UnityEngine.Object.DestroyImmediate(snapshot);}foreach(var skin in skins)skin.enabled=true;
                RenderSettings.ambientLight=ambient;RenderSettings.ambientMode=mode;camera.targetTexture=null;UnityEngine.Object.DestroyImmediate(texture);UnityEngine.Object.DestroyImmediate(board);UnityEngine.Object.DestroyImmediate(cameraObject);UnityEngine.Object.DestroyImmediate(lightObject);
            }
        }
    }
}
