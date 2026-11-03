using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace SongChoreography.Editor
{
    public static class MotionLibraryVerification
    {
        [Serializable] class Report {public bool completed;public int libraryCount,recommendedCount;public float previewTimeAdvance,previewHandMovement;public List<string> passed=new List<string>(),errors=new List<string>();}
        static Report report;static StudioApp app;static MotionLibrarySettings panel;static DancePlayback playback;static GeneratedLibrary library;
        static double start;static int phase;static float previewTime;static Vector3 hand;static DanceProject previous;
        static byte[] originalPreferences;
        static void Check(bool condition,string name){if(!condition)throw new InvalidOperationException(name);report.passed.Add(name);}
        [MenuItem("Tools/CHOREO AI/Review/Open motion library settings")]
        public static void Open(){var app=UnityEngine.Object.FindObjectOfType<StudioApp>();if(!app)throw new InvalidOperationException("请先进入 Play Mode。");app.OpenMotionLibrary();}
        [MenuItem("Tools/CHOREO AI/Review/Verify motion library settings")]
        public static void Run()
        {
            if(report!=null)throw new InvalidOperationException("动作库验证正在运行。");
            app=UnityEngine.Object.FindObjectOfType<StudioApp>();if(!app||!EditorApplication.isPlaying)throw new InvalidOperationException("请先进入 Play Mode。");
            panel=app.GetComponent<MotionLibrarySettings>();playback=app.GetComponent<DancePlayback>();library=Resources.Load<GeneratedLibrary>("GeneratedLibrary");
            report=new Report{libraryCount=library.catalog.motions.Count};originalPreferences=File.Exists(panel.SettingsPath)?File.ReadAllBytes(panel.SettingsPath):null;
            try
            {
                Check(report.libraryCount==243,"all 243 imported motions remain available for preview");
                app.OpenMotionLibrary();panel.SetCategory("jazz");panel.Preview("mixamo-jazz-dancing-101970903");
                Check(panel.PreviewedMotionId=="mixamo-jazz-dancing-101970903","unreviewed Jazz candidate can be previewed");
                previewTime=panel.PreviewSourceTime;hand=GameObject.Find("Library preview dancer").GetComponentInChildren<Animator>().GetBoneTransform(HumanBodyBones.LeftHand).position;
                phase=0;start=EditorApplication.timeSinceStartup;EditorApplication.update+=Update;
            }
            catch(Exception e){report.errors.Add(e.ToString());Finish(false);}
        }
        static void Update()
        {
            try
            {
                if(!EditorApplication.isPlaying)throw new InvalidOperationException("Play Mode stopped during library verification.");
                if(EditorApplication.timeSinceStartup-start>15)throw new TimeoutException("Library verification timed out.");
                if(phase==0&&EditorApplication.timeSinceStartup-start>1)
                {
                    report.previewTimeAdvance=panel.PreviewSourceTime-previewTime;report.previewHandMovement=Vector3.Distance(hand,GameObject.Find("Library preview dancer").GetComponentInChildren<Animator>().GetBoneTransform(HumanBodyBones.LeftHand).position);
                    Check(Mathf.Abs(report.previewTimeAdvance)>.1f&&report.previewHandMovement>.02f,"live preview advances time and deforms actual Humanoid bones");
                    panel.UseRecommendations();panel.SetSelected("mixamo-jazz-dancing-101970903",true);panel.SaveAndClose();
                    Check(!Choreographer.Eligible(library.catalog,library.catalog.characters[0].id,panel.EnabledMotionIds).Any(m=>m.id=="mixamo-jazz-dancing-101970903"),"checked unreviewed candidate stays outside automatic choreography");
                    panel.Open(0);panel.UseRecommendations();
                    foreach(var id in panel.EnabledMotionIds.ToArray())panel.SetSelected(id,false);
                    foreach(var id in new[]{"mixamo-hip-hop-dancing-101960901","mixamo-hip-hop-dancing-101960905","mixamo-hip-hop-dancing-108650901"})panel.SetSelected(id,true);
                    panel.SaveAndClose();
                    var saved=JsonUtility.FromJson<MotionLibraryPreferences>(File.ReadAllText(panel.SettingsPath));Check(saved.customSelection&&saved.motionIds.Count==3,"custom checked selection is saved locally");
                    previous=playback.Project;app.LoadPracticeExample(false);phase=1;
                }
                else if(phase==1&&playback.Project!=null&&!ReferenceEquals(previous,playback.Project))
                {
                    var selected=new HashSet<string>(panel.EnabledMotionIds);Check(playback.Project.segments.All(s=>selected.Contains(s.motionId)),"StudioApp generation respects saved library selection");
                    Check(playback.Project.segments.Select(s=>s.motionId).Distinct().Count()==3,"a three-motion selection generates only those three motions");
                    panel.Open(0);panel.UseRecommendations();panel.SaveAndClose();
                    report.recommendedCount=Choreographer.Eligible(library.catalog,library.catalog.characters[0].id,panel.EnabledMotionIds).Count;
                    Check(report.recommendedCount>0&&report.recommendedCount==panel.EnabledMotionIds.Distinct().Count(),"New Jazz recommendation contains only currently reviewed phrases");
                    Finish(true);
                }
            }
            catch(Exception e){report.errors.Add(e.ToString());Finish(false);}
        }
        static void Finish(bool completed)
        {
            EditorApplication.update-=Update;report.completed=completed;
            // Preserve a pre-existing personal selection. With no prior file,
            // leave the user's requested New Jazz recommendation selected.
            if(originalPreferences!=null)
            {File.WriteAllBytes(panel.SettingsPath,originalPreferences);panel.Initialize(library,null);}
            if(panel.IsOpen)panel.CloseWithoutSaving();Directory.CreateDirectory(".generation");File.WriteAllText(".generation/motion-library-ui-verification.json",JsonUtility.ToJson(report,true));
            Debug.Log("MOTION LIBRARY UI VERIFICATION: completed="+completed+", checks="+report.passed.Count+", errors="+report.errors.Count);report=null;
        }
    }
}
