using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

namespace SongChoreography
{
    public sealed class DancePlayback : MonoBehaviour
    {
        AudioSource source;
        PlayableGraph graph;
        AnimationMixerPlayable mixer;
        AnimationPlayableOutput output;
        readonly Dictionary<string,AnimationClip> clips=new Dictionary<string,AnimationClip>();
        AnimationClipPlayable first,second;
        string firstId,secondId;
        DanceProject project;
        MotionCatalog catalog;
        readonly Dictionary<float,AudioClip> audioClips=new Dictionary<float,AudioClip>();
        double anchorDsp,anchorSong,position;
        bool playing;
        float rate=1;
        Transform visualRoot;
        Vector3 rootOrigin;
        SkinnedMeshRenderer skin;
        Mesh posedSkin;
        readonly List<Vector3> posedVertices=new List<Vector3>();
        int[] soleVertices;
        public bool IsPlaying=>playing;
        public double Position=>position;
        public double DspAnchor=>anchorDsp;
        public double SongAnchor=>anchorSong;
        public double AudioErrorMilliseconds=> source && playing && AudioSettings.dspTime>anchorDsp && source.isPlaying ? Math.Abs(position-source.timeSamples/(double)source.clip.frequency*rate)*1000 : 0;
        public bool Ready=>project!=null && audioClips.Count==3;
        public void Initialize(Animator animator,MotionCatalog motionCatalog)
        {
            catalog=motionCatalog;
            if(!animator.avatar || !animator.avatar.isValid || !animator.avatar.isHuman) throw new InvalidOperationException("角色 Humanoid Avatar 配置无效。");
            source=gameObject.AddComponent<AudioSource>();source.playOnAwake=false;source.spatialBlend=0;
            graph=PlayableGraph.Create("Music-clocked Dance");graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
            mixer=AnimationMixerPlayable.Create(graph,2);
            output=AnimationPlayableOutput.Create(graph,"Humanoid",animator);output.SetSourcePlayable(mixer);graph.Play();
            BindGrounding(animator);
            foreach(var m in catalog.motions) { var clip=Resources.Load<AnimationClip>(m.resource);if(clip) clips[m.id]=clip; }
            var idle=catalog.motions.Find(m=>m.style=="diagnostic"&&clips.ContainsKey(m.id));
            if(idle!=null){Channel(0,idle.id,0,1);graph.Evaluate(0);}
        }
        public bool HasMotion(string id)=>clips.ContainsKey(id);
        public void Retarget(Animator animator)
        {
            if(!animator.avatar||!animator.avatar.isValid||!animator.avatar.isHuman)throw new InvalidOperationException("角色骨架不能播放人形动作。");
            if(visualRoot)visualRoot.localPosition=rootOrigin;
            output.SetTarget(animator);BindGrounding(animator);
            if(project!=null)EvaluatePose();else graph.Evaluate(0);
        }
        void BindGrounding(Animator animator)
        {
            visualRoot=animator.transform.parent?animator.transform.parent:animator.transform;rootOrigin=visualRoot.localPosition;
            skin=animator.GetComponentsInChildren<SkinnedMeshRenderer>().OrderByDescending(r=>r.sharedMesh.vertexCount).First();
            if(posedSkin)Destroy(posedSkin);posedSkin=new Mesh();posedSkin.MarkDynamic();
            var feet=new[]{HumanBodyBones.LeftFoot,HumanBodyBones.RightFoot}.Select(b=>animator.GetBoneTransform(b)).ToArray();
            // Resolve hierarchy membership once per bone, not eight native
            // Transform comparisons for every one of ~7,300 mesh vertices.
            var footBones=skin.bones.Select(bone=>feet.Any(root=>bone==root||bone.IsChildOf(root))).ToArray();
            var weights=skin.sharedMesh.boneWeights;
            soleVertices=Enumerable.Range(0,weights.Length).Where(i=>
            {
                var w=weights[i];
                return (footBones[w.boneIndex0]?w.weight0:0)+(footBones[w.boneIndex1]?w.weight1:0)+(footBones[w.boneIndex2]?w.weight2:0)+(footBones[w.boneIndex3]?w.weight3:0)>.8f;
            }).ToArray();
        }
        static float SupportHeight(MotionDefinition definition,double time)
        {
            if(!definition.groundToFloor)return 0;
            var heights=definition.supportHeights;
            if(heights==null||heights.Count==0)return .01f;
            float at=Mathf.Clamp((float)(time*definition.supportFps),0,heights.Count-1);int lower=Mathf.FloorToInt(at);
            return Mathf.Lerp(heights[lower],heights[Math.Min(lower+1,heights.Count-1)],at-lower);
        }
        void GroundPose(float height)
        {
            skin.BakeMesh(posedSkin);posedSkin.GetVertices(posedVertices);float sole=float.PositiveInfinity;
            foreach(int index in soleVertices)sole=Mathf.Min(sole,skin.transform.TransformPoint(posedVertices[index]).y);
            if(!float.IsInfinity(sole))visualRoot.position+=Vector3.up*(height-sole);
        }
        public void SetProject(DanceProject data,AudioClip normal,AudioClip slow075,AudioClip slow050)
        {
            Pause();foreach(var old in audioClips.Values) if(old) Destroy(old);audioClips.Clear();
            project=data;audioClips[1]=normal;audioClips[.75f]=slow075;audioClips[.5f]=slow050;
            rate=project.speed;position=project.position;source.clip=audioClips[rate];EvaluatePose();
        }
        public void Play()
        {
            if(!Ready || playing) return;
            if(position>=project.analysis.durationSeconds-.01)position=project.loop?project.loopStart:0;
            if(project.loop && (position<project.loopStart || position>=project.loopEnd))position=project.loopStart;
            source.clip=audioClips[rate];source.pitch=1;
            source.timeSamples=Math.Min(source.clip.samples-1,Math.Max(0,(int)Math.Round(position/rate*source.clip.frequency)));
            anchorSong=position;anchorDsp=AudioSettings.dspTime+.05;source.PlayScheduled(anchorDsp);playing=true;
        }
        public void Pause() { if(source)source.Stop();playing=false; }
        public void Seek(double time)
        {
            bool resume=playing;Pause();position=Math.Max(0,Math.Min(project?.analysis.durationSeconds??0,time));
            if(project!=null)project.position=position;EvaluatePose();if(resume)Play();
        }
        public void SetRate(float value)
        { if(!Ready)return;bool resume=playing;Pause();rate=value;project.speed=rate;source.clip=audioClips[rate];if(resume)Play(); }
        void Update()
        {
            if(!Ready)return;
            if(playing)
            {
                position=anchorSong+Math.Max(0,AudioSettings.dspTime-anchorDsp)*rate;
                double end=project.loop?project.loopEnd:project.analysis.durationSeconds;
                if(position>=end)
                {
                    if(project.loop)Seek(project.loopStart+(position-end)%(project.loopEnd-project.loopStart));
                    else { position=project.analysis.durationSeconds;Pause(); }
                }
            }
            project.position=position;EvaluatePose();
        }
        public int ActiveSegment()
        {
            if(project==null)return -1;
            for(int i=0;i<project.segments.Count;i++)if(position>=project.segments[i].startSeconds && position<project.segments[i].endSeconds)return i;
            return -1;
        }
        void Channel(int channel,string id,double time,float weight)
        {
            if(!clips.TryGetValue(id,out var clip)){mixer.SetInputWeight(channel,0);return;}
            string current=channel==0?firstId:secondId;
            if(current!=id)
            {
                var old=channel==0?first:second;
                if(old.IsValid()){mixer.DisconnectInput(channel);graph.DestroyPlayable(old);}
                var definition=catalog.motions.Find(m=>m.id==id);
                var playable=AnimationClipPlayable.Create(graph,clip);
                // Authored muscle-only fixtures have no imported foot-goal curves.
                // Enabling clip foot IK on them would pull the feet toward the body.
                playable.SetApplyFootIK(definition!=null&&definition.footIK);playable.SetApplyPlayableIK(false);
                playable.SetSpeed(0);graph.Connect(playable,0,mixer,channel);
                if(channel==0){first=playable;firstId=id;}else{second=playable;secondId=id;}
            }
            var p=channel==0?first:second;p.SetTime(Math.Max(0,Math.Min(clip.length-.0001,time)));mixer.SetInputWeight(channel,weight);
        }
        void EvaluatePose()
        {
            if(!graph.IsValid()||project==null)return;
            if(visualRoot)visualRoot.localPosition=rootOrigin;
            int index=ActiveSegment();
            if(index<0)
            {
                if(project.segments.Count==0){mixer.SetInputWeight(0,0);mixer.SetInputWeight(1,0);graph.Evaluate(0);return;}
                index=position<project.segments[0].startSeconds?0:project.segments.Count-1;
            }
            var s=project.segments[index];
            var def=catalog.motions.Find(m=>m.id==s.motionId);
            if(def==null||!clips.TryGetValue(def.id,out var clip))return;
            double phase=Choreographer.MotionCycles(s,def.beats,position);
            double time=(phase-Math.Floor(phase))*clip.length;
            double blend=Math.Min(.45,Math.Min((s.endSeconds-s.startSeconds)/s.beatCount*def.transitionBeats,(s.endSeconds-s.startSeconds)*.25));
            float alpha=Mathf.Clamp01((float)((position-s.startSeconds)/blend));
            float weight=index==0?1:alpha*alpha*(3-2*alpha);
            Channel(0,s.motionId,time,weight);mixer.SetInputWeight(1,0);
            float supportHeight=SupportHeight(def,time);bool ground=def.groundToFloor;
            if(index>0&&weight<1)
            {
                var prev=project.segments[index-1];var pd=catalog.motions.Find(m=>m.id==prev.motionId);
                if(pd!=null&&clips.TryGetValue(pd.id,out var pc))
                {
                    double endCycles=Choreographer.MotionCycles(prev,pd.beats,prev.endSeconds);
                    double endpoint=endCycles-Math.Floor(endCycles);
                    if(endpoint<.001)endpoint=1;
                    double previousTime=endpoint*pc.length-.0001;
                    // Continue an outgoing loop-approved motion during the blend;
                    // holding its final pose would introduce a velocity stop.
                    if(Choreographer.CanFollow(pd,pd))
                    {
                        double cycles=endCycles+(position-s.startSeconds)*prev.beatCount/pd.beats/(prev.endSeconds-prev.startSeconds);
                        previousTime=(cycles-Math.Floor(cycles))*pc.length;
                    }
                    Channel(1,prev.motionId,previousTime,1-weight);
                    supportHeight=Mathf.Lerp(SupportHeight(pd,previousTime),supportHeight,weight);ground|=pd.groundToFloor;
                }
            }
            graph.Evaluate(0);
            if(ground)GroundPose(supportHeight);
        }
        void OnDestroy()
            { Pause();if(graph.IsValid())graph.Destroy();if(posedSkin)Destroy(posedSkin);foreach(var clip in audioClips.Values)if(clip)Destroy(clip); }
    }
}
