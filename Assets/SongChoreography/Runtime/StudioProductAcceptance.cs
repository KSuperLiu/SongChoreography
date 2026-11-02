using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace SongChoreography
{
    public sealed partial class StudioApp
    {
        [Serializable]class ProductReport
        {
            public string version=Application.version,graphicsDevice,style;
            public int width,height,frames,renderedFrames,segments,uniqueMotions,loopSamples;
            public float p95FrameMs,p99FrameMs,meanFrameMs,testSeconds;
            public double maxAudioClockErrorMs;
            public bool allPassed;
            public List<AcceptanceCase> checks=new List<AcceptanceCase>();
        }
        ProductReport productReport;
        readonly List<float> productFrameTimes=new List<float>();
        void ProductCheck(string name,bool pass,string detail="")
        {
            productReport.checks.Add(new AcceptanceCase{name=name,passed=pass,detail=detail});
            Debug.Log((pass?"PRODUCT_PASS ":"PRODUCT_FAIL ")+name+" "+detail);
        }
        IEnumerator ProductAcceptance()
        {
            QualitySettings.vSyncCount=0;Application.targetFrameRate=60;
            Screen.SetResolution(1920,1080,FullScreenMode.FullScreenWindow);
            for(int frame=0;frame<5;frame++)yield return null;
            productReport=new ProductReport{graphicsDevice=SystemInfo.graphicsDeviceName,width=Screen.width,height=Screen.height};
            float began=Time.realtimeSinceStartup;int firstRendered=renderedFrames;
            var iterator=ProductScenarios();
            while(true)
            {
                bool more=false;object current=null;
                try{more=iterator.MoveNext();if(more)current=iterator.Current;}
                catch(Exception ex){ProductCheck("Unhandled product exception",false,ex.ToString());break;}
                if(!more)break;yield return current;
            }
            (iterator as IDisposable)?.Dispose();
            productReport.frames=productFrameTimes.Count;productReport.renderedFrames=renderedFrames-firstRendered;
            productReport.testSeconds=Time.realtimeSinceStartup-began;
            if(productFrameTimes.Count>0)
            {
                productFrameTimes.Sort();productReport.p95FrameMs=productFrameTimes[(int)(productFrameTimes.Count*.95f)];
                productReport.p99FrameMs=productFrameTimes[(int)(productFrameTimes.Count*.99f)];productReport.meanFrameMs=productFrameTimes.Average();
            }
            productReport.width=Screen.width;productReport.height=Screen.height;
            if(!Environment.GetCommandLineArgs().Contains("-state-acceptance"))ProductCheck("1080p actual dance performance",Screen.width==1920&&Screen.height==1080&&productReport.frames>1000&&productReport.p95FrameMs<=33.34f&&productReport.meanFrameMs<=33.34f,$"{Screen.width}x{Screen.height}, mean={productReport.meanFrameMs:0.00} ms, p95={productReport.p95FrameMs:0.00} ms, frames={productReport.frames}, GPU={productReport.graphicsDevice}");
            ProductCheck("Software audio clock within 50 ms",productReport.maxAudioClockErrorMs<=50,"maximum="+productReport.maxAudioClockErrorMs+" ms; excludes speaker/device latency");
            productReport.allPassed=productReport.checks.All(c=>c.passed);
            string folder=Path.Combine(AppDirectory,"ProductAcceptance");Directory.CreateDirectory(folder);
            File.WriteAllText(Path.Combine(folder,"results.json"),JsonUtility.ToJson(productReport,true));
            yield return new WaitForEndOfFrame();ScreenCapture.CaptureScreenshot(Path.Combine(folder,"final.png"));
            yield return new WaitForSecondsRealtime(.5f);
            Screen.SetResolution(1600,900,FullScreenMode.Windowed);yield return null;
            Application.Quit(productReport.allPassed?0:3);
        }
        IEnumerator ProductScenarios()
        {
            yield return null;UseBuiltin(0);while(loading)yield return null;
            if(!Ready)throw new InvalidOperationException("Builtin demo failed: "+error);
            playback.Pause();productReport.style=project.mode;
            ProductCheck("Real accurately labelled dance library",project.mode==DanceStyles.Charleston&&Choreographer.Eligible(catalog,project.mode).Count>=8);
            Choreographer.ValidateProject(project,catalog);
            productReport.segments=project.segments.Count;productReport.uniqueMotions=project.segments.Select(s=>s.motionId).Distinct().Count();
            ProductCheck("Full 40 second varied arrangement",Math.Abs(project.segments.Last().endSeconds-40)<.01&&productReport.uniqueMotions>=4,"unique="+productReport.uniqueMotions);
            var stable=project.segments.Select(s=>s.motionId).ToArray();
            ProductCheck("Seed reproduces actual library arrangement",stable.SequenceEqual(Choreographer.Generate(project,catalog).Select(s=>s.motionId)));
            yield return PhraseAcceptance();
            project.segments[2].locked=true;string locked=project.segments[2].motionId;Generate(project.mode,true);
            ProductCheck("Real dance regeneration keeps lock",project.segments[2].locked&&project.segments[2].motionId==locked);
            project.segments[2].locked=false;
            int changed=0;
            for(int i=0;i<project.segments.Count;i++)
            {
                var replacement=Choreographer.Replacements(project,catalog,i).FirstOrDefault(m=>m.id!=project.segments[i].motionId);
                if(replacement==null)continue;
                double start=project.segments[i].startSeconds,end=project.segments[i].endSeconds;
                Choreographer.Replace(project,catalog,i,replacement.id);Choreographer.ValidateProject(project,catalog);
                if(project.segments[i].startSeconds==start&&project.segments[i].endSeconds==end)changed++;
            }
            ProductCheck("Real transitions permit useful replacements",changed>=3,"replaceable slots="+changed);
            // One complete 40-second presentation for each actual character.
            foreach(string character in Environment.GetCommandLineArgs().Contains("-state-acceptance")?new string[0]:new[]{"female","male"})
            {
                SwitchCharacter(character);ResetCamera();playback.SetRate(1);playback.Seek(0);playback.Play();
                float began=Time.realtimeSinceStartup;
                while(playback.IsPlaying&&Time.realtimeSinceStartup-began<43)
                {yield return null;ProductFrame();}
                ProductCheck(character+" full presentation reaches end",playback.Position>=39.99&&!playback.IsPlaying);
            }
            // Slow audio uses separate pitch-preserving files. Exercise repeated
            // seeks, pause/resume and looping on the same DSP-derived song clock.
            foreach(float speed in Environment.GetCommandLineArgs().Contains("-state-acceptance")?new float[0]:new[]{.5f,.75f,1f})
            {
                project.loop=true;project.loopStart=6;project.loopEnd=7.25;playback.SetRate(speed);playback.Seek(6.8);playback.Play();
                float began=Time.realtimeSinceStartup;double previous=playback.Position;int loops=0;
                while(Time.realtimeSinceStartup-began<8)
                {
                    yield return null;ProductFrame();if(playback.Position<previous)loops++;previous=playback.Position;
                }
                ProductCheck(speed+" speed repeated loops",loops>=2&&playback.Position>=6&&playback.Position<7.25,"loops="+loops);productReport.loopSamples+=loops;
                playback.Pause();double at=playback.Position;yield return new WaitForSecondsRealtime(.2f);
                ProductCheck(speed+" pause remains fixed",Math.Abs(playback.Position-at)<.00001);
                playback.Seek(13.2);project.loop=false;playback.Play();yield return new WaitForSecondsRealtime(.6f);
                ProductCheck(speed+" seek resume clock",playback.Position>13.2+speed*.4&&playback.Position<13.2+speed*.8);
                playback.Pause();
            }
            project.loop=false;project.loopStart=0;project.loopEnd=40;playback.SetRate(.75f);playback.Seek(12.75);project.mirror=true;ChangeView(1);
            project.segments[1].locked=true;Save();string expected=JsonUtility.ToJson(project);string path=Path.Combine(projectFolder,"project.danceproj");
            OpenProject(path);while(loading)yield return null;
            string actual=JsonUtility.ToJson(project);
            if(actual!=expected)
            {
                string folder=Path.Combine(AppDirectory,"ProductAcceptance");Directory.CreateDirectory(folder);
                File.WriteAllText(Path.Combine(folder,"state-expected.json"),expected);File.WriteAllText(Path.Combine(folder,"state-actual.json"),actual);
            }
            bool same=SameStoredValue(JsonUtility.FromJson<DanceProject>(expected),project,"project",out string difference);
            ProductCheck("Full edit/playback state survives reload",same&&!playback.IsPlaying,"all persisted fields compared (double tolerance 1 ns), paused="+!playback.IsPlaying+"; "+difference);
            ProductCheck("Project includes all local audio",new[]{project.analysis.normalAudio,project.analysis.slow075Audio,project.analysis.slow050Audio}.All(name=>File.Exists(Choreographer.ResolveLocal(projectFolder,name))));
            // Leave an explicit, reviewable example project inside the tested package.
            string demo=Path.Combine(AppDirectory,"DemoProject");Directory.CreateDirectory(demo);
            foreach(string file in Directory.GetFiles(projectFolder))if(!file.EndsWith(".tmp",StringComparison.OrdinalIgnoreCase))File.Copy(file,Path.Combine(demo,Path.GetFileName(file)),true);
            project.mirror=false;ResetCamera();playback.SetRate(1);playback.Seek(9.25);Save();
        }
        void ProductFrame()
        {
            productFrameTimes.Add(Time.unscaledDeltaTime*1000);
            productReport.maxAudioClockErrorMs=Math.Max(productReport.maxAudioClockErrorMs,playback.AudioErrorMilliseconds);
        }
        IEnumerator PhraseAcceptance()
        {
            var originalGrid=project.grid;var originalSegments=project.segments;
            project.grid=new GridSettings{manual=true,bpm=120,firstBeatSeconds=0,eightCountOffset=3};
            project.segments=Choreographer.Generate(project,catalog);var phrase=project.segments;
            ProductCheck("Count four changes actual phrase boundaries",phrase[0].startBeatOffset==3&&phrase[0].beatCount==5&&Math.Abs(phrase[0].endSeconds-2.5)<.0001&&phrase[1].startBeatOffset==0);
            var bones=new[]{HumanBodyBones.Hips,HumanBodyBones.Head,HumanBodyBones.LeftFoot,HumanBodyBones.RightFoot,HumanBodyBones.LeftHand,HumanBodyBones.RightHand}.Select(b=>diagnosticAnimator.GetBoneTransform(b)).ToArray();
            playback.Seek(0);var start=bones.Select(b=>b.position).ToArray();
            project.segments=new List<DanceSegment>{new DanceSegment{motionId=phrase[0].motionId,startSeconds=0,endSeconds=4,beatCount=8}};
            playback.Seek(1.5);float phaseGap=bones.Select((b,i)=>Vector3.Distance(b.position,start[i])).Max();
            ProductCheck("Leading phrase starts at the matching actual animation pose",phaseGap<.001f,"max position gap="+phaseGap+" metres");
            project.segments=phrase;playback.Seek(2.5-.00001);var before=bones.Select(b=>b.position).ToArray();playback.Seek(2.5);
            float cutGap=bones.Select((b,i)=>Vector3.Distance(b.position,before[i])).Max();
            ProductCheck("Partial phrase joins at the reviewed cycle boundary",cutGap<.003f,"cut gap="+cutGap+" metres");
            playback.Seek(0);Save();OpenProject(Path.Combine(projectFolder,"project.danceproj"));while(loading)yield return null;
            ProductCheck("Leading phrase survives real project reload",project.grid.eightCountOffset==3&&project.segments[0].startBeatOffset==3&&project.segments[0].beatCount==5);
            project.grid=originalGrid;project.segments=originalSegments;playback.Seek(0);RefreshFields();
        }
        static bool SameStoredValue(object expected,object actual,string path,out string difference)
        {
            difference="";
            if(expected==null||actual==null){if(expected==actual)return true;difference=path+" null mismatch";return false;}
            var type=expected.GetType();
            if(type!=actual.GetType()){difference=path+" type mismatch";return false;}
            if(type==typeof(double)||type==typeof(float))
            {
                double a=Convert.ToDouble(expected),b=Convert.ToDouble(actual);
                if(Choreographer.Finite(a)&&Choreographer.Finite(b)&&Math.Abs(a-b)<=(type==typeof(double)?1e-9:1e-6))return true;
                difference=path+": "+a+" -> "+b;return false;
            }
            if(type.IsPrimitive||type==typeof(string)||type.IsEnum)
            {if(expected.Equals(actual))return true;difference=path+": "+expected+" -> "+actual;return false;}
            if(expected is System.Collections.IEnumerable sequence)
            {
                var a=sequence.Cast<object>().ToArray();var b=((System.Collections.IEnumerable)actual).Cast<object>().ToArray();
                if(a.Length!=b.Length){difference=path+" length mismatch";return false;}
                for(int i=0;i<a.Length;i++)if(!SameStoredValue(a[i],b[i],path+"["+i+"]",out difference))return false;
                return true;
            }
            foreach(var field in type.GetFields(BindingFlags.Public|BindingFlags.Instance))
                if(!SameStoredValue(field.GetValue(expected),field.GetValue(actual),path+"."+field.Name,out difference))return false;
            return true;
        }
    }
}
