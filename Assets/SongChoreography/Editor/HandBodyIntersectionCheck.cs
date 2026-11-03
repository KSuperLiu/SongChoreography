using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace SongChoreography.Editor
{
    // Diagnostic only: evaluates one isolated clip and the actual deformed
    // torso/upper-leg surface, without changing animation or review records.
    public static class HandBodyIntersectionCheck
    {
        [MenuItem("Tools/CHOREO AI/Review/Open Snake issue preview")]
        public static void OpenIssue()
        {
            var app=UnityEngine.Object.FindObjectOfType<StudioApp>();if(!app)throw new InvalidOperationException("请先进入 Play Mode。");
            app.OpenMotionLibrary();var panel=app.GetComponent<MotionLibrarySettings>();panel.SetCategory("all");panel.SetSearch("Snake");panel.Preview("mixamo-snake-hip-hop-dance-109070901");
            var lib=Resources.Load<GeneratedLibrary>("GeneratedLibrary");var character=lib.catalog.characters[0];
            var analysis=JsonUtility.FromJson<MusicAnalysis>(File.ReadAllText(Path.Combine(Application.streamingAssetsPath,"Practice100/analysis.json")));
            var project=new DanceProject{characterId=character.id,characterSignature=character.signature,analysis=analysis,grid=new GridSettings{manual=true,bpm=100},loopEnd=analysis.durationSeconds};
            project.segments=Choreographer.Generate(project,lib.catalog,panel.EnabledMotionIds);Choreographer.ValidateProject(project,lib.catalog);
            if(project.segments.Any(s=>s.motionId=="mixamo-snake-hip-hop-dance-109070901"))throw new InvalidOperationException("穿模片段仍参与编排。");
            File.WriteAllText(".generation/snake-hand-body-check/after-quarantine-project.json",JsonUtility.ToJson(project,true));
            Debug.Log("SNAKE QUARANTINE: preview retained; automatic generation excludes affected clip; "+project.segments.Select(s=>s.motionId).Distinct().Count()+" distinct motions, "+project.segments.Count+" slots.");
        }
        [Serializable] public class PointCheck {public string point;public Vector3 position,surface;public float signedDistance;public int insideVotes;}
        [Serializable] public class Frame {public float sourceTime;public List<PointCheck> points=new List<PointCheck>();}
        [Serializable] public class Report {public string motionId,characterSignature,motionSignature;public bool isolatedClip=true;public int bodyTriangles;public float worstSourceTime,worstSignedDistance;public string worstPoint;public List<Frame> frames=new List<Frame>();}
        static float Weight(BoneWeight w,HashSet<int> indices)
        {return (indices.Contains(w.boneIndex0)?w.weight0:0)+(indices.Contains(w.boneIndex1)?w.weight1:0)+(indices.Contains(w.boneIndex2)?w.weight2:0)+(indices.Contains(w.boneIndex3)?w.weight3:0);}
        static Vector3 Closest(Vector3 p,Vector3 a,Vector3 b,Vector3 c)
        {
            Vector3 ab=b-a,ac=c-a,ap=p-a;float d1=Vector3.Dot(ab,ap),d2=Vector3.Dot(ac,ap);if(d1<=0&&d2<=0)return a;
            Vector3 bp=p-b;float d3=Vector3.Dot(ab,bp),d4=Vector3.Dot(ac,bp);if(d3>=0&&d4<=d3)return b;
            float vc=d1*d4-d3*d2;if(vc<=0&&d1>=0&&d3<=0)return a+ab*(d1/(d1-d3));
            Vector3 cp=p-c;float d5=Vector3.Dot(ab,cp),d6=Vector3.Dot(ac,cp);if(d6>=0&&d5<=d6)return c;
            float vb=d5*d2-d1*d6;if(vb<=0&&d2>=0&&d6<=0)return a+ac*(d2/(d2-d6));
            float va=d3*d6-d5*d4;if(va<=0&&d4-d3>=0&&d5-d6>=0)return b+(c-b)*((d4-d3)/(d4-d3+d5-d6));
            float denom=1/(va+vb+vc);return a+ab*(vb*denom)+ac*(vc*denom);
        }
        static bool Ray(Vector3 origin,Vector3 direction,Vector3 a,Vector3 b,Vector3 c,out float distance)
        {
            distance=0;var ab=b-a;var ac=c-a;var h=Vector3.Cross(direction,ac);float det=Vector3.Dot(ab,h);if(Mathf.Abs(det)<1e-8f)return false;
            float inv=1/det;var s=origin-a;float u=inv*Vector3.Dot(s,h);if(u<0||u>1)return false;var q=Vector3.Cross(s,ab);float v=inv*Vector3.Dot(direction,q);if(v<0||u+v>1)return false;
            distance=inv*Vector3.Dot(ac,q);return distance>1e-5f;
        }
        static PointCheck Check(string name,Vector3 point,Vector3[] vertices,Vector3[] normals,int[] triangles)
        {
            float best=float.PositiveInfinity;Vector3 surface=Vector3.zero,normal=Vector3.up;
            for(int i=0;i<triangles.Length;i+=3)
            {
                int a=triangles[i],b=triangles[i+1],c=triangles[i+2];var closest=Closest(point,vertices[a],vertices[b],vertices[c]);float squared=(point-closest).sqrMagnitude;
                if(squared<best){best=squared;surface=closest;normal=(normals[a]+normals[b]+normals[c]).normalized;}
            }
            int votes=0;var hits=new List<float>();
            foreach(var direction in new[]{new Vector3(1,.013f,.073f).normalized,new Vector3(-1,-.013f,-.073f).normalized,new Vector3(.037f,.019f,1).normalized,new Vector3(-.037f,-.019f,-1).normalized})
            {
                hits.Clear();for(int i=0;i<triangles.Length;i+=3)if(Ray(point,direction,vertices[triangles[i]],vertices[triangles[i+1]],vertices[triangles[i+2]],out float distance))hits.Add(distance);
                hits.Sort();int unique=0;float last=-1;foreach(float hit in hits)if(hit-last>.0001f){unique++;last=hit;}if(unique%2==1)votes++;
            }
            return new PointCheck{point=name,position=point,surface=surface,signedDistance=Mathf.Sqrt(best)*(Vector3.Dot(point-surface,normal)>=0?1:-1),insideVotes=votes};
        }
        [MenuItem("Tools/CHOREO AI/Review/Check Snake hand body intersection")]
        public static void Run()
        {
            const string id="mixamo-snake-hip-hop-dance-109070901";var lib=AssetDatabase.LoadAssetAtPath<GeneratedLibrary>(GeneratedAssetBuilder.LibraryPath);var motion=lib.catalog.motions.First(m=>m.id==id);
            var preview=new PreviewRenderUtility();var actor=UnityEngine.Object.Instantiate(lib.characters[0].prefab);preview.AddSingleGO(actor);var animator=actor.GetComponentInChildren<Animator>();var skin=actor.GetComponentInChildren<SkinnedMeshRenderer>();var mesh=new Mesh();MotionPlayer player=null;
            var report=new Report{motionId=id,characterSignature=lib.catalog.characters[0].signature,motionSignature=motion.signature};
            try
            {
                var bodyBones=new HashSet<Transform>(new[]{HumanBodyBones.Hips,HumanBodyBones.Spine,HumanBodyBones.Chest,HumanBodyBones.UpperChest,HumanBodyBones.LeftUpperLeg,HumanBodyBones.RightUpperLeg}.Select(b=>animator.GetBoneTransform(b)).Where(t=>t));
                var indices=new HashSet<int>(Enumerable.Range(0,skin.bones.Length).Where(i=>bodyBones.Contains(skin.bones[i])));var weights=skin.sharedMesh.boneWeights;var all=skin.sharedMesh.triangles;var selected=new List<int>();
                for(int i=0;i<all.Length;i+=3)if(Weight(weights[all[i]],indices)>.65f&&Weight(weights[all[i+1]],indices)>.65f&&Weight(weights[all[i+2]],indices)>.65f){selected.Add(all[i]);selected.Add(all[i+1]);selected.Add(all[i+2]);}
                var triangles=selected.ToArray();report.bodyTriangles=triangles.Length/3;player=new MotionPlayer(actor);
                int frames=Mathf.CeilToInt((float)(motion.clipEnd-motion.clipStart)*30);
                for(int frame=0;frame<=frames;frame++)
                {
                    float time=Mathf.Min((float)motion.clipEnd,(float)motion.clipStart+frame/30f);player.Sample(lib.Motion(id).clip,time);skin.BakeMesh(mesh,true);
                    var vertices=mesh.vertices;var normals=mesh.normals;for(int i=0;i<vertices.Length;i++){vertices[i]=skin.transform.TransformPoint(vertices[i]);normals[i]=skin.transform.TransformDirection(normals[i]).normalized;}
                    var sample=new Frame{sourceTime=time};
                    foreach(bool left in new[]{true,false})
                    {
                        var wrist=animator.GetBoneTransform(left?HumanBodyBones.LeftHand:HumanBodyBones.RightHand);var finger=animator.GetBoneTransform(left?HumanBodyBones.LeftMiddleProximal:HumanBodyBones.RightMiddleProximal);var distal=animator.GetBoneTransform(left?HumanBodyBones.LeftMiddleDistal:HumanBodyBones.RightMiddleDistal);
                        var points=new[]{wrist.position,finger?(wrist.position+finger.position)*.5f:wrist.position,distal?distal.position:wrist.position};
                        for(int i=0;i<points.Length;i++)
                        {
                            var result=Check((left?"left-":"right-")+new[]{"wrist","palm","middle-finger"}[i],points[i],vertices,normals,triangles);sample.points.Add(result);
                            if(result.insideVotes>=3&&result.signedDistance<report.worstSignedDistance){report.worstSignedDistance=result.signedDistance;report.worstSourceTime=time;report.worstPoint=result.point;}
                        }
                    }
                    report.frames.Add(sample);
                }
                string folder=".generation/snake-hand-body-check";Directory.CreateDirectory(folder);File.WriteAllText(Path.Combine(folder,"report.json"),JsonUtility.ToJson(report,true));
                if(report.worstPoint!=null)
                {
                    player.Sample(lib.Motion(id).clip,report.worstSourceTime);preview.camera.nearClipPlane=.01f;preview.camera.farClipPlane=30;preview.camera.fieldOfView=35;preview.camera.backgroundColor=new Color(.055f,.075f,.1f);preview.camera.clearFlags=CameraClearFlags.SolidColor;
                    preview.lights[0].intensity=1.2f;preview.lights[0].transform.rotation=Quaternion.Euler(35,145,0);preview.lights[1].intensity=.7f;preview.lights[1].transform.rotation=Quaternion.Euler(25,-35,0);preview.ambientColor=Color.gray;
                    MotionReviewCapture.Capture(preview,Path.Combine(folder,"worst-front.png"),0,actor);MotionReviewCapture.Capture(preview,Path.Combine(folder,"worst-side.png"),1,actor);
                }
                Debug.Log("ISOLATED SNAKE HAND/BODY CHECK: "+report.frames.Count+" frames, torso triangles="+report.bodyTriangles+", worst="+report.worstSignedDistance+" m, source time="+report.worstSourceTime+", "+report.worstPoint);
            }
            finally{player?.Dispose();UnityEngine.Object.DestroyImmediate(mesh);preview.Cleanup();}
        }
    }
}
