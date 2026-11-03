using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace SongChoreography
{
    public sealed class MotionLibrarySettings : MonoBehaviour
    {
        [Serializable] class Recommendations {public int schemaVersion;public string title;public string[] motionIds=new string[0];}
        [Serializable] class TechnicalIssue {public string id,sourceHash,issue;}
        [Serializable] class TechnicalChecks {public string characterSignature;public TechnicalIssue[] issues=new TechnicalIssue[0];}
        const int PreviewLayer=30;
        readonly Vector3 previewOrigin=new Vector3(100,0,0);
        GeneratedLibrary library;MotionLibraryPreferences current=new MotionLibraryPreferences();
        readonly HashSet<string> draft=new HashSet<string>(StringComparer.Ordinal),recommended=new HashSet<string>(StringComparer.Ordinal);
        readonly Dictionary<string,TechnicalIssue> technicalIssues=new Dictionary<string,TechnicalIssue>();
        string technicalCharacterSignature="";
        string[] categories;int character;string category="recommended",search="",selectedId="",error="";
        bool custom,previewPlaying=true,fullSource;float cursor,rate=1,loopBlendRemaining,loopOutgoingTime;int view;
        Vector2 categoryScroll,listScroll;Action changed;
        GameObject actor,floor;Camera previewCamera;RenderTexture texture;Material floorMaterial;MotionPlayer player;
        GUIStyle text,muted,title,button,field,row,toggle;Font font;
        public bool IsOpen{get;private set;}
        public string PreviewedMotionId=>selectedId;
        public float PreviewSourceTime=>cursor;
        public string SettingsPath=>Path.Combine(Application.persistentDataPath,"motion-library-settings.json");
        public IEnumerable<string> EnabledMotionIds=>current.customSelection?current.motionIds:null;
        public void Initialize(GeneratedLibrary assets,Action onChanged)
        {
            library=assets;changed=onChanged;recommended.Clear();technicalIssues.Clear();technicalCharacterSignature="";
            try
            {
                if(File.Exists(SettingsPath))current=JsonUtility.FromJson<MotionLibraryPreferences>(File.ReadAllText(SettingsPath));
                if(current==null||current.schemaVersion!=1||current.motionIds==null)throw new InvalidDataException("动作库设置版本无效。");
            }
            catch(Exception e){current=new MotionLibraryPreferences();error="未能读取原设置："+e.Message;}
            var data=Resources.Load<TextAsset>("NewJazzRecommendations");
            if(data){var rec=JsonUtility.FromJson<Recommendations>(data.text);if(rec!=null&&rec.schemaVersion==1)foreach(var id in rec.motionIds)recommended.Add(id);}
            var checks=Resources.Load<TextAsset>("MotionTechnicalChecks");if(checks){var info=JsonUtility.FromJson<TechnicalChecks>(checks.text);if(info!=null&&library.catalog.characters.Any(c=>c.signature==info.characterSignature)){technicalCharacterSignature=info.characterSignature;foreach(var issue in info.issues)technicalIssues[issue.id]=issue;}}
            categories=library.catalog.motions.Select(Category).Distinct().OrderBy(CategoryName,StringComparer.Ordinal).ToArray();
        }
        public void Open(int characterIndex)
        {
            character=characterIndex;custom=current.customSelection;draft.Clear();
            foreach(var motion in library.catalog.motions)if(custom?current.motionIds.Contains(motion.id):Reviewed(motion))draft.Add(motion.id);
            IsOpen=true;category="recommended";search="";listScroll=Vector2.zero;
            var first=Filtered().FirstOrDefault()??library.catalog.motions.FirstOrDefault();if(first!=null)Preview(first.id);
        }
        public void SetCategory(string value){category=value;listScroll=Vector2.zero;}
        public void SetSearch(string value){search=value??"";listScroll=Vector2.zero;}
        public void SetSelected(string id,bool enabled)
        {if(!library.catalog.motions.Any(m=>m.id==id))return;custom=true;if(enabled)draft.Add(id);else draft.Remove(id);}
        public void UseRecommendations()
        {custom=true;draft.Clear();foreach(var id in recommended)if(library.catalog.motions.Any(m=>m.id==id))draft.Add(id);}
        public void SaveAndClose()
        {
            var candidate=new MotionLibraryPreferences{customSelection=custom,motionIds=draft.OrderBy(id=>id,StringComparer.Ordinal).ToList()};
            string temporary=SettingsPath+".tmp";Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath));File.WriteAllText(temporary,JsonUtility.ToJson(candidate,true));
            if(File.Exists(SettingsPath))File.Replace(temporary,SettingsPath,null);else File.Move(temporary,SettingsPath);
            current=candidate;CloseWithoutSaving();changed?.Invoke();
        }
        public void CloseWithoutSaving(){IsOpen=false;DestroyPreview();}
        bool Reviewed(MotionDefinition motion)=>library.catalog.characters.Count>character&&motion.reviewedCharacters.Contains(library.catalog.characters[character].id);
        string TechnicalStatus(MotionDefinition motion)=>library.catalog.characters.Count>character&&library.catalog.characters[character].signature==technicalCharacterSignature&&technicalIssues.TryGetValue(motion.id,out var issue)&&issue.sourceHash==motion.sha256?issue.issue:"";
        static string Category(MotionDefinition motion)=>motion.tags.FirstOrDefault(t=>t!="draft"&&t!="reviewed"&&t!="new-jazz-foundation")??"other";
        static string CategoryName(string key)
        {
            switch(key)
            {
                case "hip-hop":return "Hip Hop";case "jazz":return "Jazz 爵士";case "breakdance":return "Breaking 地板舞";
                case "salsa":return "Salsa";case "samba":return "Samba";case "swing":return "Swing";case "house":return "House";
                case "belly-dance":return "肚皮舞";case "ballet":return "芭蕾";case "northern-soul":return "Northern Soul";case "rumba":return "Rumba";
                case "pose":return "静态姿势";case "gesture":return "手势";case "locomotion":return "走跑移动";case "dance-other":return "其他舞蹈";case "test":return "测试片段";default:return "其他";
            }
        }
        List<MotionDefinition> Filtered()
        {
            return library.catalog.motions.Where(m=>(category=="all"||category=="recommended"&&recommended.Contains(m.id)||category=="selected"&&draft.Contains(m.id)||Category(m)==category)
                &&(string.IsNullOrWhiteSpace(search)||(m.name+" "+m.id+" "+string.Join(" ",m.tags)).IndexOf(search,StringComparison.OrdinalIgnoreCase)>=0)).ToList();
        }
        void EnsurePreview()
        {
            if(actor)return;
            actor=Instantiate(library.characters[character].prefab);actor.name="Library preview dancer";actor.transform.position=previewOrigin;
            foreach(var t in actor.GetComponentsInChildren<Transform>())t.gameObject.layer=PreviewLayer;
            player=new MotionPlayer(actor);
            floor=GameObject.CreatePrimitive(PrimitiveType.Plane);floor.name="Library preview floor";floor.layer=PreviewLayer;floor.transform.position=previewOrigin;Destroy(floor.GetComponent<Collider>());
            floorMaterial=new Material(Shader.Find("Standard")){color=new Color(.13f,.16f,.2f)};floor.GetComponent<Renderer>().sharedMaterial=floorMaterial;
            previewCamera=new GameObject("Library preview camera").AddComponent<Camera>();previewCamera.cullingMask=1<<PreviewLayer;previewCamera.clearFlags=CameraClearFlags.SolidColor;previewCamera.backgroundColor=new Color(.055f,.075f,.1f);previewCamera.nearClipPlane=.01f;previewCamera.farClipPlane=25;previewCamera.fieldOfView=35;
            texture=new RenderTexture(720,540,24);texture.Create();previewCamera.targetTexture=texture;SetView(0);
        }
        void SetView(int value)
        {view=value;if(!previewCamera)return;previewCamera.transform.position=previewOrigin+Quaternion.Euler(0,value==1?90:value==2?180:0,0)*new Vector3(0,.95f,3.8f);previewCamera.transform.LookAt(previewOrigin+Vector3.up*.88f);}
        public void Preview(string id)
        {
            var motion=library.catalog.motions.FirstOrDefault(m=>m.id==id);if(motion==null)return;
            try{EnsurePreview();selectedId=id;cursor=fullSource?0:(float)motion.clipStart;loopBlendRemaining=0;previewPlaying=true;player.Sample(library.Motion(id).clip,cursor);error="";}
            catch(Exception e){error=e.Message;}
        }
        void Update()
        {
            if(!IsOpen||player==null||string.IsNullOrEmpty(selectedId))return;
            try
            {
                var motion=library.catalog.motions.First(m=>m.id==selectedId);var clip=library.Motion(selectedId).clip;
                float start=fullSource?0:(float)motion.clipStart,end=fullSource?clip.length:(float)motion.clipEnd;
                if(previewPlaying)
                {
                    float next=cursor+Time.unscaledDeltaTime*rate;
                    if(next>=end){loopOutgoingTime=end-.00001f;loopBlendRemaining=.18f;}
                    cursor=start+Mathf.Repeat(next-start,Mathf.Max(.001f,end-start));
                }
                if(loopBlendRemaining>0)
                {
                    if(previewPlaying)loopBlendRemaining=Mathf.Max(0,loopBlendRemaining-Time.unscaledDeltaTime);
                    player.Sample(clip,cursor,clip,loopOutgoingTime,Mathf.SmoothStep(0,1,1-loopBlendRemaining/.18f));
                }
                else player.Sample(clip,cursor);
            }
            catch(Exception e){previewPlaying=false;error=e.Message;}
        }
        void Styles()
        {
            if(text!=null)return;font=Font.CreateDynamicFontFromOSFont(new[]{"Microsoft YaHei UI","Microsoft YaHei","Arial"},16);
            text=new GUIStyle(GUI.skin.label){font=font,fontSize=16,wordWrap=true,normal={textColor=new Color(.88f,.93f,.97f)}};
            muted=new GUIStyle(text){fontSize=13,normal={textColor=new Color(.57f,.67f,.77f)}};title=new GUIStyle(text){fontSize=28,fontStyle=FontStyle.Bold};
            button=new GUIStyle(GUI.skin.button){font=font,fontSize=15};field=new GUIStyle(GUI.skin.textField){font=font,fontSize=16};row=new GUIStyle(GUI.skin.box);toggle=new GUIStyle(GUI.skin.toggle){font=font,fontSize=15};
        }
        void Label(float x,float y,float w,float h,string value,GUIStyle style=null)=>GUI.Label(new Rect(x,y,w,h),value,style??text);
        bool Button(float x,float y,float w,float h,string value)=>GUI.Button(new Rect(x,y,w,h),value,button);
        void Guard(Action action){try{action();error="";}catch(Exception e){error=e.Message;}}
        public void Draw()
        {
            Styles();Label(28,20,800,42,"动作库设置",title);
            Label(28,67,1230,28,"预览全部动作，按类型挑选；勾选决定下次自动编排的动作范围，现有编舞保持不变。",muted);
            if(Button(1320,25,250,40,"返回工作室（不保存）")){CloseWithoutSaving();return;}
            GUI.Box(new Rect(20,110,220,720),"",row);GUI.Box(new Rect(250,110,590,720),"",row);GUI.Box(new Rect(850,110,730,720),"",row);
            Label(38,130,185,28,"动作分类");categoryScroll=GUI.BeginScrollView(new Rect(35,170,190,635),categoryScroll,new Rect(0,0,168,(categories.Length+3)*42));
            var keys=new[]{"recommended","all","selected"}.Concat(categories).ToArray();
            for(int i=0;i<keys.Length;i++)
            {
                string key=keys[i];int count=library.catalog.motions.Count(m=>key=="all"||key=="recommended"&&recommended.Contains(m.id)||key=="selected"&&draft.Contains(m.id)||Category(m)==key);
                string name=key=="recommended"?"New Jazz 推荐":key=="all"?"全部动作":key=="selected"?"我的选择":CategoryName(key);
                if(Button(0,i*42,166,36,(category==key?"● ":"")+name+"  "+count))SetCategory(key);
            }
            GUI.EndScrollView();Label(268,130,70,28,"搜索");string next=GUI.TextField(new Rect(335,128,480,30),search,field);if(next!=search)SetSearch(next);
            var visible=Filtered();Label(268,175,550,28,"当前分类 "+visible.Count+" 段 · 勾选入选，点击名称预览",muted);
            listScroll=GUI.BeginScrollView(new Rect(265,215,560,555),listScroll,new Rect(0,0,536,Math.Max(555,visible.Count*66)));
            for(int i=0;i<visible.Count;i++)
            {
                var motion=visible[i];float y=i*66;GUI.Box(new Rect(0,y,532,60),"",row);
                bool enabled=draft.Contains(motion.id),value=GUI.Toggle(new Rect(12,y+18,25,28),enabled,"",toggle);if(value!=enabled)SetSelected(motion.id,value);
                if(Button(45,y+6,475,29,(selectedId==motion.id?"▶ ":"")+motion.name))Preview(motion.id);
                string issue=TechnicalStatus(motion);Label(47,y+37,475,22,CategoryName(Category(motion))+" · "+(Reviewed(motion)?"可编排 · "+motion.beats+"拍":issue!=""?"适配待修复":"待复查")+(issue!=""?" · 原片"+issue:recommended.Contains(motion.id)?" · New Jazz 推荐":""),muted);
            }
            GUI.EndScrollView();if(Button(270,782,260,32,"勾选此分类")){foreach(var motion in visible)SetSelected(motion.id,true);}if(Button(545,782,270,32,"取消此分类")){foreach(var motion in visible)SetSelected(motion.id,false);}
            Label(870,130,675,32,"人物动作预览");if(texture)GUI.DrawTexture(new Rect(870,170,690,470),texture,ScaleMode.ScaleToFit);
            if(!string.IsNullOrEmpty(selectedId))
            {
                var motion=library.catalog.motions.First(m=>m.id==selectedId);var clip=library.Motion(selectedId).clip;
                string issue=TechnicalStatus(motion);Label(875,644,675,30,motion.name);Label(875,677,675,28,(Reviewed(motion)?"已复查八拍":"候选动作 · 待复查")+(issue!=""?" / 完整原片"+issue:"")+" / "+motion.provider+" / "+CategoryName(Category(motion)),muted);
                if(Button(875,717,90,32,previewPlaying?"暂停":"播放"))previewPlaying=!previewPlaying;
                if(Button(975,717,90,32,rate==1?"1×":"0.5×"))rate=rate==1?.5f:1;
                if(Button(1076,717,85,32,"正面"))SetView(0);if(Button(1171,717,85,32,"侧面"))SetView(1);if(Button(1266,717,85,32,"背面"))SetView(2);
                bool full=GUI.Toggle(new Rect(1370,721,180,28),fullSource,"完整原片",toggle);if(full!=fullSource){fullSource=full;cursor=fullSource?0:(float)motion.clipStart;loopBlendRemaining=0;}
                float start=fullSource?0:(float)motion.clipStart,end=fullSource?clip.length:(float)motion.clipEnd;
                float scrub=GUI.HorizontalSlider(new Rect(885,776,520,22),cursor,start,end);if(Mathf.Abs(scrub-cursor)>.0001f){cursor=scrub;loopBlendRemaining=0;}Label(1420,766,130,26,(cursor-start).ToString("0.0")+" / "+(end-start).ToString("0.0"),muted);
            }
            int ready=library.catalog.motions.Count(m=>draft.Contains(m.id)&&Reviewed(m));Label(30,846,1520,30,"已勾选 "+draft.Count+" 段 · 当前人物可编排 "+ready+" 段 · 待复查 "+(draft.Count-ready)+" 段。自动编排还需相应动作连接可用。",muted);
            if(Button(30,887,240,42,"使用 New Jazz 推荐"))UseRecommendations();
            if(Button(285,887,240,42,"恢复全部已复查")){custom=false;draft.Clear();foreach(var motion in library.catalog.motions.Where(Reviewed))draft.Add(motion.id);}
            if(Button(1295,887,275,42,"保存选择并返回"))Guard(SaveAndClose);
            if(error!="")Label(545,887,730,55,error,text);
        }
        void DestroyPreview()
        {
            player?.Dispose();player=null;if(actor)Destroy(actor);if(floor)Destroy(floor);if(previewCamera){previewCamera.targetTexture=null;Destroy(previewCamera.gameObject);}if(texture){texture.Release();Destroy(texture);}if(floorMaterial)Destroy(floorMaterial);
            actor=floor=null;previewCamera=null;texture=null;floorMaterial=null;
        }
        void OnDestroy(){DestroyPreview();if(font)Destroy(font);}
    }
}
