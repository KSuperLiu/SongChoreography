using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

namespace SongChoreography.Editor
{
    // Existing motion libraries use the same Humanoid/review pipeline as generated FBXs.
    public sealed class MotionLibraryImport : EditorWindow
    {
        string source="",id="",motionName="",provider="mixamo",sourceUrl="https://www.mixamo.com/";
        string termsUrl="https://helpx.adobe.com/creative-cloud/faq/mixamo-faq.html",tags="dance",status="";
        string clipName="";
        double bpm=120;
        float energy=.5f;
        Vector2 scroll;

        [MenuItem("Tools/CHOREO AI/Import motion FBX")]
        public static void Open()
        {var window=GetWindow<MotionLibraryImport>("导入动作 FBX");var p=window.position;window.position=new Rect(p.x,p.y,Mathf.Max(p.width,560),Mathf.Max(p.height,500));}
        void OnEnable(){minSize=new Vector2(560,500);}

        void OnGUI()
        {
            scroll=EditorGUILayout.BeginScrollView(scroll);
            EditorGUILayout.LabelField("已有动作库 → Tripo 人物",EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("导入下载好的双足 Humanoid FBX，不调用生成 API。Mixamo 建议导出 FBX for Unity、Without Skin、30 FPS、Keyframe Reduction: None。",MessageType.Info);
            if(GUILayout.Button("打开 Mixamo 动作库"))Application.OpenURL("https://www.mixamo.com/");
            EditorGUILayout.BeginHorizontal();
            source=EditorGUILayout.TextField("FBX 文件",source);
            if(GUILayout.Button("选择…",GUILayout.Width(70)))
            {
                string selected=EditorUtility.OpenFilePanel("选择动作 FBX","","fbx");
                if(!string.IsNullOrEmpty(selected))
                {
                    source=selected;motionName=Path.GetFileNameWithoutExtension(source);
                    id="mixamo-"+Regex.Replace(motionName.ToLowerInvariant(),"[^a-z0-9]+","-").Trim('-');
                    if(id.Length>64)id=id.Substring(0,64).TrimEnd('-');
                }
            }
            EditorGUILayout.EndHorizontal();
            id=EditorGUILayout.TextField("唯一 ID",id);motionName=EditorGUILayout.TextField("动作名称",motionName);
            provider=EditorGUILayout.TextField("来源平台",provider);sourceUrl=EditorGUILayout.TextField("来源网页",sourceUrl);
            termsUrl=EditorGUILayout.TextField("使用条款网页",termsUrl);
            tags=EditorGUILayout.TextField("真实舞种标签（逗号分隔）",tags);
            bpm=EditorGUILayout.DoubleField("预览 BPM（待校正）",bpm);energy=EditorGUILayout.Slider("动作强度",energy,0,1);
            clipName=EditorGUILayout.TextField("动画 Clip 名（单段可留空）",clipName);
            EditorGUILayout.HelpBox("先登记为八拍草稿。全片均分只用于开始预览，需要在素材工作台裁剪到真正的一组八拍、校正每拍并检查关节。Salsa、Rumba 等应保留原舞种，不能标为 New Jazz。",MessageType.Info);
            using(new EditorGUI.DisabledScope(EditorApplication.isPlayingOrWillChangePlaymode||EditorApplication.isCompiling))
                if(GUILayout.Button("导入并打开复查工作台"))
                {
                    try
                    {
                        var definition=new MotionDefinition
                        {
                            id=id.Trim(),name=motionName.Trim(),provider=provider.Trim(),sourceUrl=sourceUrl.Trim(),termsUrl=termsUrl.Trim(),
                            clipName=clipName.Trim(),sourceBpm=bpm,energy=energy,beats=8,
                            tags=tags.Split(new[]{',','，'},StringSplitOptions.RemoveEmptyEntries).Select(a=>a.Trim()).Where(a=>a.Length>0).Distinct().ToArray()
                        };
                        Register(source,definition);
                        status="已登记为待复查动作："+definition.id;
                        GeneratedAssetWorkbench.Open();
                    }
                    catch(Exception e){status="未导入："+e.Message;}
                }
            if(EditorApplication.isPlayingOrWillChangePlaymode)EditorGUILayout.HelpBox("请先停止 Play Mode 再导入。",MessageType.Info);
            if(!string.IsNullOrEmpty(status))EditorGUILayout.HelpBox(status,MessageType.None);
            EditorGUILayout.EndScrollView();
        }

        public static void Register(string source,MotionDefinition definition)
        {
            if(definition==null||!Regex.IsMatch(definition.id??"","^[a-z][a-z0-9-]{1,63}$"))throw new InvalidDataException("ID 需为 2～64 位小写字母、数字和连字符，以字母开头。");
            if(string.IsNullOrWhiteSpace(definition.name)||string.IsNullOrWhiteSpace(definition.provider))throw new InvalidDataException("请填写名称和来源平台。");
            if(!Choreographer.Finite(definition.sourceBpm)||definition.sourceBpm<60||definition.sourceBpm>180)throw new InvalidDataException("BPM 需在 60～180 之间。");
            if(!File.Exists(source)||!string.Equals(Path.GetExtension(source),".fbx",StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("请选择存在的 FBX 文件。");
            string catalogPath=Path.Combine(GeneratedAssetBuilder.Root,"Generation/library.json");
            var catalog=File.Exists(catalogPath)?JsonUtility.FromJson<AssetCatalog>(File.ReadAllText(catalogPath)):new AssetCatalog();
            if(catalog==null||catalog.schemaVersion!=2||catalog.characters==null||catalog.motions==null)throw new InvalidDataException("素材清单无效；未修改原清单。");
            if(catalog.characters.Any(a=>a.id==definition.id)||catalog.motions.Any(a=>a.id==definition.id))throw new InvalidDataException("ID 已存在，请使用新 ID；不会覆盖已有素材和复查。");
            string folder="Assets/Generated/Motions/"+definition.id;
            if(Directory.Exists(folder))throw new InvalidDataException("目标文件夹已存在，请使用新 ID："+folder);
            definition.sourceFile=folder+"/"+definition.id+".fbx";
            Directory.CreateDirectory(folder);
            // The original download is never moved or modified.
            File.Copy(source,definition.sourceFile,false);
            using(var sha=SHA256.Create())using(var stream=File.OpenRead(definition.sourceFile))
                definition.sha256=BitConverter.ToString(sha.ComputeHash(stream)).Replace("-","").ToLowerInvariant();
            definition.signature="";definition.reviewedCharacters.Clear();definition.beatTimes.Clear();
            catalog.motions.Add(definition);
            Directory.CreateDirectory(Path.GetDirectoryName(catalogPath));
            string temp=catalogPath+".tmp";
            File.WriteAllText(temp,JsonUtility.ToJson(catalog,true));
            if(File.Exists(catalogPath))File.Replace(temp,catalogPath,null);else File.Move(temp,catalogPath);
            GeneratedAssetBuilder.Import();
            var library=AssetDatabase.LoadAssetAtPath<GeneratedLibrary>(GeneratedAssetBuilder.LibraryPath);
            if(!library||!library.catalog.motions.Any(a=>a.id==definition.id))
                throw new InvalidDataException("FBX 已保留在待修复清单中。请查看素材工作台的导入问题，修复 Rig / Animation 后重新导入。"+
                    (library?"\n"+string.Join("\n",library.importIssues):""));
        }
    }
}
