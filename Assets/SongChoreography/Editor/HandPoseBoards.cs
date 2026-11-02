using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.Rendering;

namespace SongChoreography.Editor
{
    // Isolate actual skinned hand/forearm triangles so the torso cannot hide
    // a bent finger. These restricted visual QA artifacts remain local.
    public static class HandPoseBoards
    {
        public static void Run()
        {
            string folder=Path.GetFullPath("TestResults/HandPoseBoards");Directory.CreateDirectory(folder);
            var catalog=JsonUtility.FromJson<MotionCatalog>(Resources.Load<TextAsset>("PersonalMotionCatalog").text);
            foreach(string sex in new[]{"female","male"})foreach(var motion in catalog.motions)
            {
                var data=Resources.Load<LocalRigMotionAsset>(motion.resource+"-rig"+(sex=="male"?"-male":""));
                int worst=1;float step=0;
                for(int f=1;f<data.frameCount;f++)for(int j=22;j<52;j++)
                {int a=(f-1)*208+j*4,b=f*208+j*4;float angle=Quaternion.Angle(new Quaternion(data.rotations[a],data.rotations[a+1],data.rotations[a+2],data.rotations[a+3]),new Quaternion(data.rotations[b],data.rotations[b+1],data.rotations[b+2],data.rotations[b+3]));if(angle>step){step=angle;worst=f;}}
                var character=DancerCharacters.Create(sex);var animator=character.GetComponentInChildren<Animator>();animator.enabled=false;
                try{Render(character,data,new[]{worst,Math.Min(data.frameCount-1,data.frameCount/2)},Path.Combine(folder,sex+"-"+motion.id+"-hands.png"));}
                finally{UnityEngine.Object.DestroyImmediate(character);}
            }
        }
        static void Render(GameObject character,LocalRigMotionAsset data,int[] frames,string file)
        {
            var animator=character.GetComponentInChildren<Animator>();var bones=data.paths.Select(p=>animator.transform.Find(p)).ToArray();
            var body=character.GetComponentsInChildren<SkinnedMeshRenderer>().OrderByDescending(r=>r.sharedMesh.vertexCount).First();
            var renderers=character.GetComponentsInChildren<Renderer>();foreach(var renderer in renderers)renderer.enabled=false;
            var cameraObject=new GameObject("Hand detail camera");var camera=cameraObject.AddComponent<Camera>();camera.orthographic=true;camera.orthographicSize=.21f;camera.nearClipPlane=.01f;camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.03f,.045f,.06f);
            var target=new RenderTexture(400,400,24);camera.targetTexture=target;var board=new Texture2D(1600,800,TextureFormat.RGB24,false);
            var shape=new GameObject("Isolated hand");shape.transform.SetParent(body.transform,false);var filter=shape.AddComponent<MeshFilter>();var material=shape.AddComponent<MeshRenderer>();material.sharedMaterials=body.sharedMaterials;
            var baked=new Mesh();var lightObject=new GameObject("Detail light");var light=lightObject.AddComponent<Light>();light.type=LightType.Directional;light.intensity=.8f;lightObject.transform.rotation=Quaternion.Euler(30,145,0);
            var ambient=RenderSettings.ambientLight;var mode=RenderSettings.ambientMode;RenderSettings.ambientMode=AmbientMode.Flat;RenderSettings.ambientLight=Color.white;
            try
            {
                var weights=body.sharedMesh.boneWeights;var triangles=body.sharedMesh.triangles;
                for(int row=0;row<frames.Length;row++)
                {
                    int frame=frames[row];for(int j=0;j<52;j++){int k=frame*208+j*4;bones[j].localRotation=new Quaternion(data.rotations[k],data.rotations[k+1],data.rotations[k+2],data.rotations[k+3]);}bones[0].localPosition=data.hips[frame];
                    body.BakeMesh(baked);
                    foreach(int side in new[]{0,1})
                    {
                        var forearm=bones[18+side];var hand=bones[20+side];int first=22+side*15;
                        var selected=body.bones.Select(b=>b==forearm||b.IsChildOf(forearm)).ToArray();
                        var included=weights.Select(w=>(selected[w.boneIndex0]?w.weight0:0)+(selected[w.boneIndex1]?w.weight1:0)+(selected[w.boneIndex2]?w.weight2:0)+(selected[w.boneIndex3]?w.weight3:0)>.7f).ToArray();
                        var indices=new List<int>();for(int i=0;i<triangles.Length;i+=3)if(included[triangles[i]]&&included[triangles[i+1]]&&included[triangles[i+2]]){indices.Add(triangles[i]);indices.Add(triangles[i+1]);indices.Add(triangles[i+2]);}
                        var isolated=UnityEngine.Object.Instantiate(baked);isolated.triangles=indices.ToArray();filter.sharedMesh=isolated;
                        Vector3 forward=(bones[first+3].position-hand.position).normalized;
                        Vector3 normal=Vector3.Cross(bones[first].position-hand.position,bones[first+6].position-hand.position).normalized;
                        Vector3 center=Vector3.Lerp(hand.position,bones[first+5].position,.5f);
                        for(int view=0;view<2;view++)
                        {
                            Vector3 direction=(normal*(view==0?1:-1)+forward*.25f).normalized;cameraObject.transform.position=center+direction*2;cameraObject.transform.LookAt(center,forward);camera.Render();var previous=RenderTexture.active;RenderTexture.active=target;
                            var image=new Texture2D(400,400,TextureFormat.RGB24,false);image.ReadPixels(new Rect(0,0,400,400),0,0);image.Apply();RenderTexture.active=previous;board.SetPixels((side*2+view)*400,(1-row)*400,400,400,image.GetPixels());UnityEngine.Object.DestroyImmediate(image);
                        }
                        filter.sharedMesh=null;UnityEngine.Object.DestroyImmediate(isolated);
                    }
                }
                board.Apply();File.WriteAllBytes(file,board.EncodeToPNG());
            }
            finally
            {RenderSettings.ambientMode=mode;RenderSettings.ambientLight=ambient;camera.targetTexture=null;UnityEngine.Object.DestroyImmediate(target);UnityEngine.Object.DestroyImmediate(board);UnityEngine.Object.DestroyImmediate(baked);UnityEngine.Object.DestroyImmediate(shape);UnityEngine.Object.DestroyImmediate(cameraObject);UnityEngine.Object.DestroyImmediate(lightObject);}
        }
    }
}
