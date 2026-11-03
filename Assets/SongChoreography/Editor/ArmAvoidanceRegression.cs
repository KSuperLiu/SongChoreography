using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace SongChoreography.Editor
{
    public static class ArmAvoidanceRegression
    {
        [Serializable] class BoneInfo {public string name;public Vector3 position;public float weight;}
        [Serializable] class Sample {public string motion,mode;public float time,maxCorrection;public int vertexCount,vertex;public Vector3 point,leftWrist,rightWrist,leftElbow,rightElbow;public float clearance;public List<BoneInfo> bones=new List<BoneInfo>();}
        [Serializable] class Report {public List<Sample> samples=new List<Sample>();}
        [MenuItem("Tools/CHOREO AI/Review/Inspect arm avoidance residual")]
        public static void Inspect()
        {
            var lib=AssetDatabase.LoadAssetAtPath<GeneratedLibrary>(GeneratedAssetBuilder.LibraryPath);var actor=UnityEngine.Object.Instantiate(lib.characters[0].prefab);var a=actor.GetComponentInChildren<Animator>();var skin=actor.GetComponentInChildren<SkinnedMeshRenderer>();var mesh=new Mesh();var result=new Report();
            try
            {
                foreach(bool corrected in new[]{false,true})using(var player=new MotionPlayer(actor,corrected))using(var body=new SkinnedBodySurface(actor,a))
                {
                    foreach(var test in new[]{new[]{"mixamo-snake-hip-hop-dance-109070901","7.1666665","17369"},new[]{"mixamo-hip-hop-dancing-108690901","1.5966667","1603"},new[]{"mixamo-hip-hop-dancing-108650901","10.53","183"}})
                    {
                        float time=float.Parse(test[1],System.Globalization.CultureInfo.InvariantCulture);int vertex=int.Parse(test[2]);player.Sample(lib.Motion(test[0]).clip,time);skin.BakeMesh(mesh,true);var point=skin.transform.TransformPoint(mesh.vertices[vertex]);body.Update();
                        var s=new Sample{motion=test[0],mode=corrected?"corrected":"raw",time=time,vertex=vertex,vertexCount=mesh.vertexCount,point=point,clearance=body.Query(point).distance,maxCorrection=player.LastArmCorrection,leftWrist=a.GetBoneTransform(HumanBodyBones.LeftHand).position,rightWrist=a.GetBoneTransform(HumanBodyBones.RightHand).position,leftElbow=a.GetBoneTransform(HumanBodyBones.LeftLowerArm).position,rightElbow=a.GetBoneTransform(HumanBodyBones.RightLowerArm).position};
                        var w=skin.sharedMesh.boneWeights[vertex];int[] ids={w.boneIndex0,w.boneIndex1,w.boneIndex2,w.boneIndex3};float[] weights={w.weight0,w.weight1,w.weight2,w.weight3};for(int i=0;i<4;i++)if(weights[i]>0)s.bones.Add(new BoneInfo{name=skin.bones[ids[i]].name,position=skin.bones[ids[i]].position,weight=weights[i]});result.samples.Add(s);
                    }
                }
                Directory.CreateDirectory(".generation/arm-avoidance");File.WriteAllText(".generation/arm-avoidance/residual-inspection.json",JsonUtility.ToJson(result,true));
            }
            finally{UnityEngine.Object.DestroyImmediate(mesh);UnityEngine.Object.DestroyImmediate(actor);}
        }
    }
}
