using System;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace SongChoreography.Editor
{
    public sealed class GeneratedAssetWorkbench : EditorWindow
    {
        GeneratedLibrary library;
        PreviewRenderUtility preview;
        GameObject actor;
        MotionPlayer player;
        int characterIndex,motionIndex,nextIndex,view;
        float time,blendBeats=.5f;
        bool playing,transition,poseConfirmed,beatsConfirmed,hasWatched;
        double lastTime,maxWatched;
        string notes="";
        Vector2 scroll;
        double[] beatDraft;
        static double PhraseDuration(MotionDefinition motion)=>60*motion.beats/motion.sourceBpm;
        static double SampleTime(MotionDefinition motion,double phraseTime,double bpm)
        {return Choreographer.MotionTime(motion,new DanceSegment{startSeconds=0,endSeconds=60*motion.beats/bpm,beatCount=motion.beats},phraseTime);}
        [MenuItem("Tools/CHOREO AI/Generated asset workbench")]
        public static void Open()
        {var window=GetWindow<GeneratedAssetWorkbench>("生成素材工作台");var p=window.position;window.position=new Rect(p.x,p.y,Mathf.Max(p.width,620),Mathf.Max(p.height,700));}
        void OnEnable(){minSize=new Vector2(620,700);Reload();EditorApplication.update+=Tick;}
        void OnDisable(){EditorApplication.update-=Tick;DisposePreview();}
        void DisposePreview(){player?.Dispose();player=null;preview?.Cleanup();preview=null;actor=null;}
        void Reload(){DisposePreview();library=AssetDatabase.LoadAssetAtPath<GeneratedLibrary>(GeneratedAssetBuilder.LibraryPath);beatDraft=null;time=0;maxWatched=0;hasWatched=poseConfirmed=beatsConfirmed=false;Repaint();}
        void Tick()
        {
            double now=EditorApplication.timeSinceStartup;
            if(playing&&library&&library.catalog.motions.Count>0)
            {
                time+=(float)Math.Min(.1,now-lastTime);maxWatched+=Math.Min(.1,now-lastTime);
                var m=library.catalog.motions[motionIndex];
                float duration=(float)PhraseDuration(m)+(transition?1:0);
                if(maxWatched>=duration)hasWatched=true;
                if(time>duration)time=0;
                Repaint();
            }
            lastTime=now;
        }
        void OnGUI()
        {
            EditorGUILayout.HelpBox("下载的模型与动画先在这里预览。每个人物和有向连接分别复查，校正八拍后才能用于编排。",MessageType.Info);
            if(GUILayout.Button("导入已有动作 FBX（Mixamo 等）"))MotionLibraryImport.Open();
            if(GUILayout.Button("重新导入下载结果")){try{GeneratedAssetBuilder.Import();Reload();}catch(Exception e){EditorUtility.DisplayDialog("导入失败",e.Message,"关闭");}}
            if(!library){EditorGUILayout.LabelField("尚无素材库。先导入下载结果。");return;}
            foreach(string issue in library.importIssues)EditorGUILayout.HelpBox(issue,MessageType.Warning);
            if(library.characters.Length==0){EditorGUILayout.LabelField("先生成并导入一个 Tripo 人物。");return;}
            if(library.motions.Length==0)
            {
                int selected=EditorGUILayout.Popup("人物",Mathf.Clamp(characterIndex,0,library.characters.Length-1),library.catalog.characters.Select(a=>a.name).ToArray());
                if(selected!=characterIndex){characterIndex=selected;DisposePreview();}
                view=GUILayout.Toolbar(view,new[]{"正面","背面","左侧","右侧"});
                var area=GUILayoutUtility.GetRect(200,Mathf.Max(260,position.height*.65f),GUILayout.ExpandWidth(true));
                EnsurePreview();RenderPreview(area);
                EditorGUILayout.HelpBox("人物已导入，可先检查外观。通过上方入口导入动作库 FBX，再检查实际跳舞时的骨骼变形、拍点和衔接。",MessageType.Info);
                return;
            }
            characterIndex=Mathf.Clamp(characterIndex,0,library.characters.Length-1);motionIndex=Mathf.Clamp(motionIndex,0,library.motions.Length-1);nextIndex=Mathf.Clamp(nextIndex,0,library.motions.Length-1);
            EditorGUI.BeginChangeCheck();
            characterIndex=EditorGUILayout.Popup("人物",characterIndex,library.catalog.characters.Select(a=>a.name).ToArray());
            motionIndex=EditorGUILayout.Popup("动作",motionIndex,library.catalog.motions.Select(a=>a.name).ToArray());
            transition=EditorGUILayout.Toggle("预览有向连接",transition);
            if(transition){nextIndex=EditorGUILayout.Popup("后继动作",nextIndex,library.catalog.motions.Select(a=>a.name).ToArray());blendBeats=EditorGUILayout.Slider("混合拍数",blendBeats,.1f,1);}
            if(EditorGUI.EndChangeCheck()){DisposePreview();beatDraft=null;time=0;maxWatched=0;hasWatched=poseConfirmed=beatsConfirmed=false;}
            var character=library.catalog.characters[characterIndex];var motion=library.catalog.motions[motionIndex];
            view=GUILayout.Toolbar(view,new[]{"正面","背面","左侧","右侧"});
            Rect rect=GUILayoutUtility.GetRect(200,Mathf.Max(180,position.height*.38f),GUILayout.ExpandWidth(true));
            DrawPreview(rect,motion);
            playing=GUILayout.Toggle(playing,"播放实际骨骼动画","Button");
            time=EditorGUILayout.Slider("节拍时间轴",time,0,(float)PhraseDuration(motion)+(transition?1:0));
            EditorGUILayout.LabelField("预览 BPM："+motion.sourceBpm.ToString("0.##")+"；使用与成品相同的拍点插值与连接混合。",EditorStyles.miniLabel);
            scroll=EditorGUILayout.BeginScrollView(scroll);
            if(!transition)
            {
                EditorGUILayout.LabelField("确认每拍对应的动画秒数（首尾也是裁剪边界）",EditorStyles.boldLabel);
                var draft=beatDraft??(beatDraft=motion.beatTimes.ToArray());
                for(int i=0;i<draft.Length;i++)draft[i]=EditorGUILayout.DoubleField(i==motion.beats?"结束":"第 "+(i+1)+" 拍",draft[i]);
                if(GUILayout.Button("保存拍点并使旧复查失效"))
                {
                    bool valid=draft.Length==motion.beats+1&&draft[0]>=0&&draft.Last()<=library.Motion(motion.id).clip.length;
                    for(int i=0;i<draft.Length;i++)valid&=Choreographer.Finite(draft[i])&&(i==0||draft[i]>draft[i-1]);
                    if(!valid)EditorUtility.DisplayDialog("拍点无效","拍点必须递增且位于动画内。","关闭");
                    else{var reviews=GeneratedAssetBuilder.Reviews();reviews.clips.RemoveAll(a=>a.id==motion.id);reviews.clips.Add(new ClipSettings{id=motion.id,sourceHash=motion.sha256,clipName=motion.clipName,beatTimes=draft});GeneratedAssetBuilder.SaveReviews(reviews);Reload();}
                }
                beatsConfirmed=EditorGUILayout.Toggle("已试听节拍并确认拍点",beatsConfirmed);
            }
            poseConfirmed=EditorGUILayout.Toggle("已检查关节、接地、穿插和舞风",poseConfirmed);
            notes=EditorGUILayout.TextField("复查说明",notes);
            bool unsavedBeats=!transition&&beatDraft!=null&&!beatDraft.SequenceEqual(motion.beatTimes);
            if(unsavedBeats)EditorGUILayout.HelpBox("拍点已编辑，请先保存并重新预览，再记录复查。",MessageType.Warning);
            bool canApprove=!unsavedBeats&&hasWatched&&poseConfirmed&&!string.IsNullOrWhiteSpace(notes)&&(transition||beatsConfirmed);
            var next=library.catalog.motions[nextIndex];
            if(transition)canApprove&=motion.reviewedCharacters.Contains(character.id)&&next.reviewedCharacters.Contains(character.id);
            using(new EditorGUI.DisabledScope(!canApprove))if(GUILayout.Button(transition?"记录此有向连接的复查":"记录此人物上动作的复查"))
            {
                var reviews=GeneratedAssetBuilder.Reviews();
                if(transition)
                {reviews.transitions.RemoveAll(r=>r.characterId==character.id&&r.from==motion.id&&r.to==next.id);reviews.transitions.Add(new TransitionReview{characterId=character.id,characterSignature=character.signature,from=motion.id,fromSignature=motion.signature,to=next.id,toSignature=next.signature,blendBeats=blendBeats,notes=notes});}
                else
                {reviews.motions.RemoveAll(r=>r.characterId==character.id&&r.motionId==motion.id);reviews.motions.Add(new MotionReview{characterId=character.id,characterSignature=character.signature,motionId=motion.id,motionSignature=motion.signature,notes=notes});}
                GeneratedAssetBuilder.SaveReviews(reviews);Reload();
            }
            EditorGUILayout.LabelField(hasWatched?"已播放完整预览；仍需人工判断质量。":"先完整播放预览，再记录复查。");
            EditorGUILayout.EndScrollView();
        }
        void DrawPreview(Rect rect,MotionDefinition motion)
        {
            EnsurePreview();
            double length=PhraseDuration(motion);
            if(transition&&time>=length)
            {
                var next=library.catalog.motions[nextIndex];double dt=time-length,blend=blendBeats*60/motion.sourceBpm;
                player.Sample(library.Motion(next.id).clip,SampleTime(next,dt,motion.sourceBpm),dt<blend?library.Motion(motion.id).clip:null,motion.clipEnd,Mathf.SmoothStep(0,1,(float)(dt/blend)));
            }
            else player.Sample(library.Motion(motion.id).clip,SampleTime(motion,Math.Min(time,length),motion.sourceBpm));
            RenderPreview(rect);
        }
        void EnsurePreview()
        {
            if(preview==null)
            {
                preview=new PreviewRenderUtility();actor=Instantiate(library.characters[characterIndex].prefab);preview.AddSingleGO(actor);player=new MotionPlayer(actor);
                preview.camera.nearClipPlane=.01f;preview.camera.farClipPlane=20;preview.camera.fieldOfView=35;
                preview.lights[0].intensity=1.2f;preview.lights[0].transform.rotation=Quaternion.Euler(35,35,0);preview.lights[1].intensity=.8f;preview.ambientColor=new Color(.5f,.5f,.5f);
            }
        }
        void RenderPreview(Rect rect)
        {
            preview.camera.transform.position=Quaternion.Euler(0,view==1?180:view==2?90:view==3?-90:0,0)*new Vector3(0,0,3.7f)+Vector3.up*.9f;
            preview.camera.transform.LookAt(Vector3.up*.9f);preview.camera.backgroundColor=new Color(.06f,.08f,.1f);preview.camera.clearFlags=CameraClearFlags.SolidColor;
            preview.BeginPreview(rect,GUIStyle.none);preview.Render();GUI.DrawTexture(rect,preview.EndPreview(),ScaleMode.ScaleToFit);
        }
    }
}
