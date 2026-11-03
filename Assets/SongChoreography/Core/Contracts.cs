using System;
using System.Collections.Generic;

namespace SongChoreography
{
    [Serializable] public class AnalysisRequest
    {
        public int schemaVersion=1;
        public string sourcePath="",outputDir="";
        public double startSeconds,durationSeconds=40;
    }
    [Serializable] public class AnalysisProgress
    {
        public int schemaVersion=1;
        public float progress;
        public string message="",state="running";
    }
    [Serializable] public class MusicAnalysis
    {
        public int schemaVersion=1,sampleRate=44100;
        public string sourceName="",normalAudio="normal.wav",slow075Audio="slow075.wav",slow050Audio="slow050.wav";
        public double sourceStartSeconds,sourceDurationSeconds,durationSeconds,bpm=120;
        public bool needsManualGrid;
        public List<double> beats=new List<double>();
        public List<float> energy=new List<float>();
        public List<string> warnings=new List<string>();
    }
    [Serializable] public class GridSettings
    {
        public bool manual;
        public double bpm=120,firstBeatSeconds;
        public int eightCountOffset;
    }
    [Serializable] public class CharacterDefinition
    {
        public string id="",name="",signature="",provider="tripo",sourceFile="",sha256="",requestId="",model="",prompt="",termsUrl="";
        public float height=1.7f;
    }
    [Serializable] public class MotionDefinition
    {
        public string id="",name="",signature="",provider="fal",sourceFile="",sha256="",requestId="",model="",prompt="",termsUrl="";
        public string sourceUrl="",clipName="";
        public string[] tags=new string[0];
        public int beats=8,seed;
        public float energy=.5f;
        public double sourceBpm=120,clipStart,clipEnd;
        // Nine confirmed animation times for an eight-count phrase. Uniform
        // times are only a draft until reviewed on the actual target character.
        public List<double> beatTimes=new List<double>();
        public List<string> reviewedCharacters=new List<string>();
    }
    [Serializable] public class TransitionDefinition
    {
        public string from="",to="",characterId="";
        public float blendBeats=.5f;
    }
    [Serializable] public class AssetCatalog
    {
        public int schemaVersion=2;
        public List<CharacterDefinition> characters=new List<CharacterDefinition>();
        public List<MotionDefinition> motions=new List<MotionDefinition>();
        public List<TransitionDefinition> transitions=new List<TransitionDefinition>();
    }
    [Serializable] public class MotionLibraryPreferences
    {
        public int schemaVersion=1;
        public bool customSelection;
        public List<string> motionIds=new List<string>();
    }
    [Serializable] public class DanceSegment
    {
        public string motionId="",motionSignature="";
        public double startSeconds,endSeconds,beatCount;
        public int startBeatOffset;
        public bool locked;
    }
    [Serializable] public class CameraSettings
    {
        public float yaw=25,pitch=10,distanceScale=1,panX,panY,fieldOfView=40;
        public bool dragPans;
    }
    [Serializable] public class DanceProject
    {
        public int schemaVersion=2,seed=42;
        public string title="新编舞",characterId="",characterSignature="";
        public MusicAnalysis analysis=new MusicAnalysis();
        public GridSettings grid=new GridSettings();
        public List<DanceSegment> segments=new List<DanceSegment>();
        public double position,loopStart,loopEnd;
        public float speed=1;
        public bool loop,mirror;
        public int view;
        public CameraSettings camera=new CameraSettings();
    }
}
