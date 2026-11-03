using System;
using System.Collections;
using System.Globalization;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.Networking;

namespace SongChoreography
{
    public sealed class StudioApp : MonoBehaviour
    {
        const float Width=1600,Height=960;
        GeneratedLibrary library;
        DancePlayback playback;
        MotionLibrarySettings motionLibrary;
        DanceProject project;
        AnalyzerJob analysisJob;
        BuiltinMusicCatalog builtinMusic=new BuiltinMusicCatalog();
        string selectedMusicId="",musicLibraryError="";
        GameObject actor;
        Camera stageCamera;
        PracticeCamera orbit;
        RenderTexture image;
        GUIStyle text,muted,title,button,field,card;
        Font font;
        string source="",startText="0",durationText="40",bpmText="120",firstText="0",countText="1",loopStart="0",loopEnd="40",message="先准备人物与动作库，再导入音乐开始练习。",error="",projectFolder="";
        int selectedCharacter,selectedSegment=-1,view;
        bool busy,stylesReady,mirror;
        Vector2 motionScroll;
        Vector2 musicScroll;
        readonly CameraSettings initialCamera=new CameraSettings();
        CameraSettings CameraState=>project!=null?project.camera:initialCamera;
        string ProjectRoot=>Path.Combine(Application.persistentDataPath,"Projects");
        string MusicRoot=>Path.Combine(Application.streamingAssetsPath,"MusicLibrary");
        bool Working=>analysisJob!=null||busy;
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot(){if(!FindObjectOfType<StudioApp>())new GameObject("CHOREO AI Studio").AddComponent<StudioApp>();}
        void Awake()
        {
            Application.runInBackground=true;
            library=Resources.Load<GeneratedLibrary>("GeneratedLibrary");
            if(!library){library=ScriptableObject.CreateInstance<GeneratedLibrary>();library.catalog=new AssetCatalog();}
            Choreographer.ValidateCatalog(library.catalog);
            playback=gameObject.AddComponent<DancePlayback>();SetupStage();
            motionLibrary=gameObject.AddComponent<MotionLibrarySettings>();
            motionLibrary.Initialize(library,()=>message="动作库选择已保存，下次编排将使用所选的已复查动作。");
            if(library.characters.Length>0)Guard(()=>SelectCharacter(0));
            LoadMusicLibrary();
            if(Environment.GetCommandLineArgs().Contains("-smoke"))StartCoroutine(Smoke());
        }
        void SetupStage()
        {
            var screen=new GameObject("Screen").AddComponent<Camera>();screen.cullingMask=0;screen.depth=-10;screen.backgroundColor=new Color(.025f,.035f,.05f);screen.clearFlags=CameraClearFlags.SolidColor;
            RenderSettings.ambientMode=UnityEngine.Rendering.AmbientMode.Flat;RenderSettings.ambientLight=new Color(.55f,.58f,.65f);
            foreach(var setting in new[]{new Vector3(35,140,1.1f),new Vector3(25,-35,.6f)})
            {var light=new GameObject("Studio light").AddComponent<Light>();light.type=LightType.Directional;light.intensity=setting.z;light.transform.rotation=Quaternion.Euler(setting.x,setting.y,0);}
            var floor=GameObject.CreatePrimitive(PrimitiveType.Plane);floor.name="Practice floor";floor.transform.localScale=Vector3.one*.8f;Destroy(floor.GetComponent<Collider>());
            var material=new Material(Shader.Find("Standard"));material.color=new Color(.075f,.095f,.12f);material.SetFloat("_Glossiness",.15f);floor.GetComponent<Renderer>().sharedMaterial=material;
            stageCamera=new GameObject("Practice camera").AddComponent<Camera>();stageCamera.gameObject.AddComponent<AudioListener>();stageCamera.backgroundColor=new Color(.055f,.075f,.1f);stageCamera.clearFlags=CameraClearFlags.SolidColor;stageCamera.nearClipPlane=.01f;
            image=new RenderTexture(1240,820,24);image.Create();stageCamera.targetTexture=image;
            stageCamera.transform.position=new Vector3(0,1,3.7f);stageCamera.transform.LookAt(Vector3.up*.9f);
            orbit=gameObject.AddComponent<PracticeCamera>();
        }
        void SelectCharacter(int index)
        {
            if(index<0||index>=library.characters.Length)return;
            if(project!=null&&project.segments.Any(s=>s.locked))throw new InvalidOperationException("切换人物前请解锁片段。");
            var replacement=Instantiate(library.characters[index].prefab);
            try{playback.Bind(replacement,library);orbit.Initialize(stageCamera,replacement.transform);}
            catch{Destroy(replacement);throw;}
            if(actor)Destroy(actor);actor=replacement;selectedCharacter=index;
            if(project!=null)
            {var def=library.catalog.characters[index];project.characterId=def.id;project.characterSignature=def.signature;project.segments.Clear();project.position=0;selectedSegment=-1;message="人物已切换，请使用此人物复查过的动作重新编排。";}
            orbit.Bind(CameraState,view);
        }
        void Guard(Action action){try{action();error="";}catch(Exception exception){error=exception.Message;Debug.LogWarning(exception.Message);}}
        void Update()
        {
            if(analysisJob==null)return;
            analysisJob.Poll();
            if(!analysisJob.Finished)return;
            var finished=analysisJob;analysisJob=null;
            if(finished.ExitCode!=0){error=finished.Error;finished.Dispose();return;}
            string folder=finished.Folder;finished.Dispose();
            Guard(()=>{var analysis=JsonUtility.FromJson<MusicAnalysis>(File.ReadAllText(Path.Combine(folder,"analysis.json")));Choreographer.ValidateAnalysis(analysis);StartCoroutine(LoadAudio(folder,NewProject(analysis)));});
        }
        DanceProject NewProject(MusicAnalysis analysis)
        {
            var character=library.catalog.characters[selectedCharacter];
            var music=builtinMusic.tracks.FirstOrDefault(t=>t.id==selectedMusicId);
            return new DanceProject{title=music!=null?music.title:Path.GetFileNameWithoutExtension(analysis.sourceName),characterId=character.id,characterSignature=character.signature,analysis=analysis,grid=new GridSettings{bpm=analysis.bpm,manual=analysis.needsManualGrid},loopEnd=analysis.durationSeconds};
        }
        void LoadMusicLibrary()
        {
            string path=Path.Combine(MusicRoot,"catalog.json");if(!File.Exists(path))return;
            try
            {
                var catalog=JsonUtility.FromJson<BuiltinMusicCatalog>(File.ReadAllText(path));
                MusicLibrary.Validate(catalog,MusicRoot);builtinMusic=catalog;
            }
            catch(Exception exception){musicLibraryError=exception.Message;Debug.LogWarning(exception.Message);}
        }
        void SelectMusic(BuiltinMusicTrack track)
        {
            source=MusicLibrary.ResolveTrack(MusicRoot,track);selectedMusicId=track.id;startText="0";
            durationText=Math.Min(40,track.durationSeconds).ToString("0.###",CultureInfo.InvariantCulture);
            message="已选择 "+track.title+" · "+track.artist+"，点击分析音乐与准备慢放。";
        }
        void Analyze()
        {
            if(string.IsNullOrWhiteSpace(source)||!File.Exists(source))throw new InvalidOperationException("请先选择 WAV 或 MP3。");
            if(!double.TryParse(startText,NumberStyles.Float,CultureInfo.InvariantCulture,out double start)||!double.TryParse(durationText,NumberStyles.Float,CultureInfo.InvariantCulture,out double duration)||!Choreographer.Finite(start)||!Choreographer.Finite(duration)||start<0||duration<30||duration>60)throw new InvalidOperationException("起点须大于等于零，长度为 30～60 秒。");
            string folder=Path.Combine(ProjectRoot,DateTime.Now.ToString("yyyyMMdd-HHmmss")+"-"+Guid.NewGuid().ToString("N").Substring(0,6));
            string app=Path.GetDirectoryName(Application.dataPath);
            string python=AnalyzerRuntime.Resolve(app,PlayerPrefs.GetString("AIStudio.Python",""),Environment.GetEnvironmentVariable("SONG_CHOREO_PYTHON"));
            string analyzer=Path.Combine(Application.streamingAssetsPath,"Analyzer/analyzer.py");
            if(!File.Exists(analyzer))throw new FileNotFoundException("音乐分析组件未打包。");
            analysisJob=new AnalyzerJob(python,analyzer,new AnalysisRequest{sourcePath=source,outputDir=folder,startSeconds=start,durationSeconds=duration});
            message="正在分析，原项目保留到新音频全部加载完成。";
        }
        IEnumerator LoadAudio(string folder,DanceProject candidate,bool autoPlay=false)
        {
            busy=true;var clips=new AudioClip[3];string failure="";
            string[] names={candidate.analysis.normalAudio,candidate.analysis.slow075Audio,candidate.analysis.slow050Audio};
            for(int i=0;i<3;i++)
            {
                string path="";try{path=Choreographer.ResolveLocal(folder,names[i]);}catch(Exception e){failure=e.Message;}
                if(failure!="")break;
                using(var request=UnityWebRequestMultimedia.GetAudioClip(new Uri(path).AbsoluteUri,AudioType.WAV))
                {
                    yield return request.SendWebRequest();
                    if(request.result!=UnityWebRequest.Result.Success){failure="加载练习音频失败："+request.error;break;}
                    clips[i]=DownloadHandlerAudioClip.GetContent(request);
                }
            }
            busy=false;
            if(failure!=""){foreach(var clip in clips)if(clip)Destroy(clip);error=failure;yield break;}
            try
            {
                int index=Array.FindIndex(library.characters,a=>a.id==candidate.characterId);
                if(index<0)throw new InvalidOperationException("项目人物不在当前素材库。");
                // Swapping a loaded project must not mutate the old project's locks.
                if(index!=selectedCharacter){var previous=project;project=null;try{SelectCharacter(index);}finally{project=previous;}}
                project=candidate;projectFolder=folder;view=project.view;mirror=project.mirror;
                playback.SetProject(project,clips[0],clips[1],clips[2]);orbit.Bind(project.camera,view);
                bpmText=project.grid.bpm.ToString("0.###",CultureInfo.InvariantCulture);firstText=project.grid.firstBeatSeconds.ToString(CultureInfo.InvariantCulture);countText=(project.grid.eightCountOffset+1).ToString();
                loopStart=project.loopStart.ToString("0.###",CultureInfo.InvariantCulture);loopEnd=project.loopEnd.ToString("0.###",CultureInfo.InvariantCulture);
                selectedSegment=project.segments.Count>0?0:-1;message="音乐已就绪。先试听并校正首拍，再编排已复查动作。";error="";
                if(autoPlay){playback.Play();message="正在练习："+project.segments.Count+" 个八拍 · "+project.segments.Select(s=>s.motionId).Distinct().Count()+" 种动作。";}
            }
            catch(Exception e){foreach(var clip in clips)if(clip)Destroy(clip);error=e.Message;}
        }
        void Generate()
        {var result=Choreographer.Generate(project,library.catalog,motionLibrary.EnabledMotionIds);playback.Pause();project.segments=result;project.position=0;selectedSegment=0;playback.Seek(0);message="已编排 "+result.Count+" 个时槽；仅使用动作库设置选中的已复查动作与连接。";}
        void ApplyGrid()
        {
            if(!double.TryParse(bpmText,NumberStyles.Float,CultureInfo.InvariantCulture,out double bpm)||!double.TryParse(firstText,NumberStyles.Float,CultureInfo.InvariantCulture,out double first)||!int.TryParse(countText,out int count))throw new InvalidOperationException("节拍参数格式错误。");
            var grid=new GridSettings{manual=true,bpm=bpm,firstBeatSeconds=first,eightCountOffset=count-1};Choreographer.BeatTimes(project.analysis,grid);
            if(project.segments.Any(s=>s.locked))throw new InvalidOperationException("请先解锁片段，再校正节拍。");
            playback.Pause();project.grid=grid;project.segments.Clear();message="节拍已校正，请重新编排。";
        }
        void Save()
        {
            Choreographer.ValidateProject(project,library.catalog);
            string path=Path.Combine(projectFolder,"project.danceproj"),temporary=path+".tmp";File.WriteAllText(temporary,JsonUtility.ToJson(project,true));
            if(File.Exists(path))File.Replace(temporary,path,null);else File.Move(temporary,path);
            PlayerPrefs.SetString("AIStudio.LastProject",path);PlayerPrefs.Save();message="已保存："+path;
        }
        void OpenProject()
        {
            string file=NativeFiles.Open(true,Directory.Exists(projectFolder)?projectFolder:ProjectRoot);if(file==null)return;
            var candidate=JsonUtility.FromJson<DanceProject>(File.ReadAllText(file));Choreographer.ValidateProject(candidate,library.catalog);StartCoroutine(LoadAudio(Path.GetDirectoryName(file),candidate));
        }
        public void LoadPracticeExample(bool autoPlay=false)
        {
            if(Working)throw new InvalidOperationException("请等待当前音乐加载完成。");
            string bundled=Path.Combine(Application.streamingAssetsPath,"Practice100");
            var analysis=JsonUtility.FromJson<MusicAnalysis>(File.ReadAllText(Path.Combine(bundled,"analysis.json")));
            Choreographer.ValidateAnalysis(analysis);
            var candidate=NewProject(analysis);candidate.title="New Jazz 基础律动 · 原创 100 BPM";
            // This track's synthesis clock is known exactly. The detector's
            // approximate tempo remains in analysis.json for comparison.
            candidate.grid=new GridSettings{manual=true,bpm=100,firstBeatSeconds=0};
            candidate.segments=Choreographer.Generate(candidate,library.catalog,motionLibrary.EnabledMotionIds);
            Choreographer.ValidateProject(candidate,library.catalog);
            string folder=Path.Combine(ProjectRoot,"Practice100-"+DateTime.Now.ToString("yyyyMMdd-HHmmss")+"-"+Guid.NewGuid().ToString("N").Substring(0,6));
            Directory.CreateDirectory(folder);
            foreach(string name in new[]{analysis.normalAudio,analysis.slow075Audio,analysis.slow050Audio,"analysis.json","SOURCE.json"})
                File.Copy(Choreographer.ResolveLocal(bundled,name),Choreographer.ResolveLocal(folder,name));
            File.WriteAllText(Path.Combine(folder,"project.danceproj"),JsonUtility.ToJson(candidate,true));
            StartCoroutine(LoadAudio(folder,candidate,autoPlay));
        }
        void OpenSetup()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.ExecuteMenuItem("Tools/CHOREO AI/Generation console");
#else
            Application.OpenURL(new Uri(Path.Combine(Application.streamingAssetsPath,"Setup.html")).AbsoluteUri);
#endif
        }
        public void OpenMotionLibrary()
        {
            if(Working)throw new InvalidOperationException("请等待当前音乐加载完成。");
            if(library.characters.Length==0)throw new InvalidOperationException("请先导入人物。");
            playback.Pause();motionLibrary.Open(selectedCharacter);
        }
        void Styles()
        {
            if(stylesReady)return;stylesReady=true;font=Font.CreateDynamicFontFromOSFont(new[]{"Microsoft YaHei UI","Microsoft YaHei","Arial"},16);
            text=new GUIStyle(GUI.skin.label){font=font,fontSize=16,wordWrap=true,normal={textColor=new Color(.88f,.92f,.96f)}};
            muted=new GUIStyle(text){fontSize=13,normal={textColor=new Color(.53f,.63f,.72f)}};title=new GUIStyle(text){fontSize=30,fontStyle=FontStyle.Bold};
            button=new GUIStyle(GUI.skin.button){font=font,fontSize=15,padding=new RectOffset(12,12,8,8)};field=new GUIStyle(GUI.skin.textField){font=font,fontSize=16};card=new GUIStyle(GUI.skin.box){padding=new RectOffset(18,18,18,18)};
        }
        void Label(float x,float y,float width,float height,string value,GUIStyle style=null)=>GUI.Label(new Rect(x,y,width,height),value,style??text);
        bool Button(float x,float y,float width,float height,string value)=>GUI.Button(new Rect(x,y,width,height),value,button);
        string Input(float x,float y,float width,string value)=>GUI.TextField(new Rect(x,y,width,30),value,field);
        void Panel(float x,float y,float width,float height)=>GUI.Box(new Rect(x,y,width,height),"",card);
        void OnGUI()
        {
            Styles();GUI.matrix=Matrix4x4.Scale(new Vector3(Screen.width/Width,Screen.height/Height,1));
            if(motionLibrary&&motionLibrary.IsOpen){motionLibrary.Draw();GUI.matrix=Matrix4x4.identity;return;}
            Label(24,20,800,45,"CHOREO / AI",title);
            Label(24,68,1100,25,"TRIPO 人物  /  HUMANOID 动作库  /  本地音乐与练习",muted);
            if(Button(1040,25,240,42,"动作库设置"))Guard(OpenMotionLibrary);
            if(Button(1300,25,270,42,"生成与账号配置"))OpenSetup();
            if(library.characters.Length==0||library.motions.Length==0){Onboarding();GUI.matrix=Matrix4x4.identity;return;}
            Panel(20,110,290,735);Panel(330,110,905,735);Panel(1255,110,325,735);
            DrawImport();DrawStage();DrawInspector();DrawTransport();
            Label(25,923,1550,30,error!=""?error:message,error!=""?text:muted);
            GUI.matrix=Matrix4x4.identity;
        }
        void Onboarding()
        {
            Panel(160,180,1280,585);Label(205,220,1170,55,"开始创建你的舞者与动作库",title);
            Label(205,300,1120,75,library.characters.Length>0?"Tripo 人物已导入，下方可查看模型。导入并复查动作库 FBX 后，就能开始跟音乐练习。":"先准备 Tripo 人物和 Humanoid 动作。可使用 Mixamo 等现成动作库，也可生成动作；准备完成后会在这里显示。",text);
            Label(205,410,1100,150,"01  准备人物，下载 Mixamo 等动作库的 FBX\n\n02  在 Unity 的 Import motion FBX 入口导入动作\n\n03  预览骨骼、确认八拍拍点和连接，再开始跟音乐练习",text);
            if(library.characters.Length>0)
            {GUI.DrawTexture(new Rect(990,385,370,220),image,ScaleMode.ScaleToFit);Label(990,600,370,30,"已导入人物 · 等待动作",muted);}
            if(Button(205,625,325,50,"打开生成与配置入口"))OpenSetup();
            if(Button(550,625,200,50,"Tripo 开发者平台"))Application.OpenURL("https://developers.tripo3d.ai/en");
            if(Button(770,625,200,50,"Mixamo 动作库"))Application.OpenURL("https://www.mixamo.com/");
            Label(205,700,1100,35,"已导入人物："+library.characters.Length+"  /  已导入动作："+library.motions.Length,muted);
            if(library.importIssues.Length>0)Label(160,790,1280,110,string.Join("\n",library.importIssues.Take(3)),muted);
            if(error!="")Label(160,900,1280,40,error);
        }
        void DrawImport()
        {
            Label(38,130,250,30,"音乐与项目");GUI.enabled=!Working;
            if(Button(38,180,254,40,"选择 WAV / MP3"))Guard(()=>{var path=NativeFiles.Open(false);if(path!=null){source=path;selectedMusicId="";}});
            var selectedMusic=builtinMusic.tracks.FirstOrDefault(t=>t.id==selectedMusicId);
            Label(38,235,254,65,selectedMusic!=null?selectedMusic.title+"\n"+selectedMusic.artist:string.IsNullOrWhiteSpace(source)?"选择下方内置音乐或本地文件，截取 30～60 秒练习。":Path.GetFileName(source),muted);
            Label(38,318,110,25,"起点（秒）",muted);startText=Input(165,315,125,startText);
            Label(38,365,110,25,"长度（秒）",muted);durationText=Input(165,362,125,durationText);
            if(Button(38,420,254,42,"分析音乐与准备慢放"))Guard(Analyze);
            GUI.enabled=true;
            if(analysisJob!=null)
            {Label(38,480,254,100,analysisJob.Progress.message+"\n"+(analysisJob.Progress.progress*100).ToString("0")+"%",muted);if(Button(38,590,254,35,"取消分析")){analysisJob.Cancel();analysisJob.Dispose();analysisJob=null;message="已取消，原项目保留。";}}
            else DrawMusicLibrary();
            GUI.enabled=!Working;
            if(File.Exists(Path.Combine(Application.streamingAssetsPath,"Practice100/analysis.json"))&&Button(38,677,254,35,"100 BPM 练习示例"))Guard(()=>LoadPracticeExample(true));
            if(Button(38,725,120,38,"打开项目"))Guard(OpenProject);
            GUI.enabled=!Working&&project!=null;
            if(Button(170,725,120,38,"保存项目"))Guard(Save);
            GUI.enabled=true;
            if(Button(38,782,254,36,"打开项目目录")){Directory.CreateDirectory(ProjectRoot);Application.OpenURL(new Uri(string.IsNullOrEmpty(projectFolder)?ProjectRoot:projectFolder).AbsoluteUri);}
        }
        void DrawMusicLibrary()
        {
            Label(38,480,254,27,"内置跟练音乐 · "+builtinMusic.tracks.Count+" 首",muted);
            if(builtinMusic.tracks.Count==0)
            {Label(38,520,254,115,musicLibraryError!=""?musicLibraryError:"暂无内置音乐，可选择本地 WAV / MP3。",muted);return;}
            float listHeight=File.Exists(Path.Combine(Application.streamingAssetsPath,"Practice100/analysis.json"))?150:188;
            musicScroll=GUI.BeginScrollView(new Rect(38,515,254,listHeight),musicScroll,new Rect(0,0,230,builtinMusic.tracks.Count*61));
            GUI.enabled=!Working;
            for(int i=0;i<builtinMusic.tracks.Count;i++)
            {
                var track=builtinMusic.tracks[i];
                if(Button(0,i*61,230,32,(selectedMusicId==track.id?"♪ ":"")+track.title))Guard(()=>SelectMusic(track));
                int seconds=(int)Math.Round(track.durationSeconds);
                Label(4,i*61+34,223,24,track.genre+" · "+(seconds/60)+":"+(seconds%60).ToString("00"),muted);
            }
            GUI.enabled=true;GUI.EndScrollView();
        }
        void DrawStage()
        {
            Label(350,130,650,30,project!=null?project.title:"准备跟学");
            int change=GUI.SelectionGrid(new Rect(350,175,865,35),selectedCharacter,library.catalog.characters.Select(c=>c.name).ToArray(),Math.Max(1,library.characters.Length),button);
            if(change!=selectedCharacter&&!Working)Guard(()=>SelectCharacter(change));
            var area=new Rect(350,225,865,545);GUI.DrawTextureWithTexCoords(area,image,mirror?new Rect(1,0,-1,1):new Rect(0,0,1,1));
            if(project!=null&&project.segments.Count>0)
            {int index=playback.ActiveSegment();var segment=project.segments[index];var motion=library.catalog.motions.First(m=>m.id==segment.motionId);Label(365,240,600,35,motion.name);if(index+1<project.segments.Count)Label(365,280,700,30,"接下来："+library.catalog.motions.First(m=>m.id==project.segments[index+1].motionId).name,muted);double unit=Math.Max(0,Math.Min(.999999,(project.position-segment.startSeconds)/(segment.endSeconds-segment.startSeconds)));Label(1110,690,85,60,(1+segment.startBeatOffset+(int)(unit*segment.beatCount)).ToString(),title);}
            string[] views={"正面","背面","自由","左侧","右侧","上身","脚步"};
            for(int i=0;i<7;i++){int option=i;if(Button(350+i*99,790,92,35,views[i])){view=option;if(project!=null)project.view=view;orbit.Bind(CameraState,view);}}
            if(Button(1050,790,165,35,mirror?"镜像：开":"镜像：关")){mirror=!mirror;if(project!=null)project.mirror=mirror;}
            var e=Event.current;
            if(actor&&area.Contains(e.mousePosition)&&e.type==EventType.MouseDrag){if(e.button==0)orbit.Orbit(e.delta,mirror);else if(e.button==1)orbit.Pan(e.delta,mirror);view=PracticeCamera.Free;if(project!=null)project.view=view;e.Use();}
            if(actor&&area.Contains(e.mousePosition)&&e.type==EventType.ScrollWheel){orbit.Zoom(e.delta.y);e.Use();}
        }
        void DrawInspector()
        {
            Label(1275,130,285,30,"八拍编排");
            var character=library.catalog.characters[selectedCharacter];int approved=Choreographer.Eligible(library.catalog,character.id).Count;
            int enabled=Choreographer.Eligible(library.catalog,character.id,motionLibrary.EnabledMotionIds).Count;
            Label(1275,175,280,60,"自动编排已选："+enabled+" 种 / 已复查："+approved+"\n动作总数："+library.motions.Length+" · 已复查连接："+library.catalog.transitions.Count(e=>e.characterId==character.id),muted);
            GUI.enabled=project!=null&&!Working;
            if(Button(1275,245,285,42,"编排已复查动作"))Guard(Generate);
            if(project!=null)
            {
                Label(1275,305,55,25,"BPM",muted);bpmText=Input(1340,300,90,bpmText);
                Label(1275,345,60,25,"首拍秒",muted);firstText=Input(1340,340,90,firstText);
                Label(1440,345,50,25,"计数",muted);countText=Input(1490,340,70,countText);
                if(Button(1275,385,285,35,"应用节拍校正"))Guard(ApplyGrid);
                if(project.analysis.warnings.Count>0)Label(1275,435,285,55,project.analysis.warnings[0],muted);
                motionScroll=GUI.BeginScrollView(new Rect(1275,505,285,260),motionScroll,new Rect(0,0,262,Math.Max(260,project.segments.Count*50)));
                for(int i=0;i<project.segments.Count;i++)
                {
                    var segment=project.segments[i];var motion=library.catalog.motions.First(m=>m.id==segment.motionId);int at=i;
                    if(Button(0,i*50,209,43,(i+1)+"  "+motion.name)){selectedSegment=at;playback.Seek(segment.startSeconds);}
                    segment.locked=GUI.Toggle(new Rect(215,i*50+10,45,25),segment.locked,"锁");
                }
                GUI.EndScrollView();
                if(selectedSegment>=0&&selectedSegment<project.segments.Count&&Button(1275,782,285,36,"替换所选片段"))Guard(ReplaceSelected);
            }
            GUI.enabled=true;
        }
        void ReplaceSelected()
        {
            var s=project.segments[selectedSegment];if(s.locked)throw new InvalidOperationException("请先解锁所选片段。");
            var eligible=Choreographer.Eligible(library.catalog,project.characterId,motionLibrary.EnabledMotionIds).Where(m=>m.beats==8&&m.id!=s.motionId&&(selectedSegment==0||Choreographer.Edge(library.catalog,project.characterId,project.segments[selectedSegment-1].motionId,m.id)!=null)&&(selectedSegment==project.segments.Count-1||Choreographer.Edge(library.catalog,project.characterId,m.id,project.segments[selectedSegment+1].motionId)!=null)).ToList();
            if(eligible.Count==0)throw new InvalidOperationException("没有与前后片段均复查通过的替换动作。");
            var replacement=eligible[0];playback.Pause();s.motionId=replacement.id;s.motionSignature=replacement.signature;playback.Seek(s.startSeconds);message="已替换为 "+replacement.name+"；时间和节拍保持不变。";
        }
        void DrawTransport()
        {
            Panel(20,865,1560,46);GUI.enabled=project!=null&&!Working&&project.segments.Count>0;
            if(Button(30,870,90,35,playback.Playing?"暂停":"播放")){if(playback.Playing)playback.Pause();else playback.Play();}
            if(project!=null)
            {
                double position=GUI.HorizontalSlider(new Rect(140,885,550,20),(float)project.position,0,(float)project.analysis.durationSeconds);if(Math.Abs(position-project.position)>.04)playback.Seek(position);
                Label(705,877,160,27,project.position.ToString("0.0")+" / "+project.analysis.durationSeconds.ToString("0.0"),muted);
                float[] rates={.5f,.75f,1};for(int i=0;i<rates.Length;i++)if(Button(870+i*75,870,70,35,rates[i]+"×"))playback.SetRate(rates[i]);
                project.loop=GUI.Toggle(new Rect(1110,879,70,25),project.loop,"循环");loopStart=Input(1190,872,80,loopStart);loopEnd=Input(1280,872,80,loopEnd);
                if(Button(1380,870,180,35,"设置循环区间"))Guard(()=>{if(!double.TryParse(loopStart,NumberStyles.Float,CultureInfo.InvariantCulture,out double a)||!double.TryParse(loopEnd,NumberStyles.Float,CultureInfo.InvariantCulture,out double b)||!Choreographer.Finite(a)||!Choreographer.Finite(b)||a<0||b<=a||b>project.analysis.durationSeconds)throw new InvalidOperationException("循环区间无效。");project.loopStart=a;project.loopEnd=b;});
            }
            GUI.enabled=true;
        }
        IEnumerator Smoke()
        {
            yield return null;yield return new WaitForSeconds(1);
            string[] args=Environment.GetCommandLineArgs();int at=Array.IndexOf(args,"-report-dir");string folder=at>=0&&at+1<args.Length?args[at+1]:Path.Combine(Application.persistentDataPath,"Smoke");Directory.CreateDirectory(folder);
            ScreenCapture.CaptureScreenshot(Path.Combine(folder,"studio.png"));yield return new WaitForSeconds(.5f);
            if(actor)
            {
                stageCamera.Render();var previous=RenderTexture.active;RenderTexture.active=image;
                var capture=new Texture2D(image.width,image.height,TextureFormat.RGB24,false);capture.ReadPixels(new Rect(0,0,image.width,image.height),0,0);capture.Apply();RenderTexture.active=previous;
                File.WriteAllBytes(Path.Combine(folder,"character.png"),capture.EncodeToPNG());Destroy(capture);
            }
            File.WriteAllText(Path.Combine(folder,"report.json"),"{\"schemaVersion\":2,\"started\":true,\"characters\":"+library.characters.Length+",\"motions\":"+library.motions.Length+",\"legacyFallback\":false}");Application.Quit();
        }
        void OnDestroy(){analysisJob?.Dispose();if(image){image.Release();Destroy(image);}}
    }
}
