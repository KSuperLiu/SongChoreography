using System;
using System.Linq;
using UnityEngine;

namespace SongChoreography
{
    public sealed class DancePlayback : MonoBehaviour
    {
        AudioSource audioSource;
        AudioClip normal,slow075,slow050;
        MotionPlayer player;
        GeneratedLibrary library;
        DanceProject project;
        double anchorDsp,anchorSong;
        AnimationClip loopOutgoing;double loopOutgoingTime;float loopBlendRemaining,loopBlendDuration;
        int loopTargetSegment=-1;
        public bool Playing{get;private set;}
        public bool BlendingLoop=>loopBlendRemaining>0;
        public DanceProject Project=>project;
        public void Bind(GameObject character,GeneratedLibrary assets)
        {Pause();loopBlendRemaining=0;loopTargetSegment=-1;player?.Dispose();player=new MotionPlayer(character);library=assets;if(!audioSource){audioSource=gameObject.AddComponent<AudioSource>();audioSource.playOnAwake=false;}}
        public void SetProject(DanceProject value,AudioClip a,AudioClip b,AudioClip c)
        {Pause();loopBlendRemaining=0;loopTargetSegment=-1;ReleaseAudio();project=value;normal=a;slow075=b;slow050=c;Pose();}
        AudioClip Clip=>project.speed==.5f?slow050:project.speed==.75f?slow075:normal;
        public void Play()
        {
            if(project==null||!Clip||Playing||project.segments.Count==0)return;
            if(project.position>=project.analysis.durationSeconds-.001)project.position=project.loop?project.loopStart:0;
            if(project.loop&&(project.position<project.loopStart||project.position>=project.loopEnd))project.position=project.loopStart;
            audioSource.clip=Clip;audioSource.pitch=1;
            audioSource.timeSamples=Math.Min(Clip.samples-1,Math.Max(0,(int)Math.Round(project.position/project.speed*Clip.frequency)));
            anchorSong=project.position;anchorDsp=AudioSettings.dspTime+.05;audioSource.PlayScheduled(anchorDsp);Playing=true;
        }
        public void Pause(){if(audioSource)audioSource.Stop();Playing=false;}
        public void Seek(double time)
        {Seek(time,false);}
        void Seek(double time,bool preserveLoopBlend)
        {if(project==null)return;if(!preserveLoopBlend){loopBlendRemaining=0;loopTargetSegment=-1;}bool playing=Playing;Pause();project.position=Math.Max(0,Math.Min(project.analysis.durationSeconds,time));Pose();if(playing)Play();}
        void WrapLoop(double next)
        {
            double tail=Math.Max(project.loopStart,project.loopEnd-.00001);var segment=project.segments[SegmentAt(tail)];var motion=library.catalog.motions.First(m=>m.id==segment.motionId);loopTargetSegment=SegmentAt(next);
            loopOutgoing=library.Motion(motion.id).clip;loopOutgoingTime=Choreographer.MotionTime(motion,segment,tail);loopBlendDuration=Mathf.Min(.18f,(float)(project.loopEnd-project.loopStart)/project.speed*.25f);loopBlendRemaining=loopBlendDuration;Seek(next,true);
        }
        public void SetRate(float rate)
        {if(project==null)return;if(rate!=1&&rate!=.75f&&rate!=.5f)throw new ArgumentException("播放速度无效。");bool playing=Playing;Pause();project.speed=rate;if(playing)Play();}
        void Update()
        {
            if(project==null)return;
            if(Playing)
            {
                project.position=anchorSong+Math.Max(0,AudioSettings.dspTime-anchorDsp)*project.speed;
                double end=project.loop?project.loopEnd:project.analysis.durationSeconds;
                if(project.position>=end){if(project.loop)WrapLoop(project.loopStart+(project.position-end)%(project.loopEnd-project.loopStart));else{project.position=end;Pause();}}
                if(loopBlendRemaining>0&&AudioSettings.dspTime>=anchorDsp)loopBlendRemaining=Mathf.Max(0,loopBlendRemaining-Time.unscaledDeltaTime);
            }
            Pose();
        }
        public int ActiveSegment()
        {return project==null?-1:SegmentAt(project.position);}
        int SegmentAt(double time)
        {if(project.segments.Count==0)return -1;if(time<project.segments[0].startSeconds)return 0;for(int i=0;i<project.segments.Count;i++)if(time>=project.segments[i].startSeconds&&time<project.segments[i].endSeconds)return i;return project.segments.Count-1;}
        void Pose()
        {
            if(player==null||project==null||project.segments.Count==0)return;
            int index=ActiveSegment();var segment=project.segments[index];
            if(loopTargetSegment>=0&&index!=loopTargetSegment)loopTargetSegment=-1;
            var definition=library.catalog.motions.First(m=>m.id==segment.motionId);var clip=library.Motion(definition.id).clip;
            double time=Choreographer.MotionTime(definition,segment,project.position);
            if(loopBlendRemaining>0&&loopOutgoing){player.Sample(clip,time,loopOutgoing,loopOutgoingTime,Mathf.SmoothStep(0,1,1-loopBlendRemaining/loopBlendDuration));return;}
            // A wrap already supplies the outgoing pose. Do not reintroduce
            // the song's previous segment when this short blend finishes.
            if(index>0&&index!=loopTargetSegment)
            {
                var previous=project.segments[index-1];var edge=Choreographer.Edge(library.catalog,project.characterId,previous.motionId,definition.id);
                double duration=Math.Min((segment.endSeconds-segment.startSeconds)*.25,(segment.endSeconds-segment.startSeconds)/segment.beatCount*(edge?.blendBeats??.5));
                if(project.position<segment.startSeconds+duration)
                {
                    var outgoing=library.catalog.motions.First(m=>m.id==previous.motionId);
                    float weight=Mathf.SmoothStep(0,1,(float)((project.position-segment.startSeconds)/duration));
                    player.Sample(clip,time,library.Motion(previous.motionId).clip,Choreographer.MotionTime(outgoing,previous,previous.endSeconds),weight);return;
                }
            }
            player.Sample(clip,time);
        }
        void ReleaseAudio(){foreach(var clip in new[]{normal,slow075,slow050})if(clip)Destroy(clip);normal=slow075=slow050=null;}
        void OnDestroy(){Pause();player?.Dispose();ReleaseAudio();}
    }
}
