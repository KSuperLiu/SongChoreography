using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;

namespace SongChoreography.Editor
{
    [Serializable] public class CharacterPlan
    {public string id="",name="",prompt="";public int seed,faceLimit=40000;}
    [Serializable] public class MotionPlan
    {public string id="",name="",prompt="";public int seed,beats=8;public float bpm=120,energy=.5f;public string[] tags=new string[0];}
    [Serializable] public class TripoConfig {public string model="",rigModel="";}
    [Serializable] public class FalConfig {public string model="";}
    [Serializable] public class GenerationPlan
    {public int schemaVersion=2;public TripoConfig tripo;public FalConfig fal;public CharacterPlan[] characters;public MotionPlan[] motions;}
    public sealed class GenerationConsole : EditorWindow
    {
        string tripoKey="",falKey="",output="",python="",recoveryId="",recoveryTask="";
        int characterIndex,motionIndex,recoveryStage;
        bool paidAcknowledged;
        GenerationPlan plan;
        Process process;
        Task<string> stdout,stderr;
        Vector2 scroll;
        [MenuItem("Tools/CHOREO AI/Generation console")]
        public static void Open()=>GetWindow<GenerationConsole>("生成与配置");
        void OnEnable()
        {
            LoadPlan();python=File.Exists("Runtime/Python/python.exe")?Path.GetFullPath("Runtime/Python/python.exe"):"python";
            if(File.Exists(".env"))foreach(string line in File.ReadAllLines(".env"))
            {int at=line.IndexOf('=');if(at<0)continue;string key=line.Substring(0,at).Trim(),value=line.Substring(at+1).Trim();if(key=="TRIPO_API_KEY")tripoKey=value;else if(key=="FAL_KEY")falKey=value;}
            EditorApplication.update+=Poll;
        }
        void OnDisable(){EditorApplication.update-=Poll;if(process!=null){if(!process.HasExited)process.Kill();process.Dispose();process=null;}}
        void LoadPlan(){if(File.Exists("Generation/plan.json"))plan=JsonUtility.FromJson<GenerationPlan>(File.ReadAllText("Generation/plan.json"));}
        void Poll()
        {
            if(process==null||!process.HasExited||!stdout.IsCompleted||!stderr.IsCompleted)return;
            output=(process.ExitCode==0?"完成\n":"未完成\n")+stdout.Result+stderr.Result;process.Dispose();process=null;Repaint();
        }
        static string Quote(string value){if(value.Contains("\"")||value.Contains("\n")||value.Contains("\r"))throw new ArgumentException("参数包含非法字符。");return "\""+value+"\"";}
        void Run(string command)
        {
            try
            {
                var info=new ProcessStartInfo{FileName=python,Arguments="-I -X utf8 "+Quote(Path.GetFullPath("Tools/generation.py"))+" "+command,WorkingDirectory=Path.GetFullPath("."),UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true};
                process=Process.Start(info);stdout=process.StandardOutput.ReadToEndAsync();stderr=process.StandardError.ReadToEndAsync();output="正在执行；关闭窗口会停止本地查询，远端任务仍保留，可稍后续查。";
            }
            catch(Exception e){output=e.Message;process=null;}
        }
        void OnGUI()
        {
            scroll=EditorGUILayout.BeginScrollView(scroll);
            EditorGUILayout.LabelField("Tripo 人物 · fal 动作",EditorStyles.boldLabel);
            if(GUILayout.Button("使用已有动作库：导入 FBX（无需 fal 额度）"))MotionLibraryImport.Open();
            EditorGUILayout.HelpBox("账号、额度和 API Key 由平台管理。Key 只保存在项目根目录 .env，不进入 Assets、构建或 Git。网站会员是否包含 API 额度，以平台 API 账单为准。",MessageType.Info);
            EditorGUILayout.BeginHorizontal();if(GUILayout.Button("Tripo API 账号 / Key"))Application.OpenURL("https://developers.tripo3d.ai/en/keys");if(GUILayout.Button("fal 控制台 / 额度"))Application.OpenURL("https://fal.ai/dashboard");EditorGUILayout.EndHorizontal();
            tripoKey=EditorGUILayout.PasswordField("TRIPO_API_KEY",tripoKey);falKey=EditorGUILayout.PasswordField("FAL_KEY",falKey);
            if(GUILayout.Button("保存到本机 .env"))
            {
                if(tripoKey.Contains("\n")||tripoKey.Contains("\r")||falKey.Contains("\n")||falKey.Contains("\r"))output="Key 必须是单行。";
                else{File.WriteAllText(".env","TRIPO_API_KEY="+tripoKey.Trim()+"\nFAL_KEY="+falKey.Trim()+"\n");output="已保存到本机 .env，未显示密钥。";}
            }
            python=EditorGUILayout.TextField("Python 路径",python);
            using(new EditorGUI.DisabledScope(process!=null))if(GUILayout.Button("检查配置（只报告是否配置）"))Run("doctor");
            EditorGUILayout.Space();
            if(plan==null||plan.characters==null||plan.motions==null){EditorGUILayout.HelpBox("缺少生成计划 Generation/plan.json。",MessageType.Warning);EditorGUILayout.EndScrollView();return;}
            paidAcknowledged=EditorGUILayout.ToggleLeft("我已查看平台价格；生成及骨骼绑定会消耗 API 额度",paidAcknowledged);
            if(plan.characters.Length>0)
            {
                characterIndex=Mathf.Clamp(characterIndex,0,plan.characters.Length-1);characterIndex=EditorGUILayout.Popup("人物方案",characterIndex,plan.characters.Select(a=>a.name).ToArray());
                var character=plan.characters[characterIndex];EditorGUILayout.LabelField(character.id,EditorStyles.miniLabel);character.prompt=EditorGUILayout.TextArea(character.prompt,GUILayout.MinHeight(80));
                using(new EditorGUI.DisabledScope(!paidAcknowledged||process!=null))if(GUILayout.Button("提交此人物方案")){SavePlan();Run("character "+Quote(character.id));}
            }
            if(plan.motions.Length>0)
            {
                motionIndex=Mathf.Clamp(motionIndex,0,plan.motions.Length-1);motionIndex=EditorGUILayout.Popup("动作候选",motionIndex,plan.motions.Select(a=>a.name).ToArray());
                var motion=plan.motions[motionIndex];EditorGUILayout.LabelField(motion.id,EditorStyles.miniLabel);motion.prompt=EditorGUILayout.TextArea(motion.prompt,GUILayout.MinHeight(65));motion.bpm=EditorGUILayout.FloatField("目标 BPM（仍须复查踩拍）",motion.bpm);motion.seed=EditorGUILayout.IntField("种子",motion.seed);
                using(new EditorGUI.DisabledScope(!paidAcknowledged||process!=null))if(GUILayout.Button("提交此动作候选")){SavePlan();Run("motion "+Quote(motion.id));}
            }
            using(new EditorGUI.DisabledScope(process!=null))if(GUILayout.Button("续查已有任务并下载（最多 30 秒）"))Run("sync all --wait 30");
            EditorGUILayout.HelpBox("人物流程：生成 → 双足检查 → 自动绑定 → 下载 FBX。提交中断后若没有返回编号，需要从平台控制台找回任务 ID，不能重复付费重提。",MessageType.None);
            recoveryId=EditorGUILayout.TextField("恢复素材 ID",recoveryId);recoveryStage=EditorGUILayout.Popup("阶段",recoveryStage,new[]{"generate","rig-check","rig"});recoveryTask=EditorGUILayout.TextField("平台任务 ID",recoveryTask);
            using(new EditorGUI.DisabledScope(process!=null||string.IsNullOrWhiteSpace(recoveryId)||string.IsNullOrWhiteSpace(recoveryTask)))if(GUILayout.Button("关联找回的任务编号"))Run("recover "+Quote(recoveryId)+" "+new[]{"generate","rig-check","rig"}[recoveryStage]+" "+Quote(recoveryTask));
            EditorGUILayout.Space();
            if(GUILayout.Button("导入下载结果并打开素材复查"))
            {try{GeneratedAssetBuilder.Import();GeneratedAssetWorkbench.Open();}catch(Exception e){output=e.Message;}}
            EditorGUILayout.TextArea(output,GUILayout.MinHeight(100));
            EditorGUILayout.EndScrollView();
        }
        void SavePlan(){File.WriteAllText("Generation/plan.json",JsonUtility.ToJson(plan,true));}
    }
}
