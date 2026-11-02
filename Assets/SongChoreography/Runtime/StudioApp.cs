using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.Networking;

namespace SongChoreography
{
    public sealed partial class StudioApp : MonoBehaviour
    {
        const float W=1600,H=900;
        DancePlayback playback;
        MotionCatalog catalog;
        BuiltinMusicLibrary musicLibrary=new BuiltinMusicLibrary();
        AudioSource audition;
        int auditionToken,inspectorTab,chosenTrack=-1;
        string auditionId="";
        bool showSettings,showCredits,autoPlayOnLoad,showAnalyzerAdvanced;
        DanceProject project;
        AnalyzerJob job;
        string projectFolder,sourcePath="",selectedSourceName="",startText="0",durationText="40",pythonPath="python";
        string bpmText="120",firstBeatText="0",countText="1",seedText="42",loopStartText="0",loopEndText="40";
        string message="从内置曲目开始体验，或选择自己的音乐。",error="";
        float nextPoll;
        bool loading,dirty,stylesReady;
        int renderedFrames;
        int selected=-1;
        Vector2 segmentsScroll,replacementsScroll;
        Camera stageCamera;
        PracticeCamera cameraRig;
        GameObject characterObject;
        string selectedCharacter="female";
        readonly CameraSettings initialCamera=new CameraSettings();
        int initialView, cameraDragButton=-1;
        CameraSettings CameraState=>project!=null?project.camera:initialCamera;
        int CameraView=>project!=null?project.view:initialView;
        Animator diagnosticAnimator;
        RenderTexture preview;
        GUIStyle label,small,title,heading,button,field,accentButton,muted,lockToggle,sliderTrack,sliderThumb;
        Texture2D white;
        Color ink=new Color(.88f,.92f,.96f),teal=new Color(.25f,.87f,.77f);
        bool Busy=>job!=null||loading;
        bool Ready=>project!=null&&playback.Ready;
        bool TestMode=>Environment.GetCommandLineArgs().Any(a=>a=="-smoke"||a=="-import-smoke"||a=="-portable-smoke"||a=="-library-smoke"||a=="-acceptance"||a=="-mocap-preview"||a=="-transition-review"||a=="-product-acceptance"||a=="-state-acceptance");
        string PreferredStyle=>Choreographer.Eligible(catalog,DanceStyles.NewJazz).Count>=8?DanceStyles.NewJazz:Choreographer.Eligible(catalog,DanceStyles.Charleston).Count>=8?DanceStyles.Charleston:DanceStyles.Diagnostic;
        string Workspace=>Path.Combine(Application.persistentDataPath,"Projects");
        string AppDirectory=>Path.GetDirectoryName(Application.dataPath);
        string AnalyzerPython=>AnalyzerRuntime.Resolve(AppDirectory,pythonPath,Environment.GetEnvironmentVariable("SONG_CHOREO_PYTHON"));
        bool BundledAnalyzer=>string.Equals(AnalyzerPython,AnalyzerRuntime.BundledPath(AppDirectory),StringComparison.OrdinalIgnoreCase);

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot(){if(!FindObjectOfType<StudioApp>())new GameObject("Choreography Studio").AddComponent<StudioApp>();}
        void Awake()
        {
            Application.runInBackground=true;
            initialCamera.fieldOfView=Mathf.Clamp(PlayerPrefs.GetFloat("CameraFieldOfView",40),30,60);
            pythonPath=PlayerPrefs.GetString("AnalyzerPython","");
            try
            {
                var asset=Resources.Load<TextAsset>("MotionCatalog");
                if(!asset)throw new InvalidOperationException("动作库尚未生成，请执行 Tools / Song Choreography / Prepare Prototype。");
                catalog=JsonUtility.FromJson<MotionCatalog>(asset.text);Choreographer.Eligible(catalog,"diagnostic");
                SetupStage();
                characterObject=DancerCharacters.Create(selectedCharacter);
                diagnosticAnimator=characterObject.GetComponentInChildren<Animator>();
                playback=gameObject.AddComponent<DancePlayback>();playback.Initialize(diagnosticAnimator,catalog);
                cameraRig=gameObject.AddComponent<PracticeCamera>();cameraRig.Initialize(stageCamera,characterObject.transform);UpdateCamera();
                foreach(var m in catalog.motions)if(!playback.HasMotion(m.id))throw new InvalidOperationException("动作资源缺失："+m.resource);
                audition=gameObject.AddComponent<AudioSource>();audition.playOnAwake=false;audition.spatialBlend=0;
                string libraryPath=Path.Combine(Application.streamingAssetsPath,"MusicLibrary","catalog.json");
                if(File.Exists(libraryPath))musicLibrary=JsonUtility.FromJson<BuiltinMusicLibrary>(File.ReadAllText(libraryPath));
                if(musicLibrary.schemaVersion!=1)throw new InvalidDataException("内置曲目版本无效。");
                if(Environment.GetCommandLineArgs().Contains("-mocap-preview"))StartCoroutine(MotionPreview());
                else if(Environment.GetCommandLineArgs().Contains("-transition-review"))StartCoroutine(TransitionReview());
                else if(Environment.GetCommandLineArgs().Contains("-product-acceptance")||Environment.GetCommandLineArgs().Contains("-state-acceptance"))StartCoroutine(ProductAcceptance());
                else if(Environment.GetCommandLineArgs().Contains("-smoke")||Environment.GetCommandLineArgs().Contains("-import-smoke")||Environment.GetCommandLineArgs().Contains("-portable-smoke")||Environment.GetCommandLineArgs().Contains("-library-smoke")||Environment.GetCommandLineArgs().Contains("-acceptance"))StartCoroutine(Smoke());
                else
                {
                    string last=PlayerPrefs.GetString("LastProject","");
                    if(File.Exists(last))Guard(()=>OpenProject(last));
                }
            }
            catch(Exception ex){Fail(ex);}
        }
        void SetupStage()
        {
            // A screen camera is needed for presentation, in addition to the offscreen stage camera.
            var screenCamera=new GameObject("Interface Camera").AddComponent<Camera>();
            screenCamera.cullingMask=0;screenCamera.depth=-10;screenCamera.clearFlags=CameraClearFlags.SolidColor;screenCamera.backgroundColor=new Color(.035f,.05f,.075f);
            RenderSettings.ambientLight=new Color(.55f,.62f,.7f);
            RenderSettings.ambientMode=UnityEngine.Rendering.AmbientMode.Flat;
            var lightObject=new GameObject("Studio Key");var light=lightObject.AddComponent<Light>();light.type=LightType.Directional;light.intensity=1.1f;light.color=new Color(1,.94f,.88f);light.shadows=LightShadows.Soft;lightObject.transform.rotation=Quaternion.Euler(35,155,0);
            var fill=new GameObject("Studio Fill").AddComponent<Light>();fill.type=LightType.Directional;fill.intensity=.55f;fill.color=new Color(.78f,.87f,1);fill.transform.rotation=Quaternion.Euler(15,-135,0);
            var rim=new GameObject("Studio Rim").AddComponent<Light>();rim.type=LightType.Directional;rim.intensity=.45f;rim.color=new Color(.55f,.78f,.90f);rim.transform.rotation=Quaternion.Euler(25,10,0);
            var floor=GameObject.CreatePrimitive(PrimitiveType.Plane);floor.name="Practice floor";floor.transform.localScale=Vector3.one*.7f;
            var mat=new Material(Shader.Find("Standard"));mat.color=new Color(.018f,.028f,.044f);mat.SetFloat("_Glossiness",.12f);floor.GetComponent<Renderer>().sharedMaterial=mat;
            Destroy(floor.GetComponent<Collider>());
            var lineMat=new Material(Shader.Find("Sprites/Default"));lineMat.color=Color.white;
            for(int i=-7;i<=7;i++)for(int direction=0;direction<2;direction++)
            {
                var line=new GameObject("Studio floor guides").AddComponent<LineRenderer>();line.sharedMaterial=lineMat;line.widthMultiplier=i==0?.003f:.0015f;line.positionCount=2;
                line.SetPosition(0,direction==0?new Vector3(i*.5f,.006f,-3.5f):new Vector3(-3.5f,.006f,i*.5f));
                line.SetPosition(1,direction==0?new Vector3(i*.5f,.006f,3.5f):new Vector3(3.5f,.006f,i*.5f));
                line.startColor=line.endColor=new Color(.15f,.25f,.31f,.22f);
            }
            var ring=new GameObject("Practice area").AddComponent<LineRenderer>();ring.sharedMaterial=lineMat;ring.widthMultiplier=.003f;ring.positionCount=97;
            ring.startColor=ring.endColor=new Color(.19f,.53f,.54f,.35f);
            for(int i=0;i<97;i++){float angle=i/96f*Mathf.PI*2;ring.SetPosition(i,new Vector3(Mathf.Cos(angle)*1.35f,.008f,Mathf.Sin(angle)*1.35f));}
            stageCamera=new GameObject("Practice Camera").AddComponent<Camera>();stageCamera.clearFlags=CameraClearFlags.SolidColor;stageCamera.backgroundColor=new Color(.055f,.08f,.115f);stageCamera.fieldOfView=43;stageCamera.nearClipPlane=.1f;
            preview=new RenderTexture(1240,694,24);preview.Create();stageCamera.targetTexture=preview;UpdateCamera();
        }
        void UpdateCamera()
        {
            if(cameraRig)cameraRig.Bind(CameraState,CameraView);
        }
        void ChangeView(int view,bool clearFraming=true)
        {
            if(!cameraRig)return;
            if(project!=null){project.view=view;dirty=true;}else initialView=view;
            cameraRig.Select(view,clearFraming);
        }
        void ResetCamera()
        {if(cameraRig){cameraRig.ResetView();if(project!=null){project.view=0;dirty=true;}else initialView=0;}}
        void SwitchCharacter(string id,bool remember=true)
        {
            if(id==selectedCharacter)return;
            var switchClock=System.Diagnostics.Stopwatch.StartNew();
            var replacement=DancerCharacters.Create(id);var animator=replacement.GetComponentInChildren<Animator>();
            double createMs=switchClock.Elapsed.TotalMilliseconds;
            try{playback.Retarget(animator);}catch{Destroy(replacement);throw;}
            double retargetMs=switchClock.Elapsed.TotalMilliseconds-createMs;
            var previous=characterObject;characterObject=replacement;diagnosticAnimator=animator;selectedCharacter=id;
            cameraRig.SetTarget(replacement.transform);UpdateCamera();Destroy(previous);
            if(remember&&project!=null){project.character=id;dirty=true;}
            Debug.Log($"CHARACTER_SWITCH {id}: create={createMs:0.00} ms, retarget={retargetMs:0.00} ms, total={switchClock.Elapsed.TotalMilliseconds:0.00} ms");
            message=id=="female"?"已切换为女角色，音乐位置保持不变。":"已切换为男角色，音乐位置保持不变。";
        }
        void StageInput(Rect area)
        {
            if(!cameraRig||showSettings||showCredits)return;
            var e=Event.current;int id=GUIUtility.GetControlID(60821,FocusType.Passive,area);
            bool mirror=Ready&&project.mirror;
            if(e.type==EventType.MouseDown&&area.Contains(e.mousePosition)&&e.button<=2)
            {
                GUI.FocusControl(null);GUIUtility.hotControl=id;cameraDragButton=e.button;
                if(e.clickCount==2){ResetCamera();GUIUtility.hotControl=0;cameraDragButton=-1;}
                e.Use();
            }
            else if(e.type==EventType.MouseDrag&&GUIUtility.hotControl==id)
            {
                if(cameraDragButton==0&&!CameraState.dragPans){ChangeView(PracticeCamera.Free,false);cameraRig.Orbit(e.delta,mirror);}
                else cameraRig.Pan(e.delta,mirror);
                if(project!=null)dirty=true;e.Use();
            }
            else if(e.type==EventType.MouseUp&&GUIUtility.hotControl==id)
            {GUIUtility.hotControl=0;cameraDragButton=-1;e.Use();}
            else if(e.type==EventType.ScrollWheel&&area.Contains(e.mousePosition))
            {cameraRig.Zoom(e.delta.y);if(project!=null)dirty=true;e.Use();}
        }
        void Update()
        {
            if(auditionId!=""&&audition&&audition.clip&&!audition.isPlaying)StopAudition();
            if(job!=null&&Time.realtimeSinceStartup>nextPoll)
            {
                nextPoll=Time.realtimeSinceStartup+.15f;job.Poll();
                if(job.Finished)
                {
                    var finished=job;job=null;
                    if(finished.ExitCode==0)Guard(()=>LoadAnalysis(finished.Folder));
                    else error=finished.Progress.state=="error"?finished.Progress.message:finished.Error;
                    finished.Dispose();
                }
            }
            if(Ready&&!TestMode&&!showSettings&&!showCredits&&GUIUtility.keyboardControl==0)
            {
                if(Input.GetKeyDown(KeyCode.Space))TogglePlay();
                if(Input.GetKeyDown(KeyCode.LeftArrow))playback.Seek(playback.Position-2);
                if(Input.GetKeyDown(KeyCode.RightArrow))playback.Seek(playback.Position+2);
            }
        }
        void Guard(Action action){try{error="";action();}catch(Exception ex){Fail(ex);}}
        void Fail(Exception ex){error=ex.Message;Debug.LogException(ex);}
        static bool TryNumber(string text,out double number)=>double.TryParse(text,NumberStyles.Float,CultureInfo.InvariantCulture,out number)&&Choreographer.Finite(number);
        void StartAnalysis()
        {
            if(Busy)return;
            if(!File.Exists(sourcePath))throw new FileNotFoundException("请选择存在的音乐文件。");
            if(!TryNumber(startText,out var start)||start<0||!TryNumber(durationText,out var duration)||duration<30||duration>60)throw new ArgumentException("起点应大于等于零，片段长度为 30～60 秒。");
            StopAudition();
            var folder=Path.Combine(Workspace,DateTime.Now.ToString("yyyyMMdd-HHmmss")+"-"+Guid.NewGuid().ToString("N").Substring(0,6));
            PlayerPrefs.SetString("AnalyzerPython",pythonPath);
            if(Environment.GetCommandLineArgs().Contains("-portable-smoke")&&!BundledAnalyzer)throw new InvalidOperationException("便携检查需要完整发布包中的内置分析引擎。");
            Debug.Log("ANALYZER_RUNTIME "+(BundledAnalyzer?"bundled":"custom/development"));
            job=new AnalyzerJob(AnalyzerPython,Path.Combine(Application.streamingAssetsPath,"Analyzer","analyzer.py"),new AnalysisRequest{sourcePath=Path.GetFullPath(sourcePath),outputDir=folder,startSeconds=start,durationSeconds=duration});
            message="正在离线分析，当前编舞可以继续观看。";
        }
        string LibraryRoot=>Path.Combine(Application.streamingAssetsPath,"MusicLibrary");
        void StopAudition()
        {
            auditionToken++;auditionId="";
            if(audition){audition.Stop();if(audition.clip)Destroy(audition.clip);audition.clip=null;}
        }
        void PreviewTrack(BuiltinTrack track)
        {
            if(auditionId==track.id){StopAudition();return;}
            StopAudition();if(Ready)playback.Pause();auditionId=track.id;
            StartCoroutine(AuditionTrack(track,auditionToken));
        }
        IEnumerator AuditionTrack(BuiltinTrack track,int token)
        {
            string path=Choreographer.ResolveLocal(LibraryRoot,track.id+"/normal.wav");
            using(var request=UnityWebRequestMultimedia.GetAudioClip(new Uri(path).AbsoluteUri,AudioType.WAV))
            {
                yield return request.SendWebRequest();
                if(request.result!=UnityWebRequest.Result.Success){if(token==auditionToken){error="试听失败："+request.error;auditionId="";}yield break;}
                var clip=DownloadHandlerAudioClip.GetContent(request);
                if(token!=auditionToken){Destroy(clip);yield break;}
                audition.clip=clip;audition.Play();message="正在试听 "+track.title+" · "+track.artist;
            }
        }
        void UseBuiltin(int index)
        {
            if(Busy)return;
            if(Ready)Save();
            var track=musicLibrary.tracks[index];StopAudition();if(Ready)playback.Pause();chosenTrack=index;
            string source=Choreographer.ResolveLocal(LibraryRoot,track.id);
            string destination=Path.Combine(Workspace,track.id+"-"+Guid.NewGuid().ToString("N").Substring(0,6));Directory.CreateDirectory(destination);
            foreach(var f in Directory.GetFiles(source))if(!f.EndsWith(".meta",StringComparison.OrdinalIgnoreCase))File.Copy(f,Path.Combine(destination,Path.GetFileName(f)));
            sourcePath=Path.Combine(source,"normal.wav");startText="0";durationText=track.excerptDuration.ToString(CultureInfo.InvariantCulture);
            autoPlayOnLoad=true;LoadAnalysis(destination);
        }
        void LoadAnalysis(string folder)
        {
            var analysis=JsonUtility.FromJson<MusicAnalysis>(File.ReadAllText(Path.Combine(folder,"analysis.json")));
            Choreographer.ValidateAnalysis(analysis);
            var camera=CameraState;
            var p=new DanceProject{title=Path.GetFileNameWithoutExtension(analysis.sourceName),mode=PreferredStyle,analysis=analysis,loopEnd=analysis.durationSeconds,character=selectedCharacter,view=CameraView,
                camera=new CameraSettings{yaw=camera.yaw,pitch=camera.pitch,distanceScale=camera.distanceScale,panX=camera.panX,panY=camera.panY,fieldOfView=camera.fieldOfView,dragPans=camera.dragPans},
                grid=new GridSettings{bpm=analysis.bpm,firstBeatSeconds=analysis.beats.Count>0?analysis.beats[0]:0}};
            // Prefer an explicitly approved library and display its actual style.
            if(!analysis.needsManualGrid)p.segments=Choreographer.Generate(p,catalog);
            StartCoroutine(LoadAudioProject(p,folder));
        }
        IEnumerator LoadAudioProject(DanceProject p,string folder)
        {
            loading=true;
            string[] names={p.analysis.normalAudio,p.analysis.slow075Audio,p.analysis.slow050Audio};
            var loaded=new List<AudioClip>();
            foreach(var name in names)
            {
                string path=null;Guard(()=>{path=Choreographer.ResolveLocal(folder,name);if(!File.Exists(path))throw new FileNotFoundException("项目音频缺失："+name);});
                if(path==null||!File.Exists(path)){loading=false;foreach(var c in loaded)Destroy(c);yield break;}
                using(var request=UnityWebRequestMultimedia.GetAudioClip(new Uri(path).AbsoluteUri,AudioType.WAV))
                {
                    yield return request.SendWebRequest();
                    if(request.result!=UnityWebRequest.Result.Success){error="音频读取失败："+request.error;loading=false;foreach(var c in loaded)Destroy(c);yield break;}
                    var clip=DownloadHandlerAudioClip.GetContent(request);
                    if(!clip){error="音频无法解码："+name;loading=false;foreach(var c in loaded)Destroy(c);yield break;}
                    float rate=loaded.Count==0?1:loaded.Count==1?.75f:.5f;
                    if(Math.Abs(clip.length-p.analysis.durationSeconds/rate)>.03 || clip.frequency!=p.analysis.sampleRate)
                    {error="音频时长或采样率与项目记录不匹配："+name;Destroy(clip);foreach(var c in loaded)Destroy(c);loading=false;yield break;}
                    loaded.Add(clip);
                }
            }
            // Commit only after every clip is loaded and the current work is saved.
            if(Ready)
            {
                bool saved=true;try{Save();}catch(Exception ex){Fail(ex);saved=false;}
                if(!saved){foreach(var c in loaded)Destroy(c);loading=false;yield break;}
            }
            if(p.camera==null)p.camera=new CameraSettings();if(string.IsNullOrEmpty(p.character))p.character="female";
            if(p.character!=selectedCharacter)SwitchCharacter(p.character,false);
            project=p;projectFolder=folder;playback.SetProject(p,loaded[0],loaded[1],loaded[2]);selected=p.segments.Count>0?0:-1;
            selectedSourceName=p.title;
            chosenTrack=File.Exists(Path.Combine(folder,"CREDITS.txt"))?musicLibrary.tracks.FindIndex(t=>t.title==p.title):-1;
            RefreshFields();loading=false;dirty=true;UpdateCamera();Guard(Save);
            message=p.analysis.needsManualGrid?"音乐已导入，请校正节拍后生成。":p.mode==DanceStyles.Diagnostic?"已就绪。当前为技术演示动作，可验证播放和编辑流程。":"已就绪。点击播放开始 "+DanceStyles.Name(p.mode)+" 跟拍练习。";
            if(autoPlayOnLoad){autoPlayOnLoad=false;playback.Play();}
        }
        void RefreshFields()
        {
            bpmText=project.grid.bpm.ToString("0.###",CultureInfo.InvariantCulture);firstBeatText=project.grid.firstBeatSeconds.ToString("0.###",CultureInfo.InvariantCulture);
            countText=(project.grid.eightCountOffset+1).ToString();seedText=project.seed.ToString();
            loopStartText=project.loopStart.ToString("0.###",CultureInfo.InvariantCulture);loopEndText=project.loopEnd.ToString("0.###",CultureInfo.InvariantCulture);
        }
        void OpenProject(string path)
        {
            if(string.IsNullOrEmpty(path)||Busy)return;
            var p=JsonUtility.FromJson<DanceProject>(File.ReadAllText(path));Choreographer.ValidateProject(p,catalog);
            StopAudition();chosenTrack=-1;
            sourcePath=Choreographer.ResolveLocal(Path.GetDirectoryName(Path.GetFullPath(path)),p.analysis.normalAudio);startText="0";durationText=p.analysis.durationSeconds.ToString("0.###",CultureInfo.InvariantCulture);
            StartCoroutine(LoadAudioProject(p,Path.GetDirectoryName(Path.GetFullPath(path))));
        }
        void Save()
        {
            if(!Ready)return;
            project.position=playback.Position;Choreographer.ValidateProject(project,catalog);
            string path=Path.Combine(projectFolder,"project.danceproj"),temp=path+".tmp";
            File.WriteAllText(temp,JsonUtility.ToJson(project,true));
            if(File.Exists(path))File.Replace(temp,path,null);else File.Move(temp,path);
            if(!TestMode)PlayerPrefs.SetString("LastProject",path);
            dirty=false;message="编舞已保存到本地，可通过“打开项目”继续练习。";
        }
        void OpenDemo()
        {
            if(Busy)return;
            string source=Path.Combine(Application.streamingAssetsPath,"Demo");
            string destination=Path.Combine(Workspace,"Demo-"+Guid.NewGuid().ToString("N").Substring(0,6));Directory.CreateDirectory(destination);
            foreach(var f in Directory.GetFiles(source))if(!f.EndsWith(".meta",StringComparison.OrdinalIgnoreCase))File.Copy(f,Path.Combine(destination,Path.GetFileName(f)));
            var path=Path.Combine(destination,"project.danceproj");
            if(File.Exists(path))OpenProject(path);else LoadAnalysis(destination);
        }
        void ApplyGrid()
        {
            if(!Ready)return;
            if(!TryNumber(bpmText,out var bpm)||!TryNumber(firstBeatText,out var first)||!int.TryParse(countText,out var count))throw new ArgumentException("请输入有效的 BPM、首拍秒数和 1～8 的计数。");
            var old=project.grid;
            project.grid=new GridSettings{manual=true,bpm=bpm,firstBeatSeconds=first,eightCountOffset=count-1};
            try{project.segments=Choreographer.Generate(project,catalog);}catch{project.grid=old;throw;}
            dirty=true;message="已校正节拍并重新编排，锁定片段保持不变。";
        }
        void Generate(string mode,bool changeSeed)
        {
            if(!Ready)return;
            if(!int.TryParse(seedText,out var seed))throw new ArgumentException("种子应为整数。");
            if(project.mode!=mode && project.segments.Any(s=>s.locked))throw new InvalidOperationException("切换舞种前请先解除所有锁定。");
            var oldMode=project.mode;var oldSeed=project.seed;project.mode=mode;project.seed=changeSeed?unchecked(seed+1):seed;
            try{project.segments=Choreographer.Generate(project,catalog);}catch{project.mode=oldMode;project.seed=oldSeed;throw;}
            seedText=project.seed.ToString();selected=0;dirty=true;message=mode=="diagnostic"?"技术演示序列已生成（非 New Jazz）。":DanceStyles.Name(mode)+" 编舞已生成。";
        }
        void ApplyLoop()
        {
            if(!TryNumber(loopStartText,out var start)||!TryNumber(loopEndText,out var end)||start<0||end<=start+.05||end>project.analysis.durationSeconds)throw new ArgumentException("循环终点需大于起点，且在所选音乐片段内。");
            project.loopStart=start;project.loopEnd=end;project.loop=true;dirty=true;
            playback.Seek(start);
        }
        void TogglePlay(){if(!Ready)return;StopAudition();if(playback.IsPlaying)playback.Pause();else playback.Play();}
        void EnsureStyles()
        {
            if(stylesReady)return;
            white=Texture2D.whiteTexture;
            var font=Font.CreateDynamicFontFromOSFont(new[]{"Microsoft YaHei UI","Microsoft YaHei","Arial"},18);
            label=new GUIStyle{font=font,fontSize=16,wordWrap=true,normal={textColor=ink}};
            small=new GUIStyle(label){fontSize=13};muted=new GUIStyle(small){normal={textColor=new Color(.52f,.62f,.7f)}};
            title=new GUIStyle(label){fontSize=30,fontStyle=FontStyle.Bold};heading=new GUIStyle(label){fontSize=18,fontStyle=FontStyle.Bold};
            button=new GUIStyle{font=font,fontSize=15,alignment=TextAnchor.MiddleCenter,padding=new RectOffset(8,8,4,4),clipping=TextClipping.Clip};
            field=new GUIStyle{font=font,fontSize=15,padding=new RectOffset(10,10,5,5),alignment=TextAnchor.MiddleLeft,clipping=TextClipping.Clip};
            var normal=Solid(new Color(.105f,.16f,.205f));var hover=Solid(new Color(.16f,.25f,.30f));var active=Solid(new Color(.12f,.34f,.31f));var input=Solid(new Color(.028f,.046f,.065f));
            PaintStates(button,normal,ink);button.hover.background=hover;button.active.background=active;button.focused.background=hover;
            PaintStates(field,input,ink);
            accentButton=new GUIStyle(button){fontStyle=FontStyle.Bold};PaintStates(accentButton,Solid(teal),new Color(.025f,.075f,.075f));accentButton.hover.background=Solid(new Color(.47f,.96f,.86f));
            lockToggle=new GUIStyle(button){fontSize=13};lockToggle.onNormal.background=active;lockToggle.onNormal.textColor=teal;lockToggle.onHover.background=active;
            sliderTrack=new GUIStyle{fixedHeight=4};PaintStates(sliderTrack,normal,ink);
            sliderThumb=new GUIStyle{fixedWidth=14,fixedHeight=14};PaintStates(sliderThumb,Solid(teal),ink);
            stylesReady=true;
        }
        static void PaintStates(GUIStyle style,Texture2D texture,Color color)
        {
            foreach(var state in new[]{style.normal,style.hover,style.active,style.focused,style.onNormal,style.onHover,style.onActive,style.onFocused})
            {
                state.background=texture;state.textColor=color;
#if UNITY_EDITOR
                state.scaledBackgrounds=new Texture2D[0];
#endif
            }
            style.border=new RectOffset();style.margin=new RectOffset();
        }
        static Texture2D Solid(Color color)
        {var texture=new Texture2D(1,1);texture.SetPixel(0,0,color);texture.Apply();return texture;}
        void Box(Rect rect,Color color){var old=GUI.color;GUI.color=color;GUI.DrawTexture(rect,white);GUI.color=old;}
        void Text(float x,float y,float width,float height,string text,GUIStyle style=null)=>GUI.Label(new Rect(x,y,width,height),text,style??label);
        bool Button(float x,float y,float width,float height,string text,bool accent=false)=>GUI.Button(new Rect(x,y,width,height),text,accent?accentButton:button);
        string Field(float x,float y,float width,string value)
        {Box(new Rect(x-1,y-1,width+2,32),new Color(.15f,.22f,.27f));return GUI.TextField(new Rect(x,y,width,30),value,field);}
        static string Clock(double seconds)=>$"{(int)seconds/60:00}:{seconds%60:00.0}";
        void OnGUI()
        {
            if(TestMode&&(Event.current.isKey||Event.current.isMouse||Event.current.type==EventType.ScrollWheel))return;
            if(Event.current.type==EventType.Repaint)renderedFrames++;
            EnsureStyles();float scale=Math.Min(Screen.width/W,Screen.height/H);GUI.matrix=Matrix4x4.TRS(new Vector3((Screen.width-W*scale)/2,(Screen.height-H*scale)/2,0),Quaternion.identity,new Vector3(scale,scale,1));
            Box(new Rect(0,0,W,H),new Color(.035f,.05f,.075f));
            if(reviewFrames!=null){DrawReviewBoard();GUI.matrix=Matrix4x4.identity;return;}
            Text(24,17,620,44,"CHOREO  /  编舞练习室",title);Text(26,62,900,25,"本地音乐  ·  节拍编排  ·  3D 跟学",muted);
            Text(1110,30,172,30,dirty?"● 有未保存的修改":"本地音乐工作区",muted);
            GUI.enabled=!Busy&&musicLibrary.tracks.Count>0;
            if(Button(1292,24,145,34,"一键演示",true))Guard(()=>UseBuiltin(0));GUI.enabled=true;
            if(Button(1450,24,110,34,"设置"))showSettings=!showSettings;
            Box(new Rect(20,108,275,658),new Color(.064f,.083f,.117f));
            Box(new Rect(1265,108,315,658),new Color(.064f,.083f,.117f));
            DrawImport();DrawStage();DrawInspector();DrawTransport();
            Box(new Rect(20,844,1560,40),new Color(.065f,.095f,.12f));
            Text(32,851,1535,28,!string.IsNullOrEmpty(error)?"提示："+error:message,!string.IsNullOrEmpty(error)?new GUIStyle(small){normal={textColor=new Color(1,.65f,.43f)}}:small);
            if(showSettings||showCredits)DrawDialog();
            GUI.matrix=Matrix4x4.identity;
        }
        void DrawImport()
        {
            Text(36,124,240,28,"01  选择音乐",heading);
            GUI.enabled=!Busy;
            if(Button(36,165,240,38,"导入我的音乐…"))Guard(()=>{var p=NativeFiles.Open(false);if(p!=null){sourcePath=p;selectedSourceName=Path.GetFileNameWithoutExtension(p);chosenTrack=-1;}});
            Text(36,216,240,35,string.IsNullOrEmpty(sourcePath)?"支持 WAV / MP3，音频保存在本机。":selectedSourceName,muted);
            Box(new Rect(36,261,240,1),new Color(.15f,.20f,.25f));Text(36,277,240,25,"免费测试曲目",heading);
            for(int i=0;i<musicLibrary.tracks.Count;i++)
            {
                var track=musicLibrary.tracks[i];float y=313+i*77;
                Box(new Rect(36,y,240,68),chosenTrack==i?new Color(.10f,.21f,.23f):new Color(.08f,.115f,.15f));
                Text(46,y+8,145,24,track.title,small);Text(46,y+34,145,25,track.genre,muted);
                if(Button(200,y+5,68,26,auditionId==track.id?"停止":"试听"))Guard(()=>PreviewTrack(track));
                int index=i;if(Button(200,y+36,68,26,"使用"))Guard(()=>UseBuiltin(index));
            }
            if(musicLibrary.tracks.Count==0)Text(36,317,240,120,"内置曲目未准备，请先导入音乐。",small);
            Text(36,552,112,22,"片段起点 / 秒",muted);Text(161,552,112,22,"长度 / 30～60 秒",muted);
            GUI.enabled=!Busy&&chosenTrack<0;
            startText=Field(36,579,112,startText);durationText=Field(161,579,112,durationText);
            GUI.enabled=!Busy&&!string.IsNullOrEmpty(sourcePath);
            if(Button(36,624,240,38,chosenTrack>=0?"开始练习":"分析并打开",true))Guard(()=>
            {
                if(chosenTrack<0)StartAnalysis();
                else if(Ready&&project.title==musicLibrary.tracks[chosenTrack].title){StopAudition();playback.Seek(0);playback.Play();}
                else UseBuiltin(chosenTrack);
            });
            GUI.enabled=true;
            if(job!=null)
            {
                Text(36,673,170,35,job.Progress.message,muted);Box(new Rect(36,709,240,3),new Color(.12f,.2f,.24f));Box(new Rect(36,709,240*job.Progress.progress,3),teal);
                if(Button(213,674,63,27,"取消"))Guard(()=>{job.Cancel();job.Dispose();job=null;message="分析已取消，当前项目保持不变。";});
            }
            else Text(36,676,240,31,"内置曲目已离线分析，点击“使用”即可播放。",muted);
            GUI.enabled=!Busy;
            if(Button(36,725,115,28,"打开项目"))Guard(()=>OpenProject(NativeFiles.Open(true)));
            if(Button(160,725,115,28,"资源署名"))showCredits=true;
            GUI.enabled=true;
        }
        void DrawStage()
        {
            Text(319,110,650,30,Ready?project.title:"先选一首歌，开始体验",heading);
            GUI.enabled=!Busy&&cameraRig;
            if(Button(1056,109,86,28,"女角色",selectedCharacter=="female"))Guard(()=>SwitchCharacter("female"));
            if(Button(1150,109,94,28,"男角色",selectedCharacter=="male"))Guard(()=>SwitchCharacter("male"));
            GUI.enabled=true;
            string subtitle=Ready?$"{project.analysis.durationSeconds:0} 秒片段  ·  {project.grid.bpm:0.#} BPM  ·  {DanceStyles.Name(project.mode)}":"内置免费音乐可直接播放，也支持导入自己的歌曲。";
            if(chosenTrack>=0)subtitle+="  /  Kevin MacLeod · CC BY 4.0";
            Text(319,146,920,30,subtitle,muted);
            Rect area=new Rect(315,187,930,520);Box(area,new Color(.055f,.08f,.115f));
            if(preview)GUI.DrawTextureWithTexCoords(area,preview,Ready&&project.mirror?new Rect(1,0,-1,1):new Rect(0,0,1,1));
            Text(333,204,850,25,Ready?(project.mode=="diagnostic"?"动作预览  /  演示模式":DanceStyles.Name(project.mode)+"  /  跟拍练习"):"3D 动作预览",small);
            Text(333,235,850,22,(CameraState.dragPans?"左键拖动平移":"左键拖动旋转")+" · 右键平移 · 滚轮缩放 · 双击复位",muted);
            StageInput(area);
            if(!Ready)Text(333,663,850,25,"点击左侧曲目的“使用”，即可听音乐、看动作。",muted);
            if(Ready)
            {
                var beats=project.grid.manual||project.analysis.beats.Count>=8?Choreographer.BeatTimes(project.analysis,project.grid):new List<double>();
                int at=beats.BinarySearch(playback.Position);if(at<0)at=~at-1;
                int count=at<0?0:(at+project.grid.eightCountOffset)%8+1;
                Text(1152,628,70,65,count==0?"—":count.ToString(),new GUIStyle(title){fontSize=48,normal={textColor=teal}});
                int segment=playback.ActiveSegment();
                if(segment>=0)Text(333,663,730,24,$"{segment+1:00}  /  {catalog.motions.Find(m=>m.id==project.segments[segment].motionId)?.name}",small);
            }
            GUI.enabled=cameraRig;
            var options=new[]{new{index=0,name="正面"},new{index=1,name="背面"},new{index=3,name="左侧"},new{index=4,name="右侧"},new{index=5,name="上身"},new{index=6,name="脚步"},new{index=2,name="自由"}};
            for(int i=0;i<options.Length;i++){var option=options[i];if(Button(315+i*76,721,70,32,option.name,CameraView==option.index))ChangeView(option.index);}
            if(Button(851,721,70,32,"复位"))ResetCamera();
            GUI.enabled=Ready;
            if(Button(929,721,112,32,Ready&&project.mirror?"镜像：开":"镜像：关")){project.mirror=!project.mirror;dirty=true;}
            if(Button(1100,721,145,32,"保存项目",true))Guard(Save);
            GUI.enabled=true;
        }
        void DrawInspector()
        {
            Text(1283,124,280,26,"02  编舞与节拍",heading);
            if(Button(1283,164,85,31,"动作",inspectorTab==0))inspectorTab=0;
            if(Button(1376,164,85,31,"节拍",inspectorTab==1))inspectorTab=1;
            if(Button(1469,164,87,31,"镜头",inspectorTab==2))inspectorTab=2;
            if(inspectorTab==2){DrawCameraPanel();return;}
            if(!Ready)
            {
                Text(1283,225,273,40,"先选择一首音乐",heading);
                Text(1283,277,273,95,"点击左侧内置曲目的“使用”，可以立即体验。\n\n也可以导入自己的音乐，完成分析后编辑动作组合。",small);
                Text(1283,602,273,110,PreferredStyle==DanceStyles.Diagnostic?"动作素材尚未就绪。":"已提供 Charleston 八拍练习版。首次跟学建议使用 0.5× 或 0.75×。",muted);
                return;
            }
            GUI.enabled=Ready&&!Busy;
            if(inspectorTab==1)
            {
                Text(1283,225,273,28,"让动作对齐你听到的节拍",heading);
                Text(1283,273,120,22,"速度 / BPM",muted);Text(1423,273,133,22,"首拍位置 / 秒",muted);
                bpmText=Field(1283,302,130,bpmText);firstBeatText=Field(1423,302,133,firstBeatText);
                Text(1283,356,273,24,"首拍对应计数 / 1～8",muted);countText=Field(1283,387,273,countText);
                if(Button(1283,442,273,38,"应用校正",true))Guard(ApplyGrid);
                Text(1283,510,273,150,"自动检测不能保证乐句和八拍起点正确，请试听后校正。\n\n计数设为 4 时，首段从动作第 4 拍开始，跳完 4～8 后进入下一组八拍。\n\n更改前请解除受影响的锁定。",muted);
                GUI.enabled=true;return;
            }
            if(Button(1283,213,273,34,PreferredStyle==DanceStyles.Diagnostic?"生成演示组合":"生成 "+DanceStyles.Name(PreferredStyle)+" 组合"))Guard(()=>Generate(PreferredStyle,false));
            if(Button(1283,258,273,34,"换一版 · 保留锁定片段"))Guard(()=>Generate(project.mode,true));
            int eligible=catalog==null?0:Choreographer.Eligible(catalog,"new-jazz").Count;
            if(eligible>=8){if(Button(1283,302,273,28,"生成 New Jazz",true))Guard(()=>Generate("new-jazz",false));}
            else Text(1283,302,273,24,project.mode==DanceStyles.Charleston?"八拍练习版 · 建议从慢速开始":"New Jazz 尚未开放 · 缺少已审核动作",muted);
            GUI.enabled=Ready&&!Busy;
            Text(1283,333,273,22,"片段  /  点击定位，勾选锁定",muted);
            int count=Ready?project.segments.Count:0;
            segmentsScroll=GUI.BeginScrollView(new Rect(1283,362,273,178),segmentsScroll,new Rect(0,0,250,Math.Max(178,count*49)));
            for(int i=0;i<count;i++)
            {
                var s=project.segments[i];var motion=catalog.motions.Find(m=>m.id==s.motionId);
                if(GUI.Button(new Rect(0,i*49,211,44),"",i==selected?accentButton:button)){selected=i;playback.Seek(s.startSeconds);}
                var rowStyle=new GUIStyle(small){wordWrap=false,clipping=TextClipping.Clip,normal={textColor=i==selected?new Color(.025f,.075f,.075f):ink}};
                GUI.Label(new Rect(8,i*49+3,198,17),$"{i+1:00}  {Clock(s.startSeconds)}～{Clock(s.endSeconds)}",rowStyle);
                GUI.Label(new Rect(8,i*49+21,198,20),motion?.name,rowStyle);
                bool locked=GUI.Toggle(new Rect(220,i*49+10,26,24),s.locked,s.locked?"锁":"○",lockToggle);if(locked!=s.locked){s.locked=locked;dirty=true;}
            }
            GUI.EndScrollView();
            Text(1283,553,273,24,"替换选中片段",heading);
            var replacements=Ready&&selected>=0&&selected<project.segments.Count?Choreographer.Replacements(project,catalog,selected):new List<MotionDefinition>();
            replacementsScroll=GUI.BeginScrollView(new Rect(1283,588,273,150),replacementsScroll,new Rect(0,0,250,Math.Max(150,replacements.Count*30)));
            if(Ready&&selected>=0&&selected<project.segments.Count)
            {
                GUI.enabled=!Busy&&!project.segments[selected].locked;
                for(int i=0;i<replacements.Count;i++)
                {var m=replacements[i];if(GUI.Button(new Rect(0,i*30,248,27),m.name,button))Guard(()=>{Choreographer.Replace(project,catalog,selected,m.id);dirty=true;message="已替换动作，片段时长保持不变。";});}
            }
            GUI.EndScrollView();GUI.enabled=true;
        }
        void DrawTransport()
        {
            Box(new Rect(20,780,1560,52),new Color(.075f,.11f,.15f));
            GUI.enabled=Ready;
            if(Button(30,790,82,31,Ready&&playback.IsPlaying?"暂停":"播放",true))TogglePlay();
            float time=Ready?(float)playback.Position:0,duration=Ready?(float)project.analysis.durationSeconds:40;
            float seek=GUI.HorizontalSlider(new Rect(126,801,410,22),time,0,duration,sliderTrack,sliderThumb);
            if(Ready&&Math.Abs(seek-time)>.025){playback.Seek(seek);dirty=true;}
            Text(550,796,160,25,Clock(time)+" / "+Clock(duration),small);
            float[] speeds={.5f,.75f,1};for(int i=0;i<3;i++)if(Button(715+i*67,791,60,30,speeds[i]+"×",Ready&&project.speed==speeds[i])){playback.SetRate(speeds[i]);dirty=true;}
            if(Button(923,791,90,30,Ready&&project.loop?"循环：开":"循环：关")){project.loop=!project.loop;dirty=true;}
            loopStartText=Field(1023,791,80,loopStartText);Text(1112,796,24,24,"至",small);loopEndText=Field(1140,791,80,loopEndText);
            if(Button(1233,791,99,30,"设置区间"))Guard(ApplyLoop);
            Text(1350,798,212,22,"空格 播放  /  ← → 定位",muted);GUI.enabled=true;
        }
        void DrawCameraPanel()
        {
            Text(1283,221,273,28,"镜头设置",heading);
            Text(1283,256,273,22,"也可直接拖动画面和滚轮调整。",muted);
            var s=CameraState;
            float fov=CameraSlider(289,"视野",s.fieldOfView,30,60,"°");
            if(Math.Abs(fov-s.fieldOfView)>.01f){cameraRig.SetFieldOfView(fov);if(project!=null)dirty=true;}
            float distance=CameraSlider(351,"距离",s.distanceScale,.55f,2.4f,"×");
            if(Math.Abs(distance-s.distanceScale)>.001f){cameraRig.SetDistance(distance);if(project!=null)dirty=true;}
            float yaw=CameraSlider(413,"水平旋转",s.yaw,-180,180,"°");
            float pitch=CameraSlider(475,"俯仰",s.pitch,-15,65,"°");
            if(Math.Abs(yaw-s.yaw)>.01f||Math.Abs(pitch-s.pitch)>.01f){ChangeView(2,false);cameraRig.SetOrbit(yaw,pitch);}
            float panX=CameraSlider(537,"水平平移",s.panX,-1,1,"");
            float panY=CameraSlider(599,"竖直平移",s.panY,-1,1,"");
            if(Math.Abs(panX-s.panX)>.001f||Math.Abs(panY-s.panY)>.001f){cameraRig.SetPan(panX,panY);if(project!=null)dirty=true;}
            if(Button(1283,663,130,30,"拖动旋转",!s.dragPans)){s.dragPans=false;if(project!=null)dirty=true;}
            if(Button(1423,663,133,30,"拖动平移",s.dragPans)){s.dragPans=true;if(project!=null)dirty=true;}
            if(Button(1283,715,273,34,"恢复默认镜头"))ResetCamera();
        }
        float CameraSlider(float y,string name,float value,float min,float max,string unit)
        {
            Text(1283,y,160,22,name,muted);Text(1446,y,110,22,value.ToString(unit=="°"?"0":"0.00")+unit,small);
            return GUI.HorizontalSlider(new Rect(1283,y+30,273,18),value,min,max,sliderTrack,sliderThumb);
        }
        void DrawDialog()
        {
            Box(new Rect(0,0,W,H),new Color(0,0,0,.75f));GUI.enabled=true;
            GUI.ModalWindow(91827,new Rect(400,130,800,640),_=>
            {
                Box(new Rect(0,0,800,640),new Color(.065f,.09f,.125f));
                Text(26,24,630,36,showCredits?"资源 · 作者与授权":"高级设置",heading);
                if(Button(680,22,94,32,"关闭")){showSettings=false;showCredits=false;}
                if(showCredits)
                {
                    for(int i=0;i<musicLibrary.tracks.Count;i++)
                    {
                        var track=musicLibrary.tracks[i];float y=89+i*112;
                        Text(26,y,550,28,track.title+"  /  "+track.artist,heading);
                        Text(26,y+34,550,45,"incompetech.com · Creative Commons Attribution 4.0\n截取原曲 15～55 秒，并提供保持音高的慢放版本。",muted);
                        if(Button(605,y,77,30,"来源"))Application.OpenURL(track.sourceUrl);
                        if(Button(694,y,77,30,"授权"))Application.OpenURL(track.licenseUrl);
                    }
                    Text(26,414,540,25,"角色：Quaternius · Universal Base Characters · CC0 1.0",small);
                    if(Button(605,411,166,30,"角色来源与授权"))Application.OpenURL("https://quaternius.com/packs/universalbasecharacters.html");
                    Text(26,457,550,28,"动作：CMU Graphics Lab Motion Capture · subject 93",small);
                    if(Button(605,454,166,30,"动作来源与说明"))Application.OpenURL("https://mocap.cs.cmu.edu/");
                    Text(26,498,744,55,"Charleston 练习改编：四段动捕与四个左右变体，已作循环、平滑、慢速重定时与上肢调整。New Jazz 素材仍待补齐。",muted);
                    Text(26,570,744,45,"原作者未参与或背书本应用。全部来源、许可及修改记录随完整发布包提供。",muted);
                }
                else
                {
                    Text(26,89,744,62,"音乐分析在本机离线完成。完整发布包自带分析引擎，导入歌曲无需安装额外环境。",small);
                    Text(26,170,744,24,BundledAnalyzer?"分析引擎：内置 · 已就绪":"分析引擎：自定义／开发环境",muted);
                    if(Button(26,204,200,32,showAnalyzerAdvanced?"收起高级设置":"高级：自定义分析器"))showAnalyzerAdvanced=!showAnalyzerAdvanced;
                    if(showAnalyzerAdvanced)pythonPath=Field(240,204,530,pythonPath);
                    Text(26,267,400,24,"编排随机种子",muted);seedText=Field(26,300,200,seedText);
                    Text(270,267,500,24,"镜头视野 / 30～60°",muted);
                    float fov=GUI.HorizontalSlider(new Rect(270,312,390,18),CameraState.fieldOfView,30,60,sliderTrack,sliderThumb);
                    Text(684,301,85,30,fov.ToString("0")+"°",small);
                    if(Math.Abs(fov-CameraState.fieldOfView)>.01f){cameraRig.SetFieldOfView(fov);if(project!=null)dirty=true;}
                    Text(26,350,744,45,"同一种子可复现编排结果。修改这里不会自动改变当前的音乐或动作。",muted);
                    if(Button(26,429,180,38,"保存设置",true)){PlayerPrefs.SetString("AnalyzerPython",pythonPath);PlayerPrefs.SetFloat("CameraFieldOfView",CameraState.fieldOfView);showSettings=false;message="设置已保存。";}
                    GUI.enabled=Ready;
                    if(Button(224,429,180,38,"打开项目目录"))Application.OpenURL(new Uri(projectFolder).AbsoluteUri);
                    GUI.enabled=true;
                }
            },"",new GUIStyle());
        }
        IEnumerator Smoke()
        {
            QualitySettings.vSyncCount=0;Application.targetFrameRate=-1;
            yield return null;
            bool importTest=Environment.GetCommandLineArgs().Contains("-import-smoke")||Environment.GetCommandLineArgs().Contains("-portable-smoke");
            bool acceptanceTest=Environment.GetCommandLineArgs().Contains("-acceptance");
            bool libraryTest=Environment.GetCommandLineArgs().Contains("-library-smoke")||acceptanceTest;int libraryLoaded=0;
            if(libraryTest)
            {
                foreach(var track in musicLibrary.tracks)
                {
                    int index=musicLibrary.tracks.IndexOf(track);Guard(()=>UseBuiltin(index));while(loading)yield return null;
                    if(!Ready||project.title!=track.title||!File.Exists(Path.Combine(projectFolder,"CREDITS.txt")))break;
                    libraryLoaded++;yield return new WaitForSecondsRealtime(.2f);
                }
                if(libraryLoaded!=3){Debug.LogError("LIBRARY SMOKE FAILED: "+error);Application.Quit(2);yield break;}
            }
            else if(importTest)
            {
                sourcePath=Path.Combine(Application.streamingAssetsPath,"Demo","normal.wav");durationText="30";Guard(StartAnalysis);
                while(job!=null||loading)yield return null;
            }
            else {Guard(OpenDemo);while(loading)yield return null;}
            if(!Ready){Debug.LogError("SMOKE FAILED: "+error);Application.Quit(2);yield break;}
            if(acceptanceTest)
            {
                yield return RunAcceptanceChecks();
                if(acceptanceFailed){Debug.LogError("ACCEPTANCE FAILED");Application.Quit(3);yield break;}
            }
            int frames=0;int initialRendered=renderedFrames;float start=Time.realtimeSinceStartup;double maxError=0;bool clockAdvances=true;
            var frameTimes=new List<float>();
            foreach(float rate in new[]{1f,.75f,.5f})
            {
                playback.Pause();playback.SetRate(rate);playback.Seek(5);playback.Play();
                float phaseStart=Time.realtimeSinceStartup;
                while(Time.realtimeSinceStartup-phaseStart<1.2f)
                {yield return null;frames++;frameTimes.Add(Time.unscaledDeltaTime*1000);if(Time.realtimeSinceStartup-phaseStart>.2)maxError=Math.Max(maxError,playback.AudioErrorMilliseconds);}
                clockAdvances &= playback.Position>5+rate*.9;
            }
            playback.Pause();double paused=playback.Position;yield return new WaitForSecondsRealtime(.15f);
            bool pauseStable=Math.Abs(playback.Position-paused)<.0001;
            project.loop=true;project.loopStart=2;project.loopEnd=2.3;playback.SetRate(1);playback.Seek(2.25);playback.Play();
            yield return new WaitForSecondsRealtime(.65f);
            bool loopWorks=playback.Position>=2&&playback.Position<2.3;
            playback.Pause();project.loop=false;project.loopStart=0;project.loopEnd=project.analysis.durationSeconds;
            playback.Seek(9.25);yield return null;
            Save();string saved=Path.Combine(projectFolder,"project.danceproj");Guard(()=>OpenProject(saved));while(loading)yield return null;
            bool reloadWorks=Ready&&Math.Abs(playback.Position-9.25)<.001&&project.segments.Count>0;
            string output=Path.GetDirectoryName(Application.dataPath);
            ScreenCapture.CaptureScreenshot(Path.Combine(output,"Smoke.png"));
            frameTimes.Sort();
            float lowestFoot=Math.Min(diagnosticAnimator.GetBoneTransform(HumanBodyBones.LeftFoot).position.y,diagnosticAnimator.GetBoneTransform(HumanBodyBones.RightFoot).position.y);
            var result=new SmokeResult{frames=frames,renderedFrames=renderedFrames-initialRendered,p95FrameMs=frameTimes[(int)(frameTimes.Count*.95f)],seconds=Time.realtimeSinceStartup-start,maxAudioClockErrorMs=maxError,avatarValid=true,segments=project.segments.Count,clockAdvances=clockAdvances,pauseStable=pauseStable,loopWorks=loopWorks,reloadWorks=reloadWorks,analyzerImported=importTest,analyzerBundled=BundledAnalyzer,lowestFootHeight=lowestFoot,libraryTracksLoaded=libraryLoaded};
            File.WriteAllText(Path.Combine(output,"smoke-result.json"),JsonUtility.ToJson(result,true));
            Save();yield return new WaitForSecondsRealtime(.6f);Application.Quit(maxError>50||!clockAdvances||!pauseStable||!loopWorks||!reloadWorks||result.renderedFrames==0||lowestFoot<-.03f||lowestFoot>.25f?3:0);
        }
        [Serializable]class SmokeResult{public int frames,segments,renderedFrames,libraryTracksLoaded;public float seconds,p95FrameMs,lowestFootHeight;public double maxAudioClockErrorMs;public bool avatarValid,clockAdvances,pauseStable,loopWorks,reloadWorks,analyzerImported,analyzerBundled;}
        void OnApplicationQuit(){if(job!=null){job.Cancel();job.Dispose();job=null;}if(Ready)Guard(Save);}
        void OnDestroy(){if(job!=null)job.Dispose();if(preview){preview.Release();Destroy(preview);}}
    }
}
