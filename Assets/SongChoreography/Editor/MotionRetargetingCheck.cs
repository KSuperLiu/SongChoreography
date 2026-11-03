using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace SongChoreography.Editor
{
    public static class MotionRetargetingCheck
    {
        [Serializable] class Sample
        {
            public string motion;
            public float time;
            public Vector3 modelEuler,bodyEuler,shoulderForward,feetForward;
        }
        [Serializable] class Report {public List<Sample> samples=new List<Sample>();}
        static Sample Measure(Animator animator,string motion,float time)
        {
            Vector3 left=animator.GetBoneTransform(HumanBodyBones.LeftUpperArm).position,right=animator.GetBoneTransform(HumanBodyBones.RightUpperArm).position;
            var forward=Vector3.Cross(right-left,Vector3.up).normalized;
            Vector3 feet=Vector3.zero;
            foreach(var pair in new[]{new[]{HumanBodyBones.LeftFoot,HumanBodyBones.LeftToes},new[]{HumanBodyBones.RightFoot,HumanBodyBones.RightToes}})
            {var foot=animator.GetBoneTransform(pair[0]);var toe=animator.GetBoneTransform(pair[1]);if(foot&&toe)feet+=toe.position-foot.position;}
            var pose=new HumanPose();using(var handler=new HumanPoseHandler(animator.avatar,animator.transform))handler.GetHumanPose(ref pose);
            return new Sample{motion=motion,time=time,modelEuler=animator.transform.eulerAngles,bodyEuler=pose.bodyRotation.eulerAngles,shoulderForward=forward,feetForward=feet.normalized};
        }
        [MenuItem("Tools/CHOREO AI/Check motion retargeting")]
        public static void Run()
        {
            var library=AssetDatabase.LoadAssetAtPath<GeneratedLibrary>(GeneratedAssetBuilder.LibraryPath);
            if(!library||library.characters.Length==0)throw new InvalidOperationException("先导入人物。");
            var scene=EditorSceneManager.NewPreviewScene();
            var actor=(GameObject)PrefabUtility.InstantiatePrefab(library.characters[0].prefab,scene);var animator=actor.GetComponentInChildren<Animator>();
            var report=new Report();
            try
            {
                report.samples.Add(Measure(animator,"bind",0));
                using(var player=new MotionPlayer(actor))foreach(var motion in library.motions)
                    for(int i=0;i<5;i++){float time=motion.clip.length*i/5;player.Sample(motion.clip,time);report.samples.Add(Measure(animator,motion.id,time));}
                Directory.CreateDirectory(".generation");File.WriteAllText(".generation/retargeting-check.json",JsonUtility.ToJson(report,true));
                Debug.Log("RETARGETING CHECK: "+report.samples.Count+" poses; .generation/retargeting-check.json. This records geometry, not artistic approval.");
            }
            finally{UnityEngine.Object.DestroyImmediate(actor);EditorSceneManager.ClosePreviewScene(scene);}
        }
    }
}
