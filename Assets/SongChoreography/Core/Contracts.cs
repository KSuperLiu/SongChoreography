using System;
using System.Collections.Generic;

namespace SongChoreography
{
    [Serializable] public class BuiltinTrack
    {
        public string id="",title="",artist="",genre="",sourceUrl="",license="",licenseUrl="",sha256="";
        public double publishedBpm,excerptStart,excerptDuration;
    }
    [Serializable] public class BuiltinMusicLibrary
    {
        public int schemaVersion=1;
        public List<BuiltinTrack> tracks=new List<BuiltinTrack>();
    }
    [Serializable] public class AnalysisRequest
    {
        public int schemaVersion = 1;
        public string sourcePath, outputDir;
        public double startSeconds, durationSeconds = 40;
    }
    [Serializable] public class AnalysisProgress
    {
        public int schemaVersion;
        public float progress;
        public string message, state;
    }
    [Serializable] public class MusicAnalysis
    {
        public int schemaVersion = 1, sampleRate = 44100;
        public string sourceName = "", normalAudio = "normal.wav", slow075Audio = "slow075.wav", slow050Audio = "slow050.wav";
        public double sourceStartSeconds, sourceDurationSeconds, durationSeconds, bpm = 120;
        public bool needsManualGrid;
        public List<double> beats = new List<double>();
        public List<float> energy = new List<float>();
        public List<string> warnings = new List<string>();
    }
    [Serializable] public class GridSettings
    {
        public bool manual;
        public double bpm = 120, firstBeatSeconds;
        // Count of the first detected beat, zero-based. It sets the initial
        // partial phrase and animation phase without shifting the audio.
        public int eightCountOffset;
    }
    [Serializable] public class MotionDefinition
    {
        public string id = "", name = "", resource = "", style = "diagnostic";
        public int beats = 8, difficulty = 1;
        public float energy = .5f;
        public float transitionBeats=.25f;
        public bool approved;
        public bool personalUseOnly;
        public bool localRigMotion;
        public string sourceNext = "";
        public bool footIK;
        public bool groundToFloor;
        public int supportFps=30;
        public List<float> supportHeights=new List<float>();
        public string source = "", license = "", reviewer = "", reviewNotes = "";
        public List<string> safeNext = new List<string>();
    }
    [Serializable] public class MotionCatalog
    {
        public int schemaVersion = 1;
        public bool personalUseOnly;
        public string sourceNotice = "";
        public List<MotionDefinition> motions = new List<MotionDefinition>();
    }
    [Serializable] public class DanceSegment
    {
        public string motionId = "";
        public double startSeconds, endSeconds;
        public float beatCount;
        public int startBeatOffset;
        public bool locked;
    }
    [Serializable] public class DanceProject
    {
        public int schemaVersion = 1, seed = 42;
        public string title = "未命名编舞", mode = "diagnostic";
        public MusicAnalysis analysis = new MusicAnalysis();
        public GridSettings grid = new GridSettings();
        public List<DanceSegment> segments = new List<DanceSegment>();
        public double position, loopStart, loopEnd;
        public float speed = 1;
        public bool loop, mirror;
        public int view;
        public string character = "female";
        public CameraSettings camera = new CameraSettings();
    }
    [Serializable] public class CameraSettings
    {
        public float yaw=25, pitch=10, distanceScale=1, panX, panY, fieldOfView=40;
        public bool dragPans;
    }
}
