using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using UnityEngine;

namespace SongChoreography
{
    public sealed class AnalyzerJob : IDisposable
    {
        Process process;
        Task<string> errors;
        string folder;
        public AnalysisProgress Progress { get;private set; }=new AnalysisProgress{message="准备分析…",state="running"};
        public string Folder=>folder;
        public bool Finished=>process!=null&&process.HasExited;
        public int ExitCode=>process.ExitCode;
        public string Error=>errors!=null&&errors.IsCompleted?errors.Result:"分析器异常退出，请查看项目日志。";
        static string Quote(string value)
        {
            // Paths cannot contain double quotes on Windows. Reject instead of invoking a shell.
            if(value.IndexOf('"')>=0)throw new ArgumentException("路径不能包含双引号。");
            return "\""+value+"\"";
        }
        public AnalyzerJob(string python,string analyzer,AnalysisRequest request)
        {
            folder=request.outputDir;Directory.CreateDirectory(folder);
            string path=Path.Combine(folder,"request.json");File.WriteAllText(path,JsonUtility.ToJson(request,true));
            var info=new ProcessStartInfo{FileName=python,Arguments="-I -X utf8 "+Quote(analyzer)+" --request "+Quote(path),UseShellExecute=false,CreateNoWindow=true,RedirectStandardError=true,WorkingDirectory=folder};
            info.EnvironmentVariables["PYTHONUTF8"]="1";
            info.EnvironmentVariables["NUMBA_CACHE_DIR"]=Path.Combine(Application.persistentDataPath,"AnalyzerCache");
            process=new Process{StartInfo=info};process.Start();errors=process.StandardError.ReadToEndAsync();
        }
        public void Poll()
        {
            string file=Path.Combine(folder,"progress.json");
            if(!File.Exists(file))return;
            try { var p=JsonUtility.FromJson<AnalysisProgress>(File.ReadAllText(file));if(p!=null)Progress=p; }
            catch(IOException){}catch(ArgumentException){}
        }
        public void Cancel()
        {
            File.WriteAllText(Path.Combine(folder,"cancel.flag"),"cancel");
            if(process!=null&&!process.HasExited)process.Kill();
        }
        public void Dispose()
        { if(process!=null){if(!process.HasExited)process.Kill();process.Dispose();process=null;} }
    }
}
