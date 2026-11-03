using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace SongChoreography.Editor
{
    // Incremental Editor audit: leaves the Editor responsive and checkpoints
    // completed clips. Dense mode checks actual hand/forearm skin vertices.
    public static class ArmCollisionAudit
    {
        [Serializable] public class Edge {public string from,to;public float blendBeats=1;public bool previewLoop;}
        [Serializable] public class Plan
        {public string[] motions=new string[0];public Edge[] edges=new Edge[0];public string output=".generation/arm-collision-audit";public bool correct=true,dense,fullSource=true,mapped,compareRaw,resume,guardBakedArms=true;public float bpm=100,fps=30,workBudgetMs=55;public int meshStride=1,surfaceEveryFrames=1;}
        [Serializable] public class ClipResult
        {
            public string id,name,sourceHash,worstPoint;public int frames,badFrames,pointsChecked;public float length,worstTime,worstClearance=1,maxWristCorrection,maxBoneLengthError;
            public double meanSampleMs,p95SampleMs,maxSampleMs;public int geometryFrames;public float maxAddedHandStep,maxHandStep,maxAddedStepTime,maxAddedElbowStep,maxElbowStep,maxAddedHandAcceleration,maxAddedElbowAcceleration,maxWristRotationError,maxLegPoseError,repeatPoseError,seekPoseError;
            public int continuityBadFrames;public float maxRawHandStep,maxRawElbowStep,maxHandAcceleration,maxRawHandAcceleration,maxElbowAcceleration,maxRawElbowAcceleration,maxExcessHandStep,maxExcessElbowStep,maxExcessHandAcceleration,maxExcessElbowAcceleration;
        }
        [Serializable] public class Report
        {public int playbackRevision;public string sourceCharacterSignature;public bool corrected,dense,fullSource,mapped,completed;public float bpm,fps;public int meshStride,surfaceEveryFrames,totalClips,totalFrames,processedFrames;public double totalSourceSeconds;public List<ClipResult> clips=new List<ClipResult>();public List<string> errors=new List<string>();}
        [Serializable] class Progress {public string state,motion;public int completedClips,totalClips,processedFrames,totalFrames;}
        sealed class MeshSamples {public SkinnedMeshRenderer skin;public int[] indices;}
        struct Probe {public Transform bone;public float radius;public string name;}
        static Plan plan;static Report report;static GeneratedLibrary library;static List<MotionDefinition> motions;static int index,frame,frameCount;
        static Scene scene;static GameObject actor,rawActor;static Animator animator,rawAnimator;static MotionPlayer player,rawPlayer;static SkinnedBodySurface body;static Mesh baked;
        static Vector3[] previousHands,previousRawHands,previousCorrectionStep,previousActualStep,previousSourceStep;
        static List<Probe> probes;static List<MeshSamples> meshSamples;static readonly List<Vector3> vertices=new List<Vector3>();static readonly List<double> timings=new List<double>();
        static ClipResult current;static string output;static float leftUpper,leftLower,rightUpper,rightLower;
        [MenuItem("Tools/CHOREO AI/Review/Start arm collision audit")]
        public static void Start()
        {
            if(report!=null)throw new InvalidOperationException("手臂复查正在运行。");if(EditorApplication.isPlaying)throw new InvalidOperationException("请先停止 Play Mode。");
            plan=JsonUtility.FromJson<Plan>(File.ReadAllText(".generation/arm-collision-audit-plan.json"));output=Choreographer.ResolveLocal(GeneratedAssetBuilder.Root,plan.output);Directory.CreateDirectory(output);
            library=AssetDatabase.LoadAssetAtPath<GeneratedLibrary>(GeneratedAssetBuilder.LibraryPath);motions=plan.edges.Length>0?plan.edges.Select(e=>library.catalog.motions.First(m=>m.id==e.to)).ToList():library.catalog.motions.Where(m=>plan.motions.Length==0||plan.motions.Contains(m.id)).ToList();
            report=new Report{playbackRevision=MotionPlayer.PlaybackRevision,sourceCharacterSignature=library.catalog.characters[0].signature,corrected=plan.correct,dense=plan.dense,fullSource=plan.fullSource,mapped=plan.mapped,bpm=plan.bpm,fps=plan.fps,meshStride=plan.meshStride,surfaceEveryFrames=Mathf.Max(1,plan.surfaceEveryFrames),totalClips=motions.Count};
            for(int i=0;i<motions.Count;i++){float length=Length(motions[i],i);report.totalSourceSeconds+=length;report.totalFrames+=Mathf.CeilToInt(length*plan.fps)+1;}
            index=0;
            if(plan.resume&&File.Exists(Path.Combine(output,"report.json")))
            {
                var prior=JsonUtility.FromJson<Report>(File.ReadAllText(Path.Combine(output,"report.json")));
                if(plan.edges.Length>0||prior.errors.Count>0||prior.sourceCharacterSignature!=report.sourceCharacterSignature||prior.playbackRevision!=report.playbackRevision||prior.totalClips!=report.totalClips||prior.totalFrames!=report.totalFrames||prior.fps!=report.fps||prior.meshStride!=report.meshStride||Mathf.Max(1,prior.surfaceEveryFrames)!=report.surfaceEveryFrames||prior.dense!=report.dense||prior.fullSource!=report.fullSource||prior.mapped!=report.mapped||prior.corrected!=report.corrected||prior.bpm!=report.bpm||!prior.clips.Select(c=>c.id+":"+c.sourceHash).SequenceEqual(motions.Take(prior.clips.Count).Select(m=>m.id+":"+m.sha256))){report=null;plan=null;throw new InvalidDataException("断点与当前人物、动作或检查设置不一致，请重新开始。");}
                report=prior;report.completed=false;index=prior.clips.Count;report.processedFrames=prior.clips.Sum(c=>c.frames);
            }
            File.WriteAllText(Path.Combine(output,"plan.json"),JsonUtility.ToJson(plan,true));SaveProgress("running");EditorApplication.update+=Pump;AssemblyReloadEvents.beforeAssemblyReload+=Cancel;UnityEngine.Debug.Log("ARM AUDIT START: "+motions.Count+" clips, "+report.totalFrames+" frames, corrected="+plan.correct+", dense="+plan.dense);
        }
        static float Length(MotionDefinition m,int i)=>plan.edges.Length>0?.3f+(plan.edges[i].previewLoop?.18f:60*plan.edges[i].blendBeats/plan.bpm)+.6f:plan.mapped?60*m.beats/plan.bpm:plan.fullSource?library.Motion(m.id).clip.length:(float)(m.clipEnd-m.clipStart);
        static float Length(Transform a,Transform b)=>Vector3.Distance(a.position,b.position);
        static Transform Bone(HumanBodyBones bone)=>animator.GetBoneTransform(bone);
        static void BeginClip()
        {
            var m=motions[index];scene=EditorSceneManager.NewPreviewScene();actor=(GameObject)PrefabUtility.InstantiatePrefab(library.characters[0].prefab,scene);animator=actor.GetComponentInChildren<Animator>();
            player=new MotionPlayer(actor,plan.correct,true,plan.guardBakedArms);body=new SkinnedBodySurface(actor,animator);baked=new Mesh();probes=new List<Probe>();meshSamples=new List<MeshSamples>();
            previousHands=previousRawHands=previousCorrectionStep=previousActualStep=previousSourceStep=null;if(plan.compareRaw){rawActor=(GameObject)PrefabUtility.InstantiatePrefab(library.characters[0].prefab,scene);rawAnimator=rawActor.GetComponentInChildren<Animator>();rawPlayer=new MotionPlayer(rawActor,false);}
            foreach(bool left in new[]{true,false})
            {
                string side=left?"left-":"right-";probes.Add(new Probe{bone=Bone(left?HumanBodyBones.LeftHand:HumanBodyBones.RightHand),radius=.018f,name=side+"wrist"});
                int first=(int)(left?HumanBodyBones.LeftThumbProximal:HumanBodyBones.RightThumbProximal);
                for(int i=first;i<first+15;i++){var bone=Bone((HumanBodyBones)i);if(bone)probes.Add(new Probe{bone=bone,radius=.008f,name=side+((HumanBodyBones)i)});}
            }
            if(plan.dense)
            {
                var handBones=new HashSet<Transform>(Bone(HumanBodyBones.LeftHand).GetComponentsInChildren<Transform>().Concat(Bone(HumanBodyBones.RightHand).GetComponentsInChildren<Transform>()));handBones.Add(Bone(HumanBodyBones.LeftLowerArm));handBones.Add(Bone(HumanBodyBones.RightLowerArm));
                foreach(var skin in actor.GetComponentsInChildren<SkinnedMeshRenderer>())
                {
                    var indices=new HashSet<int>(Enumerable.Range(0,skin.bones.Length).Where(i=>handBones.Contains(skin.bones[i])));var weights=skin.sharedMesh.boneWeights;var samples=new List<int>();
                    for(int i=0;i<weights.Length;i++){var w=weights[i];float sum=(indices.Contains(w.boneIndex0)?w.weight0:0)+(indices.Contains(w.boneIndex1)?w.weight1:0)+(indices.Contains(w.boneIndex2)?w.weight2:0)+(indices.Contains(w.boneIndex3)?w.weight3:0);if(sum>=.75f)samples.Add(i);}
                    meshSamples.Add(new MeshSamples{skin=skin,indices=samples.ToArray()});
                }
            }
            current=new ClipResult{id=plan.edges.Length>0?plan.edges[index].from+"--"+m.id:m.id,name=m.name,sourceHash=m.sha256,length=Length(m,index)};frame=0;frameCount=Mathf.CeilToInt(current.length*plan.fps);timings.Clear();
            leftUpper=Length(Bone(HumanBodyBones.LeftUpperArm),Bone(HumanBodyBones.LeftLowerArm));leftLower=Length(Bone(HumanBodyBones.LeftLowerArm),Bone(HumanBodyBones.LeftHand));rightUpper=Length(Bone(HumanBodyBones.RightUpperArm),Bone(HumanBodyBones.RightLowerArm));rightLower=Length(Bone(HumanBodyBones.RightLowerArm),Bone(HumanBodyBones.RightHand));
        }
        static bool TestPoint(Vector3 point,float radius,string name,float time)
        {
            var contact=radius==0?body.Interior(point):body.Query(point);current.pointsChecked++;if(!contact.found)return false;float clearance=contact.distance-radius;
            if(clearance<current.worstClearance){current.worstClearance=clearance;current.worstTime=time;current.worstPoint=name;}
            return clearance<-.002f;
        }
        static void SampleFrame()
        {
            var m=motions[index];float t=Mathf.Min(current.length,frame/plan.fps);double source=plan.mapped?Choreographer.MotionTime(m,new DanceSegment{startSeconds=0,endSeconds=current.length,beatCount=m.beats},t):t+(plan.fullSource?0:m.clipStart);
            long begin=Stopwatch.GetTimestamp();Sample(player,m,t,source);double ms=(Stopwatch.GetTimestamp()-begin)*1000d/Stopwatch.Frequency;timings.Add(ms);current.maxWristCorrection=Mathf.Max(current.maxWristCorrection,player.LastArmCorrection);
            var hands=Hands(animator);if(rawPlayer!=null)Sample(rawPlayer,m,t,source);var rawHands=rawAnimator?Hands(rawAnimator):null;
            if(previousHands!=null)for(int i=0;i<2;i++)
            {var step=hands[i]-previousHands[i];current.maxHandStep=Mathf.Max(current.maxHandStep,step.magnitude);if(rawHands!=null){float added=(step-(rawHands[i]-previousRawHands[i])).magnitude;if(added>current.maxAddedHandStep){current.maxAddedHandStep=added;current.maxAddedStepTime=t;}}}
            if(previousHands!=null)for(int i=2;i<4;i++){var step=hands[i]-previousHands[i];current.maxElbowStep=Mathf.Max(current.maxElbowStep,step.magnitude);if(rawHands!=null)current.maxAddedElbowStep=Mathf.Max(current.maxAddedElbowStep,(step-(rawHands[i]-previousRawHands[i])).magnitude);}
            if(rawHands!=null&&previousHands!=null)
            {
                var correctionStep=new Vector3[4];for(int i=0;i<4;i++){correctionStep[i]=(hands[i]-previousHands[i])-(rawHands[i]-previousRawHands[i]);if(previousCorrectionStep!=null){float acceleration=(correctionStep[i]-previousCorrectionStep[i]).magnitude;if(i<2)current.maxAddedHandAcceleration=Mathf.Max(current.maxAddedHandAcceleration,acceleration);else current.maxAddedElbowAcceleration=Mathf.Max(current.maxAddedElbowAcceleration,acceleration);}}
                previousCorrectionStep=correctionStep;
                bool pop=false;var actualSteps=new Vector3[4];var sourceSteps=new Vector3[4];
                for(int i=0;i<4;i++)
                {
                    actualSteps[i]=hands[i]-previousHands[i];sourceSteps[i]=rawHands[i]-previousRawHands[i];float speed=actualSteps[i].magnitude,rawSpeed=sourceSteps[i].magnitude,excess=Mathf.Max(0,speed-rawSpeed);bool hand=i<2;
                    if(hand){current.maxRawHandStep=Mathf.Max(current.maxRawHandStep,rawSpeed);current.maxExcessHandStep=Mathf.Max(current.maxExcessHandStep,excess);}else{current.maxRawElbowStep=Mathf.Max(current.maxRawElbowStep,rawSpeed);current.maxExcessElbowStep=Mathf.Max(current.maxExcessElbowStep,excess);}
                    pop|=speed>Mathf.Max(hand?.08f:.06f,rawSpeed*1.75f+.01f)&&excess>(hand?.05f:.04f);
                    if(previousActualStep!=null){float acceleration=(actualSteps[i]-previousActualStep[i]).magnitude,rawAcceleration=(sourceSteps[i]-previousSourceStep[i]).magnitude,extra=Mathf.Max(0,acceleration-rawAcceleration);if(hand){current.maxHandAcceleration=Mathf.Max(current.maxHandAcceleration,acceleration);current.maxRawHandAcceleration=Mathf.Max(current.maxRawHandAcceleration,rawAcceleration);current.maxExcessHandAcceleration=Mathf.Max(current.maxExcessHandAcceleration,extra);}else{current.maxElbowAcceleration=Mathf.Max(current.maxElbowAcceleration,acceleration);current.maxRawElbowAcceleration=Mathf.Max(current.maxRawElbowAcceleration,rawAcceleration);current.maxExcessElbowAcceleration=Mathf.Max(current.maxExcessElbowAcceleration,extra);}pop|=acceleration>(hand?.07f:.06f)&&extra>(hand?.05f:.04f);}
                }
                if(pop)current.continuityBadFrames++;previousActualStep=actualSteps;previousSourceStep=sourceSteps;
                var hips=animator.GetBoneTransform(HumanBodyBones.Hips).position;var rawHips=rawAnimator.GetBoneTransform(HumanBodyBones.Hips).position;
                foreach(var side in new[]{new[]{HumanBodyBones.LeftHand,HumanBodyBones.LeftFoot},new[]{HumanBodyBones.RightHand,HumanBodyBones.RightFoot}}){current.maxWristRotationError=Mathf.Max(current.maxWristRotationError,Quaternion.Angle(animator.GetBoneTransform(side[0]).rotation,rawAnimator.GetBoneTransform(side[0]).rotation));current.maxLegPoseError=Mathf.Max(current.maxLegPoseError,Vector3.Distance(animator.GetBoneTransform(side[1]).position-hips,rawAnimator.GetBoneTransform(side[1]).position-rawHips));}
            }
            previousHands=hands;previousRawHands=rawHands;
            if(frame%Mathf.Max(1,plan.surfaceEveryFrames)!=0&&frame!=frameCount){current.frames++;report.processedFrames++;frame++;return;}
            current.geometryFrames++;
            body.Update();bool bad=false;foreach(var p in probes)bad|=TestPoint(p.bone.position,plan.dense?0:p.radius,p.name,(float)source);
            foreach(bool left in new[]{true,false})
            {
                var shoulder=Bone(left?HumanBodyBones.LeftUpperArm:HumanBodyBones.RightUpperArm);var elbow=Bone(left?HumanBodyBones.LeftLowerArm:HumanBodyBones.RightLowerArm);var wrist=Bone(left?HumanBodyBones.LeftHand:HumanBodyBones.RightHand);
                foreach(float unit in new[]{.25f,.5f,.75f})bad|=TestPoint(Vector3.Lerp(elbow.position,wrist.position,unit),plan.dense?0:.022f,(left?"left-":"right-")+"forearm-"+unit,(float)source);
                bad|=TestPoint(elbow.position,plan.dense?0:.024f,(left?"left-":"right-")+"elbow",(float)source);
                current.maxBoneLengthError=Mathf.Max(current.maxBoneLengthError,Mathf.Abs(Length(shoulder,elbow)-(left?leftUpper:rightUpper)),Mathf.Abs(Length(elbow,wrist)-(left?leftLower:rightLower)));
            }
            if(plan.dense)foreach(var skin in meshSamples)
            {
                skin.skin.BakeMesh(baked,true);baked.GetVertices(vertices);var matrix=skin.skin.transform.localToWorldMatrix;
                for(int sample=0;sample<skin.indices.Length;sample+=Mathf.Max(1,plan.meshStride)){int i=skin.indices[sample];bad|=TestPoint(matrix.MultiplyPoint3x4(vertices[i]),0,"hand-forearm-skin-"+i,(float)source);}
            }
            if(bad)current.badFrames++;current.frames++;report.processedFrames++;frame++;
        }
        static Vector3[] Hands(Animator a)
        {var hips=a.GetBoneTransform(HumanBodyBones.Hips).position;return new[]{a.GetBoneTransform(HumanBodyBones.LeftHand).position-hips,a.GetBoneTransform(HumanBodyBones.RightHand).position-hips,a.GetBoneTransform(HumanBodyBones.LeftLowerArm).position-hips,a.GetBoneTransform(HumanBodyBones.RightLowerArm).position-hips};}
        static void Sample(MotionPlayer p,MotionDefinition m,float t,double source)
        {
            if(plan.edges.Length==0){p.Sample(library.Motion(m.id).clip,source);return;}
            var edge=plan.edges[index];var from=library.catalog.motions.First(a=>a.id==edge.from);float blend=edge.previewLoop?.18f:60*edge.blendBeats/plan.bpm;double incoming=t-.3;
            if(incoming<0){double tail=edge.previewLoop?from.clipEnd+incoming:Choreographer.MotionTime(from,new DanceSegment{startSeconds=0,endSeconds=60*from.beats/plan.bpm,beatCount=from.beats},60*from.beats/plan.bpm+incoming);p.Sample(library.Motion(from.id).clip,tail);return;}
            double time=edge.previewLoop?m.clipStart+incoming:Choreographer.MotionTime(m,new DanceSegment{startSeconds=0,endSeconds=60*m.beats/plan.bpm,beatCount=m.beats},incoming);
            if(incoming<blend)p.Sample(library.Motion(m.id).clip,time,library.Motion(from.id).clip,from.clipEnd,Mathf.SmoothStep(0,1,(float)(incoming/blend)));else p.Sample(library.Motion(m.id).clip,time);
        }
        static void VerifyDeterminism()
        {
            var m=motions[index];double source=plan.mapped?Choreographer.MotionTime(m,new DanceSegment{startSeconds=0,endSeconds=current.length,beatCount=m.beats},current.length):current.length+(plan.fullSource?0:m.clipStart);
            var first=Hands(animator);for(int repeat=0;repeat<3;repeat++){Sample(player,m,current.length,source);var pose=Hands(animator);for(int i=0;i<4;i++)current.repeatPoseError=Mathf.Max(current.repeatPoseError,Vector3.Distance(first[i],pose[i]));}
            var other=library.motions.FirstOrDefault(a=>a.clip!=library.Motion(m.id).clip);if(other!=null)player.Sample(other.clip,Mathf.Min(.37f,other.clip.length));Sample(player,m,current.length,source);var back=Hands(animator);for(int i=0;i<4;i++)current.seekPoseError=Mathf.Max(current.seekPoseError,Vector3.Distance(first[i],back[i]));
        }
        static void Pump()
        {
            if(report==null)return;long begin=Stopwatch.GetTimestamp();
            try
            {
                while((Stopwatch.GetTimestamp()-begin)/(double)Stopwatch.Frequency<Mathf.Clamp(plan.workBudgetMs,10,500)/1000d)
                {
                    if(index>=motions.Count){Finish();return;}
                    if(current==null)BeginClip();SampleFrame();
                    if(frame>frameCount)
                    {
                        VerifyDeterminism();
                        timings.Sort();current.meanSampleMs=timings.Average();current.maxSampleMs=timings.Last();current.p95SampleMs=timings[Math.Min(timings.Count-1,(int)(timings.Count*.95))];report.clips.Add(current);index++;EndClip();SaveProgress("running");
                    }
                }
            }
            catch(Exception e){report.errors.Add((index<motions.Count?motions[index].id:"")+": "+e);EndClip();index++;SaveProgress("running");}
            EditorApplication.QueuePlayerLoopUpdate();
        }
        static void EndClip(){player?.Dispose();player=null;rawPlayer?.Dispose();rawPlayer=null;rawAnimator=null;if(rawActor)UnityEngine.Object.DestroyImmediate(rawActor);rawActor=null;body?.Dispose();body=null;if(baked)UnityEngine.Object.DestroyImmediate(baked);baked=null;if(actor)UnityEngine.Object.DestroyImmediate(actor);actor=null;if(scene.IsValid())EditorSceneManager.ClosePreviewScene(scene);current=null;}
        static void SaveProgress(string state)
        {
            File.WriteAllText(Path.Combine(output,"report.json"),JsonUtility.ToJson(report,true));File.WriteAllText(Path.Combine(output,"progress.json"),JsonUtility.ToJson(new Progress{state=state,motion=index<motions.Count?motions[index].id:"",completedClips=index,totalClips=report.totalClips,processedFrames=report.processedFrames,totalFrames=report.totalFrames},true));
        }
        static void Finish()
        {
            EditorApplication.update-=Pump;AssemblyReloadEvents.beforeAssemblyReload-=Cancel;report.completed=report.errors.Count==0;SaveProgress(report.completed?"completed":"failed");UnityEngine.Debug.Log("ARM AUDIT FINISH: "+report.clips.Count+" clips, "+report.processedFrames+" frames, residual clips="+report.clips.Count(c=>c.badFrames>0)+", errors="+report.errors.Count);report=null;
        }
        [MenuItem("Tools/CHOREO AI/Review/Cancel arm collision audit")]
        public static void Cancel()
        {
            if(report==null)return;EditorApplication.update-=Pump;AssemblyReloadEvents.beforeAssemblyReload-=Cancel;EndClip();SaveProgress("interrupted");report=null;
        }
    }
}
