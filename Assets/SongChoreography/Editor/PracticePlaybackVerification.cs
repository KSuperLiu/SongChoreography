using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace SongChoreography.Editor
{
    // Runs in the user's Editor Play Mode, through the normal StudioApp loader.
    public static class PracticePlaybackVerification
    {
        [Serializable] public class Plan {public int expectedDistinctMotions;public string[] requiredMotionIds=new string[0];public string output=".generation/practice-playmode-verification.json";}
        [Serializable] public class Tick
        {
            public string phase,motion;public double dsp,song,audioSong;public float speed,pitch,minY;
            public bool playing,audioPlaying;public int segment;public Vector3 head,leftHand,rightHand,leftFoot,rightFoot;
        }
        [Serializable] public class Report
        {
            public string unityVersion,characterSignature;public bool completed;public int segmentCount;
            public double bpm,firstBeat;public List<Tick> samples=new List<Tick>();public List<string> errors=new List<string>();
            public string[] sequence;public int seed,uniqueMotionCount,expectedDistinctMotions,collisionFrames,collisionBadFrames,collisionPoints,loopWraps,loopHandoffs;public float worstPenetration,maxLoopWrapHandStep,maxLoopHandoffHandStep;
        }
        static StudioApp app;static DancePlayback playback;static Report report;static int phase;
        static Plan plan;static DanceProject priorProject;
        static double started,nextTick,phaseStarted;static Mesh mesh;static readonly List<Vector3> vertices=new List<Vector3>();
        static SkinnedBodySurface body;static Animator bodyAnimator;static SkinnedMeshRenderer[] skins;static List<int[]> handVertices;static double nextCollision;
        static int priorFrame,priorPhase;static double priorSong;static Vector3 priorLeft,priorRight;
        static bool priorLoopBlend;
        static readonly string[] PhaseNames={"full-song","rate-075","rate-050","loop","loop-transition"};
        [MenuItem("Tools/CHOREO AI/Review/Verify practice in Play Mode")]
        public static void Run()
        {
            if(!EditorApplication.isPlaying)throw new InvalidOperationException("请先进入 Unity Play Mode。");
            if(report!=null)throw new InvalidOperationException("练习验证正在运行。");
            app=UnityEngine.Object.FindObjectOfType<StudioApp>();if(!app)throw new InvalidOperationException("StudioApp 尚未初始化。");
            plan=File.Exists(".generation/practice-verification-plan.json")?JsonUtility.FromJson<Plan>(File.ReadAllText(".generation/practice-verification-plan.json")):new Plan();
            Choreographer.ResolveLocal(GeneratedAssetBuilder.Root,plan.output);
            playback=app.GetComponent<DancePlayback>();priorProject=playback.Project;report=new Report{unityVersion=Application.unityVersion,expectedDistinctMotions=plan.expectedDistinctMotions};phase=0;started=EditorApplication.timeSinceStartup;nextTick=nextCollision=0;phaseStarted=0;mesh=new Mesh();body=null;bodyAnimator=null;
            Application.logMessageReceived+=Log;EditorApplication.update+=Update;
            priorFrame=-1;priorPhase=-1;
            try{app.LoadPracticeExample(plan.requiredMotionIds.Length==0);}catch(Exception e){report.errors.Add(e.ToString());Finish(false);throw;}
        }
        static void Log(string text,string stack,LogType type)
        {if(report!=null&&(type==LogType.Error||type==LogType.Exception||type==LogType.Assert))report.errors.Add(text);}
        static void Update()
        {
            try
            {
                if(!EditorApplication.isPlaying||!playback){report.errors.Add("Play Mode was stopped during verification.");Finish(false);return;}
                if(EditorApplication.timeSinceStartup-started>90){report.errors.Add("Practice verification timed out.");Finish(false);return;}
                var p=playback.Project;if(p==null||ReferenceEquals(p,priorProject))return;
                if(phaseStarted==0)
                {
                    var catalog=Resources.Load<GeneratedLibrary>("GeneratedLibrary").catalog;
                    if(plan.requiredMotionIds.Length>0)
                    {
                        var settings=app.GetComponent<MotionLibrarySettings>();bool found=false;
                        for(int seed=0;seed<1000;seed++)
                        {
                            p.seed=seed;var candidate=Choreographer.Generate(p,catalog,settings.EnabledMotionIds);
                            if(!plan.requiredMotionIds.All(id=>candidate.Any(s=>s.motionId==id)))continue;
                            playback.Pause();p.segments=candidate;playback.Seek(0);playback.Play();found=true;break;
                        }
                        if(!found)throw new InvalidOperationException("没有生成覆盖本轮新增动作的验证编排。");
                    }
                    report.seed=p.seed;
                    Choreographer.ValidateProject(p,catalog);
                    report.sequence=p.segments.Select(s=>s.motionId).ToArray();report.uniqueMotionCount=report.sequence.Distinct().Count();
                    if(report.uniqueMotionCount<plan.expectedDistinctMotions)throw new InvalidOperationException("练习动作种类不足："+report.uniqueMotionCount+" / "+plan.expectedDistinctMotions);
                    phaseStarted=AudioSettings.dspTime;report.characterSignature=p.characterSignature;report.segmentCount=p.segments.Count;report.bpm=p.grid.bpm;report.firstBeat=p.grid.firstBeatSeconds;
                }
                if(EditorApplication.timeSinceStartup>=nextTick)
                {
                    nextTick=EditorApplication.timeSinceStartup+.1;
                    var a=UnityEngine.Object.FindObjectOfType<Animator>();var geometry=MotionReviewCapture.Measure(a,a.transform.root.gameObject,(float)p.position,mesh,vertices);var source=playback.GetComponent<AudioSource>();int segment=playback.ActiveSegment();
                    report.samples.Add(new Tick{phase=PhaseNames[phase],motion=p.segments[segment].motionId,dsp=AudioSettings.dspTime,song=p.position,audioSong=source.time*p.speed,speed=p.speed,pitch=source.pitch,playing=playback.Playing,audioPlaying=source.isPlaying,segment=segment,minY=geometry.minY,head=geometry.head,leftHand=geometry.leftHand,rightHand=geometry.rightHand,leftFoot=geometry.leftFoot,rightFoot=geometry.rightFoot});
                    if(EditorApplication.timeSinceStartup>=nextCollision){nextCollision=EditorApplication.timeSinceStartup+.2;CheckCollision(a);}
                }
                if(bodyAnimator&&priorFrame!=Time.frameCount)
                {
                    var hips=bodyAnimator.GetBoneTransform(HumanBodyBones.Hips).position;var left=bodyAnimator.GetBoneTransform(HumanBodyBones.LeftHand).position-hips;var right=bodyAnimator.GetBoneTransform(HumanBodyBones.RightHand).position-hips;
                    if(phase>=3&&priorPhase==phase)
                    {
                        if(p.position<priorSong-.1){report.loopWraps++;report.maxLoopWrapHandStep=Mathf.Max(report.maxLoopWrapHandStep,Vector3.Distance(left,priorLeft),Vector3.Distance(right,priorRight));}
                        if(priorLoopBlend&&!playback.BlendingLoop){report.loopHandoffs++;report.maxLoopHandoffHandStep=Mathf.Max(report.maxLoopHandoffHandStep,Vector3.Distance(left,priorLeft),Vector3.Distance(right,priorRight));}
                    }
                    priorFrame=Time.frameCount;priorPhase=phase;priorSong=p.position;priorLeft=left;priorRight=right;priorLoopBlend=playback.BlendingLoop;
                }
                if(phase==0&&p.position>=p.analysis.durationSeconds-.001&&!playback.Playing){phase=1;playback.SetRate(.75f);playback.Seek(0);playback.Play();phaseStarted=AudioSettings.dspTime;}
                else if(phase==1&&AudioSettings.dspTime-phaseStarted>2.5){phase=2;playback.SetRate(.5f);playback.Seek(0);phaseStarted=AudioSettings.dspTime;}
                else if(phase==2&&AudioSettings.dspTime-phaseStarted>2.5){phase=3;playback.SetRate(1);p.loop=true;p.loopStart=.6;p.loopEnd=1.8;playback.Seek(.6);phaseStarted=AudioSettings.dspTime;}
                else if(phase==3&&AudioSettings.dspTime-phaseStarted>3.4){phase=4;p.loopStart=p.segments[2].startSeconds+.12;p.loopEnd=p.segments[2].startSeconds+.85;playback.Seek(p.loopStart);phaseStarted=AudioSettings.dspTime;}
                else if(phase==4&&AudioSettings.dspTime-phaseStarted>3.4){p.loop=false;p.loopStart=0;p.loopEnd=p.analysis.durationSeconds;playback.SetRate(1);playback.Seek(0);Finish(true);}
            }
            catch(Exception e){report.errors.Add(e.ToString());Finish(false);}
        }
        static void CheckCollision(Animator animator)
        {
            if(bodyAnimator!=animator)
            {
                body?.Dispose();bodyAnimator=animator;body=new SkinnedBodySurface(animator.transform.root.gameObject,animator);skins=animator.transform.root.GetComponentsInChildren<SkinnedMeshRenderer>();handVertices=new List<int[]>();
                var bones=new HashSet<Transform>(animator.GetBoneTransform(HumanBodyBones.LeftHand).GetComponentsInChildren<Transform>().Concat(animator.GetBoneTransform(HumanBodyBones.RightHand).GetComponentsInChildren<Transform>()));bones.Add(animator.GetBoneTransform(HumanBodyBones.LeftLowerArm));bones.Add(animator.GetBoneTransform(HumanBodyBones.RightLowerArm));
                foreach(var skin in skins){var ids=new HashSet<int>(Enumerable.Range(0,skin.bones.Length).Where(i=>bones.Contains(skin.bones[i])));var w=skin.sharedMesh.boneWeights;handVertices.Add(Enumerable.Range(0,w.Length).Where(i=>(ids.Contains(w[i].boneIndex0)?w[i].weight0:0)+(ids.Contains(w[i].boneIndex1)?w[i].weight1:0)+(ids.Contains(w[i].boneIndex2)?w[i].weight2:0)+(ids.Contains(w[i].boneIndex3)?w[i].weight3:0)>=.75f).ToArray());}
            }
            body.Update();bool bad=false;
            for(int j=0;j<skins.Length;j++){skins[j].BakeMesh(mesh,true);mesh.GetVertices(vertices);var matrix=skins[j].transform.localToWorldMatrix;for(int i=0;i<handVertices[j].Length;i+=8){var contact=body.Interior(matrix.MultiplyPoint3x4(vertices[handVertices[j][i]]));report.collisionPoints++;if(contact.found){report.worstPenetration=Mathf.Min(report.worstPenetration,contact.distance);if(contact.distance<-.002f)bad=true;}}}
            report.collisionFrames++;if(bad)report.collisionBadFrames++;
        }
        static void Finish(bool completed)
        {
            EditorApplication.update-=Update;Application.logMessageReceived-=Log;
            if(report.collisionBadFrames>0)report.errors.Add("Live hand/forearm skin penetrated the body in "+report.collisionBadFrames+" sampled frames.");
            if(completed&&(report.loopWraps<5||report.loopHandoffs<5))report.errors.Add("Insufficient interior/transition music loop wraps and handoffs observed.");if(report.maxLoopWrapHandStep>.08f)report.errors.Add("Music loop wrap introduced a hand jump larger than 8 cm.");if(report.maxLoopHandoffHandStep>.12f)report.errors.Add("Music loop handoff introduced a hand jump larger than 12 cm.");
            body?.Dispose();body=null;bodyAnimator=null;
            report.completed=completed;string output=Choreographer.ResolveLocal(GeneratedAssetBuilder.Root,plan.output);Directory.CreateDirectory(Path.GetDirectoryName(output));File.WriteAllText(output,JsonUtility.ToJson(report,true));
            Debug.Log("PRACTICE PLAY MODE VERIFICATION: completed="+completed+", samples="+report.samples.Count+", errors="+report.errors.Count);
            report=null;if(mesh)UnityEngine.Object.DestroyImmediate(mesh);mesh=null;
        }
    }
}
