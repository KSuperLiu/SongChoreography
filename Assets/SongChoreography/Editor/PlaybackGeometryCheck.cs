using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace SongChoreography.Editor
{
    public static class PlaybackGeometryCheck
    {
        [Serializable] class Snapshot
        {
            public string name; public Vector3 root,model,modelScale,hips,leftFoot,rightFoot;
            public List<Skin> skins=new List<Skin>();
        }
        [Serializable] class Skin { public string name;public Vector3 position,scale;public Bounds bounds;public float defaultMin,unscaledMin,scaledMin,cpuMin,unscaledNoScaleMin; }
        [Serializable] class Report {public List<Snapshot> snapshots=new List<Snapshot>();}
        static float Minimum(SkinnedMeshRenderer skin,Mesh mesh)
        {float min=float.PositiveInfinity;foreach(var v in mesh.vertices)min=Mathf.Min(min,skin.transform.TransformPoint(v).y);return min;}
        static Snapshot Measure(GameObject actor,string name)
        {
            var a=actor.GetComponentInChildren<Animator>();var s=new Snapshot{name=name,root=actor.transform.position,model=a.transform.position,modelScale=a.transform.lossyScale,hips=a.GetBoneTransform(HumanBodyBones.Hips).position,leftFoot=a.GetBoneTransform(HumanBodyBones.LeftFoot).position,rightFoot=a.GetBoneTransform(HumanBodyBones.RightFoot).position};
            var mesh=new Mesh();try{foreach(var skin in actor.GetComponentsInChildren<SkinnedMeshRenderer>())
            {var sk=new Skin{name=skin.name,position=skin.transform.position,scale=skin.transform.lossyScale,bounds=skin.bounds};skin.BakeMesh(mesh);sk.defaultMin=Minimum(skin,mesh);skin.BakeMesh(mesh,false);sk.unscaledMin=Minimum(skin,mesh);skin.BakeMesh(mesh,true);sk.scaledMin=Minimum(skin,mesh);float lo=float.PositiveInfinity;foreach(var v in mesh.vertices)lo=Mathf.Min(lo,(skin.transform.position+skin.transform.rotation*v).y);sk.unscaledNoScaleMin=lo;
                var source=skin.sharedMesh;var vs=source.vertices;var weights=source.boneWeights;var bind=source.bindposes;var bones=skin.bones;lo=float.PositiveInfinity;
                for(int i=0;i<vs.Length;i++){var w=weights[i];Vector3 pt=Vector3.zero;pt+=bones[w.boneIndex0].localToWorldMatrix.MultiplyPoint3x4(bind[w.boneIndex0].MultiplyPoint3x4(vs[i]))*w.weight0;pt+=bones[w.boneIndex1].localToWorldMatrix.MultiplyPoint3x4(bind[w.boneIndex1].MultiplyPoint3x4(vs[i]))*w.weight1;pt+=bones[w.boneIndex2].localToWorldMatrix.MultiplyPoint3x4(bind[w.boneIndex2].MultiplyPoint3x4(vs[i]))*w.weight2;pt+=bones[w.boneIndex3].localToWorldMatrix.MultiplyPoint3x4(bind[w.boneIndex3].MultiplyPoint3x4(vs[i]))*w.weight3;lo=Mathf.Min(lo,pt.y);}sk.cpuMin=lo;s.skins.Add(sk);}}
            finally{UnityEngine.Object.DestroyImmediate(mesh);}return s;
        }
        [MenuItem("Tools/CHOREO AI/Review/Check geometry")]
        public static void Run()
        {
            var lib=AssetDatabase.LoadAssetAtPath<GeneratedLibrary>(GeneratedAssetBuilder.LibraryPath);var actor=UnityEngine.Object.Instantiate(lib.characters[0].prefab);var r=new Report();
            try{r.snapshots.Add(Measure(actor,"bind"));using(var p=new MotionPlayer(actor)){p.Sample(lib.Motion("mixamo-body-wave-v1").clip,0);r.snapshots.Add(Measure(actor,"body-wave-0"));}}
            finally{UnityEngine.Object.DestroyImmediate(actor);}File.WriteAllText(".generation/playback-geometry.json",JsonUtility.ToJson(r,true));
        }
    }
}
